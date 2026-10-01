# Backup externo no Google Drive (cifrado)

Issue #1253, item "Mínimo a fazer antes" da #1180. Scripts em `scripts/infra/backup-externo/`.

| O quê | Onde |
|---|---|
| Destino | `gdrive-crypt:easystok/` (remote `crypt` sobre `gdrive:`, cifrado na VPS antes de sair) |
| Conteúdo | dump mais recente de `/opt/backups` ou `~/backups` + `uploads-AAAA-MM-DD.tar.gz` do volume `easystok_uploads-data` |
| Quando | cron do usuário `felipe` às **03:45** (o backup local roda às 03:15) |
| Retenção | **7 diários** em `diario/AAAA-MM-DD/` e **4 semanais** (domingo) em `semanal/AAAA-MM-DD/` |
| Segredos | só em `~/.config/easystok-backup/rclone.conf` na VPS (chmod 600). Nunca no repo nem no chat |
| Log | `~/logs/backup-externo.log` na VPS |

## A. Autorizar o Google Drive e gravar o rclone.conf (uma vez, na sua máquina)

O token OAuth exige navegador, então o rclone.conf nasce na sua máquina e vai pronto para a VPS.
Com o rclone instalado localmente (`winget install Rclone.Rclone`), num PowerShell:

```powershell
rclone config --config "$env:USERPROFILE\easystok-rclone.conf"
```

1. `n` → nome **`gdrive`** → tipo `drive` → client_id/secret em branco → scope `drive.file`
   → sem service account → auto config `y` (abre o navegador; entre na conta do Drive).
2. `n` → nome **`gdrive-crypt`** → tipo `crypt` → remote **`gdrive:easystok-backup`**
   → filename_encryption `standard` → directory_name_encryption `true`
   → senha: `g` (gerar, 256 bits) → salt: `g` (gerar). **Guarde as duas no seu gerenciador de senhas.**
3. Envie para a VPS e apague a cópia local:

```powershell
ssh hostinger "mkdir -p ~/.config/easystok-backup && chmod 700 ~/.config/easystok-backup"; scp "$env:USERPROFILE\easystok-rclone.conf" hostinger:.config/easystok-backup/rclone.conf; Remove-Item "$env:USERPROFILE\easystok-rclone.conf"
```

> **Sem as senhas do crypt o backup é ilegível.** Se a VPS sumir, o rclone.conf vai junto: as senhas
> precisam existir fora dela (gerenciador de senhas). Para restaurar em outra máquina, recrie o
> `gdrive-crypt` com as mesmas senhas.

## B. Instalar (da sua máquina, 1 comando)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\infra\backup-externo\instalar.ps1
```

Instala o rclone em `~/bin` (confere SHA256), copia `enviar.sh` e `testar-restauracao.sh` para
`~/backup-externo/`, valida os remotes (lê só `type` e `remote`, nunca o token), cria
`gdrive-crypt:easystok` e grava o cron. Idempotente. Sem o rclone.conf, para com exit 2.
`-SoMostrar` imprime o comando sem executar.

## C. Enviar agora e testar a restauração

```powershell
powershell -ExecutionPolicy Bypass -File scripts\infra\backup-externo\enviar-agora.ps1
powershell -ExecutionPolicy Bypass -File scripts\infra\backup-externo\testar-restauracao.ps1
```

O teste baixa o dia mais recente, sobe um postgres descartável (`docker run --rm`, mesma imagem do
`shared-postgres`, porta aleatória em 127.0.0.1, sem volume), roda `pg_restore --list` e um restore
real na base `restore_teste`, lista as tabelas com contagem e confere o tar de uploads. O container
é removido mesmo em falha. Não toca no banco de produção. Rode depois do primeiro envio e uma vez por
mês.

## Falhas

`enviar.sh` termina com exit ≠ 0 e registra no log quando: falta rclone ou rclone.conf; não há dump
ou o mais recente tem mais de 26 h; o tar do volume falha; o `rclone cryptcheck` do envio não
confere. Outro envio em andamento é recusado pela trava `/tmp/easystok-backup-externo.lock`. O cron
não avisa ninguém: conferir o log até existir alerta (fora do escopo da #1253).

## Testes locais

`scripts/infra/backup-externo/retencao.test.sh` prova a retenção com datas simuladas (bash, GNU date):

```bash
docker run --rm -v "$PWD/scripts/infra/backup-externo:/t" -w /t bash:5 sh -c "apk add -q coreutils; bash retencao.test.sh"
```
