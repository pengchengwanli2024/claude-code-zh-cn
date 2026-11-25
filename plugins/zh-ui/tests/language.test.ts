import { test, expect } from 'claude-code/testing'
import catalog from '../locales/zh-CN.js'
import { commandDescription, coverage } from '../hooks/menus.js'
import { hint, label, lookup, spinner } from '../hooks/translate.js'

test('every catalog entry has source and target; only model names are untranslated', () => {
  const brands = ['Haiku', 'Opus', 'Opus 4.1', 'Sonnet']
  for (const group of Object.values(catalog)) {
    for (const [en, zh] of Object.values(group)) {
      expect(en.trim().length > 0 && zh.trim().length > 0).toBe(true)
      expect(/[\u3400-\u9fff]/u.test(zh) || brands.includes(zh)).toBe(true)
    }
  }
})

test('shows the scope of approval and retains unknown strings verbatim', () => {
  expect(label('Yes, for this session')).toBe('是，本次会话允许')
  expect(label('Accessing workspace:')).toBe('正在访问工作区：')
  expect(label(' Deny ')).toBe(' 拒绝 ')
  expect(label('Allow /tmp/Allow')).toBe('Allow /tmp/Allow')
  expect(label(null)).toBe(null)
})

test('keeps model names, handles spinner punctuation and compound hotkeys', () => {
  expect(commandDescription('model', 'Switch model (currently Opus X)')).toBe('切换模型（当前为 Opus X）')
  expect(commandDescription('help', 'NEW meaning')).toBe('NEW meaning')
  expect(spinner('Thinking').endsWith('…')).toBe(false)
  expect(hint('esc to interrupt · ← for agents')).toBe('按 Esc 中断 · 按 ← 查看智能体')
})

test('local lookup works through the registered command handler', async ($) => {
  const result = await $.command.run({ command: 'zh-lookup', args: 'Yes, for this session' })
  expect(result.text).toBe('Yes, for this session  →  是，本次会话允许')
  expect(lookup('word-not-in-catalog')).toBe('当前词库未收录这段文字。')
})

test('coverage detects untranslated additions and excludes third-party menus', () => {
  const commands = [
    { name: 'help', source: 'builtin', description: 'Show help and available commands' },
    { name: 'new-one', source: 'builtin', description: 'Brand new command' },
    { name: 'external', source: 'plugin', description: 'Third-party text' }
  ]
  const report = coverage(commands, [{ key: 'language', provider: { plugin: 'engine' }, label: 'Language' }])
  expect(report.includes('1 / 2')).toBe(true)
  expect(report.includes('/new-one')).toBe(true)
  expect(report.includes('external')).toBe(false)
})
