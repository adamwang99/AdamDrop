@echo off
cd /d "%~dp0"
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
taskkill /im AdamDrop.exe /f >nul 2>&1
taskkill /im iPhoneDrop.exe /f >nul 2>&1
netsh http delete urlacl url=http://+:8765/ >nul 2>&1
netsh advfirewall firewall delete rule name="AdamDrop" >nul 2>&1
netsh advfirewall firewall delete rule name="iPhoneDrop" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v AdamDrop /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v iPhoneDrop /f >nul 2>&1
powershell -NoProfile -Command "Remove-Item -ErrorAction SilentlyContinue (Join-Path ([Environment]::GetFolderPath('Startup')) 'AdamDrop.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'AdamDrop.lnk'), (Join-Path ([Environment]::GetFolderPath('Startup')) 'iPhoneDrop.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'iPhoneDrop.lnk')"
echo Da go AdamDrop (cong, tuong lua, khoi dong cung Windows, loi tat). Co the xoa thu muc nay.
pause
