# Builds HallowOverlay.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - nothing to install.
#   .\build.ps1          the app
#   .\build.ps1 -Tests   also tests\OverlayTests.exe (1.24: stats formatting, the frame-time graph, position memory, Gear's status.json,
#                        now playing / ad muted, Clips' replay, Hub's notify lines, the game hold, light / dark, the update text,
#                        settings migration, and a test copy over the family pipe). Nothing is shown on screen.
param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'HallowOverlay.exe'
$refs = "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
    "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\System.Runtime.Serialization.dll", "$fw\System.Core.dll", "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Xml.dll", "$fw\System.Xml.Linq.dll", "$fw\System.Security.dll", "$fw\System.Runtime.dll", "$fw\System.Management.dll",
    "$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd", "$env:WINDIR\System32\WinMetadata\Windows.Media.winmd"   # now playing without Mixer (Windows media sessions)
$common = @('/nologo', '/optimize+', "/win32icon:$src\hallow.ico", "/resource:$src\Theme.xaml,HallowOverlay.Theme.xaml") + ($refs | ForEach-Object { "/reference:$_" })
$files = Get-ChildItem $src -Filter *.cs | ForEach-Object FullName
& "$fw\csc.exe" @($common + @('/target:winexe', "/out:$out") + $files)
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
"Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
if ($Tests) {
    $t = Join-Path $PSScriptRoot 'tests\OverlayTests.exe'
    & "$fw\csc.exe" @($common + @('/target:exe', "/out:$t", '/main:HallowOverlay.Tests.Run') + $files + (Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *Tests.cs | ForEach-Object FullName))
    if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
    "Built $t"
}
