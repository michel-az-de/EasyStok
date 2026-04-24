@echo off
REM Baixa o APK mais recente do workflow "Build Casa da Baba APK" pra C:\rep\EasyStok\builds\
REM Uso: duplo clique no arquivo, ou terminal: scripts\baixar-apk.bat
REM Requer: GitHub CLI instalado e autenticado (gh auth status)

setlocal

REM Adiciona gh ao PATH caso não esteja (instalacao default do Windows)
set "PATH=%PATH%;C:\Program Files\GitHub CLI"

REM Descobre a raiz do repo (pasta acima desse script)
set "REPO_ROOT=%~dp0.."
set "BUILDS_DIR=%REPO_ROOT%\builds"

REM Cria builds\ se nao existir
if not exist "%BUILDS_DIR%" mkdir "%BUILDS_DIR%"

echo.
echo Buscando run mais recente do workflow "Build Casa da Baba APK"...
echo.

REM Pega o ID do run mais recente bem-sucedido
for /f "usebackq tokens=*" %%i in (`gh run list --workflow=build-casa-da-baba-apk.yml --status=success --limit=1 --json databaseId -q ".[0].databaseId"`) do set "RUN_ID=%%i"

if "%RUN_ID%"=="" (
    echo ERRO: nao achei nenhum run bem-sucedido.
    echo Veja em https://github.com/michel-az-de/EasyStok/actions
    pause
    exit /b 1
)

echo Run ID: %RUN_ID%
echo Baixando artifact casa-da-baba-debug-apk...
echo.

REM Remove APK anterior se existir (gh run download nao sobrescreve por padrao)
if exist "%BUILDS_DIR%\app-debug.apk" del "%BUILDS_DIR%\app-debug.apk"

gh run download %RUN_ID% --name casa-da-baba-debug-apk --dir "%BUILDS_DIR%"

if errorlevel 1 (
    echo.
    echo ERRO: falha ao baixar artifact. Veja se voce esta logado: gh auth status
    pause
    exit /b 1
)

echo.
echo ======================================================
echo APK salvo em: %BUILDS_DIR%\app-debug.apk
echo ======================================================
echo.
echo Agora passa esse arquivo pro celular (WhatsApp, Drive, cabo)
echo e abre no Android pra instalar.
echo.
pause
