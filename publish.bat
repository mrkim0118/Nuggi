@echo off
setlocal
cd /d "%~dp0"

set "PUBLISH_DIR=%CD%\publish\Nuggi-win-x64"
set "ZIP_PATH=%CD%\publish\Nuggi-win-x64.zip"
set "USER_MODEL_DIR=%USERPROFILE%\.u2net"

if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)

if exist "%ZIP_PATH%" (
    del /q "%ZIP_PATH%"
)

echo [1/6] Publishing WinForms app...
dotnet publish Nuggi.csproj -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" -v minimal
if errorlevel 1 goto :error

echo [2/6] Copying Python virtual environment...
robocopy ".venv" "%PUBLISH_DIR%\.venv" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error

echo [3/6] Copying Python helper files...
robocopy "scripts" "%PUBLISH_DIR%\scripts" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error
robocopy "nuggi" "%PUBLISH_DIR%\nuggi" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error
copy /Y "requirements.txt" "%PUBLISH_DIR%\requirements.txt" >nul
copy /Y "README.md" "%PUBLISH_DIR%\README.md" >nul

echo [4/6] Copying bundled model cache when available...
if exist "%USER_MODEL_DIR%" (
    robocopy "%USER_MODEL_DIR%" "%PUBLISH_DIR%\.u2net" /E >nul
    if errorlevel 8 goto :error
)

echo [5/6] Creating launcher...
(
    echo @echo off
    echo setlocal
    echo cd /d "%%~dp0"
    echo start "" ".\Nuggi.exe"
) > "%PUBLISH_DIR%\Launch Nuggi.bat"

echo [6/6] Creating zip archive...
tar.exe -a -cf "%ZIP_PATH%" -C "%PUBLISH_DIR%" .
if errorlevel 1 goto :error

echo Publish complete.
echo Folder: %PUBLISH_DIR%
echo Zip: %ZIP_PATH%
exit /b 0

:error
echo Publish failed.
exit /b 1
