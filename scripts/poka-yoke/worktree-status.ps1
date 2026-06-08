<#
worktree-status -- inventario SEGURO de git worktrees do EasyStok (Classe C, ADR-0029).

Lista cada worktree com: branch, HEAD, se a branch esta merjada em master (trabalho ja
contido), e se ha mudancas nao commitadas. Marca CANDIDATO a remover = merjado + limpo +
nao-principal + nao-atual. NAO remove nada.

REGRA DE OURO: "Candidato" significa apenas merjado+limpo -- NAO significa "sessao morta".
O harness do Claude Code cria/remove um worktree POR SESSAO; um worktree merjado+limpo pode
ser uma sessao paralela AINDA ATIVA. NUNCA remova sem o Felipe confirmar que a sessao
daquele worktree esta encerrada. O worktree ATUAL e excluido automaticamente.

Uso: powershell -File scripts/poka-yoke/worktree-status.ps1
Definido em ADR-0029. Ver .poka-yoke/registry.yaml (traps, Classe C).
#>
$ErrorActionPreference = 'Stop'

function Canon([string]$p) {
    try { return (Resolve-Path $p -ErrorAction Stop).Path.TrimEnd('\', '/').ToLowerInvariant() }
    catch { return ($p -replace '/', '\').TrimEnd('\').ToLowerInvariant() }
}

$main  = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mainC = Canon $main
$cwdC  = Canon (Get-Location).Path

$lines = git -C $main worktree list
$rows = foreach ($ln in $lines) {
    if (-not $ln.Trim()) { continue }
    $parts  = $ln -split '\s+'
    $path   = $parts[0]
    $head   = if ($parts.Count -ge 2) { $parts[1] } else { '' }
    $branch = if ($parts.Count -ge 3) { ($parts[2] -replace '[\[\]]', '') } else { '(detached)' }

    $pathC     = Canon $path
    $isMain    = ($pathC -eq $mainC)
    $isCurrent = ($cwdC -eq $pathC) -or ($cwdC.StartsWith($pathC + '\'))

    $merged = $null
    if (-not $isMain -and $head) {
        git -C $main merge-base --is-ancestor $head master
        $merged = ($LASTEXITCODE -eq 0)
    }

    $dirty = $null
    if (-not $isMain -and (Test-Path $path)) {
        $dirty = [bool](git -C $path status --porcelain)
    }

    [PSCustomObject]@{
        Branch    = $branch
        Head      = if ($head) { $head.Substring(0, [Math]::Min(8, $head.Length)) } else { '' }
        Merged    = $merged
        Dirty     = $dirty
        Atual     = $isCurrent
        Principal = $isMain
        Candidato = ((-not $isMain) -and (-not $isCurrent) -and ($merged -eq $true) -and ($dirty -eq $false))
        Path      = $path
    }
}

$rows | Format-Table -AutoSize
$cands = @($rows | Where-Object { $_.Candidato })
Write-Host ""
Write-Host ("Candidatos a remover (merjado + limpo + nao-principal + nao-atual): " + $cands.Count) -ForegroundColor Cyan
Write-Host "ATENCAO: harness cria/remove worktree por sessao. 'Candidato' NAO e 'sessao morta'." -ForegroundColor Yellow
Write-Host "So remova com OK do Felipe, sabendo que a sessao daquele worktree esta encerrada:" -ForegroundColor DarkGray
Write-Host "  git -C `"$main`" worktree remove <path>  ;  git -C `"$main`" branch -D <branch>" -ForegroundColor DarkGray
