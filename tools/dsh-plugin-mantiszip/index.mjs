/**
 * MantisZip 维护插件（DeepSeek Harness / Cordis）—— 薄壳层。
 *
 * 设计：所有检查逻辑都在仓库脚本 `scripts/verify-repo.ps1` 里，本文件只负责
 *   1. 解析参数、校验取值（target / action 组合）；
 *   2. 用 `-Task <task> -Json` 起一个 pwsh 子进程；
 *   3. 把脚本吐出的 JSON 原样返回（输出契约逐字段不变），供 render() 渲染。
 *
 * 这样 DSH / opencode / Reasonix / CI / 人工 shell 共用同一份实现，换 harness
 * 不会丢功能；插件本身不导入任何第三方包，只用 node: 内置模块。
 *
 * 四个工具全部只读：
 *   mantiszip_verify   构建/测试并输出紧凑诊断
 *   mantiszip_i18n     三语 key 对齐 + 占位符一致性
 *   mantiszip_version  版本号 6 处一致性
 *   mantiszip_guards   代码规则守卫（快捷键登记 / i18n 绑定登记 / 文件选择器 / 图标登记）
 *
 * 环境要求：PowerShell 7（pwsh）。可用 MANTISZIP_PWSH 覆盖可执行文件路径。
 */

import { spawn } from 'node:child_process'
import { stat } from 'node:fs/promises'
import path from 'node:path'

export const name = 'mantiszip-tools'
export const inject = ['tools']

/** 逻辑实现所在的仓库脚本（相对仓库根目录）。 */
const SCRIPT_RELATIVE = 'scripts/verify-repo.ps1'

/** 项目别名 → csproj 相对路径；solution 表示整仓。 */
const TARGETS = {
  avalonia: 'src/MantisZip.UI.Avalonia/MantisZip.UI.Avalonia.csproj',
  core: 'src/MantisZip.Core/MantisZip.Core.csproj',
  shell: 'src/MantisZip.ShellExt/MantisZip.ShellExt.csproj',
  tests: 'tests/MantisZip.Tests/MantisZip.Tests.csproj',
  'ui-tests': 'tests/MantisZip.UI.Avalonia.Tests/MantisZip.UI.Avalonia.Tests.csproj',
  solution: 'MantisZip.sln',
}

/** 脚本 JSON 输出的上限；超过即视为异常，避免把内存撑爆。 */
const MAX_JSON_BYTES = 4 * 1024 * 1024

/** 插件配置（由 cordis.patch.yml 的 config.root 注入），可缺省。 */
let configuredRoot = ''

// ---------------------------------------------------------------------------
// 公共工具
// ---------------------------------------------------------------------------

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

/** 文本内容块。 */
function text(value) {
  return [{ type: 'text', text: value }]
}

/** 把毫秒格式化成人类可读的秒。 */
function seconds(ms) {
  return `${(ms / 1000).toFixed(1)}s`
}

/** PowerShell 7 可执行文件；Windows 显式带扩展名，避免解析到别的同名程序。 */
function powershellPath() {
  if (process.env.MANTISZIP_PWSH) return process.env.MANTISZIP_PWSH
  return process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'
}

/**
 * 运行一个子进程，收集 stdout/stderr。
 * @param {string} file 可执行文件
 * @param {string[]} args 参数
 * @param {{ cwd: string, timeoutMs: number, signal?: AbortSignal }} options
 * @returns {Promise<{ code: number, stdout: string, stderr: string, timedOut: boolean, aborted: boolean }>}
 */
function runProcess(file, args, options) {
  return new Promise((resolve, reject) => {
    let child
    try {
      child = spawn(file, args, {
        cwd: options.cwd,
        windowsHide: true,
        stdio: ['ignore', 'pipe', 'pipe'],
      })
    } catch (error) {
      reject(error)
      return
    }

    let stdout = ''
    let stderr = ''
    let timedOut = false
    let aborted = false
    let settled = false
    let overflow = false

    child.stdout?.setEncoding('utf8')
    child.stderr?.setEncoding('utf8')
    child.stdout?.on('data', (chunk) => {
      if (stdout.length + chunk.length > MAX_JSON_BYTES) {
        overflow = true
        return
      }
      stdout += chunk
    })
    child.stderr?.on('data', (chunk) => {
      // stderr 只在报错时用于补充上下文，留最后 8 KB 足够。
      stderr = (stderr + chunk).slice(-8192)
    })

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

    const cleanup = () => {
      clearTimeout(timer)
      options.signal?.removeEventListener('abort', onAbort)
    }

    child.on('error', (error) => {
      if (settled) return
      settled = true
      cleanup()
      reject(error)
    })
    child.on('close', (code) => {
      if (settled) return
      settled = true
      cleanup()
      if (overflow) {
        reject(new Error(`维护脚本输出超过 ${MAX_JSON_BYTES} 字节，已中止解析。`))
        return
      }
      resolve({ code: code ?? -1, stdout, stderr, timedOut, aborted })
    })
  })
}

/**
 * 调用仓库脚本并解析其 JSON 输出。
 * 脚本在 ok=false 时以退出码 1 结束，这是正常结果，不算失败。
 * @param {string} root 仓库根目录
 * @param {string[]} taskArgs 追加到脚本命令行的参数（含 -Task）
 * @param {{ timeoutMs: number, signal?: AbortSignal }} options
 * @returns {Promise<any>} 脚本输出的 JSON 对象
 */
async function runScript(root, taskArgs, options) {
  const scriptPath = path.join(root, SCRIPT_RELATIVE)
  try {
    await stat(scriptPath)
  } catch {
    throw new Error(
      `找不到维护脚本：${scriptPath}。`
      + '本插件依赖仓库内的 scripts/verify-repo.ps1，请确认仓库版本未被裁剪。',
    )
  }

  const argv = ['-NoProfile', '-NoLogo', '-NonInteractive']
  if (process.platform === 'win32') argv.push('-ExecutionPolicy', 'Bypass')
  argv.push('-File', scriptPath, '-Json', ...taskArgs)

  const result = await runProcess(powershellPath(), argv, {
    cwd: root,
    timeoutMs: options.timeoutMs,
    signal: options.signal,
  })

  if (result.aborted) throw new Error('调用被取消，未取得诊断结果。')
  if (result.timedOut) {
    throw new Error(`维护脚本执行超时（${seconds(options.timeoutMs)}），已强制终止。`)
  }

  const raw = result.stdout.trim()
  if (!raw) {
    throw new Error(
      `维护脚本没有输出（exit ${result.code}）。`
      + (result.stderr ? `\n--- stderr ---\n${result.stderr}` : ''),
    )
  }

  let payload
  try {
    payload = JSON.parse(raw)
  } catch {
    throw new Error(
      `维护脚本输出不是合法 JSON（exit ${result.code}）。`
      + `\n--- stdout（前 800 字符）---\n${raw.slice(0, 800)}`
      + (result.stderr ? `\n--- stderr ---\n${result.stderr}` : ''),
    )
  }

  return payload
}

// ---------------------------------------------------------------------------
// 工具 1：mantiszip_verify
// ---------------------------------------------------------------------------

/** 诊断位置：项目级（无文件）/ 文件（无行列号，如 restore 失败）/ 文件(行,列)。 */
function whereOf(item) {
  if (!item.file) return '(项目级)'
  if (item.line === null || item.line === undefined) return item.file
  return `${item.file}(${item.line},${item.column})`
}

/** 渲染 mantiszip_verify 的结果文本。 */
function renderVerify(value) {
  const lines = []
  const verb = value.action === 'test' ? '测试' : '构建'
  lines.push(value.ok
    ? `✅ ${verb}通过：${value.target}（${seconds(value.durationMs)}，${value.warningCount} 警告）`
    : `❌ ${verb}失败：${value.target}（${seconds(value.durationMs)}，${value.errorCount} 错误 / ${value.warningCount} 警告，exit ${value.exitCode}）`)

  if (value.runner === 'mtp') {
    lines.push('ℹ️ VSTest testhost 不可用，已回退 dotnet exec 直跑测试程序集（xunit MTP）。')
  }

  if (value.errors.length) {
    lines.push('', '错误：')
    for (const item of value.errors) {
      lines.push(`- ${whereOf(item)} ${item.code}: ${item.message}`)
    }
    if (value.errorCount > value.errors.length) {
      lines.push(`- …已省略 ${value.errorCount - value.errors.length} 条`)
    }
  }

  if (value.warnings.length) {
    lines.push('', '警告：')
    for (const item of value.warnings.slice(0, 10)) {
      lines.push(`- ${whereOf(item)} ${item.code}: ${item.message}`)
    }
    if (value.warningCount > 10) lines.push(`- …共 ${value.warningCount} 条警告`)
  }

  if (value.timedOut) lines.push('', '⚠️ 构建超时，已强制终止（可调大 timeoutMs 或减少项目范围）。')
  if (value.aborted) lines.push('', '⚠️ 调用被取消。')
  if (value.truncated) lines.push('', '⚠️ 输出过长已截断，诊断可能不完整。')

  if (Array.isArray(value.tail) && value.tail.length) {
    lines.push('', '输出尾部：')
    for (const item of value.tail) lines.push(`- ${item}`)
  }

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
          runner: { type: 'string' },
          hostCrashed: { type: 'boolean' },
          testsReported: { type: 'boolean' },
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
          tail: { type: 'array', items: { type: 'string' } },
          fallbackFrom: { type: 'string' },
          primaryExitCode: { type: 'integer' },
          mtpFallback: { type: 'object' },
        },
        required: [
          'ok', 'target', 'action', 'runner', 'hostCrashed', 'testsReported', 'exitCode',
          'durationMs', 'command', 'errorCount', 'warningCount', 'truncated', 'timedOut',
          'aborted', 'errors', 'warnings', 'tail',
        ],
      },
      render: (_args, value) => text(renderVerify(value)),
    },
    timeoutMs: 15 * 60 * 1000,
    async execute(args, exec) {
      const root = await resolveRoot(args.root)
      const target = args.target ?? 'avalonia'
      const action = args.action ?? 'build'
      if (!TARGETS[target]) {
        throw new Error(`未知 target "${target}"，可选：${Object.keys(TARGETS).join(' / ')}`)
      }
      if (action === 'test' && !target.includes('tests')) {
        throw new Error(`target "${target}" 不是测试项目；test 只对 tests / ui-tests 有意义`)
      }

      const timeoutMs = Number.isFinite(args.timeoutMs) && args.timeoutMs > 0 ? args.timeoutMs : 600000
      const extraArgs = Array.isArray(args.extraArgs)
        ? args.extraArgs.filter((item) => typeof item === 'string')
        : []

      return await runScript(root, [
        '-Task', action,
        '-Target', target,
        '-TimeoutMs', String(timeoutMs),
        '-ExtraArgsJson', JSON.stringify(extraArgs),
      ], {
        // 脚本自己会先超时并返回 timedOut=true，所以给它留出收尾余量。
        timeoutMs: timeoutMs + 60000,
        signal: exec?.signal,
      })
    },
  }
}

// ---------------------------------------------------------------------------
// 工具 2：mantiszip_i18n
// ---------------------------------------------------------------------------

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
      const taskArgs = ['-Task', 'i18n']
      if (typeof args.dir === 'string' && args.dir.trim()) {
        taskArgs.push('-LocalizationDir', args.dir.trim())
      }
      return await runScript(root, taskArgs, { timeoutMs: 60000, signal: exec?.signal })
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
      return await runScript(root, ['-Task', 'version'], { timeoutMs: 30000, signal: exec?.signal })
    },
  }
}

// ---------------------------------------------------------------------------
// 工具 4：mantiszip_guards
// ---------------------------------------------------------------------------

function renderGuards(value) {
  const guards = Array.isArray(value.guards) ? value.guards : []
  const lines = []
  lines.push(value.ok
    ? `✅ 规则守卫全部通过（${guards.length} 项）`
    : `❌ 规则守卫失败 ${value.failedCount} 项（共 ${guards.length} 项）`)

  for (const guard of guards) {
    lines.push('')
    if (guard.ok) {
      lines.push(`### ✅ ${guard.title}`)
      continue
    }
    lines.push(`### ❌ ${guard.title}`)
    const issues = Array.isArray(guard.issues) ? guard.issues : []
    for (const issue of issues.slice(0, 20)) lines.push(`- ${issue}`)
    if (issues.length > 20) lines.push(`- …共 ${issues.length} 条`)
  }

  return lines.join('\n')
}

function guardsTool() {
  return {
    name: 'mantiszip_guards',
    description:
      '运行 MantisZip 的代码规则守卫：① 菜单 InputGesture 是否真的登记在 MainWindow.KeyBindings（规则 10.1）'
      + '② 主窗口 LocalizedStrings 绑定 key 是否登记到 UpdateLocalizedStrings() 且三语齐全（规则 13）'
      + '③ 是否绕过 CustomFilePickerDialog 直接调原生 StorageProvider（规则 16）'
      + '④ AppIcons.axaml 图标是否登记到 IconTestViewModel（规则 8）。只读，不会修改任何文件。',
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
          failedCount: { type: 'integer' },
          guards: { type: 'array', items: { type: 'object' } },
        },
        required: ['ok', 'failedCount', 'guards'],
      },
      render: (_args, value) => text(renderGuards(value)),
    },
    timeoutMs: 60 * 1000,
    async execute(args, exec) {
      const root = await resolveRoot(args.root)
      return await runScript(root, ['-Task', 'guards'], { timeoutMs: 60000, signal: exec?.signal })
    },
  }
}

// ---------------------------------------------------------------------------
// 入口
// ---------------------------------------------------------------------------

/**
 * 注册四个只读维护工具。
 * @param {any} ctx Cordis 上下文（需注入 tools 服务）
 * @param {{ root?: string } | undefined} config 部署配置；root 指向仓库绝对路径
 */
export function apply(ctx, config) {
  if (!ctx?.tools?.register) throw new Error('mantiszip-tools 需要 ctx.tools 服务')
  configuredRoot = typeof config?.root === 'string' ? config.root : ''
  ctx.tools.register(verifyTool())
  ctx.tools.register(i18nTool())
  ctx.tools.register(versionTool())
  ctx.tools.register(guardsTool())
}
