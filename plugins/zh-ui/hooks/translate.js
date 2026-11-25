import catalog from '../locales/zh-CN.js'

const rows = Object.values(catalog).flatMap(group => Object.values(group))
const translations = new Map(rows.map(([en, zh]) => [en.trim(), zh.trim()]))
const spinnerWords = new Map(Object.values(catalog.spinners).map(([en, zh]) => [
  en.trim(), zh.trim().replace(/(?:…|\.\.\.)$/, '')
]))

// Match the entire label. A shell command, a path, or a new sentence containing
// an English label must never be rewritten just because part of it matched.
export function label(value) {
  if (typeof value !== 'string') return value
  const key = value.trim()
  const translated = translations.get(key)
  return translated ? value.replace(key, () => translated) : value
}

export function spinner(value) {
  return spinnerWords.get(value) ?? label(value)
}

export function hint(value) {
  if (typeof value !== 'string') return value
  if (label(value) !== value) return label(value)
  return value.split(' · ').map(part => {
    const decorated = part.match(/^(⏵⏵\s+)?([^()]+?)\s+\(([^()]+)\)$/u)
    if (!decorated) {
      const enclosed = /^\(([^()]+)\)$/.exec(part)
      return enclosed ? '(' + label(enclosed[1]) + ')' : label(part)
    }
    return (decorated[1] ?? '') + label(decorated[2]) + ' (' + label(decorated[3]) + ')'
  }).join(' · ')
}

export function lookup(input) {
  const query = (input ?? '').trim().toLowerCase()
  if (!query) return '输入 /zh-lookup 加英文或中文关键词，例如 /zh-lookup permission。'
  const found = rows.filter(([en, zh]) => en.toLowerCase().includes(query) || zh.toLowerCase().includes(query))
  found.sort(([a, b], [c, d]) => Number(c.toLowerCase() === query || d === query) - Number(a.toLowerCase() === query || b === query))
  const lines = [...new Set(found.map(([en, zh]) => en.trim() + '  →  ' + zh.trim()))]
  if (!lines.length) return '当前词库未收录这段文字。'
  const visible = lines.slice(0, 20)
  if (lines.length > visible.length) visible.push('共 ' + lines.length + ' 条匹配。可输入更长的关键词缩小范围。')
  return visible.join('\n')
}
