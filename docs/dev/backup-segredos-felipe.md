# Backup no Drive: o que só o Felipe faz (#2977)

O passo 1 de [backup-drive.md](backup-drive.md) (Drive compartilhado) está feito, e a cópia antiga
segue na VPS. Este roteiro é o resto. Nenhum agente gera, vê ou cola estes segredos. Rode de cima
para baixo, no PowerShell da estação.

| Valor | |
|---|---|
| Drive compartilhado | `EasyStok Backup`, ID `0AOtKvTkSaRWCUk9PVA` |
| Projeto Google Cloud | `easystok-backup` |
| Contas de serviço | não usadas. Ficam sem chave; não crie nenhuma chave JSON |

## 0. Crie o client ID OAuth (uma vez)

No projeto `easystok-backup`, console do Google Cloud: primeiro **APIs e serviços > Biblioteca >
Google Drive API > Ativar** (sem ela o login do rclone falha com `403 accessNotConfigured`). Depois
**APIs e serviços > Tela de consentimento
OAuth** (tipo **interno**, só a organização FMA, nome `EasyStok Backup`; nunca externo: em status de teste
o refresh token expira em 7 dias e o backup pararia em silêncio). Depois **Credenciais > Criar credenciais
> ID do cliente OAuth > Aplicativo para computador**. Anote o **ID do cliente** e o **segredo do
cliente** no cofre de senhas. Não cole esses valores no chat.

## 1. Login e arquivo separado (estação)

Crie o remote num arquivo novo, nunca no `rclone.conf` antigo:

```powershell
$d = "$env:LOCALAPPDATA\easystok-backup"; New-Item -ItemType Directory -Force $d | Out-Null
rclone --config "$d\ezdrive.conf" config
```

No assistente: `n` (novo remote), nome `ezdrive`, tipo `drive`, cole o ID e o segredo do passo 0,
escopo: digite `drive.file` por extenso (não use número: no rclone 1.75.1 a opção 2 é `drive.readonly`), ID do Drive compartilhado `0AOtKvTkSaRWCUk9PVA` em `team_drive`, e
`Use auto config` = sim. O navegador abre; entre com a conta do Drive. Não há chave JSON.

## 2. VPS: copie só o arquivo novo

O `rclone.conf` da VPS tem o backup antigo (`[gdrive]`, `[gdrive-crypt]`) e a única cópia da senha
do crypt. **Nunca use `>` nele.** Copie só o arquivo novo, para outro caminho:

```powershell
ssh hostinger 'install -d -m 700 ~/.config/easystok-backup'
```
```powershell
scp "$env:LOCALAPPDATA\easystok-backup\ezdrive.conf" hostinger:.config/easystok-backup/ezdrive.conf
```
```powershell
ssh hostinger 'chmod 600 ~/.config/easystok-backup/ezdrive.conf; grep -c "^\[ezdrive\]" ~/.config/easystok-backup/ezdrive.conf'
```

A última linha só conta o cabeçalho `[ezdrive]` do arquivo novo; a saída precisa ser 1 e não mostra
segredo. O `rclone.conf` antigo não é lido.

Senha do repositório restic: gere no cofre de senhas (32 caracteres ou mais) e cole no prompt. Ela
não aparece na tela. Anote também em papel guardado fora do escritório.

```powershell
ssh -t hostinger "f=~/.config/easystok-backup/restic.senha; [ -e `$f ] && { echo 'restic.senha ja existe: nada feito'; exit 1; }; umask 077; read -rs -p 'senha: ' s; printf '%s\n' `"`$s`" > `$f; unset s; echo"
```

## 3. Instale na VPS e tire o primeiro snapshot

```powershell
pwsh scripts/backup/instalar.ps1
```
```powershell
ssh hostinger 'systemctl --user start easystok-backup.service; cat ~/backup-restic/estado-diario'
```

`rc=0` é sucesso. O instalador recusa se o `client_id` estiver vazio.

## 4. Estação: senha, tarefas e primeira cópia

Antes, instale o rclone (`winget install Rclone.Rclone`). O restic 0.19.1 é instalado pelo
`registrar-tarefas.ps1`, com SHA256 conferido. Depois:

```powershell
pwsh scripts/backup/estacao/guardar-senha.ps1
```
```powershell
pwsh scripts/backup/estacao/registrar-tarefas.ps1
```
```powershell
pwsh scripts/backup/estacao/puxar.ps1
```

## 5. Limpe e avise

Apague qualquer `sa-*.json` que ainda exista em Downloads e esvazie a lixeira. As cópias
`rclone.conf.antes-*` da VPS têm segredo: apague só depois de o backup novo estar provado e de a
senha do crypt antigo estar no cofre. Copie para o cofre os valores de `EZ_CRYPTO_KEK_ID`,
`EZ_CRYPTO_KEK` e, se houver, os `_ANTERIOR`. Depois avise o agente, que confere sem ler segredo:
o `estado-diario` e o `restic snapshots` pela estação.
