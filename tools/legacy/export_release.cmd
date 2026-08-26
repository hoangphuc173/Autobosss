@echo off
chcp 65001 >nul
echo ===================================================
echo     DONG GOI TOAN BO GAME + TOOL SANG MAY KHAC
echo ===================================================
echo.

set "DEST=%USERPROFILE%\Desktop\AutoBoss_Release_Pack"
if exist "%DEST%" (
    echo [!] Dang xoa thu muc Release cu tren Desktop...
    rmdir /s /q "%DEST%"
)

echo [+] Dang copy toan bo thu muc Game hien tai sang Desktop...
echo     Viec nay co the mat vai chuc giay. Vui long cho...
xcopy /E /I /H /Y "%~dp0*" "%DEST%" >nul

echo [+] Dang don dep ma nguon va file thua de giam dung luong...
if exist "%DEST%\source" rmdir /s /q "%DEST%\source"
if exist "%DEST%\PyCaptcha\.venv" rmdir /s /q "%DEST%\PyCaptcha\.venv"
if exist "%DEST%\PyCaptcha\build" rmdir /s /q "%DEST%\PyCaptcha\build"
if exist "%DEST%\PyCaptcha\src" rmdir /s /q "%DEST%\PyCaptcha\src"
if exist "%DEST%\.git" rmdir /s /q "%DEST%\.git"
if exist "%DEST%\build.cmd" del /f /q "%DEST%\build.cmd"
if exist "%DEST%\export_release.cmd" del /f /q "%DEST%\export_release.cmd"
if exist "%DEST%\PyCaptcha\build_exe.cmd" del /f /q "%DEST%\PyCaptcha\build_exe.cmd"

echo.
echo ===================================================
echo HOAN TAT!
echo Toan bo GAME + TOOL doc lap da nam o thu muc:
echo %DEST%
echo.
echo Ban chi viec copy THU MUC NAY sang bat ky may tinh nao.
echo Tren may do chi can vao thu muc va chay file game, khong can cai them bat cu phan mem nao khac!
echo ===================================================
pause
