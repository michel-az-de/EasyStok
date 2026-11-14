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
  IdentityFile C:/Users/<voce>/.ssh/baba-mcp_ed25519
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

## baba-db (leitura do banco)

Servidor `node src/db/server.mjs`; tools `sobre`, `tabelas`, `consultar` (um SELECT/WITH/EXPLAIN, ate 1000 linhas, CSV redigido). Na VPS, como root:

```bash
docker exec -i shared-postgres psql -U <superusuario> -d easystock -v baba_ro_pw="$SENHA" -f - < /opt/baba-mcp/baba_ro.sql
install -m 600 -o root -g root /dev/null /opt/baba-mcp/baba_ro.env   # preencher: PG_CONTAINER=shared-postgres PG_DB=easystock PG_USER=baba_ro PGPASSWORD=... EMPRESA_ID=<uuid da Casa da Baba>
```

Instale tambem a lista de funcoes permitidas: `install -m 644 -o root -g root scripts/mcp/psql_ro_funcoes.txt /opt/baba-mcp/` (o gate recusa qualquer funcao fora dela, e comentarios SQL).

Reexecute `baba_ro.sql` apos migracao que crie tabela de negocio. Entrada de MCP: `command: node`, `args: [.../src/db/server.mjs]`.

## Testes

```bash
npm test
```

Cobrem redacao (token, JWT, senha, connection string, e-mail, CPF, telefone), recusa de injecao no gate e no cliente SSH, agrupamento de erros e horas em Brasilia.

## Limites conhecidos

- A redacao local e segunda camada. Antes de confiar nos logs do console da API, rode `logs_api` e confira que nada sensivel aparece (a redacao provada no servidor e a do sink de arquivo).
- `docker logs` usa o driver do Docker; se o container for recriado, o historico anterior some.

## baba-caixa (#2524)

Consulta e opera o caixa pela API (regras de negocio ficam la; o nivel do usuario limita o que cada pessoa faz). Nao usa SSH nem SQL.

| Variavel | Valor |
|---|---|
| `BABA_API_BASE` | `https://api.easystok.online` (so https, ou http em localhost) |
| `BABA_API_EMAIL`, `BABA_API_PASSWORD` | login nominal da pessoa, **so no ambiente local** |
| `BABA_EMPRESA_ID` | opcional, se o login tiver mais de uma empresa |
| `BABA_CAIXA_ESCRITA` | `1` habilita escrita; padrao desligada |

Leitura: `caixa_dia`, `caixa_movimentos`, `caixa_linhas`, `caixa_fechamentos`. Escrita (`caixa_abrir`, `caixa_fechar`, `caixa_lancar_movimento`): so com `BABA_CAIXA_ESCRITA=1` **e** confirmacao da pessoa no cliente (elicitation do MCP, fora do canal do modelo); cliente sem suporte a confirmacao recusa. O cliente HTTP so fala com `/api/auth/login` e `/api/caixa/*`. Registro: `node tools/mcp-baba/src/caixa/server.mjs` (script `npm run caixa`).

Atencao: o login da API sem dispositivo revoga as outras sessoes web da mesma conta (AuthController). Use para o MCP uma conta propria (ou aceite que o navegador deslogue ao expirar o token). Um timeout numa escrita pode ter gravado no servidor: confira o caixa antes de repetir. As leituras devolvem `data` e `meta` (total, paginas): ao somar um periodo, confira `meta.pages`.

## baba-producao (#2525)

Mesma conta e variaveis do baba-caixa (`BABA_API_*`), pela API de producao do atendimento. Leitura: `producao_estoque_do_dia`, `producao_sugestao`, `producao_receitas`, `producao_receita`, `producao_insumos`, `producao_perdas`, `producao_perdas_vencidos`. Escrita: so `producao_lancar_perda` (baixa o estoque), com `BABA_PRODUCAO_ESCRITA=1` **e** confirmacao da pessoa no cliente; acima de R$ 50 a API exige Gerente. Registrar producao do dia e editar receita ficam de fora: tem fluxo proprio no app. Cliente HTTP liberado para `/api/atendimento/producao/*`. Script `npm run producao`. Timeout numa escrita pode ter gravado: confira `producao_perdas` antes de repetir.
No PowerShell 5.1 `~` nao expande em argumentos: gere a chave com `ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\baba-mcp_ed25519"`. Instale na VPS as copias de `scripts/mcp/` em LF (o `.gitattributes` garante no checkout; se vier CRLF, `tr -d '\r'`). O `baba_ro.sql` pula (com NOTICE) tabela sem RLS, como `empresas`.
