<#
.SYNOPSIS
Decompõe o tamanho de uma PR (additions) em buckets para estimar
quanto é trabalho real vs auto-gerado/format.

.DESCRIPTION
PRs em projeto .NET com EF Core frequentemente parecem gigantes porque
contêm migrations regeneradas (Designer.cs, ModelSnapshot.cs com ~10k
linhas cada). Este script separa por categoria — útil ANTES de planejar
rebase, cherry-pick, ou estimar esforço de review.

Categorias:
- migration_efcore: paths com Migrations/, Designer.cs, ModelSnapshot.cs
- wwwroot: paths com wwwroot/ (assets, PWA, JS gerado)
- docs: arquivos .md e paths em docs/
- json: arquivos .json (configs, project files, swagger)
- format_only: heurística — adds == deletions, ambos > 0 (provavelmente reformatação)
- feature: resto (código de feature real)

.PARAMETER PrNumber
Número da PR a analisar (ex: 234)

.PARAMETER Json
Output em JSON em vez de tabela legível

.EXAMPLE
./scripts/pr-real-size.ps1 234

.EXAMPLE
./scripts/pr-real-size.ps1 234 -Json | ConvertFrom-Json | Select feature
#>

param(
    [Parameter(Mandatory = $true)]
    [int]$PrNumber,

    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# Pega os files da PR via gh CLI
$prJson = & gh pr view $PrNumber --json files,number,title,additions,deletions 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao buscar PR #$PrNumber via gh CLI: $prJson"
    exit 1
}

$pr = $prJson | ConvertFrom-Json

# Inicializa buckets
$buckets = [ordered]@{
    migration_efcore = @{ adds = 0; dels = 0; files = 0 }
    wwwroot          = @{ adds = 0; dels = 0; files = 0 }
    docs             = @{ adds = 0; dels = 0; files = 0 }
    json             = @{ adds = 0; dels = 0; files = 0 }
    format_only      = @{ adds = 0; dels = 0; files = 0 }
    feature          = @{ adds = 0; dels = 0; files = 0 }
}

foreach ($file in $pr.files) {
    $path = $file.path
    $adds = $file.additions
    $dels = $file.deletions

    # Ordem importa: testar primeiro os mais específicos
    $bucket = if ($path -match 'Migrations/.*\.(Designer\.cs|cs)$' -or $path -match 'ModelSnapshot\.cs$') {
        'migration_efcore'
    }
    elseif ($path -match '(^|/)wwwroot/') {
        'wwwroot'
    }
    elseif ($path -match '\.md$' -or $path -match '(^|/)docs/') {
        'docs'
    }
    elseif ($path -match '\.json$') {
        'json'
    }
    elseif ($adds -gt 0 -and $adds -eq $dels) {
        # Heurística format-only: mesmo número de adds e dels sugere
        # reformatação (whitespace, encoding, line endings)
        'format_only'
    }
    else {
        'feature'
    }

    $buckets[$bucket].adds += $adds
    $buckets[$bucket].dels += $dels
    $buckets[$bucket].files += 1
}

$totalAdds = ($buckets.Values | ForEach-Object { $_.adds } | Measure-Object -Sum).Sum
$totalDels = ($buckets.Values | ForEach-Object { $_.dels } | Measure-Object -Sum).Sum
$totalFiles = ($buckets.Values | ForEach-Object { $_.files } | Measure-Object -Sum).Sum

if ($Json) {
    $result = [ordered]@{
        pr_number   = $pr.number
        title       = $pr.title
        total_adds  = $pr.additions
        total_dels  = $pr.deletions
        total_files = $totalFiles
        buckets     = $buckets
    }
    $result | ConvertTo-Json -Depth 5
}
else {
    Write-Output "PR #$($pr.number): $($pr.title)"
    Write-Output "Total reportado pelo GitHub: +$($pr.additions) / -$($pr.deletions) em $totalFiles arquivos"
    Write-Output ""
    Write-Output ("{0,-18} {1,8} {2,8} {3,6}  {4,7}" -f 'Bucket', 'Adds', 'Dels', 'Files', '% adds')
    Write-Output ("{0,-18} {1,8} {2,8} {3,6}  {4,7}" -f ('-' * 18), ('-' * 8), ('-' * 8), ('-' * 6), ('-' * 7))

    foreach ($name in $buckets.Keys) {
        $b = $buckets[$name]
        $pct = if ($totalAdds -gt 0) { ($b.adds / $totalAdds) * 100 } else { 0 }
        Write-Output ("{0,-18} {1,8} {2,8} {3,6}  {4,6:N1}%" -f $name, $b.adds, $b.dels, $b.files, $pct)
    }

    Write-Output ""
    $featureAdds = $buckets['feature'].adds
    $featurePct = if ($totalAdds -gt 0) { ($featureAdds / $totalAdds) * 100 } else { 0 }
    Write-Output ("Conteudo de feature real: +{0} linhas ({1:N1}% do total reportado)" -f $featureAdds, $featurePct)

    if ($featurePct -lt 30) {
        Write-Output ""
        Write-Output "AVISO: <30% da PR e codigo de feature. Conteudo grande mas auto-gerado."
        Write-Output "       Considerar refazer pequeno via cherry-pick seletivo em vez de rebase."
    }
}
