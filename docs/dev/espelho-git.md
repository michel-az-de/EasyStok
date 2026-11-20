# Espelho do repositório na VPS (#2958)

Cópia bare do repositório na VPS, atualizada a cada 15 min a partir do GitHub, para dois usos:

1. **Deploy com o GitHub fora**: `scripts/deploy/vps-deploy.sh --origem espelho`.
2. **Recuperação do código**: o `backup.sh` da E3 (ADR-0068) leva o `git bundle --all` do
   espelho e o JSON de issues e PRs no snapshot diário cifrado.

O GitHub continua sendo a fonte, com CI, PR e review. O espelho só lê, e nenhum segredo é
necessário porque o repositório é público.

| Peça | Onde | Quando |
|---|---|---|
| `espelho.sh` (`git remote update --prune`) | VPS, `~/git/espelho.sh`, timer de usuário `easystok-espelho.timer` | a cada 15 min |
| Espelho bare | `~/git/EasyStok.git` | sempre |
| Issues e PRs (API pública, 100 por página) | `~/git/EasyStok-meta/issues-NNN.json` | 1 vez por dia |
| Estado da última execução | `~/git/estado-espelho` (`rc=0` é sucesso, `master=` é o SHA) | a cada execução |

O espelho fica em `~/git`, e não em `/srv/git` como a issue sugeria, porque o usuário `felipe`
não tem `sudo` sem senha e o timer roda como usuário.

## Instalação (uma vez)

Da estação, com o repositório atualizado:

```powershell
scp -r scripts/espelho hostinger:espelho-src
```

```powershell
ssh hostinger 'bash ~/espelho-src/instalar.sh && rm -rf ~/espelho-src'
```

A primeira sincronização baixa cerca de 70 MB. O instalador avisa se o linger estiver desligado.

## Deploy com o GitHub fora

```powershell
bash scripts/deploy/vps-deploy.sh --origem espelho
```

A guarda da #1336 continua valendo, agora contra o master do espelho. O log mostra
`origem: ESPELHO da VPS`. Se o espelho estiver atrasado, o master dele é o último que chegou antes
da queda. Confira o `master=` em `~/git/estado-espelho`.

## Restaurar o código sem o GitHub

Do espelho, na VPS:

```bash
git clone ~/git/EasyStok.git /tmp/EasyStok
```

Do bundle no backup (com o restic da E3 instalado):

```bash
restic restore latest --tag diario --include /git --target /tmp/r
git bundle verify /tmp/r/<caminho>/git/easystok.bundle
git clone /tmp/r/<caminho>/git/easystok.bundle EasyStok
```

## Pendências

- O envio do bundle ao Drive depende da E3 instalada, que espera a conta do Drive (#2977).
- A restauração mensal do bundle e o alerta de falha do espelho entram na estação junto com a E3.

## Desligar

```bash
systemctl --user disable --now easystok-espelho.timer
rm -rf ~/git
```
