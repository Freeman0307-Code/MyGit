@echo off
title MyGit Publish (self-contained single file)
rem 发布自包含单文件版本：目标电脑无需安装任何 .NET / C# 环境，直接双击 MyGit.exe 运行
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 未找到 dotnet，请安装 .NET 9 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo ============================================
echo   MyGit: publishing self-contained win-x64...
echo   Output: publish\win-x64\MyGit.exe
echo ============================================
dotnet publish MyGit.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
    -o publish\win-x64
if errorlevel 1 (
    echo.
    echo [PUBLISH FAILED] 发布失败，请检查上方错误信息。
    pause
    exit /b 1
)
rem 清理调试符号与文档文件，发布目录只留可运行文件
del /q "publish\win-x64\*.pdb" "publish\win-x64\*.xml" 2>nul
echo.
echo ============================================
echo   Publish OK: publish\win-x64\MyGit.exe
echo   把整个 publish\win-x64 文件夹拷到任何 Windows
echo   x64 电脑上，双击 MyGit.exe 即可运行（免安装）。
echo ============================================
pause
