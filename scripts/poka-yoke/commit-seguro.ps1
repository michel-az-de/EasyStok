<#
commit-seguro -- danca de commit segura do EasyStok (Classe B, ADR-0029).

Encapsula o que todo agente refaz a mao e que o ambiente local #448 sabota:
  1. valida identidade git (R12: autor canonico)
  2. stage POR PATHSPEC (R2: nunca add . / add -A)
  3. diff --cached --stat (mostra o que vai entrar)
  4. checa padroes proibidos no stage (R11: bin/obj/dll/exe/pdb, .claude/projects)
  5. (opcional -Build) roda build-check antes, p/ commits de codigo (R4)
  6. commit POR PATHSPEC (anti-race do auto-commit #448)
  7. VALIDA o HEAD resultante (autor/subject/arquivos) contra o pedido --
     pega o sequestro do #448 (HEAD nao mudou, ou subject != pedido)

Uso:
  scripts/poka-yoke/commit-seguro.ps1 -Message "tipo(escopo): desc`n`ncloses #N" -Paths a.cs,b.cs
  -Build      : roda build-check antes do commit (use em mudanca de codigo)
  -NoVerify   : commit --no-verify (SO com OK do Felipe, quando o lock do #448 trava o Husky)

Sai 0 se o commit saiu como pedido; !=0 (e avisa) se algo divergiu (provavel sequestro).
Definido em ADR-0029. Ver .poka-yoke/registry.yaml (canonical_commands.commit-seguro).
#>
param(
  [Parameter(Mandatory = $true)][string]$Message,
  [Parameter(Mandatory = $true)][string[]]$Paths,
  [switch]$Build,
  [switch]$NoVerify,
  [string]$ExpectedAuthorEmail = 'felipe.azevedo@gmail.com',
  [string]$Repo
)
$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }

function Fail($m) { Write-Host "[commit-seguro] ERRO: $m" -ForegroundColor Red; exit 1 }

# 1. identidade (R12)
$email = (git -C $Repo config user.email).Trim()
if ($email -ne $ExpectedAuthorEmail) { Fail "git user.email = '$email', esperado '$ExpectedAuthorEmail' (R12)." }

# 2. HEAD antes (detectar sequestro)
$headBefore = (git -C $Repo rev-parse HEAD).Trim()

# 3. stage por pathspec (R2)
git -C $Repo add -- $Paths
if ($LASTEXITCODE -ne 0) { Fail "git add falhou." }

# 4. diff --cached
Write-Host "[commit-seguro] staged:" -ForegroundColor Cyan
git -C $Repo diff --cached --stat

# 5. padroes proibidos (R11)
$staged = git -C $Repo diff --cached --name-only
$bad = $staged | Where-Object {
  $_ -match '(^|/)(bin|obj)/' -or $_ -match '^(publish|dist)/' -or $_ -match '\.(dll|exe|pdb)$' -or $_ -match '^\.claude/projects/'
}
if ($bad) { Fail "padroes proibidos no stage (R11): $($bad -join ', ')" }

# 6. (opcional) build-check antes do commit
if ($Build) {
  Write-Host "[commit-seguro] build-check..." -ForegroundColor Cyan
  & (Join-Path $PSScriptRoot 'build-check.ps1')
  if ($LASTEXITCODE -ne 0) { Fail "build-check vermelho; nao commito (R4)." }
}

# 7. commit por pathspec (anti-race #448)
if ($NoVerify) { git -C $Repo commit --no-verify -m $Message -- $Paths }
else { git -C $Repo commit -m $Message -- $Paths }

# 8. validar HEAD
$headAfter = (git -C $Repo rev-parse HEAD).Trim()
if ($headAfter -eq $headBefore) {
  Fail "HEAD nao mudou: commit nao aconteceu (Husky bloqueou? se for lock #448, rode com -NoVerify e OK do Felipe)."
}
$author  = (git -C $Repo log -1 --format='%ae').Trim()
$subject = (git -C $Repo log -1 --format='%s').Trim()
$files   = @(git -C $Repo show --name-only --format='' HEAD | Where-Object { $_ -ne '' })
$want    = (($Message -split "`n")[0]).Trim()

if ($author -ne $ExpectedAuthorEmail) {
  Fail "HEAD autor '$author' != '$ExpectedAuthorEmail' -- possivel sequestro #448. Recupere (com OK do Felipe): git -C $Repo reset --soft $headBefore"
}
if ($subject -ne $want) {
  Fail "HEAD subject '$subject' != pedido '$want' -- possivel sequestro #448 (varreu seus arquivos p/ outro commit). Recupere: git -C $Repo reset --soft $headBefore"
}
$missing = @($Paths | Where-Object { ($files -notcontains ($_ -replace '\\', '/')) })
if ($missing) {
  Write-Host "[commit-seguro] AVISO: pedidos nao vistos no HEAD: $($missing -join ', ') (revertidos por build? ou glob nao casou nome exato)" -ForegroundColor Yellow
}

Write-Host "[commit-seguro] OK -> $headAfter | $author | $subject" -ForegroundColor Green
Write-Host "[commit-seguro] arquivos: $($files -join ', ')"
exit 0
