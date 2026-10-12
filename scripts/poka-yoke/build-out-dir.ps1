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
