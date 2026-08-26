@echo off
echo === Building AutoBossGrabber ===
cd /d "%~dp0\source"
dotnet build AutoBossGrabber.csproj -c Release
if errorlevel 1 goto end
echo.
echo === Copying DLL to game plugins ===
REM Deploy vao dung thu muc game co BepInEx (chinh la repo root nay).
REM Dung duong dan tuong doi %~dp0 de KHONG bao gio tro nham vao folder khong ton tai.
set "PLUGINDIR=%~dp0BepInEx\plugins"
if not exist "%PLUGINDIR%" (
  echo FAIL: plugin folder not found: "%PLUGINDIR%"
  echo   -> Kiem tra lai game co cai BepInEx chua.
  goto end
)
copy /Y "bin\Release\net6.0\AutoBossGrabber.dll" "%PLUGINDIR%\AutoBossGrabber.dll"
if errorlevel 1 (
  echo FAIL: cannot copy DLL to "%PLUGINDIR%"
  echo   -> Dam bao game DA DONG (khong bi khoa file DLL).
  goto end
)
echo.
echo === DONE ===
echo Deployed to: "%PLUGINDIR%\AutoBossGrabber.dll"
echo Run the game. Press F2 to dump UI panels.
:end
pause