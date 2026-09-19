@echo off
rem ============================================================
rem  AdamDrop - build script (khong can quyen quan tri)
rem  Bien dich AdamDrop.cs thanh MOT tep AdamDrop.exe, nhung
rem  toan bo web\ vao trong exe. Chi can .NET Framework 4 co san
rem  trong Windows (Windows 10/11 deu co).
rem ============================================================
setlocal EnableDelayedExpansion
title Build AdamDrop
cd /d "%~dp0"

set PORT=8765
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [LOI] Khong tim thay csc.exe cua .NET Framework 4.
  echo       Windows 10/11 nao cung co san. Neu thieu, bat .NET Framework 4 trong
  echo       Control Panel > Programs > Turn Windows features on or off.
  pause
  exit /b 1
)

rem --- Dung ban dang chay (tep exe bi khoa khi dang chay)
taskkill /im AdamDrop.exe /f >nul 2>&1

echo [1/2] Bien dich...
set "RES=/resource:web/index.html,web.index.html /resource:web/dashboard.html,web.dashboard.html /resource:web/qrcode.js,web.qrcode.js /resource:web/logo.png,web.logo.png /resource:web/logo-small.png,web.logo-small.png /resource:web/logo-small-2x.png,web.logo-small-2x.png /resource:web/logo-16.png,web.logo-16.png /resource:web/logo-20.png,web.logo-20.png /resource:web/logo-32.png,web.logo-32.png /resource:web/logo32.png,web.logo32.png /resource:web/logo-180.png,web.logo-180.png /resource:web/logo.ico,web.logo.ico /resource:web/adam-chan-dung.png,web.adam-chan-dung.png"
rem Tep phim tat dung san chua MA KHOA nen khong nam trong repo; co thi nhung, khong co thi thoi
if exist "web\AdamDrop.shortcut" set "RES=!RES! /resource:web\AdamDrop.shortcut,web.AdamDrop.shortcut"
if exist "web\AdamDrop.auto.shortcut" set "RES=!RES! /resource:web\AdamDrop.auto.shortcut,web.AdamDrop.auto.shortcut"
for %%F in (web\guide\*.png) do set "RES=!RES! /resource:%%F,web.guide.%%~nxF"

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:AdamDrop.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll !RES! AdamDrop.cs

echo [2/2] Kiem tra ket qua...
if not exist "AdamDrop.exe" (
  echo [LOI] Bien dich that bai - xem loi phia tren.
  pause
  exit /b 1
)
for %%A in (AdamDrop.exe) do echo Xong: %%A  %%~zA byte
exit /b 0
