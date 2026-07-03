<#
gate.ps1 -- gate unico pre-commit: build-check + arch-tests SEM rebuild (~50s quente).
Substitui a dupla execucao que existia (build-check + dotnet test manual do R4 +
dotnet test repetido pelo Husky), que custava ~4-7min por commit (issue 813).

Mecanica:
  1. build incremental do EasyStok.CI.slnf para %TEMP%\easystok-build-check
     (receita identica ao build-check.ps1: -o temp + UseAppHost=false = imune
     ao lock de bin do ambiente local, issue 448).
  2. robocopy incremental do output temp para <repo>\.build\arch-gate\.
     POR QUE: os arch-tests acham a raiz subindo de AppContext.BaseDirectory ate
     um *.sln (ArchTestPaths.cs, gotcha documentado la). Rodar a DLL do %TEMP%
     quebra o walk-up; rodar de .build\arch-gate (dentro do repo, no .gitignore)
     funciona sem tocar em codigo de teste.
  3. dotnet test na DLL copiada (dotnet test sobre DLL nao compila nada) com
     --filter Category=Architecture.

Uso:   powershell -File scripts/poka-yoke/gate.ps1
Saida: exit 0 = verde (build + arquitetura); exit != 0 = etapa que falhou.

Definido em ADR-0040. Registrado em .poka-yoke/registry.yaml (canonical_commands.gate).
#>
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$slnf     = Join-Path $repoRoot 'EasyStok.CI.slnf'
$buildOut = Join-Path $env:TEMP 'easystok-build-check'
$gateDir  = Join-Path $repoRoot '.build\arch-gate'

if (-not (Test-Path $slnf)) {
    Write-Host "[gate] NAO achei $slnf -- rode a partir do repo." -ForegroundColor Red
    exit 2
}

# -- 1. Build (mesma receita do build-check.ps1) -----------------------------
Write-Host "[gate] 1/3 build incremental: $slnf -> $buildOut"
dotnet build $slnf --nologo -p:UseAppHost=false -o $buildOut
if ($LASTEXITCODE -ne 0) {
    Write-Host "[gate] VERMELHO no build (exit $LASTEXITCODE) -- erro REAL de compilacao, nao e lock." -ForegroundColor Red
    exit $LASTEXITCODE
}

# -- 2. Espelho para dentro do repo (walk-up dos arch-tests acha o .sln) -----
Write-Host "[gate] 2/3 robocopy incremental -> $gateDir"
robocopy $buildOut $gateDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {   # robocopy: 0-7 = sucesso, >=8 = falha real
    Write-Host "[gate] VERMELHO no robocopy (exit $LASTEXITCODE)." -ForegroundColor Red
    exit 3
}

# -- 3. Arch-tests sem rebuild (dotnet test em DLL nao compila nada) ---------
$dll = Join-Path $gateDir 'EasyStock.ArchitectureTests.dll'
Write-Host "[gate] 3/3 arch-tests (sem rebuild): $dll"
dotnet test $dll --filter "Category=Architecture" --nologo --verbosity minimal
$testExit = $LASTEXITCODE

if ($testExit -eq 0) {
    Write-Host "[gate] VERDE -- build + arquitetura OK." -ForegroundColor Green
} else {
    Write-Host "[gate] VERMELHO -- arch-tests falharam (exit $testExit)." -ForegroundColor Red
}
exit $testExit
