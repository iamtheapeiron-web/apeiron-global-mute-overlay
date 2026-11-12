@echo off
setlocal
cd /d "%~dp0"
set "APEIRON_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
call :build EN ""
if errorlevel 1 exit /b 1
call :build RU /define:APEIRON_RU
if errorlevel 1 exit /b 1
if /I not "%~1"=="test" exit /b 0
call :test EN
if errorlevel 1 exit /b 1
call :test RU
exit /b %errorlevel%

:build
if not exist Build\%~1 mkdir Build\%~1
"%APEIRON_CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ %~2 /out:Build\%~1\APEIRON_Global_Mute_Overlay.exe /win32icon:Assets\APEIRON.ico /win32manifest:Source\app.manifest /resource:Assets\APEIRON.ico,APEIRON.ico /resource:Assets\APEIRON_icon.png,APEIRON_icon.png /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll Source\*.cs
if errorlevel 1 exit /b 1
echo Built Build\%~1\APEIRON_Global_Mute_Overlay.exe
exit /b 0

:test
"%APEIRON_CSC%" /nologo /codepage:65001 /target:exe /platform:x64 /out:Build\%~1\Tests.exe /r:Build\%~1\APEIRON_Global_Mute_Overlay.exe /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Windows.Forms.dll Tests\*.cs
if errorlevel 1 exit /b 1
Build\%~1\Tests.exe
exit /b %errorlevel%
