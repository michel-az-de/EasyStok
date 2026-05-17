# Verifica thresholds de cobertura POR MODULO a partir do Cobertura.xml gerado
# pelo ReportGenerator. Falha o build se qualquer modulo crítico cair abaixo do
# alvo. Use complementar ao gate global do irongut/CodeCoverageSummary.
#
# Convencao: <ThresholdPercent> e LINHA-cobertura (line-rate * 100), arredondado.

[CmdletBinding()]
param(
    [string]$CoberturaXmlPath = "./coverage-report/Cobertura.xml"
)

$thresholds = @{
    "EasyStock.Domain"      = 70
    "EasyStock.Application" = 45
    "EasyStock.Api"         = 9
    "EasyStock.Infra.Async" = 50
}
# Api gate intencionalmente baixo (9%) nesta onda. O modulo tem ~5500 linhas
# em controllers, services, background jobs — subir significativamente exige
# E2E tests via WebApplicationFactory + Testcontainers, escopo da Onda 2
# (ver plano em ~/.claude/plans/ + roadmap stability). Meta evolutiva: +5pp
# por onda ate atingir 25-30%.

if (-not (Test-Path $CoberturaXmlPath)) {
    Write-Error "Cobertura.xml nao encontrado em $CoberturaXmlPath"
    exit 2
}

[xml]$xml = Get-Content $CoberturaXmlPath
$failures = @()
$results = @()

foreach ($pkg in $xml.coverage.packages.package) {
    $name = $pkg.name
    $lineRate = [double]$pkg.'line-rate'
    $coveragePct = [math]::Round($lineRate * 100, 1)

    if ($thresholds.ContainsKey($name)) {
        $required = $thresholds[$name]
        $status = if ($coveragePct -ge $required) { "OK" } else { "FAIL" }
        $results += [PSCustomObject]@{
            Module      = $name
            Coverage    = "$coveragePct%"
            Required    = "$required%"
            Status      = $status
        }
        if ($coveragePct -lt $required) {
            $failures += "$name = $coveragePct% (alvo: $required%)"
        }
    } else {
        $results += [PSCustomObject]@{
            Module      = $name
            Coverage    = "$coveragePct%"
            Required    = "(sem gate)"
            Status      = "INFO"
        }
    }
}

$results | Format-Table -AutoSize

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "FAIL: modulos abaixo do threshold:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "OK: todos os modulos com gate atingiram seus thresholds." -ForegroundColor Green
exit 0
