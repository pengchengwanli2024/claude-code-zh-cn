$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll',
    'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'WPF\WindowsBase.dll') |
    ForEach-Object { '/r:' + (Join-Path $framework $_) }
$directory = Join-Path ([IO.Path]::GetTempPath()) ('zh-ui-tests-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($directory) | Out-Null
$exe = Join-Path $directory 'Tests.exe'
$fixture = Join-Path $directory 'fixture.asar'
try {
    $sources = @('DesktopZh.cs','ControlTranslator.cs','NativeCatalog.cs','tests\DesktopTests.cs') |
        ForEach-Object { Join-Path $PSScriptRoot $_ }
    & (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /target:exe /main:DesktopTests "/out:$exe" @references @sources
    if ($LASTEXITCODE) { throw 'Compile failed.' }
    & $exe $fixture
    if ($LASTEXITCODE) { throw 'Desktop tests failed.' }
} finally {
    foreach ($file in @($fixture,$exe)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    [IO.Directory]::Delete($directory, $false)
}
