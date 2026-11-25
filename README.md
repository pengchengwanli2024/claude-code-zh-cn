# Claude Code 简体中文界面

zh-ui 把 Claude Code 的内置命令说明、设置标签和部分运行状态翻译成简体中文。输入 `/config`，会看到「自动压缩上下文」「默认权限模式」「自动更新渠道」等设置。输入斜杠搜索命令时，名称仍可照常输入，说明以中文显示。插件也会请 Claude 默认用中文回复，用户可在对话中指定其他语言。

## Windows 桌面版

下载并解压仓库源码，打开解压后的目录，在 PowerShell 中运行安装脚本。安装程序在当前用户目录编译中文显示工具，并在桌面和开始菜单创建「Claude Code 中文版」。双击快捷方式即可打开官方 Claude；进入 Code 页面后，可直接点击中文标签操作。系统托盘菜单可以暂停、恢复或退出中文显示。

```powershell
.\desktop\Install.ps1
```

要启用新按钮和菜单项的本机机译，安装 Python 3.12 后运行 `.\desktop\Install.ps1 -OfflineTranslation`。此选项首次下载推理库和约 71 MB 的模型。标有「≈」的标签由本机模型翻译，可从托盘暂停显示以核对原文。未命中词库的无障碍操作标签按类别和文字筛选后送入本机模型。

桌面工具通过 Windows 无障碍接口读取当前 Claude 窗口中可见的控件名称，在文字位置绘制可点击穿透的中文层。官方签名程序由原更新器维护。已在 Windows MSIX 版 Claude 6.28899.6.0 的 Code 页面验证「上下文」「系统工具」「可用空间」等标签。桌面词库收录 28,051 个界面原文指纹。条目数无法推算覆盖百分比。图像文字、未暴露给无障碍接口的控件、部分弹窗、动态内容和新版本文案可能显示原文。界面缩放及控件宽度会影响译文显示。

卸载桌面显示工具可运行 `.\desktop\Install.ps1 -Uninstall`。如果另外安装了离线翻译，其 Python 环境和模型保存在 `%LOCALAPPDATA%\zh-ui`，可留给终端对照窗口复用。

## 从 GitHub 安装终端插件

在终端执行以下命令，然后重新启动 Claude Code。Windows 离线对照窗口需要另外下载仓库源码，安装步骤见下文。

```shell
claude plugin marketplace add pengchengwanli2024/claude-code-zh-cn
claude plugin install zh-ui@zh-ui-community
```

## 试用插件

从本仓库根目录启动一次 Claude Code。已验证版本为 2.1.332，依赖官方 Mods 接口。

```shell
claude --plugin-dir ./plugins/zh-ui
```

会话内运行 `/zh-coverage`，可查看当前环境中内置命令和设置标签的覆盖数量。运行 `/zh-lookup Yes, for this session`，会显示「是，本次会话允许」。使用这两个命令无需调用模型。

## 安装与卸载

在仓库根目录执行以下命令，然后重新启动 Claude Code。插件管理器会将插件安装到用户范围。

```shell
claude plugin marketplace add ./
claude plugin install zh-ui@zh-ui-community
```

更新源码后运行 `claude plugin marketplace update zh-ui-community` 和 `claude plugin update zh-ui@zh-ui-community`，重启会话生效。移除插件用 `claude plugin uninstall zh-ui@zh-ui-community`，移除本地市场用 `claude plugin marketplace remove zh-ui-community`。临时试用时，直接用普通的 `claude` 启动下一次会话即可。

## Windows 原生弹窗与新文字

权限对话框、首次启动引导和部分标签由 Claude Code 自己绘制。Windows 对照窗口会读取同一终端当前可见的文字，先查本地词库。启用离线模型后，未收录的含英文字母的行或分栏也会显示原文和标为「机译」的中文。可将两个窗口并排使用，键盘输入和选项确认都在 Claude Code 原窗口完成。关闭对照窗口后，终端会话可继续使用。

仅使用词库需要 Windows 原生 `claude.exe` 和带 Tk 的 Python。以下命令从仓库根目录运行，示例使用 Python 3.12。

```powershell
py -3.12 windows/claude_zh.py --dictionary-only
```

要翻译新版本中尚未收录的屏幕文字，首次运行以下四行。第一行建立独立 Python 环境，第二行安装推理库，第三行下载并校验约 71 MB 的英译中模型。以后只需执行最后一行。也可以在任意工作目录以绝对路径启动脚本，让 Claude Code 在该目录工作。

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r windows/requirements-mt.txt
.\.venv\Scripts\python.exe windows/prepare_model.py
.\.venv\Scripts\python.exe windows/claude_zh.py
```

若终端未找到可执行文件，在启动命令后附加 `--claude "C:\实际安装位置\claude.exe"`。对照窗口需要支持 Windows 控制台缓冲区的终端。模型存放在 `%LOCALAPPDATA%\zh-ui\translate-en_zh-1_9`，可通过 `--model` 指定其他已解包的同款模型。推理在本机 CPU 上完成，屏幕内容仅在进程内使用。词库、模型和插件分别维护。

## 覆盖范围

| 界面 | 工作方式 |
| --- | --- |
| 斜杠命令菜单、帮助中的内置命令说明、设置行标签 | 官方菜单接口直接显示中文。动态模型名沿用当前值 |
| 等待动画、回合耗时的动词、模式标签、提示、通知、部分命令输出 | 渲染时翻译命中的文字。终端操作提示追加中文以保留原有可点击控件，窄窗口可能截断 |
| 权限框、首次启动、固定页签、设置选项值、其他英文 | Windows 对照窗口提供旁译。权限判断以 Claude Code 原文、命令和路径为准 |
| Claude 的自然语言回复 | 默认请求使用简体中文。模型行为由会话和上级配置决定 |
| Windows 桌面版 Code 页面 | 独立中文层覆盖词库命中的可见控件。离线机译可补充部分新按钮和菜单项 |
| IDE 自身菜单、浏览器登录页、图片里的文字 | 沿用各自应用的显示语言 |

在 Windows 的 Claude Code 2.1.332 交互会话中，覆盖检查得到 **87/87 条内置命令说明**和 **39/39 个设置标签**。词库有 **1,555 个键**，包含同义项和不同界面复用的文字。以上数字衡量各自的集合。其他账号、平台、功能开关和后续版本可能提供不同的菜单。旁译按可见行处理，跨行断句、代码、专有名词和机译准确性均有局限。

## 升级与验证

跨版本验证使用了一个隔离的用户配置目录：先在 2.1.331 安装市场和插件，再用 2.1.332 读取同一目录。两个版本都成功执行了查词与覆盖检查，期间无需重装插件。

项目使用 Claude Code 的插件市场和官方事件接口。安装目录、原生程序和更新器由 Claude Code 管理。遇到不认识的命令说明或界面文字时，插件保留原文。升级后运行覆盖检查可列出待补译的命令和设置。

```shell
claude plugin validate --strict .
claude plugin validate --strict ./plugins/zh-ui
claude plugin test ./plugins/zh-ui
python -m unittest discover -s windows -v
```

贡献界面译文可编辑 `plugins/zh-ui/locales/zh-CN.js`，每个键对应一对英文和中文。它是以 ES 模块导出的 JSON 字面量，插件与 Windows 对照程序读取同一份数据。菜单专用摘要在 `commands.js`，命令和已审阅英文说明的指纹同时匹配时使用。复现漏译请附上 Claude Code 版本、界面名称和去除个人信息的原文。

## 来源和许可证

本项目采用 MIT 许可证。基础词库沿用 cc-i18n 的英文技术版与简体中文技术版，固定提交为 `cab6f7e02be49d5228050fa16fc5dcf14b3a3cf3`。本项目补充了当前版本菜单、设置和启动页译文，保留上游 MIT 声明。插件本身是独立实现。[上游词库](https://github.com/joshchaotang/claude-code-i18n/tree/cab6f7e02be49d5228050fa16fc5dcf14b3a3cf3/src/translations) · [第三方许可证](notices/cc-i18n-LICENSE.txt)

桌面词库来自 MIT 许可的 zh-ui-helper，固定提交为 `da587c5633c8a853a0ea83e9324df8ee12c00e6a`。使用该版本的 `client-patch/locales` 目录运行 `python desktop/build_catalog.py <locales目录>`，即可按指纹重建词库。补充桌面译文可编辑 `desktop/labels-extra.json`。[上游桌面词库](https://github.com/joyfoxai/zh-ui-helper/tree/da587c5633c8a853a0ea83e9324df8ee12c00e6a/client-patch/locales) · [上游许可](notices/desktop-catalog-LICENSE.txt) · [原作者许可](notices/javaht-LICENSE.txt)

这是社区插件。官方扩展能力和权限框的限制见 [Claude Code 界面文档](https://code.claude.com/docs/en/plugins/mods/interface)。离线模型由用户从 Argos 下载，模型自带说明随安装保存在缓存目录。[Argos 模型索引](https://github.com/argosopentech/argospm-index)
