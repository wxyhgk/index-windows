@echo off
setlocal
cd /d "%~dp0"

REM === 代理设置（网络不通时取消注释或改为 1）===
set USE_PROXY=1
set PROXY=http://127.0.0.1:7890

if "%USE_PROXY%"=="1" (
    echo [Proxy] Using %PROXY%
    set HTTP_PROXY=%PROXY%
    set HTTPS_PROXY=%PROXY%
    set http_proxy=%PROXY%
    set https_proxy=%PROXY%
    REM git 代理
    git config --global http.proxy %PROXY%
    git config --global https.proxy %PROXY%
)

REM === 1. Clone MolGrapher 仓库（如果不存在）===
if not exist "MolGrapher\setup.py" (
    echo [1/5] Cloning MolGrapher repository...
    git clone https://github.com/DS4SD/MolGrapher.git
    if errorlevel 1 (
        echo Failed to clone MolGrapher. Check your network or proxy settings.
        pause
        exit /b 1
    )
)

REM === 2. 创建虚拟环境并安装依赖 ===
if not exist "venv\Scripts\activate.bat" (
    echo [2/5] Creating Python virtual environment...
    python -m venv venv
    call venv\Scripts\activate.bat
    echo [3/5] Installing dependencies (first run may take 5-10 minutes)...
    pip install -r requirements.txt
    pip install -e ./MolGrapher
    if errorlevel 1 (
        echo Failed to install dependencies.
        pause
        exit /b 1
    )
) else (
    call venv\Scripts\activate.bat
)

REM === 3. 下载模型（首次约 455MB）===
echo [4/5] Checking model files...
python download_models.py
if errorlevel 1 (
    echo Model download failed.
    pause
    exit /b 1
)

REM === 4. 启动服务 ===
echo [5/5] Starting MolGrapher Service on http://127.0.0.1:8100
echo.
echo   Health:  curl http://127.0.0.1:8100/health
echo   Test:    curl -X POST http://127.0.0.1:8100/recognize -F "file=@test.png"
echo.
python app.py
pause
