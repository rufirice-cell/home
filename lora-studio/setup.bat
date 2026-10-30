@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ===== Settings (change here if needed) =====
set "SD_SCRIPTS_REPO=https://github.com/kohya-ss/sd-scripts.git"
rem Tag / branch / commit of sd-scripts (tested with v0.12.3). Set SD_SCRIPTS_REF=main for the latest.
if not defined SD_SCRIPTS_REF set "SD_SCRIPTS_REF=v0.12.3"
rem PyTorch build for CUDA 12.4 (works with RTX 30/40 series and recent drivers)
set "TORCH_PACKAGES=torch==2.6.0 torchvision==0.21.0"
set "TORCH_INDEX=https://download.pytorch.org/whl/cu124"
rem =============================================

echo.
echo ===== LoRA Studio setup =====
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Git was not found. Install it from https://git-scm.com/download/win
    goto :fail
)

rem --- Find Python 3.10 (recommended) or 3.11 ---
set "PYCMD="
py -3.10 -c "import sys" >nul 2>nul && set "PYCMD=py -3.10"
if not defined PYCMD py -3.11 -c "import sys" >nul 2>nul && set "PYCMD=py -3.11"
if not defined PYCMD python -c "import sys; sys.exit(0 if (3,10) <= sys.version_info[:2] <= (3,11) else 1)" >nul 2>nul && set "PYCMD=python"
if not defined PYCMD (
    echo [ERROR] Python 3.10 was not found. Install Python 3.10.x 64-bit from https://www.python.org/downloads/windows/
    goto :fail
)
echo Using Python: %PYCMD%

rem --- 1. Clone sd-scripts ---
if not exist "sd-scripts\sdxl_train_network.py" (
    echo.
    echo [1/5] Cloning kohya-ss/sd-scripts ...
    git clone "%SD_SCRIPTS_REPO%" sd-scripts
    if errorlevel 1 goto :fail
) else (
    echo [1/5] sd-scripts already exists. Skipping clone.
)
if defined SD_SCRIPTS_REF (
    echo Checking out sd-scripts %SD_SCRIPTS_REF% ...
    git -C sd-scripts fetch --tags origin
    git -C sd-scripts checkout "%SD_SCRIPTS_REF%"
    if errorlevel 1 goto :fail
)

rem --- 2. Create venv ---
set "VPY=%~dp0sd-scripts\venv\Scripts\python.exe"
if not exist "%VPY%" (
    echo.
    echo [2/5] Creating virtual environment ...
    %PYCMD% -m venv "%~dp0sd-scripts\venv"
    if errorlevel 1 goto :fail
) else (
    echo [2/5] Virtual environment already exists.
)
"%VPY%" -m pip install --upgrade pip

rem --- 3. PyTorch (CUDA) ---
echo.
echo [3/5] Installing PyTorch (about 2.5GB download) ...
"%VPY%" -m pip install %TORCH_PACKAGES% --index-url %TORCH_INDEX%
if errorlevel 1 goto :fail

rem --- 4. sd-scripts requirements ---
echo.
echo [4/5] Installing sd-scripts requirements ...
pushd "%~dp0sd-scripts"
"%VPY%" -m pip install --upgrade -r requirements.txt
if errorlevel 1 (
    popd
    goto :fail
)
popd

rem --- 5. WD14 Tagger (ONNX) for auto captioning ---
echo.
echo [5/5] Installing ONNX Runtime for auto captioning ...
"%VPY%" -m pip install onnx onnxruntime-gpu
if errorlevel 1 goto :fail

echo.
echo ===== Checking installation =====
"%VPY%" "%~dp0check_install.py"

echo.
echo Setup finished. Double-click run.bat to start LoRA Studio.
pause
exit /b 0

:fail
echo.
echo [ERROR] Setup failed. Check the messages above.
pause
exit /b 1
