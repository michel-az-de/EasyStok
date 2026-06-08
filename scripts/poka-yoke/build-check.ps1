<#
build-check -- compila o que realmente deploya (EasyStok.CI.slnf, sem o Mobile/maui)
de forma IMUNE ao lock de bin do ambiente local automatico (#448).

Por que existe:
  O passo 0 do CLAUDE.md mandava `dotnet build EasyStok.sln`, que (a) puxa o
  EasyStok.Mobile (precisa da workload maui) e (b) falha com MSB3021/MSB3027 quando o
  `dotnet watch` do ambiente local (#448) segura os .exe/.dll de Admin/Web.
  Este comando usa o solution filter de CI (o mesmo do ci.yml), builda para um
  diretorio temporario e sem apphost -- nao toca os bins que o ambiente local segura.

  Obs: build de uma solution com -o emite o warning NETSDK1194 (flatten de saidas).
  E esperado e benigno aqui: so checamos compilacao, a saida temporaria e descartada.

Uso:   powershell -File scripts/poka-yoke/build-check.ps1
Saida: exit 0 = verde (compila o que deploya); exit != 0 = erro REAL de compilacao.

Definido em ADR-0029. Registrado em .poka-yoke/registry.yaml (canonical_commands.build-check).
#>
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$slnf     = Join-Path $repoRoot 'EasyStok.CI.slnf'
$outDir   = Join-Path $env:TEMP 'easystok-build-check'

if (-not (Test-Path $slnf)) {
    Write-Host "[build-check] NAO achei $slnf -- rode a partir do repo." -ForegroundColor Red
    exit 2
}

Write-Host "[build-check] solution filter: $slnf"
Write-Host "[build-check] saida temporaria: $outDir (fora dos bins do ambiente local)"

dotnet build $slnf --nologo -p:UseAppHost=false -o $outDir
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host "[build-check] VERDE -- compilou o que deploya, sem tocar os bins do ambiente local." -ForegroundColor Green
} else {
    Write-Host "[build-check] VERMELHO -- erro REAL de compilacao (exit $code). Nao e lock; investigar." -ForegroundColor Red
}
exit $code
