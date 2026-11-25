import commands from '../locales/commands.js'
import { label } from './translate.js'

function fingerprint(text) {
  let hash = 2166136261
  for (let index = 0; index < text.length; index += 1) {
    hash = Math.imul(hash ^ text.charCodeAt(index), 16777619)
  }
  return (hash >>> 0).toString(16)
}

export function commandDescription(name, text) {
  if (typeof text !== 'string') return text
  if (name === 'model') {
    const current = /^Switch model \(currently ([^()\n]+)\)$/.exec(text)
    if (current) return '切换模型（当前为 ' + current[1] + '）'
  }
  const entry = commands[name]
  if (entry && entry[0] === fingerprint(text)) return entry[1]
  return label(text)
}

export function coverage(commandRows, settingsRows) {
  const builtin = commandRows.filter(row => row.source === 'builtin')
  const settings = settingsRows.filter(row => row.provider.plugin === 'engine')
  const missingCommands = builtin.filter(row => !/[\u3400-\u9fff]/u.test(commandDescription(row.name, row.description)))
  const missingSettings = settings.filter(row => !/[\u3400-\u9fff]/u.test(label(row.label)))
  const lines = [
    '本次会话可枚举的内置菜单',
    '命令说明：' + (builtin.length - missingCommands.length) + ' / ' + builtin.length,
    '设置标签：' + (settings.length - missingSettings.length) + ' / ' + settings.length,
    '权限弹窗、启动页和未打开的其他界面不在这个计数内。'
  ]
  for (const row of missingCommands) lines.push('待翻译命令：/' + row.name)
  for (const row of missingSettings) lines.push('待翻译设置：' + row.key + ' — ' + row.label)
  return lines.join('\n')
}
