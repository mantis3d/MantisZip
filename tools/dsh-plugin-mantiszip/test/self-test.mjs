/**
 * mantiszip-dsh-plugin 自检脚本（不依赖 DSH 运行时）。
 *
 * 校验三件事：
 *   1. 模块导出契约：name / inject / apply 能注册出 3 个工具；
 *   2. 工具声明符合 dsh-tools 的 JSON Schema 子集（否则注册会抛 UNSUPPORTED_SCHEMA）；
 *   3. 三个工具在真实仓库上跑通，返回值的字段与 output.schema 对齐。
 *
 * 用法：
 *   node tools/dsh-plugin-mantiszip/test/self-test.mjs            # 含一次 dotnet build
 *   node tools/dsh-plugin-mantiszip/test/self-test.mjs --skip-build
 */

import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const pluginRoot = path.resolve(here, '..')
const repoRoot = path.resolve(pluginRoot, '..', '..')
const skipBuild = process.argv.includes('--skip-build')

const ALLOWED_KEYWORDS = new Set([
  'type', 'oneOf', 'properties', 'required', 'additionalProperties', 'items', 'enum', 'const',
  'description', 'title', 'default', 'examples',
])
const SCALAR_TYPES = new Set(['object', 'array', 'string', 'number', 'integer', 'boolean', 'null'])

let failures = 0
function check(label, condition, detail = '') {
  if (condition) {
    console.log(`  ✅ ${label}`)
  } else {
    failures += 1
    console.log(`  ❌ ${label}${detail ? ` — ${detail}` : ''}`)
  }
}

/** 递归校验 schema 只使用允许的关键字与类型。 */
function validateSchema(schema, where) {
  if (schema === null || typeof schema !== 'object' || Array.isArray(schema)) {
    return [`${where}: schema 必须是对象`]
  }
  const problems = []
  for (const key of Object.keys(schema)) {
    if (!ALLOWED_KEYWORDS.has(key)) problems.push(`${where}: 不允许的关键字 ${key}`)
  }
  if (schema.type !== undefined && !SCALAR_TYPES.has(schema.type)) {
    problems.push(`${where}: 未知 type ${schema.type}`)
  }
  if (schema.properties !== undefined) {
    if (typeof schema.properties !== 'object' || Array.isArray(schema.properties)) {
      problems.push(`${where}: properties 必须是对象`)
    } else {
      for (const [name, child] of Object.entries(schema.properties)) {
        problems.push(...validateSchema(child, `${where}.properties.${name}`))
      }
    }
  }
  if (schema.items !== undefined) problems.push(...validateSchema(schema.items, `${where}.items`))
  if (schema.required !== undefined) {
    if (!Array.isArray(schema.required) || schema.required.some((item) => typeof item !== 'string')) {
      problems.push(`${where}: required 必须是字符串数组`)
    }
  }
  if (schema.enum !== undefined) {
    if (!Array.isArray(schema.enum) || schema.enum.length === 0) {
      problems.push(`${where}: enum 必须是非空数组`)
    } else if (schema.type !== undefined && !schema.enum.every((item) => typeof item === schema.type || (schema.type === 'integer' && Number.isInteger(item)))) {
      problems.push(`${where}: enum 取值与 type 不一致`)
    }
  }
  if (schema.oneOf !== undefined) {
    if (!Array.isArray(schema.oneOf) || schema.oneOf.length !== 1) problems.push(`${where}: oneOf 只支持恰好一项`)
    else problems.push(...validateSchema(schema.oneOf[0], `${where}.oneOf[0]`))
  }
  return problems
}

/** 校验返回值字段与 output.schema 的 required/additionalProperties 对齐。 */
function validateValue(schema, value, where) {
  const problems = []
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return [`${where}: 返回值不是对象`]
  }
  const declared = schema.properties ?? {}
  for (const name of schema.required ?? []) {
    if (!(name in value)) problems.push(`${where}: 缺少 required 字段 ${name}`)
  }
  if (schema.additionalProperties === false) {
    for (const name of Object.keys(value)) {
      if (!(name in declared)) problems.push(`${where}: 多出未声明字段 ${name}`)
    }
  }
  for (const [name, child] of Object.entries(declared)) {
    if (!(name in value)) continue
    const item = value[name]
    if (child.type === 'array' && !Array.isArray(item)) problems.push(`${where}.${name}: 应为数组`)
    if (child.type === 'integer' && !Number.isInteger(item)) problems.push(`${where}.${name}: 应为整数`)
    if (child.type === 'boolean' && typeof item !== 'boolean') problems.push(`${where}.${name}: 应为布尔`)
    if (child.type === 'object' && (item === null || typeof item !== 'object' || Array.isArray(item))) {
      problems.push(`${where}.${name}: 应为对象`)
    }
  }
  return problems
}

const module = await import(pathToFileURL(path.join(pluginRoot, 'index.mjs')).href)
const registered = []
const ctx = { tools: { register: (definition) => { registered.push(definition); return () => {} } } }

console.log('\n[1] 模块契约与 schema')
check('导出 name 字符串', typeof module.name === 'string', String(module.name))
check('导出 inject = [tools]', Array.isArray(module.inject) && module.inject.includes('tools'))
check('导出 apply 函数', typeof module.apply === 'function')

module.apply(ctx)
check('注册了 3 个工具', registered.length === 3, `实际 ${registered.length}`)
const names = registered.map((item) => item.name)
check('工具名符合 mantiszip_*', names.every((item) => item.startsWith('mantiszip_')))
check('工具名不重复', new Set(names).size === names.length)
check('每个工具都有 output.render', registered.every((item) => typeof item.output?.render === 'function'))

for (const definition of registered) {
  const problems = [
    ...validateSchema(definition.parameters, `${definition.name}.parameters`),
    ...validateSchema(definition.output.schema, `${definition.name}.output`),
  ]
  check(`${definition.name} schema 合法`, problems.length === 0, problems.join('; '))
}

const exec = { signal: new AbortController().signal }
const byName = Object.fromEntries(registered.map((item) => [item.name, item]))

console.log('\n[2] mantiszip_version')
{
  const definition = byName.mantiszip_version
  const value = await definition.execute({ root: repoRoot }, exec)
  const problems = validateValue(definition.output.schema, value, 'value')
  check('返回值字段合法', problems.length === 0, problems.join('; '))
  check('六个来源全部解析到', value.missingSources.length === 0, value.missingSources.join(', '))
  check('版本号一致', value.ok === true, `unique=${value.unique.join('/')}`)
  console.log(`     → ${value.unique.join(' / ') || '(none)'}`)
  const blocks = definition.output.render({}, value)
  check('render 返回 text 块', Array.isArray(blocks) && blocks.every((block) => block?.type === 'text'))
}

console.log('\n[3] mantiszip_i18n')
{
  const definition = byName.mantiszip_i18n
  const value = await definition.execute({ root: repoRoot }, exec)
  const problems = validateValue(definition.output.schema, value, 'value')
  check('返回值字段合法', problems.length === 0, problems.join('; '))
  check('三份文件 key 数量一致', new Set(Object.values(value.keyCounts)).size === 1,
    `zh-CN=${value.keyCounts.zhCN} en=${value.keyCounts.en} zh-TW=${value.keyCounts.zhTW}`)
  const structuralIssues = Object.values(value.missing).flat().length
    + Object.values(value.extra).flat().length
    + value.placeholderMismatch.length
  check('无缺失/多余/占位符不一致', structuralIssues === 0,
    `missing=${JSON.stringify(value.missing)} extra=${JSON.stringify(value.extra)} placeholders=${value.placeholderMismatch.length}`)
  console.log(`     → keys=${value.keyCounts.zhCN} issues=${value.issueCount} empty=${value.emptyValues.length}`)
  if (value.duplicates.length) {
    console.log(`     ⚠️ 重复 key（仓库既有问题，工具会报出）：${value.duplicates.map((item) => `${item.locale}:${item.key}`).join(', ')}`)
  }
}

if (skipBuild) {
  console.log('\n[4] mantiszip_verify  — 已按 --skip-build 跳过')
} else {
  console.log('\n[4] mantiszip_verify（dotnet build avalonia，可能耗时数十秒）')
  const definition = byName.mantiszip_verify
  const startedAt = Date.now()
  const value = await definition.execute({ root: repoRoot, target: 'avalonia' }, exec)
  const problems = validateValue(definition.output.schema, value, 'value')
  check('返回值字段合法', problems.length === 0, problems.join('; '))
  check('构建成功', value.ok === true,
    `exit=${value.exitCode} errors=${value.errorCount} 首条=${value.errors[0]?.message ?? ''}`)
  check('耗时字段合理', value.durationMs > 0, `${value.durationMs}ms`)
  console.log(`     → exit=${value.exitCode} errors=${value.errorCount} warnings=${value.warningCount} 用时 ${(Date.now() - startedAt) / 1000}s`)
}

console.log(failures === 0 ? '\n全部通过 ✅\n' : `\n${failures} 项失败 ❌\n`)
process.exit(failures === 0 ? 0 : 1)
