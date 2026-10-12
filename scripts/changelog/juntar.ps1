<#
.SYNOPSIS
  Junta os fragmentos de changelog.d/ no CHANGELOG.md (ADR-0055).

.DESCRIPTION
  Cada fragmento changelog.d/<issue>.md traz seções "### Added", "### Fixed" etc. O script
  coloca as entradas dentro de "## [Unreleased]", na seção certa, e apaga os fragmentos.
  Com -Versao, a seção Unreleased vira "## [<versao>] - <data>" e nasce um Unreleased vazio.

.EXAMPLE
  powershell -File scripts/changelog/juntar.ps1              # só junta no Unreleased
  powershell -File scripts/changelog/juntar.ps1 -Versao 1.2.0
#>
param(
    [string]$Versao,
    [string]$Raiz = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)
$ErrorActionPreference = 'Stop'

$ordem = 'Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security'
$pastaFragmentos = Join-Path $Raiz 'changelog.d'
$arquivoChangelog = Join-Path $Raiz 'CHANGELOG.md'
$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Lê os fragmentos e agrupa as entradas por seção, na ordem dos arquivos (número da issue).
$fragmentos = Get-ChildItem $pastaFragmentos -Filter '*.md' |
    Where-Object { $_.Name -ne 'README.md' } | Sort-Object Name
$porSecao = @{}
foreach ($f in $fragmentos) {
    $secao = $null
    foreach ($linha in [IO.File]::ReadAllLines($f.FullName, $utf8)) {
        if ($linha -match '^###\s+(\w+)\s*$') {
            $secao = $Matches[1]
            if ($ordem -notcontains $secao) { throw "Seção '$secao' inválida em $($f.Name). Use: $($ordem -join ', ')." }
            continue
        }
        if (-not $secao) {
            if ($linha.Trim()) { throw "$($f.Name): entrada antes de qualquer '### Seção'." }
            continue
        }
        if (-not $porSecao.ContainsKey($secao)) { $porSecao[$secao] = New-Object System.Collections.Generic.List[string] }
        $porSecao[$secao].Add($linha)
    }
}
foreach ($s in @($porSecao.Keys)) {
    # Tira linhas em branco das pontas de cada seção.
    $l = $porSecao[$s]
    while ($l.Count -and -not $l[0].Trim()) { $l.RemoveAt(0) }
    while ($l.Count -and -not $l[$l.Count - 1].Trim()) { $l.RemoveAt($l.Count - 1) }
}

# 2. Localiza a seção Unreleased no CHANGELOG.
$linhas = New-Object System.Collections.Generic.List[string]
$linhas.AddRange([IO.File]::ReadAllLines($arquivoChangelog, $utf8))
$inicio = $linhas.IndexOf('## [Unreleased]')
if ($inicio -lt 0) { throw "CHANGELOG.md sem '## [Unreleased]'." }
$fim = $inicio + 1
while ($fim -lt $linhas.Count -and -not $linhas[$fim].StartsWith('## ')) { $fim++ }

# 3. Insere cada seção: dentro do "### X" que já existe no Unreleased, ou cria o cabeçalho.
foreach ($secao in $ordem) {
    if (-not $porSecao.ContainsKey($secao) -or -not $porSecao[$secao].Count) { continue }
    $cabecalho = -1
    for ($i = $inicio + 1; $i -lt $fim; $i++) { if ($linhas[$i] -eq "### $secao") { $cabecalho = $i; break } }
    if ($cabecalho -ge 0) {
        $linhas.InsertRange($cabecalho + 1, $porSecao[$secao])
        $fim += $porSecao[$secao].Count
        continue
    }
    # Sem cabeçalho: entra antes da primeira seção de ordem maior, ou no fim do Unreleased.
    $pos = $fim
    for ($i = $inicio + 1; $i -lt $fim; $i++) {
        if ($linhas[$i] -match '^###\s+(\w+)' -and $ordem.IndexOf($Matches[1]) -gt $ordem.IndexOf($secao)) { $pos = $i; break }
    }
    $bloco = @("### $secao") + $porSecao[$secao] + @('')
    $linhas.InsertRange($pos, [string[]]$bloco)
    $fim += $bloco.Count
}

# 4. Com versão: Unreleased vira a versão e nasce um Unreleased vazio acima.
if ($Versao) {
    $linhas[$inicio] = "## [$Versao] - $(Get-Date -Format 'yyyy-MM-dd')"
    $linhas.InsertRange($inicio, [string[]]@('## [Unreleased]', ''))
}

[IO.File]::WriteAllText($arquivoChangelog, (($linhas -join "`n") + "`n"), $utf8)
$fragmentos | Remove-Item
Write-Host "Juntados $($fragmentos.Count) fragmento(s) no CHANGELOG.md."
