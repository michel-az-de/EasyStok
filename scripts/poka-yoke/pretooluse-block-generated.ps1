<#
pretooluse-block-generated -- hook PreToolUse do Claude Code (Classe A, ADR-0029).

NEGA Edit/Write/MultiEdit num arquivo GERADO/COPIADO (a copia), apontando a FONTE certa.
Editar a copia e sempre-erro: o proximo build a reverte (#527). Editar a FONTE nunca e bloqueado.

Recebe o payload PreToolUse via STDIN (JSON). Para NEGAR: imprime JSON com
hookSpecificOutput.permissionDecision=deny + permissionDecisionReason e sai 0 (mecanismo
oficial; NAO misturar com exit code). Para PERMITIR: sai 0 sem saida.
Em caso de erro do proprio hook: sai 0 (fail-open -- nunca brickar a edicao por bug do guard;
a rede PostToolUse + o teste copia==fonte pegam downstream).

Regras espelham .poka-yoke/registry.yaml (generated_files severity=block). Configurado em .claude/settings.json.
#>
$ErrorActionPreference = 'SilentlyContinue'
try {
  $raw = [Console]::In.ReadToEnd()
  if (-not $raw) { exit 0 }
  $j = $raw | ConvertFrom-Json
  $fp = [string]$j.tool_input.file_path
  if (-not $fp) { exit 0 }
  $p = ($fp -replace '\\', '/')

  # cada regra: substring que identifica a COPIA gerada + a FONTE a editar
  $rules = @(
    @{ match = 'EasyStock.Web/wwwroot/etiqueta/';               src = 'EasyStock.Api/wwwroot/pwa/etiqueta (copiado pelo target CopyEtiquetaAssets)' },
    @{ match = 'EasyStok.Mobile/Resources/Raw/pwa/';            src = 'EasyStock.Api/wwwroot/pwa (mirror manual; sincronize de la)' },
    @{ match = 'EasyStock.Web/wwwroot/css/tailwind.dist.css';   src = 'tailwind.src.css + tailwind.config.js + .cshtml/.js (gerado por TailwindBuild)' },
    @{ match = 'EasyStock.Admin/wwwroot/css/tailwind.dist.css'; src = 'tailwind.src.css + tailwind.config.js + .cshtml/.js (gerado por TailwindBuild)' }
  )

  foreach ($r in $rules) {
    if ($p -like ('*' + $r.match + '*')) {
      $reason = "[poka-yoke] '$p' e um arquivo GERADO/COPIADO -- o build reverte qualquer edicao aqui (#527). Edite a FONTE: $($r.src). Detalhes em .poka-yoke/registry.yaml (ADR-0029)."
      $out = @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } }
      ($out | ConvertTo-Json -Depth 6 -Compress)
      exit 0
    }
  }
  exit 0
}
catch { exit 0 }
