@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK 8 was not found on PATH.
    exit /b 1
)

dotnet restore AudioConverter.sln
if errorlevel 1 exit /b 1

dotnet test AudioConverter.sln -c Release --no-restore
if errorlevel 1 exit /b 1

dotnet publish src\AudioConverter.Desktop\AudioConverter.Desktop.csproj -c Release -r win-x64 --self-contained false -o release\codectone-lightweight -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 exit /b 1

dotnet publish src\AudioConverter.Desktop\AudioConverter.Desktop.csproj -c Release -r win-x64 --self-contained true -o release\codectone-standalone -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 exit /b 1

if not exist "release\final" mkdir "release\final"
copy /y "release\codectone-lightweight\CodecTone.exe" "release\final\CodecTone.exe" >nul
if errorlevel 1 exit /b 1
copy /y "release\codectone-standalone\CodecTone.exe" "release\final\CodecTone-Standalone.exe" >nul
if errorlevel 1 exit /b 1

if exist "release\final\AudioConverter.exe" del /q "release\final\AudioConverter.exe"
if exist "release\final\AudioConverter-Standalone.exe" del /q "release\final\AudioConverter-Standalone.exe"

dotnet publish src\AudioConverter.Cli\AudioConverter.Cli.csproj -c Release -r win-x64 --self-contained true -o release\cli -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 exit /b 1
if exist "release\cli\AudioConverter.Cli.exe" del /q "release\cli\AudioConverter.Cli.exe"

rem Signs the executables only when a certificate is configured; see docs\distribution.md.
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\sign-release.ps1"
if errorlevel 1 exit /b 1

echo Built lightweight GUI: %~dp0release\final\CodecTone.exe
echo Built standalone GUI: %~dp0release\final\CodecTone-Standalone.exe
echo Built CLI: %~dp0release\cli\CodecTone.Cli.exe
exit /b 0
