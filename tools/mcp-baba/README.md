# mcp-baba

Servidores MCP da Casa da Baba. Spec: [docs/plan/mcp-casa-da-baba](../../docs/plan/mcp-casa-da-baba/README.md). Roda na maquina de quem usa; nada fica exposto na internet.

Hoje: `baba-diag` (somente leitura, #2521).

## Usar (maquina do Felipe ou da Thatiane)

```bash
cd tools/mcp-baba && npm ci
```

Registre no Claude Code (`~/.claude.json` ou `claude mcp add`):

```bash
claude mcp add baba-diag -- node /caminho/EasyStok/tools/mcp-baba/src/diag/server.mjs
```

A ferramenta `sobre` mostra o commit local do servidor e o alvo SSH.

Variaveis (todas opcionais):

| Variavel | Padrao |
|---|---|
| `BABA_SSH_TARGET` | `baba-mcp` (entrada do `~/.ssh/config`; **nunca** `hostinger`, o codigo recusa) |
| `BABA_API_BASE` | `https://api.easystok.online` |

`~/.ssh/config` da pessoa:

```
Host baba-mcp
  HostName <mesmo host da VPS>
  User baba-mcp
  IdentityFile ~/.ssh/baba-mcp_ed25519
  IdentitiesOnly yes
```

## Instalar na VPS (Felipe, uma vez, como root)

A chave de deploy e root; o MCP usa chave propria presa ao `gate.sh`.

```bash
useradd -m -s /bin/bash baba-mcp            # sem grupo docker
install -d -o root -g root -m 755 /opt/baba-mcp
install -o root -g root -m 755 scripts/mcp/gate.sh /opt/baba-mcp/gate.sh
echo 'baba-mcp ALL=(root) NOPASSWD: /opt/baba-mcp/gate.sh --root *' > /etc/sudoers.d/baba-mcp
echo "Defaults!/opt/baba-mcp/gate.sh env_reset,!setenv" >> /etc/sudoers.d/baba-mcp
chmod 440 /etc/sudoers.d/baba-mcp && visudo -c
# leitura do historico de deploy (ajuste se o caminho for outro)
setfacl -m u:baba-mcp:x /home/felipe /home/felipe/build && setfacl -m u:baba-mcp:r /home/felipe/build/deploy-history.log
# chave publica da pessoa (uma linha por pessoa), restrita ao gate:
install -d -m 755 -o root -g root /home/baba-mcp/.ssh
echo 'command="/opt/baba-mcp/gate.sh",restrict ssh-ed25519 AAAA... pessoa' >> /home/baba-mcp/.ssh/authorized_keys
chown root:root /home/baba-mcp/.ssh /home/baba-mcp/.ssh/authorized_keys && chmod 755 /home/baba-mcp/.ssh && chmod 644 /home/baba-mcp/.ssh/authorized_keys   # root e dono: baba-mcp nao reescreve a propria chave
```

Comandos aceitos pelo gate: `logs <ez-api|ez-worker|ez-web> <5m|2h> <1..5000>`, `containers`, `deploy_historico <1..200>`. O restante e recusado (exit 64).

## Testes

```bash
npm test
```

Cobrem redacao (token, JWT, senha, connection string, e-mail, CPF, telefone), recusa de injecao no gate e no cliente SSH, agrupamento de erros e horas em Brasilia.

## Limites conhecidos

- A redacao local e segunda camada. Antes de confiar nos logs do console da API, rode `logs_api` e confira que nada sensivel aparece (a redacao provada no servidor e a do sink de arquivo).
- `docker logs` usa o driver do Docker; se o container for recriado, o historico anterior some.
