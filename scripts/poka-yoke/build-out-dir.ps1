<#
build-out-dir -- pasta de saida do build do gate, UNICA por worktree (issue 1117).

Antes todo worktree da maquina compilava para %TEMP%\easystok-build-check. Com sessoes
paralelas, o build de um worktree sobrescrevia a saida do outro entre o build e o robocopy
do gate.ps1, e os arch-tests carregavam DLLs de outra branch (ReflectionTypeLoadException).
O sufixo e um hash do caminho do repo: estavel no mesmo worktree (build incremental
preservado) e diferente entre worktrees.

Uso (dot-source):  . (Join-Path $PSScriptRoot 'build-out-dir.ps1'); Get-BuildOutDir $repoRoot
#>
function Get-BuildOutDir([string]$RepoRoot) {
    $key   = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/').ToLowerInvariant()
    $sha   = [Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))
    $hash  = -join ($bytes[0..3] | ForEach-Object { $_.ToString('x2') })
    Join-Path $env:TEMP "easystok-build-check-$hash"
}

<#
Get-BuildOutDirOrfas -- pastas de build que nenhum worktree vivo usa mais (issue 2653).

O #1117 deu uma pasta por worktree, mas nada a apaga quando o worktree sai: em 15/11 eram
364 pastas e 54,8 GB no %TEMP%. Orfa = hash que nao bate com nenhum `git worktree list` do
repo E sem escrita ha mais de $Horas horas (folga para um gate de outro clone em andamento).
Juncao ou link no topo do TEMP nunca e orfa: apagar seguiria para o alvo.
So lista; quem apaga e o limpar-build-orfao.ps1 com -Apagar.
#>
function Get-BuildOutDirOrfas([string]$RepoRoot, [int]$Horas = 24) {
    $vivas = @{}
    foreach ($ln in (git -C $RepoRoot worktree list --porcelain)) {
        if ($ln -like 'worktree *') { $vivas[(Get-BuildOutDir $ln.Substring(9)).ToLowerInvariant()] = $true }
    }
    $vivas[(Get-BuildOutDir $RepoRoot).ToLowerInvariant()] = $true
    $limite = (Get-Date).AddHours(-$Horas)
    Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'easystok-build-check-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^easystok-build-check-[0-9a-f]{8}$' } |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
        Where-Object { -not $vivas.ContainsKey($_.FullName.ToLowerInvariant()) } |
        Where-Object { $_.LastWriteTime -lt $limite }
}
