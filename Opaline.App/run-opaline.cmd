@echo off
cd /d "%~dp0"
echo [%date% %time%] launcher start> crash.log
echo dir=%cd%>> crash.log
dir /b Microsoft.WindowsAppRuntime*.dll Microsoft.WinUI.dll Opaline.App.exe >> crash.log 2>&1
echo.>> crash.log
echo Launching Opaline.App.exe ...>> crash.log
Opaline.App.exe
set ERR=%ERRORLEVEL%
echo.>> crash.log
echo [%date% %time%] exit code %ERR%>> crash.log
echo Exit code: %ERR%
echo See crash.log in this folder.
pause
