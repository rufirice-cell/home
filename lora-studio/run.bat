@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PY=%~dp0sd-scripts\venv\Scripts\python.exe"
if not exist "%PY%" (
    echo [ERROR] sd-scripts\venv was not found. Please run setup.bat first.
    pause
    exit /b 1
)

"%PY%" "%~dp0main.py"
if errorlevel 1 (
    echo.
    echo [ERROR] LoRA Studio exited with an error. See the messages above.
    pause
)
