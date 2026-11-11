@echo off
setlocal
cd /d "%~dp0"

if not exist "libs-source\libs\net6\MelonLoader.dll" (
  echo Libraries submodule is missing.
  echo Run: git submodule update --init --recursive
  exit /b 1
)

dotnet build PvzFmimic.csproj --configuration Release --verbosity minimal
if errorlevel 1 exit /b %errorlevel%

echo.
echo Build finished. Output: bin\Release\net6.0\PvzFmimic.dll
