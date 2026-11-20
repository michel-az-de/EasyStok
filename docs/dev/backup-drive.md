# Backup do EasyStok no Google Drive (restic)

Issue #2977, [ADR-0068](../adr/0068-backup-restic-drive-compartilhado.md). Scripts em `scripts/backup/`.
Substitui o `docs/dev/backup-externo.md` da #1253.

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

## Instalação (Felipe, uma vez)

Os segredos são criados e colados só pelo Felipe; o roteiro com os valores reais está em [backup-segredos-felipe.md](backup-segredos-felipe.md). Nenhum agente vê a senha, as chaves das contas de
serviço nem as KEKs.

1. **Drive compartilhado.** No Drive da empresa, crie o Drive compartilhado do backup. Anote o ID (o
   final da URL).
2. **Duas contas de serviço** num projeto do Google Cloud da empresa, com a API do Drive ligada:
   - `ez-backup-vps`: membro do Drive compartilhado como **Gerente de conteúdo** (não apaga
     definitivamente). A chave JSON vai só para a VPS.
   - `ez-backup-estacao`: membro como **Administrador**. A chave JSON fica só na estação.
3. **VPS** (ssh como felipe):
   ```bash
   install -d -m 700 ~/.config/easystok-backup
   # copie a chave da ez-backup-vps para ~/.config/easystok-backup/sa.json e: chmod 600 nela
   cat > ~/.config/easystok-backup/rclone.conf <<'EOF'
   [ezdrive]
   type = drive
   scope = drive
   service_account_file = /home/felipe/.config/easystok-backup/sa.json
   team_drive = <ID do Drive compartilhado>
   EOF
   chmod 600 ~/.config/easystok-backup/rclone.conf
   # senha do repositório: gere no cofre de senhas e cole no prompt (não aparece na tela)
   ( umask 077; read -rs -p 'senha: ' s; printf '%s\n' "$s" > ~/.config/easystok-backup/restic.senha; unset s; echo )
   sudo loginctl enable-linger felipe
   ```
4. **Papel e cofre.** Anote a senha do repositório em papel guardado fora do escritório. Copie para
   o cofre de senhas os valores de `EZ_CRYPTO_KEK_ID`, `EZ_CRYPTO_KEK` e, se houver, os `_ANTERIOR`.
5. **Instale na VPS** a partir da estação:
   ```powershell
   pwsh scripts/backup/instalar.ps1
   ```
   O instalador confere SHA256 do restic, permissões e chaves do `rclone.conf` sem ler segredo, cria o
   repositório só se ele não existir e liga o timer. Primeiro snapshot na hora:
   `ssh hostinger 'systemctl --user start easystok-backup.service'`.
6. **Estação.** Copie a chave da `ez-backup-estacao` para `%LOCALAPPDATA%\easystok-backup\sa.json`
   e crie `%LOCALAPPDATA%\easystok-backup\rclone.conf` igual ao da VPS, apontando para ela. Depois:
   ```powershell
   pwsh scripts/backup/estacao/guardar-senha.ps1
   ```
   ```powershell
   pwsh scripts/backup/estacao/registrar-tarefas.ps1
   ```
   ```powershell
   pwsh scripts/backup/estacao/puxar.ps1
   ```

## Corte do backup antigo (com o GO do Felipe)

Depois de 3 snapshots diários seguidos e da primeira restauração aprovada:

1. Na VPS, tire do `crontab -e` a linha do `backup-externo/enviar.sh` e apague a pasta
   `~/backup-externo` (só os scripts; os dumps antigos ficam até a decisão do item 3).
2. Na estação, desabilite a tarefa `PuxarBackupsVPS`.
3. Decida o destino dos dados antigos no Drive pessoal e nas pastas locais da cópia antiga.

## Restaurar de verdade

```bash
export RESTIC_REPOSITORY=rclone:ezdrive:easystok-restic RESTIC_PASSWORD_FILE=~/.config/easystok-backup/restic.senha RCLONE_CONFIG=~/.config/easystok-backup/rclone.conf
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
| 3. Nomes e conteúdo ilegíveis no Drive | listar o Drive compartilhado: só `data/`, `index/`, `snapshots/` com nomes hash |
| 4. VPS não alcança a cópia | a VPS só escuta 80 e 443 e não tem rota para a estação; o repositório local não aparece no `rclone.conf` da VPS |
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
Precisam de Docker. O workflow `backup-scripts.yml` roda os três nas PRs que mexem em `scripts/backup/`.
