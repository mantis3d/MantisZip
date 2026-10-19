/**
 * MantisZip 维护插件（DeepSeek Harness / Cordis）。
 *
 * 设计约束：本文件**不导入任何第三方包**，只用 node: 内置模块。
 * 原因是插件通过 profile 目录加载时，Node 对裸包名的解析可能落不到
 * profile 的 node_modules 上；零依赖可以彻底绕开这个解析问题，
 * 同时也不需要构建步骤（纯 ESM，直接 import）。
 *
 * 三个工具全部只读：
 *   mantiszip_verify   构建/测试并输出紧凑诊断
 *   mantiszip_i18n     三语 key 对齐 + 占位符一致性
 *   mantiszip_version  版本号 6 处一致性
 */

import { spawn } from 'node:child_process'
import { readFile, stat, mkdir } from 'node:fs/promises'
import path from 'node:path'

export const name = 'mantiszip-tools'
export const inject = ['tools']

/** 允许出现的 JSON Schema 关键字（与 dsh-tools 的强制子集一致）。 */
const SCHEMA_KEYWORDS = new Set([
  'type', 'oneOf', 'properties', 'required', 'additionalProperties', 'items', 'enum', 'const',
  'description', 'title', 'default', 'examples',
])

/** 项目别名 → csproj 相对路径；solution 表示整仓。 */
const TARGETS = {
  avalonia: 'src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj',
  core: 'src/MantisZip.Core/MantisZip.Core.csproj',
  shell: 'src/MantisZip.ShellExt/MantisZip.ShellExt.csproj',
  tests: 'tests/MantisZip.Tests/MantisZip.Tests.csproj',
  'ui-tests': 'tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj',
  solution: 'MantisZip.sln',
}

const I18N_FILES = {
  zhCN: 'strings.zh-CN.json',
  en: 'strings.en.json',
  zhTW: 'strings.zh-TW.json',
}

/** 版本号 6 处来源。 */
const VERSION_SOURCES = [
  { key: 'appConstants', file: 'src/MantisZip.UI.Avalonia/AppConstants.cs', re: /public\s+const\s+string\s+Version\s*=\s*"([^"]+)"/ },
  { key: 'csproj', file: 'src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj', re: /<Version>([^<]+)<\/Version>/ },
  { key: 'plan', file: 'docs/PLAN.md', re: /\*\*当前版本\*\*\s*[:：]\s*v?([0-9][0-9A-Za-z.+\-]*)/ },
  { key: 'progress', file: 'docs/PROGRESS.md', re: /\*\*当前版本\*\*\s*[:：]\s*v?([0-9][0-9A-Za-z.+\-]*)/ },
  { key: 'installer', file: 'installer.iss', re: /#define\s+MyAppVersion\s+"([^"]+)"/ },
  { key: 'installerSelfContained', file: 'installer-selfcontained.iss', re: /#define\s+MyAppVersion\s+"([^"]+)"/ },
]

/** 单次工具调用允许的最大错误/警告条目数。 */
const MAX_ITEMS = 100
/** 单次构建允许的最大输出字节（解析是流式的，这里只限制兜底原文）。 */
const MAX_RAW_BYTES = 256 * 1024

// ---------------------------------------------------------------------------
// 公共工具
// ---------------------------------------------------------------------------

/** 插件配置（由 cordis.patch.yml 的 config.root 注入），可缺省。 */
let configuredRoot = ''

/**
 * 解析工作区根目录。优先级：
 *   1. 模型传入的 root；
 *   2. cordis.patch.yml 里的 config.root（安装脚本写入仓库绝对路径）；
 *   3. 环境变量 DSH_WORKSPACE / DSH_WORKSPACE_ROOT / INIT_CWD；
 *   4. 进程 cwd 及其祖先目录中第一个含 MantisZip.sln 的目录。
 * 全部失败时给出尝试过的路径，避免「不是仓库」这类模糊报错。
 * @param {string | undefined} rootArg 模型传入的 root
 * @returns {Promise<string>} 绝对路径
 */
async function resolveRoot(rootArg) {
  // 显式传入的 root 必须严格可信：不做回退，避免「传错了却悄悄用别的目录」。
  if (rootArg && typeof rootArg === 'string' && rootArg.trim()) {
    const explicit = path.resolve(rootArg.trim())
    try {
      await stat(path.join(explicit, 'MantisZip.sln'))
      return explicit
    } catch {
      throw new Error(`指定的 root 不是 MantisZip 仓库（缺少 MantisZip.sln）：${explicit}`)
    }
  }

  const candidates = []
  const push = (value) => {
    if (!value || typeof value !== 'string') return
    const resolved = path.resolve(value.trim())
    if (!candidates.includes(resolved)) candidates.push(resolved)
  }

  push(configuredRoot)
  push(process.env.DSH_WORKSPACE)
  push(process.env.DSH_WORKSPACE_ROOT)
  push(process.env.INIT_CWD)

  let cursor = process.cwd()
  for (let depth = 0; depth < 8; depth += 1) {
    push(cursor)
    const parent = path.dirname(cursor)
    if (parent === cursor) break
    cursor = parent
  }

  for (const candidate of candidates) {
    try {
      await stat(path.join(candidate, 'MantisZip.sln'))
      return candidate
    } catch {
      // 继续尝试下一个候选目录
    }
  }

  throw new Error(
    '找不到 MantisZip 仓库根目录（需要含 MantisZip.sln）。'
    + `已尝试：${candidates.join(' | ')}。`
    + '可在 cordis.patch.yml 里给 mantiszip-tools 行配置 config.root，或调用时传 root 参数。',
  )
}

/** 截断过长文本，避免单条诊断撑爆上下文。 */
function clip(text, limit = 300) {
  const value = String(text ?? '').trim()
  return value.length <= limit ? value : `${value.slice(0, limit)}…`
}

/** 文本内容块。 */
function text(value) {
  return [{ type: 'text', text: value }]
}

/** 把毫秒格式化成人类可读的秒。 */
function seconds(ms) {
  return `${(ms / 1000).toFixed(1)}s`
}

/**
 * 运行一个子进程，流式收集 stdout/stderr。
 * 强制英文 UI 便于解析；-nodeReuse:false 避免 MSBuild 节点残留。
 * @param {string} file 可执行文件
 * @param {string[]} args 参数
 * @param {{ cwd: string, env?: Record<string,string>, timeoutMs: number, signal?: AbortSignal }} options
 * @returns {Promise<{ code: number, stdout: string, stderr: string, timedOut: boolean, aborted: boolean, truncated: boolean }>}
 */
function runProcess(file, args, options) {
  return new Promise((resolve, reject) => {
    let child
    try {
      child = spawn(file, args, {
        cwd: options.cwd,
        env: { ...process.env, ...options.env },
        windowsHide: true,
        stdio: ['ignore', 'pipe', 'pipe'],
      })
    } catch (error) {
      reject(error)
      return
    }

    let stdout = ''
    let stderr = ''
    let truncated = false
    let timedOut = false
    let aborted = false
    let settled = false

    const append = (target, chunk) => {
      if (target.length >= MAX_RAW_BYTES) {
        truncated = true
        return target
      }
      const next = target + chunk
      if (next.length > MAX_RAW_BYTES) {
        truncated = true
        return next.slice(0, MAX_RAW_BYTES)
      }
      return next
    }

    child.stdout?.setEncoding('utf8')
    child.stderr?.setEncoding('utf8')
    child.stdout?.on('data', (chunk) => { stdout = append(stdout, chunk) })
    child.stderr?.on('data', (chunk) => { stderr = append(stderr, chunk) })

    const timer = setTimeout(() => {
      timedOut = true
      child.kill('SIGKILL')
    }, options.timeoutMs)
    timer.unref?.()

    const onAbort = () => {
      aborted = true
      child.kill('SIGKILL')
    }
    options.signal?.addEventListener('abort', onAbort, { once: true })

    const finish = (code) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      options.signal?.removeEventListener('abort', onAbort)
      resolve({ code, stdout, stderr, timedOut, aborted, truncated })
    }

    child.on('error', (error) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      options.signal?.removeEventListener('abort', onAbort)
      reject(error)
    })
    child.on('close', (code) => finish(code ?? -1))
  })
}

// ---------------------------------------------------------------------------
// 工具 1：mantiszip_verify
// ---------------------------------------------------------------------------

/** MSBuild 诊断行：path(line,col): error CS0000: message [proj] */
const MSBUILD_FILE_DIAG = /^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*)$/
/** 无文件诊断：error NU1301: message */
const MSBUILD_BARE_DIAG = /^(?<severity>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.*)$/
/** dotnet 自身错误：error: message */
const DOTNET_BARE_DIAG = /^(?<severity>error|warning)\s*:\s*(?<message>.+)$/
/** 末尾的 [C:\path\Proj.csproj] 归属标记。 */
const PROJECT_SUFFIX = /\s*\[[^\]]+\]\s*$/

/**
 * 从输出中抽取诊断条目（去重 + 截断）。
 * @param {string} raw 合并后的输出
 * @returns {{ errors: any[], warnings: any[], errorCount: number, warningCount: number }}
 */
function parseDiagnostics(raw) {
  const errors = []
  const warnings = []
  const seen = new Set()

  for (const line of raw.split(/\r?\n/)) {
    const trimmed = line.trim()
    if (!trimmed) continue

    let severity = ''
    let file = ''
    let lineNo = 0
    let column = 0
    let code = ''
    let message = ''

    const withFile = MSBUILD_FILE_DIAG.exec(trimmed)
    if (withFile?.groups) {
      severity = withFile.groups.severity
      file = withFile.groups.file.trim()
      lineNo = Number(withFile.groups.line)
      column = Number(withFile.groups.column)
      code = withFile.groups.code
      message = withFile.groups.message.replace(PROJECT_SUFFIX, '')
    } else {
      const bare = MSBUILD_BARE_DIAG.exec(trimmed) ?? DOTNET_BARE_DIAG.exec(trimmed)
      if (!bare?.groups) continue
      severity = bare.groups.severity
      code = bare.groups.code ?? 'DOTNET'
      message = (bare.groups.message ?? '').replace(PROJECT_SUFFIX, '')
    }

    const item = { file, line: lineNo, column, code, message: clip(message) }
    const signature = `${item.file}|${item.line}|${item.column}|${item.code}|${item.message}`
    if (seen.has(signature)) continue
    seen.add(signature)

    if (severity === 'error') errors.push(item)
    else warnings.push(item)
  }

  return {
    errors: errors.slice(0, MAX_ITEMS),
    warnings: warnings.slice(0, MAX_ITEMS),
    errorCount: errors.length,
    warningCount: warnings.length,
  }
}

/** 渲染 mantiszip_verify 的结果文本。 */
function renderVerify(value) {
  const lines = []
  const head = value.ok
    ? `✅ ${value.action === 'test' ? '测试' : '构建'}通过：${value.target}（${seconds(value.durationMs)}，${value.warningCount} 警告）`
    : `❌ ${value.action === 'test' ? '测试' : '构建'}失败：${value.target}（${seconds(value.durationMs)}，${value.errorCount} 错误 / ${value.warningCount} 警告，exit ${value.exitCode}）`
  lines.push(head)

  if (value.errors.length) {
    lines.push('', '错误：')
    for (const item of value.errors) {
      const where = item.file ? `${item.file}(${item.line},${item.column})` : '(项目级)'
      lines.push(`- ${where} ${item.code}: ${item.message}`)
    }
    if (value.errorCount > value.errors.length) {
      lines.push(`- …已省略 ${value.errorCount - value.errors.length} 条`)
    }
  }

  if (value.warnings.length) {
    lines.push('', '警告：')
    for (const item of value.warnings.slice(0, 10)) {
      const where = item.file ? `${item.file}(${item.line},${item.column})` : '(项目级)'
      lines.push(`- ${where} ${item.code}: ${item.message}`)
    }
    if (value.warningCount > 10) lines.push(`- …共 ${value.warningCount} 条警告`)
  }

  if (value.timedOut) lines.push('', '⚠️ 构建超时，已强制终止（可调大 timeoutMs 或减少项目范围）。')
  if (value.aborted) lines.push('', '⚠️ 调用被取消。')
  if (value.truncated) lines.push('', '⚠️ 输出过长已截断，诊断可能不完整。')

  return lines.join('\n')
}

function verifyTool() {
  return {
    name: 'mantiszip_verify',
    description:
      '构建或测试 MantisZip 的某个项目，并把 MSBuild 输出压成紧凑诊断（文件:行:列 + 错误码 + 首条信息）。'
      + '改完代码后用它自检，比直接跑 dotnet build 省几十倍输出。只读，不会修改任何文件。',
    parameters: {
      type: 'object',
      additionalProperties: false,
      properties: {
        target: {
          type: 'string',
          enum: Object.keys(TARGETS),
          description: '要构建的项目：avalonia（默认）/core/shell/tests/ui-tests/solution。',
        },
        action: {
          type: 'string',
          enum: ['build', 'test'],
          description: 'build（默认）或 test；test 仅对测试项目有意义。',
        },
        extraArgs: {
          type: 'array',
          items: { type: 'string' },
          description: '追加给 dotnet 的原始参数，例如 ["--no-restore"]。',
        },
        timeoutMs: {
          type: 'integer',
          description: '超时毫秒数，默认 600000（10 分钟）。',
        },
        root: {
          type: 'string',
          description: '仓库根目录；默认取当前工作目录。',
        },
      },
    },
    output: {
      schema: {
        type: 'object',
        additionalProperties: false,
        properties: {
          ok: { type: 'boolean' },
          target: { type: 'string' },
          action: { type: 'string' },
          exitCode: { type: 'integer' },
          durationMs: { type: 'integer' },
          command: { type: 'string' },
          errorCount: { type: 'integer' },
          warningCount: { type: 'integer' },
          truncated: { type: 'boolean' },
          timedOut: { type: 'boolean' },
          aborted: { type: 'boolean' },
          errors: { type: 'array', items: { type: 'object' } },
          warnings: { type: 'array', items: { type: 'object' } },
        },
        required: [
          'ok', 'target', 'action', 'exitCode', 'durationMs', 'command',
          'errorCount', 'warningCount', 'truncated', 'timedOut', 'aborted',
          'errors', 'warnings',
        ],
      },
      render: (_args, value) => text(renderVerify(value)),
    },
    timeoutMs: 15 * 60 * 1000,
    async execute(args, exec) {
      const root = await resolveRoot(args.root)
      const target = args.target ?? 'avalonia'
      const action = args.action ?? 'build'
      const projectPath = TARGETS[target]
      if (!projectPath) throw new Error(`未知 target "${target}"，可选：${Object.keys(TARGETS).join(' / ')}`)
      if (action === 'test' && !target.includes('tests')) {
        throw new Error(`target "${target}" 不是测试项目；test 只对 tests / ui-tests 有意义`)
      }

      const timeoutMs = Number.isFinite(args.timeoutMs) && args.timeoutMs > 0 ? args.timeoutMs : 600000
      const argv = [
        action,
        projectPath,
        '-nologo',
        '-nodeReuse:false',
        '-v:m',
        ...(action === 'build' ? ['-clp:NoSummary'] : []),
        ...(Array.isArray(args.extraArgs) ? args.extraArgs.filter((item) => typeof item === 'string') : []),
      ]

      const startedAt = Date.now()
      const result = await runProcess('dotnet', argv, {
        cwd: root,
        env: {
          DOTNET_CLI_UI_LANGUAGE: 'en',
          VSLANG: '1033',
          DOTNET_NOLOGO: '1',
          DOTNET_CLI_TELEMETRY_OPTOUT: '1',
        },
        timeoutMs,
        signal: exec?.signal,
      })
      const durationMs = Date.now() - startedAt
      const parsed = parseDiagnostics(`${result.stdout}\n${result.stderr}`)
      const ok = result.code === 0 && parsed.errorCount === 0

      return {
        ok,
        target,
        action,
        exitCode: result.code,
        durationMs,
        command: `dotnet ${argv.join(' ')}`,
        errorCount: parsed.errorCount,
        warningCount: parsed.warningCount,
        truncated: result.truncated,
        timedOut: result.timedOut,
        aborted: result.aborted,
        errors: parsed.errors,
        warnings: parsed.warnings,
      }
    },
  }
}

// ---------------------------------------------------------------------------
// 工具 2：mantiszip_i18n
// ---------------------------------------------------------------------------

/** 读取 JSON 文本并去掉 BOM。 */
function parseJson(textValue) {
  return JSON.parse(textValue.replace(/^\uFEFF/, ''))
}

/** 扫描重复 key（扁平文件，按行首 key 判定）。 */
function findDuplicateKeys(raw) {
  const seen = new Map()
  const duplicates = []
  const pattern = /^\s*"((?:[^"\\]|\\.)*)"\s*:/gm
  let match
  while ((match = pattern.exec(raw)) !== null) {
    const key = match[1]
    const count = (seen.get(key) ?? 0) + 1
    seen.set(key, count)
    if (count === 2) duplicates.push(key)
  }
  return duplicates
}

/** 提取字符串里的 {0} 类占位符下标集合。 */
function placeholderIndexes(value) {
  const indexes = new Set()
  const pattern = /\{(\d+)/g
  let match
  while ((match = pattern.exec(String(value))) !== null) indexes.add(Number(match[1]))
  return indexes
}

function renderI18n(value) {
  const lines = []
  lines.push(value.ok
    ? `✅ 三语一致：${value.keyCounts.zhCN} 个 key（en / zh-TW 无缺失、无多余、占位符一致）`
    : `❌ 三语不一致：${value.issueCount} 个问题（基准 zh-CN 共 ${value.keyCounts.zhCN} 个 key）`)

  for (const [locale, keys] of Object.entries(value.missing)) {
    if (!keys.length) continue
    lines.push('', `缺失（${locale} 少 ${keys.length} 个）：`)
    for (const key of keys.slice(0, 20)) lines.push(`- ${key}`)
    if (keys.length > 20) lines.push(`- …共 ${keys.length} 个`)
  }

  for (const [locale, keys] of Object.entries(value.extra)) {
    if (!keys.length) continue
    lines.push('', `多余（${locale} 多 ${keys.length} 个）：`)
    for (const key of keys.slice(0, 20)) lines.push(`- ${key}`)
    if (keys.length > 20) lines.push(`- …共 ${keys.length} 个`)
  }

  if (value.placeholderMismatch.length) {
    lines.push('', `占位符不一致（${value.placeholderMismatch.length} 个）：`)
    for (const item of value.placeholderMismatch.slice(0, 20)) {
      lines.push(`- ${item.key}: zh-CN {${item.expected.join('},{')}} vs ${item.locale} {${item.actual.join('},{')}}`)
    }
    if (value.placeholderMismatch.length > 20) {
      lines.push(`- …共 ${value.placeholderMismatch.length} 个`)
    }
  }

  if (value.duplicates.length) {
    lines.push('', `重复 key（${value.duplicates.length} 个）：`)
    for (const item of value.duplicates.slice(0, 10)) lines.push(`- ${item.locale}: ${item.key}`)
  }

  if (value.emptyValues.length) {
    lines.push('', `空值（${value.emptyValues.length} 个，仅提示）：`)
    for (const item of value.emptyValues.slice(0, 10)) lines.push(`- ${item.locale}: ${item.key}`)
  }

  return lines.join('\n')
}

function i18nTool() {
  return {
    name: 'mantiszip_i18n',
    description:
      '检查 MantisZip 三份语言文件（zh-CN / en / zh-TW）的 key 集合是否一致、{0} 占位符是否对齐、'
      + '是否存在重复 key 或空值。新增界面文案后运行。只读，不会修改任何文件。',
    parameters: {
      type: 'object',
      additionalProperties: false,
      properties: {
        dir: {
          type: 'string',
          description: '语言文件目录，默认 src/MantisZip.UI.Avalonia/Localization。',
        },
        root: {
          type: 'string',
          description: '仓库根目录；默认取当前工作目录。',
        },
      },
    },
    output: {
      schema: {
        type: 'object',
        additionalProperties: false,
        properties: {
          ok: { type: 'boolean' },
          issueCount: { type: 'integer' },
          keyCounts: { type: 'object' },
          missing: { type: 'object' },
          extra: { type: 'object' },
          placeholderMismatch: { type: 'array', items: { type: 'object' } },
          duplicates: { type: 'array', items: { type: 'object' } },
          emptyValues: { type: 'array', items: { type: 'object' } },
        },
        required: [
          'ok', 'issueCount', 'keyCounts', 'missing', 'extra',
          'placeholderMismatch', 'duplicates', 'emptyValues',
        ],
      },
      render: (_args, value) => text(renderI18n(value)),
    },
    timeoutMs: 60 * 1000,
    async execute(args, exec) {
      const root = await resolveRoot(args.root)
      const dir = path.resolve(root, args.dir ?? 'src/MantisZip.UI.Avalonia/Localization')

      const loaded = {}
      for (const [locale, fileName] of Object.entries(I18N_FILES)) {
        const filePath = path.join(dir, fileName)
        let raw
        try {
          raw = await readFile(filePath, 'utf8')
        } catch {
          throw new Error(`读不到语言文件：${filePath}`)
        }
        exec?.signal?.throwIfAborted?.()
        loaded[locale] = { raw, data: parseJson(raw) }
      }

      const keyCounts = Object.fromEntries(
        Object.entries(loaded).map(([locale, item]) => [locale, Object.keys(item.data).length]),
      )
      const baseKeys = Object.keys(loaded.zhCN.data)

      const missing = {}
      const extra = {}
      for (const locale of ['en', 'zhTW']) {
        const keys = new Set(Object.keys(loaded[locale].data))
        missing[locale] = baseKeys.filter((key) => !keys.has(key))
        extra[locale] = [...keys].filter((key) => !(key in loaded.zhCN.data))
      }

      const placeholderMismatch = []
      for (const locale of ['en', 'zhTW']) {
        for (const key of baseKeys) {
          const baseValue = loaded.zhCN.data[key]
          const targetValue = loaded[locale].data[key]
          if (typeof baseValue !== 'string' || typeof targetValue !== 'string') continue
          const expected = [...placeholderIndexes(baseValue)].sort((a, b) => a - b)
          const actual = [...placeholderIndexes(targetValue)].sort((a, b) => a - b)
          if (expected.join(',') !== actual.join(',')) {
            placeholderMismatch.push({ key, locale, expected, actual })
          }
        }
      }

      const duplicates = []
      for (const [locale, item] of Object.entries(loaded)) {
        for (const key of findDuplicateKeys(item.raw)) duplicates.push({ locale, key })
      }

      const emptyValues = []
      for (const [locale, item] of Object.entries(loaded)) {
        for (const [key, value] of Object.entries(item.data)) {
          if (typeof value === 'string' && value.trim() === '') emptyValues.push({ locale, key })
        }
      }

      const issueCount = Object.values(missing).reduce((sum, list) => sum + list.length, 0)
        + Object.values(extra).reduce((sum, list) => sum + list.length, 0)
        + placeholderMismatch.length
        + duplicates.length

      return {
        ok: issueCount === 0,
        issueCount,
        keyCounts,
        missing,
        extra,
        placeholderMismatch: placeholderMismatch.slice(0, MAX_ITEMS),
        duplicates: duplicates.slice(0, MAX_ITEMS),
        emptyValues: emptyValues.slice(0, MAX_ITEMS),
      }
    },
  }
}

// ---------------------------------------------------------------------------
// 工具 3：mantiszip_version
// ---------------------------------------------------------------------------

function renderVersion(value) {
  const lines = []
  lines.push(value.ok
    ? `✅ 版本号一致：${value.unique[0]}（6 处全部匹配）`
    : `❌ 版本号不一致：发现 ${value.unique.length} 个不同取值 / ${value.missingSources.length} 处缺失`)

  lines.push('', '| 位置 | 取值 |', '| --- | --- |')
  for (const [key, version] of Object.entries(value.versions)) {
    lines.push(`| ${key} | ${version ?? '(未找到)'} |`)
  }

  if (value.missingSources.length) {
    lines.push('', `未匹配到版本号：${value.missingSources.join(', ')}`)
  }
  if (!value.ok && value.unique.length > 1) {
    lines.push('', `不同取值：${value.unique.join(' / ')}`)
  }

  return lines.join('\n')
}

function versionTool() {
  return {
    name: 'mantiszip_version',
    description:
      '核对 MantisZip 发版版本号在 6 处的取值是否一致（AppConstants.Version、csproj <Version>、'
      + 'docs/PLAN.md、docs/PROGRESS.md、installer.iss、installer-selfcontained.iss）。'
      + '发版前运行；只读检查，不会改文件。',
    parameters: {
      type: 'object',
      additionalProperties: false,
      properties: {
        root: {
          type: 'string',
          description: '仓库根目录；默认取当前工作目录。',
        },
      },
    },
    output: {
      schema: {
        type: 'object',
        additionalProperties: false,
        properties: {
          ok: { type: 'boolean' },
          versions: { type: 'object' },
          unique: { type: 'array', items: { type: 'string' } },
          missingSources: { type: 'array', items: { type: 'string' } },
        },
        required: ['ok', 'versions', 'unique', 'missingSources'],
      },
      render: (_args, value) => text(renderVersion(value)),
    },
    timeoutMs: 30 * 1000,
    async execute(args, exec) {
      const root = await resolveRoot(args.root)

      const versions = {}
      const missingSources = []
      for (const source of VERSION_SOURCES) {
        const filePath = path.join(root, source.file)
        let raw
        try {
          raw = await readFile(filePath, 'utf8')
        } catch {
          missingSources.push(`${source.key}（读不到 ${source.file}）`)
          continue
        }
        exec?.signal?.throwIfAborted?.()
        const match = source.re.exec(raw)
        if (!match) {
          missingSources.push(source.key)
          continue
        }
        versions[source.key] = match[1].trim().replace(/[.。,，]$/, '')
      }

      const unique = [...new Set(Object.values(versions))]
      return {
        ok: missingSources.length === 0 && unique.length === 1,
        versions,
        unique,
        missingSources,
      }
    },
  }
}

// ---------------------------------------------------------------------------
// 入口
// ---------------------------------------------------------------------------

/**
 * 注册三个只读维护工具。
 * @param {any} ctx Cordis 上下文（需注入 tools 服务）
 * @param {{ root?: string } | undefined} config 部署配置；root 指向仓库绝对路径
 */
export function apply(ctx, config) {
  if (!ctx?.tools?.register) throw new Error('mantiszip-tools 需要 ctx.tools 服务')
  configuredRoot = typeof config?.root === 'string' ? config.root : ''
  ctx.tools.register(verifyTool())
  ctx.tools.register(i18nTool())
  ctx.tools.register(versionTool())
}
