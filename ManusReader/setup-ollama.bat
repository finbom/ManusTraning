@echo off
echo ============================================================
echo  ManusReader - Ollama Setup
echo ============================================================
echo.

:: Check if Ollama is installed
where ollama >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [!] Ollama hittades inte.
    echo.
    echo Ladda ner och installera Ollama fran:
    echo   https://ollama.com/download
    echo.
    echo Starta om detta skript efter installationen.
    pause
    exit /b 1
)

echo [OK] Ollama ar installerat.
echo.

:: Pull the recommended model
echo Laddar ner modell: llama3.2 (ca 2 GB, tar nagra minuter)...
ollama pull llama3.2

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [!] Kunde inte ladda ner llama3.2.
    echo     Prova att starta Ollama manuellt: ollama serve
    echo     Och sedan: ollama pull llama3.2
    pause
    exit /b 1
)

echo.
echo [OK] Modellen ar klar!
echo.
echo Starta Ollama i bakgrunden:
echo   ollama serve
echo.
echo Sedan kan du anvanda "Oppna Word+AI" i ManusReader.
echo.
pause
