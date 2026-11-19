# Hallow Overlay uninstaller (run from Windows Settings > Apps, or directly).
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Overlay'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Overlay?", 'Uninstall Hallow Overlay', 'YesNo', 'Question') -ne 'Yes') { return }
Get-Process HallowOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowOverlay' -ErrorAction SilentlyContinue
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Overlay.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowOverlay' -Recurse -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'HallowOverlay') -Recurse -Force -ErrorAction SilentlyContinue
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show('Hallow Overlay is uninstalled.', 'Hallow Overlay') | Out-Null