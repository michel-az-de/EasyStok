# Backup do EasyStok no Google Drive (restic)

Issue #2977, [ADR-0068](../adr/0068-backup-restic-drive-compartilhado.md). Scripts em `scripts/backup/`.
Substitui o `docs/dev/backup-externo.md` da #1253. Desde a #3027, a autenticação é login OAuth do
rclone, não chave JSON de conta de serviço (a política da organização bloqueia a criação de chaves).

| Peça | Onde | Quando |
|---|---|---|
| `backup.sh` (snapshot diário) | VPS, timer de usuário `easystok-backup.timer` | 03:00 de Brasília |
| `backup.sh pre-deploy` | VPS, chamado pelo `vps-deploy.sh` | a cada deploy |
| `estacao/puxar.ps1` (cópia local, retenção, prune guardado) | estação local | 06:30 |
| `estacao/vigiar.ps1` (batimento) | estação local | 08:00 |
| `estacao/restaurar.ps1` (prova dos 4 itens) | estação local | dia 1, 09:00 |

Estado da última execução na VPS: `~/backup-restic/estado-diario` e `estado-pre-deploy` (`rc=0` é
sucesso). Relatórios da estação: `%LOCALAPPDATA%\easystok-backup\relatorios`. Falha vira issue com a
label `backup`.

## Remote e arquivo de configuração

- Remote `ezdrive` (type `drive`, scope `drive.file`, `team_drive` do Drive compartilhado), com
  `client_id` e `client_secret` próprios e `token` do login OAuth.
- Arquivo separado: `~/.config/easystok-backup/ezdrive.conf` na VPS e
  `%LOCALAPPDATA%\easystok-backup\ezdrive.conf` na estação. O `rclone.conf` da VPS é do backup antigo
  (`[gdrive]`, `[gdrive-crypt]`): o rclone reescreve o arquivo inteiro ao renovar token, então o
  backup novo não o usa.
- O repositório fica numa subpasta: `rclone:ezdrive:easystok-restic`.

## Instalação (Felipe, uma vez)

Os segredos são criados e colados só pelo Felipe; o roteiro com os passos está em
[backup-segredos-felipe.md](backup-segredos-felipe.md). Nenhum agente vê a senha, o `client_secret`, o
token nem as KEKs.

1. **Drive compartilhado.** Já criado: `EasyStok Backup`, ID `0AOtKvTkSaRWCUk9PVA` (final da URL).
2. **Client ID OAuth** (tipo Aplicativo para computador) no projeto `easystok-backup`. Não use o
   client ID compartilhado do rclone: ele está sendo desativado em 2026. O instalador recusa
   `client_id` vazio.
3. **Login e arquivo separado** na estação, com `rclone --config <caminho>\ezdrive.conf config`,
   escopo `drive.file`. Depois copie só esse arquivo para a VPS, para `~/.config/easystok-backup/ezdrive.conf`,
   com `chmod 600`. Nunca use `>` no `rclone.conf` da VPS.
4. **Senha e cofre.** Gere a senha do repositório no cofre de senhas e cole no prompt do roteiro.
   Anote-a em papel guardado fora do escritório. Copie para o cofre os valores de
   `EZ_CRYPTO_KEK_ID`, `EZ_CRYPTO_KEK` e, se houver, os `_ANTERIOR`.
5. **Linger.** `loginctl enable-linger` (sem sudo: a política set-self-linger permite).
6. **Instale na VPS** a partir da estação: `pwsh scripts/backup/instalar.ps1`. O instalador confere o
   SHA256 do restic, as permissões e as chaves do `ezdrive.conf` (só nomes, sem valores), cria o
   repositório só se ele não existir e liga o timer. Primeiro snapshot: `ssh hostinger 'systemctl --user start easystok-backup.service'`.
7. **Estação.** Instale o rclone (`winget install Rclone.Rclone`). O `registrar-tarefas.ps1` instala
   o restic 0.19.1 com SHA256 fixado em `%LOCALAPPDATA%\easystok-backup\bin\restic.exe`. Depois:
   `guardar-senha.ps1`, `registrar-tarefas.ps1` e `puxar.ps1`.

## Achados da prova real (rclone com scope drive.file)

- Numa subpasta, `mkdir`, `copy`, `lsf`, `check`, `delete` e `rmdir` dão `rc=0`.
- Listar a raiz do Drive compartilhado falha com 403 `insufficient authentication scopes`. Por isso
  nenhum script ou doc usa a raiz como teste de saúde; o repositório fica na subpasta
  `easystok-restic`. Para conferir, liste `ezdrive:easystok-restic`.
- Com o client ID compartilhado, cada chamada leva cerca de 20 s. Por isso os testes não dependem do
  tempo de resposta do Drive: rodam com rclone falso.

## Risco aceito pelo dono

A VPS e a estação usam o mesmo login OAuth. Um token roubado da VPS tem o mesmo poder que o da
estação, inclusive apagar snapshots e o próprio repositório. A separação de credenciais que existia
com duas contas de serviço deixa de valer. Aceito pelo dono em 2026-11-20 porque a política da
organização bloqueia chave de conta de serviço. A guarda de prune de `puxar.ps1`
(`Test-GuardaPrune`) fica como proteção contra bug de código, não contra invasão. Mitigações:
`ezdrive.conf` com modo 600; restaurar o token pela conta Google (revogar o acesso do app) se houver
suspeita de vazamento.

## Corte do backup antigo (com o GO do Felipe)

Depois de 3 snapshots diários seguidos e da primeira restauração aprovada:

1. Na VPS, tire do `crontab -e` a linha do `backup-externo/enviar.sh` e apague a pasta
   `~/backup-externo` (só os scripts; os dumps antigos ficam até a decisão do item 3).
2. Na estação, desabilite a tarefa `PuxarBackupsVPS`.
3. Decida o destino dos dados antigos no Drive pessoal e nas pastas locais da cópia antiga.

## Restaurar de verdade

```bash
export RESTIC_REPOSITORY=rclone:ezdrive:easystok-restic RESTIC_PASSWORD_FILE=~/.config/easystok-backup/restic.senha RCLONE_CONFIG=~/.config/easystok-backup/ezdrive.conf
~/bin/restic -o rclone.program=$HOME/bin/rclone snapshots --tag diario
~/bin/restic -o rclone.program=$HOME/bin/rclone restore latest --tag diario --target /tmp/restaurado
```

Em `stage/db`: aplique `globals.sql` com `psql`, crie o banco `easystock` com dono `easystock` e rode
`pg_restore -d easystock easystock.dump`. Confira as contagens com `manifesto.txt`. Em
`stage/config`: o `.env` com as KEKs. Em `stage/volumes`: um tar por volume. Apague `/tmp/restaurado`
no fim: ele tem o `.env` em claro.

## Provas do aceite (E3)

| Aceite | Como provar |
|---|---|
| 1. Snapshot diário 3 dias seguidos | `restic snapshots --tag diario` com 3 datas seguidas e `rc=0` em `estado-diario` |
| 2. Restauração aprovada nos 4 itens | relatório `restauracao-<data>.txt` com os 4 `OK` |
| 3. Nomes e conteúdo ilegíveis no Drive | listar `ezdrive:easystok-restic` (não a raiz, que dá 403): só `data/`, `index/`, `snapshots/` com nomes hash |
| 4. VPS não alcança a cópia | a VPS só escuta 80 e 443 e não tem rota para a estação; o repositório local não aparece no `ezdrive.conf` da VPS |
| 5. Morto no meio não trava o seguinte | `systemctl --user kill -s KILL easystok-backup.service` durante a execução e um `start` em seguida com `rc=0` |

## Testes

```powershell
bash scripts/backup/backup.test.sh
```
```powershell
bash scripts/backup/instalar.test.sh
```
```powershell
pwsh scripts/backup/estacao/estacao.test.ps1
```
Precisam de Docker. Os testes de `instalar` usam um `ezdrive.conf` de mentira; nenhum contata o Drive.
O workflow `backup-scripts.yml` roda os três nas PRs que mexem em `scripts/backup/`.
