@echo off
setlocal
cd /d "%~dp0"
if not exist Build mkdir Build
set "APEIRON_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%APEIRON_CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:Build\APEIRON_Global_Mute_Overlay.exe /win32icon:Assets\APEIRON.ico /win32manifest:Source\app.manifest /resource:Assets\APEIRON.ico,APEIRON.ico /resource:Assets\APEIRON_icon.png,APEIRON_icon.png /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll Source\*.cs
if errorlevel 1 exit /b 1
echo Built Build\APEIRON_Global_Mute_Overlay.exe
if /I not "%~1"=="test" exit /b 0
"%APEIRON_CSC%" /nologo /target:exe /platform:x64 /out:Build\Tests.exe /r:Build\APEIRON_Global_Mute_Overlay.exe /r:System.dll /r:System.Core.dll /r:System.Xml.dll Tests\*.cs
if errorlevel 1 exit /b 1
Build\Tests.exe
