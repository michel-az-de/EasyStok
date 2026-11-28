@echo off
rem posttooluse-lint-console -- fast-gate do posttooluse-lint-console.mjs (#3401, ADR-0029).
rem Toda edicao dispara o PostToolUse; so a que cita o Console paga o cold-start do node. O findstr
rem captura E filtra numa passada: linha que casa vai para %F%; arquivo vazio = nao e o Console, sai.
rem Padrao propositalmente FROUXO (o .mjs re-checa src + .js/.jsx). findstr le linha de 60 KB (medido
rem em #3401). Responde em JSON pelo stdout do .mjs; fail-open.
setlocal
set "F=%TEMP%\pk_lint_%RANDOM%%RANDOM%.json"
findstr /i /c:"EasyStock.Console" > "%F%" 2>nul
for %%A in ("%F%") do set "SZ=%%~zA"
if "%SZ%"=="0" goto :clean
node "%~dp0posttooluse-lint-console.mjs" < "%F%"
:clean
del "%F%" >nul 2>&1
exit /b 0
