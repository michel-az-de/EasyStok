@echo off
rem posttooluse-gate -- fast-gate do posttooluse-validate-head.ps1 (ADR-0029).
rem >95% dos Bash/PowerShell nao sao git commit/push; o cold-start do PowerShell
rem era pago em todos. O findstr captura E filtra numa passada: linha que casa
rem vai para %F%; arquivo vazio = nao e commit/push, sai. Falso positivo e
rem inofensivo (o .ps1 re-checa com regex estrita). Fail-open.
setlocal
set "F=%TEMP%\pk_post_%RANDOM%%RANDOM%.json"
findstr /r /c:"git.*commit" /c:"git.*push" > "%F%" 2>nul
for %%A in ("%F%") do set "SZ=%%~zA"
if "%SZ%"=="0" goto :clean
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0posttooluse-validate-head.ps1" < "%F%"
:clean
del "%F%" >nul 2>&1
exit /b 0
