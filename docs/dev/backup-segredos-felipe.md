# Backup no Drive: o que só o Felipe faz (#2977)

Os passos 1 e 2 de [backup-drive.md](backup-drive.md) estão feitos. Este roteiro é o resto. Nenhum
agente gera, vê ou cola estes segredos. Rode de cima para baixo, no PowerShell da estação.

| Valor | |
|---|---|
| Drive compartilhado | `EasyStok Backup`, ID `0AOtKvTkSaRWCUk9PVA` |
| Conta da VPS | `ez-backup-vps@easystok-backup.iam.gserviceaccount.com` |
| Conta da estação | `ez-backup-estacao@easystok-backup.iam.gserviceaccount.com` |

## 0. Libere a criação de chave no projeto

A organização FMA aplica a política herdada `iam.managed.disableServiceAccountKeyCreation`. Com ela
ativa, o console recusa criar a chave do passo 1. Só quem tem o papel **Administrador de políticas
da organização** pode abrir uma exceção, de preferência só no projeto `easystok-backup`: **IAM e
administrador > Políticas da organização**, essa política, **Gerenciar política**. Sem a exceção, o
desenho com conta de serviço não anda. Uma alternativa (OAuth, por exemplo) é decisão nova, tomada
antes de seguir.

## 1. Gere as 2 chaves JSON

No console do Google Cloud, projeto `easystok-backup`: **IAM e administrador > Contas de serviço**.
Para cada conta, abra **Chaves > Adicionar chave > Criar nova chave > JSON**. O arquivo cai em
Downloads. Renomeie para `sa-vps.json` e `sa-estacao.json`.

## 2. VPS: chave, rclone.conf e senha

```powershell
ssh hostinger 'install -d -m 700 ~/.config/easystok-backup'
```
```powershell
scp $HOME\Downloads\sa-vps.json hostinger:.config/easystok-backup/sa.json
```
```powershell
ssh hostinger 'chmod 600 ~/.config/easystok-backup/sa.json'
```

O `rclone.conf` da VPS **já existe**, com os remotos `[gdrive]` e `[gdrive-crypt]` do backup antigo,
e é a única cópia da senha do crypt. Nunca use `>` nele. O comando abaixo guarda uma cópia datada,
acrescenta o `[ezdrive]` só se ele não existir e lista os remotos. A saída precisa mostrar
`[gdrive]`, `[gdrive-crypt]` e `[ezdrive]`.

```powershell
ssh hostinger 'f=~/.config/easystok-backup/rclone.conf; [ -f $f ] && cp -p $f $f.antes-$(date +%Y%m%d-%H%M%S); grep -q "^\[ezdrive\]" $f 2>/dev/null || printf "\n[ezdrive]\ntype = drive\nscope = drive\nservice_account_file = /home/felipe/.config/easystok-backup/sa.json\nteam_drive = 0AOtKvTkSaRWCUk9PVA\n" >> $f; chmod 600 $f; grep -n "^\[" $f'
```

Senha do repositório restic: gere no cofre de senhas (32 caracteres ou mais) e cole no prompt. Ela
não aparece na tela. Anote também em papel guardado fora do escritório.

```powershell
ssh -t hostinger "f=~/.config/easystok-backup/restic.senha; [ -e `$f ] && { echo 'restic.senha ja existe: nada feito'; exit 1; }; umask 077; read -rs -p 'senha: ' s; printf '%s\n' `"`$s`" > `$f; unset s; echo"
```

## 3. Linger

Já ligado na VPS desde 2026-11-20 (`Linger=yes`). Não precisa de sudo. Para conferir ou religar:

```powershell
ssh hostinger 'loginctl enable-linger; loginctl show-user felipe -p Linger'
```

## 4. Instale na VPS e tire o primeiro snapshot

```powershell
pwsh scripts/backup/instalar.ps1
```
```powershell
ssh hostinger 'systemctl --user start easystok-backup.service; cat ~/backup-restic/estado-diario'
```

`rc=0` é sucesso.

## 5. Estação: chave, rclone.conf e senha

```powershell
$d = "$env:LOCALAPPDATA\easystok-backup"; New-Item -ItemType Directory -Force $d | Out-Null; Move-Item $HOME\Downloads\sa-estacao.json "$d\sa.json"
```
```powershell
$d = "$env:LOCALAPPDATA\easystok-backup"; $f = "$d\rclone.conf"; if (Test-Path $f) { Copy-Item $f "$f.antes-$(Get-Date -Format yyyyMMdd-HHmmss)" }; if (-not ((Test-Path $f) -and (Select-String -Quiet '^\[ezdrive\]' $f))) { "`n[ezdrive]`ntype = drive`nscope = drive`nservice_account_file = $($d -replace '\\','/')/sa.json`nteam_drive = 0AOtKvTkSaRWCUk9PVA`n" | Add-Content -NoNewline $f }; Select-String '^\[' $f
```
```powershell
pwsh scripts/backup/estacao/guardar-senha.ps1
```
```powershell
pwsh scripts/backup/estacao/registrar-tarefas.ps1
```
```powershell
pwsh scripts/backup/estacao/puxar.ps1
```

## 6. Limpe e avise

Apague `sa-vps.json` de Downloads e esvazie a lixeira. As cópias `rclone.conf.antes-*` têm segredo:
apague só depois de o backup novo estar provado e de a senha do crypt antigo estar no cofre. Copie para o cofre os valores de
`EZ_CRYPTO_KEK_ID`, `EZ_CRYPTO_KEK` e, se houver, os `_ANTERIOR`. Depois avise o agente, que confere
sem ler segredo: o `estado-diario` e o `restic snapshots` pela estação.
