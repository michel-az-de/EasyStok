# Backup no Drive: o que só o Felipe faz (#2977)

Os passos 1 e 2 de [backup-drive.md](backup-drive.md) estão feitos. Este roteiro é o resto. Nenhum
agente gera, vê ou cola estes segredos. Rode de cima para baixo, no PowerShell da estação.

| Valor | |
|---|---|
| Drive compartilhado | `EasyStok Backup`, ID `0AOtKvTkSaRWCUk9PVA` |
| Conta da VPS | `ez-backup-vps@easystok-backup.iam.gserviceaccount.com` |
| Conta da estação | `ez-backup-estacao@easystok-backup.iam.gserviceaccount.com` |

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
ssh hostinger 'chmod 600 ~/.config/easystok-backup/sa.json && printf "[ezdrive]\ntype = drive\nscope = drive\nservice_account_file = /home/felipe/.config/easystok-backup/sa.json\nteam_drive = 0AOtKvTkSaRWCUk9PVA\n" > ~/.config/easystok-backup/rclone.conf && chmod 600 ~/.config/easystok-backup/rclone.conf'
```

Senha do repositório restic: gere no cofre de senhas (32 caracteres ou mais) e cole no prompt. Ela
não aparece na tela. Anote também em papel guardado fora do escritório.

```powershell
ssh -t hostinger "umask 077; read -rs -p 'senha: ' s; printf '%s\n' `"`$s`" > ~/.config/easystok-backup/restic.senha; unset s; echo"
```

## 3. Linger (pede a senha do sudo)

```powershell
ssh -t hostinger 'sudo loginctl enable-linger felipe'
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
$d = "$env:LOCALAPPDATA\easystok-backup"; "[ezdrive]`ntype = drive`nscope = drive`nservice_account_file = $($d -replace '\\','/')/sa.json`nteam_drive = 0AOtKvTkSaRWCUk9PVA`n" | Set-Content -NoNewline "$d\rclone.conf"
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

Apague `sa-vps.json` de Downloads e esvazie a lixeira. Copie para o cofre os valores de
`EZ_CRYPTO_KEK_ID`, `EZ_CRYPTO_KEK` e, se houver, os `_ANTERIOR`. Depois avise o agente, que confere
sem ler segredo: o `estado-diario` e o `restic snapshots` pela estação.
