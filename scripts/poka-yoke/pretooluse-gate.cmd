@echo off
rem pretooluse-gate -- fast-gate do pretooluse-block-generated.ps1 (ADR-0029).
rem O cold-start do PowerShell era pago em TODA edicao; este gate delega ao .ps1
rem original (fonte da verdade) so quando o payload PODE conter arquivo gerado.
rem O findstr captura E filtra numa passada: linha que casa vai para %F%; arquivo
rem vazio = nenhuma regra se aplica. Padroes propositalmente FROUXOS (falso
rem positivo e inofensivo: o .ps1 re-checa com as regras exatas). No JSON os
rem backslashes chegam dobrados (\\), por isso os padroes com \\\\. Fail-open.
setlocal
set "F=%TEMP%\pk_pre_%RANDOM%%RANDOM%.json"
findstr /c:"wwwroot/etiqueta" /c:"wwwroot\\\\etiqueta" /c:"tailwind.dist.css" /c:"Raw/pwa" /c:"Raw\\\\pwa" > "%F%" 2>nul
for %%A in ("%F%") do set "SZ=%%~zA"
if "%SZ%"=="0" goto :clean
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pretooluse-block-generated.ps1" < "%F%"
:clean
del "%F%" >nul 2>&1
exit /b 0
