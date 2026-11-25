import { commandDescription, coverage } from './menus.js'
import { hint, label, lookup, spinner } from './translate.js'

export function register(on) {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'zh-lookup',
      description: '查询界面用语的中英文对照',
      argumentHint: '[英文或中文关键词]'
    })
    await $.command.register({ name: 'zh-coverage', description: '检查当前版本的内置命令和设置标签翻译覆盖率' })
    return next(e)
  })

  on('command.run', { command: 'zh-lookup' }, async ($, e) => ({ text: lookup(e.args) }))

  on('command.run', { command: 'zh-coverage' }, async ($) => {
    return { text: coverage(await $.command.list(), await $.config.list()) }
  })

  on('command.describe', async ($, e, next) => {
    if (e.provider.plugin !== 'engine') return next(e)
    const description = commandDescription(e.command, e.description)
    return description === e.description ? next(e) : next({ ...e, description })
  })

  on('config.describe', async ($, e, next) => {
    if (e.provider.plugin !== 'engine') return next(e)
    const name = label(e.label)
    const description = label(e.description)
    if (name === e.label && description === e.description) return next(e)
    return next({ ...e, label: name, description })
  })

  on('prompt.context', async ($, e, next) => {
    return next({ ...e, blocks: [...e.blocks, {
      name: 'zhUiLanguage',
      text: '默认使用简体中文回复和提问。遵循用户明确指定的语言。保留命令、路径、代码、标识符和引文的原文。'
    }] })
  })

  on('ui.render', { component: 'Spinner' }, async ($, e, next) => {
    const { word, message } = e.props
    const translated = spinner(word)
    const detail = label(message)
    if (translated === word && detail === message) return next(e)
    return next({ ...e, props: { ...e.props, word: translated, message: detail } })
  })

  on('ui.render', { component: 'TurnDuration' }, async ($, e, next) => {
    const word = label(e.props.word)
    if (word === e.props.word) return next(e)
    return next({ ...e, props: { ...e.props, word } })
  })

  on('ui.render', { component: 'InfoNotice' }, async ($, e, next) => {
    const text = label(e.props.text)
    return text === e.props.text ? next(e) : next({ ...e, props: { ...e.props, text } })
  })

  on('ui.render', { component: 'PromptHint' }, async ($, e, next) => {
    const translated = hint(e.props.hint)
    if (translated === e.props.hint) return next(e)
    // Appending the translation leaves the terminal's original clickable pills active.
    const props = e.surface === 'terminal'
      ? { ...e.props, tail: [e.props.tail, translated].filter(Boolean).join(' · ') }
      : { ...e.props, hint: translated }
    return next({ ...e, props })
  })

  on('ui.render', { component: 'SessionMode' }, async ($, e, next) => {
    const modes = e.props.modes.map(label)
    if (modes.every((mode, i) => mode === e.props.modes[i])) return next(e)
    return next({ ...e, props: { ...e.props, modes } })
  })

  on('ui.render', { component: 'CommandOutput' }, async ($, e, next) => {
    if (!['help', 'config', 'status', 'usage', 'model', 'theme'].includes(e.props.command)) return next(e)
    const text = e.props.text.split('\n').map(label).join('\n')
    return text === e.props.text ? next(e) : next({ ...e, props: { ...e.props, text } })
  })
}
