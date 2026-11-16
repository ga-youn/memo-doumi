@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OPTS=/nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu32bitpreferred /win32manifest:app.manifest
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll

if not exist build mkdir build
if not exist assets mkdir assets

if not exist assets\icon.ico (
  "%CSC%" %OPTS% %REFS% /out:build\_iconpass.exe src\*.cs || exit /b 1
  start "" /wait build\_iconpass.exe --export-icon "%~dp0assets\icon.ico"
  del build\_iconpass.exe
)

"%CSC%" %OPTS% %REFS% /win32icon:assets\icon.ico /out:build\MemoDoumi.exe src\*.cs || exit /b 1
if exist assets\sprites xcopy /y /q /i assets\sprites build\sprites >nul
if exist assets\fonts xcopy /y /q /i assets\fonts build\fonts >nul
echo Built build\MemoDoumi.exe
