@echo off
setlocal EnableDelayedExpansion
title Cai dat AdamDrop
cd /d "%~dp0"

rem --- Tu nang quyen quan tri (can de mo cong mang + them rule tuong lua)
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Dang xin quyen quan tri... hay bam YES o hop thoai cua Windows.
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
  exit /b
)

set PORT=8765
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" if not exist "AdamDrop.exe" (
  echo [LOI] May nay khong co csc.exe cua .NET Framework 4 va goi nay cung khong co
  echo       san AdamDrop.exe. Hay bat .NET Framework 4 trong Windows Features roi chay lai.
  pause
  exit /b 1
)

echo [1/6] Dung cac ban dang chay (AdamDrop / iPhoneDrop cu)...
taskkill /im AdamDrop.exe /f >nul 2>&1
taskkill /im iPhoneDrop.exe /f >nul 2>&1

if exist "%CSC%" (
echo [2/6] Bien dich (1 tep exe, toan bo trang web nhung ben trong)...
)
set "RES=/resource:web/index.html,web.index.html /resource:web/dashboard.html,web.dashboard.html /resource:web/qrcode.js,web.qrcode.js /resource:web/logo.png,web.logo.png /resource:web/logo-small.png,web.logo-small.png /resource:web/logo-small-2x.png,web.logo-small-2x.png /resource:web/logo-16.png,web.logo-16.png /resource:web/logo-20.png,web.logo-20.png /resource:web/logo-32.png,web.logo-32.png /resource:web/logo32.png,web.logo32.png /resource:web/logo-180.png,web.logo-180.png /resource:web/logo.ico,web.logo.ico /resource:web/adam-chan-dung.png,web.adam-chan-dung.png"
rem Tep phim tat dung san chua MA KHOA nen khong phat hanh cong khai; co thi nhung, khong co thi bo qua
if exist "web\AdamDrop.shortcut" set "RES=!RES! /resource:web\AdamDrop.shortcut,web.AdamDrop.shortcut"
if exist "web\AdamDrop.auto.shortcut" set "RES=!RES! /resource:web\AdamDrop.auto.shortcut,web.AdamDrop.auto.shortcut"
for %%F in (web\guide\*.png) do set "RES=!RES! /resource:%%F,web.guide.%%~nxF"
if exist "%CSC%" (
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:AdamDrop.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll %RES% AdamDrop.cs
if errorlevel 1 (
  echo [LOI] Bien dich that bai.
  pause
  exit /b 1
)
) else (
echo [2/6] May nay khong co trinh bien dich - dung tep AdamDrop.exe dung san trong goi.
)

echo [3/6] Mo cong %PORT% cho chuong trinh...
netsh http delete urlacl url=http://+:%PORT%/ >nul 2>&1
netsh http add urlacl url=http://+:%PORT%/ sddl="D:(A;;GX;;;WD)" >nul

echo [4/6] Mo tuong lua (mang rieng tu, mien lam viec va ca mang cong cong)...
netsh advfirewall firewall delete rule name="AdamDrop" >nul 2>&1
netsh advfirewall firewall add rule name="AdamDrop" dir=in action=allow protocol=TCP localport=%PORT% profile=private,domain,public >nul

echo [5/6] Don ban cu iPhoneDrop (neu co)...
netsh advfirewall firewall delete rule name="iPhoneDrop" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v iPhoneDrop /f >nul 2>&1
powershell -NoProfile -Command "$s=New-Object -ComObject WScript.Shell; Remove-Item -ErrorAction SilentlyContinue (Join-Path ([Environment]::GetFolderPath('Startup')) 'iPhoneDrop.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'iPhoneDrop.lnk')"

echo [6/6] Cho tu chay khi dang nhap Windows + loi tat Desktop...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v AdamDrop /t REG_SZ /d "\"%~dp0AdamDrop.exe\"" /f >nul
powershell -NoProfile -Command "Remove-Item -ErrorAction SilentlyContinue (Join-Path ([Environment]::GetFolderPath('Startup')) 'AdamDrop.lnk'); $s=New-Object -ComObject WScript.Shell; $l=$s.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'AdamDrop.lnk')); $l.TargetPath='%~dp0AdamDrop.exe'; $l.Arguments='--show'; $l.WorkingDirectory='%~dp0'; $l.Save()"

powershell -NoProfile -Command "$p=Get-NetConnectionProfile | Where-Object {$_.NetworkCategory -eq 'Public' -and $_.IPv4Connectivity -eq 'Internet'}; if($p){ Write-Host ''; Write-Host '  [i] Mang dang o che do Cong cong:' ($p.Name -join ', '); Write-Host '      AdamDrop van hoat dong (da mo ca mang cong cong) nhung chi ai co dung ma khoa moi gui duoc tep.'; Write-Host '' }"

echo.
echo Xong! Dang mo AdamDrop...
rem chay bang quyen nguoi dung thuong (khong phai quyen quan tri)
explorer.exe "%~dp0AdamDrop.exe"
timeout /t 3 >nul
start "" "http://localhost:%PORT%/dashboard"
timeout /t 4 >nul
