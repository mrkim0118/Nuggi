@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo [1/4] Creating Python virtual environment...
    py -3 -m venv .venv 2>nul
    if errorlevel 1 python -m venv .venv
    if errorlevel 1 goto :error
)

".venv\Scripts\python.exe" -c "import rembg, PIL, onnxruntime" >nul 2>&1
if errorlevel 1 (
    echo [2/4] Installing Python requirements...
    ".venv\Scripts\python.exe" -m pip install -r requirements.txt
    if errorlevel 1 goto :error
) else (
    echo [2/4] Python requirements already installed.
)

echo [3/4] Building WinForms app...
dotnet build Nuggi.csproj -c Debug -v minimal
if errorlevel 1 goto :error

echo [4/4] Launching Nuggi...
start "" ".\bin\Debug\net8.0-windows\Nuggi.exe"
exit /b 0

:error
echo Failed to launch Nuggi.
pause
exit /b 1
