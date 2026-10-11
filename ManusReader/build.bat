@echo off
echo ============================================
echo  ManusReader - Kompilerar...
echo ============================================

:: Försök hitta dotnet - antingen system eller lokal installation
set DOTNET_EXE=dotnet
if exist "%LOCALAPPDATA%\dotnet-sdk\dotnet.exe" set DOTNET_EXE=%LOCALAPPDATA%\dotnet-sdk\dotnet.exe
if exist "%ProgramFiles%\dotnet\dotnet.exe" set DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe

%DOTNET_EXE% publish ManusReader.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo FEL: Kompilering misslyckades!
    pause
    exit /b 1
)

echo.
echo Kopierar exempelmanus till publish-mappen...
if exist exempelmanus.txt copy /Y exempelmanus.txt publish\exempelmanus.txt

echo.
echo ============================================
echo  Klart! Programmet finns i mappen: publish\
echo  Kör: publish\ManusReader.exe
echo ============================================
pause
