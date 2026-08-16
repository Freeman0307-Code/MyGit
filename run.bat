@echo off
title MyGit Launcher
rem 定位到本批处理所在目录
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 未找到 dotnet，请安装 .NET 9 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo ============================================
echo   MyGit - 游戏存档式版本管理 (building and launching...)
echo   首次编译稍慢，之后每次启动都会自动增量编译。
echo ============================================

dotnet run --project MyGit.csproj

echo.
echo App exited. Press any key to close this window...
pause >nul
