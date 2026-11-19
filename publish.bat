@echo off
setlocal
cd /d "%~dp0"

set "PUBLISH_DIR=%CD%\publish\Nuggi-win-x64"
set "ZIP_PATH=%CD%\publish\Nuggi-win-x64.zip"
set "USER_MODEL_DIR=%USERPROFILE%\.u2net"

if not exist ".venv\Scripts\python.exe" (
    echo .venv was not found. Run run.bat once first.
    goto :error
)

if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)

if exist "%ZIP_PATH%" (
    del /q "%ZIP_PATH%"
)

echo [1/7] Publishing WinForms app...
dotnet publish Nuggi.csproj -c Release -r win-x64 --self-contained false -o "%PUBLISH_DIR%" -v minimal -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 goto :error

if exist "%PUBLISH_DIR%\*.pdb" del /q "%PUBLISH_DIR%\*.pdb"

echo [2/7] Copying portable Python runtime...
rem A copied .venv only works where its base Python is installed, so ship the base Python itself plus the venv packages.
for /f "usebackq delims=" %%P in (`.venv\Scripts\python.exe -c "import sys; print(sys.base_prefix)"`) do set "PYTHON_HOME=%%P"
if not defined PYTHON_HOME goto :error
robocopy "%PYTHON_HOME%" "%PUBLISH_DIR%\python" /E /XD "%PYTHON_HOME%\Lib\site-packages" "%PYTHON_HOME%\Lib\test" "%PYTHON_HOME%\Lib\idlelib" "%PYTHON_HOME%\Lib\tkinter" "%PYTHON_HOME%\tcl" "%PYTHON_HOME%\Doc" "%PYTHON_HOME%\include" "%PYTHON_HOME%\libs" "%PYTHON_HOME%\Tools" "%PYTHON_HOME%\Scripts" "__pycache__" >nul
if errorlevel 8 goto :error
robocopy ".venv\Lib\site-packages" "%PUBLISH_DIR%\python\Lib\site-packages" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error

echo [3/7] Copying Python helper files...
robocopy "scripts" "%PUBLISH_DIR%\scripts" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error
robocopy "nuggi" "%PUBLISH_DIR%\nuggi" /E /XD "__pycache__" >nul
if errorlevel 8 goto :error
copy /Y "requirements.txt" "%PUBLISH_DIR%\requirements.txt" >nul
copy /Y "README.md" "%PUBLISH_DIR%\README.md" >nul
copy /Y "README.ko.md" "%PUBLISH_DIR%\README.ko.md" >nul

echo [4/7] Copying bundled model cache when available...
if exist "%USER_MODEL_DIR%" (
    robocopy "%USER_MODEL_DIR%" "%PUBLISH_DIR%\.u2net" *.onnx >nul
    if errorlevel 8 goto :error
)

echo [5/7] Creating launcher...
(
    echo @echo off
    echo setlocal
    echo cd /d "%%~dp0"
    echo start "" ".\Nuggi.exe"
) > "%PUBLISH_DIR%\Launch Nuggi.bat"

echo [6/7] Creating zip archive...
tar.exe -a -cf "%ZIP_PATH%" -C "%PUBLISH_DIR%" .
if errorlevel 1 goto :error

echo [7/7] Creating installer...
set "ISCC="
for %%I in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do (
    if not defined ISCC if exist "%%~I" set "ISCC=%%~I"
)
if not defined ISCC (
    echo Inno Setup 6 was not found, so the installer was skipped. Install it from https://jrsoftware.org/isdl.php
    goto :done
)
for /f "usebackq delims=" %%V in (`dotnet msbuild Nuggi.csproj -getProperty:Version`) do set "APP_VERSION=%%V"
rem The installer refuses install folders too long for the deepest package file (Windows 260-character path limit).
for /f "usebackq delims=" %%L in (`powershell -NoProfile -Command "$d = '%PUBLISH_DIR%'; (Get-ChildItem -LiteralPath $d -Recurse -File | ForEach-Object { $_.FullName.Length - $d.Length - 1 } | Measure-Object -Maximum).Maximum"`) do set "MAX_REL_PATH=%%L"
if not defined MAX_REL_PATH goto :error
"%ISCC%" /Q /DAppVersion=%APP_VERSION% /DMaxRelPath=%MAX_REL_PATH% "installer\Nuggi.iss"
if errorlevel 1 goto :error
echo Installer: %CD%\publish\Nuggi-Setup-%APP_VERSION%.exe

:done
echo Publish complete.
echo Folder: %PUBLISH_DIR%
echo Zip: %ZIP_PATH%
exit /b 0

:error
echo Publish failed.
exit /b 1
