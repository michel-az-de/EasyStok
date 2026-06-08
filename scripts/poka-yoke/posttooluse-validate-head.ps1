<#
posttooluse-validate-head -- hook PostToolUse do Claude Code (Classe B, ADR-0029).

Apos um `git commit`/`git push` rodado num Bash/PowerShell, surface o HEAD resultante no
contexto (sha/autor/subject/qtd arquivos) e AVISA se o autor nao for o canonico -- rede de
seguranca contra o sequestro do auto-commit do ambiente local (#448), inclusive para commits
feitos SEM o commit-seguro.ps1.

Recebe o payload PostToolUse via STDIN (JSON). SEMPRE sai 0 (PostToolUse nao bloqueia e nao
deve atrapalhar o fluxo). Emite hookSpecificOutput.additionalContext (e systemMessage na anomalia).
Configurado em .claude/settings.json. Ver .poka-yoke/registry.yaml (traps.autocommit-hijack).
#>
$ErrorActionPreference = 'SilentlyContinue'
$expected = 'felipe.azevedo@gmail.com'

try {
  $raw = [Console]::In.ReadToEnd()
  if (-not $raw) { exit 0 }
  $j = $raw | ConvertFrom-Json
  $cmd = [string]$j.tool_input.command
  if (-not $cmd) { exit 0 }

  # so age quando o comando foi um git commit / git push
  if ($cmd -notmatch 'git\s+(-C\s+\S+\s+)?(commit|push)') { exit 0 }

  # resolve o repo: -C <path literal> se existir; senao a raiz derivada do script; senao cwd
  $repo = $null
  if ($cmd -match 'git\s+-C\s+"?([^"\s]+)"?') {
    $p = $matches[1]
    if ($p -notmatch '^\$' -and (Test-Path $p)) { $repo = $p }
  }
  if (-not $repo) { $repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
  if (-not (Test-Path (Join-Path $repo '.git')) -and $j.cwd) { $repo = $j.cwd }

  $sha     = (git -C $repo rev-parse --short HEAD 2>$null)
  if (-not $sha) { exit 0 }
  $author  = (git -C $repo log -1 --format='%ae' 2>$null)
  $subject = (git -C $repo log -1 --format='%s' 2>$null)
  $nfiles  = @(git -C $repo show --name-only --format='' HEAD 2>$null | Where-Object { $_ -ne '' }).Count

  $ctx = "[poka-yoke] HEAD apos git em ${repo}: $sha | $author | $subject ($nfiles arquivos)"
  $out = @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'; additionalContext = $ctx } }

  if ($author -and $author -ne $expected) {
    $warn = "AVISO poka-yoke: autor do HEAD '$author' != canonico '$expected' (R12). Possivel identidade errada ou sequestro #448. Cheque antes de pushar."
    $out.hookSpecificOutput.additionalContext = "$warn`n$ctx"
    $out.systemMessage = $warn
  }

  ($out | ConvertTo-Json -Depth 6 -Compress)
  exit 0
}
catch {
  exit 0
}
