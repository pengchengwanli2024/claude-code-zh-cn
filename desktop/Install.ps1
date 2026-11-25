[CmdletBinding()]
param([switch]$Uninstall, [switch]$OfflineTranslation)
$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'zh-ui\desktop-0.1.0'
$exe = Join-Path $installDir 'DesktopZh.exe'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Claude Code 中文版.lnk'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Code 中文版.lnk'
function Stop-OwnViewer {
    Get-Process -Name DesktopZh -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exe } | ForEach-Object { $_.Kill(); $_.WaitForExit() }
}
if ($Uninstall) {
    Stop-OwnViewer
    foreach ($link in @($desktopLink, $startLink)) {
        if ((Test-Path -LiteralPath $link) -and ((New-Object -ComObject WScript.Shell).CreateShortcut($link).TargetPath -eq $exe)) {
            Remove-Item -LiteralPath $link
        }
    }
    foreach ($name in @('DesktopZh.exe','labels.json.gz','labels-extra.json','offline.py','translate_controls.py')) {
        $file = Join-Path $installDir $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    return
}
if (!(Get-AppxPackage -Name Claude)) { throw '未找到已注册的 Claude 桌面应用。' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '需要 Windows 自带的 .NET Framework 4.x。' }
$buildDir = Join-Path ([IO.Path]::GetTempPath()) ('zh-ui-build-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($buildDir) | Out-Null
$refs = @('System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','WPF\UIAutomationClient.dll',
          'WPF\UIAutomationTypes.dll','WPF\WindowsBase.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$artifact = Join-Path $buildDir 'DesktopZh.exe'
try {
    & $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$artifact" @refs (Join-Path $PSScriptRoot 'DesktopZh.cs') (Join-Path $PSScriptRoot 'ControlTranslator.cs') (Join-Path $PSScriptRoot 'NativeCatalog.cs')
    if ($LASTEXITCODE) { throw '无法编译桌面中文显示程序。' }
    [IO.Directory]::CreateDirectory($installDir) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    foreach ($link in @($desktopLink, $startLink)) {
        if ((Test-Path -LiteralPath $link) -and ($shell.CreateShortcut($link).TargetPath -ne $exe)) {
            throw "同名快捷方式已存在，请先重命名：$link"
        }
    }
    if ($OfflineTranslation) {
        $python = Join-Path $env:LOCALAPPDATA 'zh-ui\mt-runtime\Scripts\python.exe'
        if (!(Test-Path -LiteralPath $python)) {
            py -3.12 -m venv (Join-Path $env:LOCALAPPDATA 'zh-ui\mt-runtime')
            if ($LASTEXITCODE) { throw '无法创建 Python 3.12 离线翻译环境。' }
        }
        & $python -m pip install -r (Join-Path $PSScriptRoot '..\windows\requirements-mt.txt')
        if ($LASTEXITCODE) { throw '无法安装离线翻译依赖。' }
        & $python (Join-Path $PSScriptRoot '..\windows\prepare_model.py')
        if ($LASTEXITCODE) { throw '无法安装离线模型。' }
    }
    Stop-OwnViewer
    Copy-Item -LiteralPath $artifact -Destination $exe -Force
    foreach ($name in @('labels.json.gz','labels-extra.json','translate_controls.py')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installDir $name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\windows\offline.py') -Destination (Join-Path $installDir 'offline.py') -Force
    foreach ($path in @($desktopLink,$startLink)) {
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = $exe
        $link.Arguments = '--launch'
        $link.Description = '启动官方 Claude 并显示中文界面标签'
        $link.IconLocation = (Join-Path (Get-AppxPackage Claude).InstallLocation 'app\claude.exe') + ',0'
        $link.Save()
    }
    Start-Process -FilePath $exe -WindowStyle Hidden
    Write-Output $desktopLink
} finally {
    if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact }
    if ([IO.Directory]::Exists($buildDir)) { [IO.Directory]::Delete($buildDir, $false) }
}
