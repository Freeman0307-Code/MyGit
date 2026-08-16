@echo off
title MyGit Build
rem 定位到本批处理所在目录
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 未找到 dotnet，请安装 .NET 9 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo ============================================
echo   MyGit: building...
echo ============================================
dotnet build MyGit.csproj -c Debug
if errorlevel 1 (
    echo.
    echo [BUILD FAILED] 编译失败，请检查上方错误信息。
    pause
    exit /b 1
)
echo.
echo Build OK.
pause
