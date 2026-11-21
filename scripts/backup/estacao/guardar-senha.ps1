# Guarda a senha do repositorio restic na estacao com DPAPI (so este usuario nesta maquina
# abre). Quem roda e o Felipe: a senha sai do gerenciador de senhas direto para o prompt
# mascarado, nunca para arquivo em claro, historico ou agente (E3, #2977, ADR-0068).
. "$PSScriptRoot\comum.ps1"
$cfg = Get-EzbkConfig
New-Item -ItemType Directory -Force $cfg.Dados | Out-Null
$s1 = Read-Host 'Senha do repositorio restic' -AsSecureString
$s2 = Read-Host 'Repita' -AsSecureString
$t1 = [Net.NetworkCredential]::new('', $s1).Password; $t2 = [Net.NetworkCredential]::new('', $s2).Password
if (-not $t1 -or $t1 -ne $t2) { $t1 = $t2 = $null; throw 'senhas vazias ou diferentes' }
$t1 = $t2 = $null
$s1 | ConvertFrom-SecureString | Set-Content $cfg.SenhaArquivo
"senha guardada em $($cfg.SenhaArquivo) (DPAPI)"
