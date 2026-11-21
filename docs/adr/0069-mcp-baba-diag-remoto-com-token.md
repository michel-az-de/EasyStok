# ADR-0069 · MCP baba-diag publicado em mcp.easystok.online, com token por pessoa

- Status: Aceito (Felipe no chat, 2026-11-21: "aprovo mergiar e publciar"; PR #3059). Implementação: #3062
- Data: 2026-11-21
- Decisor: Felipe
- Revisa: decisão 2 de `docs/plan/mcp-casa-da-baba/README.md` ("nenhuma porta de MCP exposta na
  internet"), só para o `baba-diag`
- Relacionados: #3057, spec #2520, `baba-diag` #2521, `tools/mcp-baba/README.md`, `scripts/mcp/gate.sh`

## Contexto

- Os servidores de `tools/mcp-baba` rodam em stdio na máquina de cada pessoa. Usar em outro computador
  pede clone do repo, Node 22+, `npm ci`, chave SSH nova, o Felipe autorizar essa chave como root na
  VPS e uma entrada `baba-mcp` no `~/.ssh/config`.
- O Felipe pediu um endereço no domínio que já temos, para instalar com um comando.
- `src/diag/server.mjs` e `src/db/server.mjs` criam um único `McpServer` no topo do módulo, ligado ao
  `StdioServerTransport`, e o SHA vem de `git` local. Do jeito que estão, não servem HTTP.
- O `baba-db` depende de uma barreira que ainda não existe: a spec (linha 104) diz que o filtro de SQL
  é higiene e que a defesa real é a policy `current_user <> 'baba_ro'`, que nenhuma migração criou.
- O Caddy compartilhado (`/opt/stacks/shared/Caddyfile`, container `shared-caddy`) publica os hosts e
  não é versionado. O `scripts/deploy/console-cache.sh` já o edita com `caddy validate` e `caddy reload`.
- A rede `edge` do compose de produção junta o Caddy, `shared-postgres`, `ez-redis` e `ez-api`.

## Decisão

1. **Só o `baba-diag` vai para a internet nesta fase.** Ferramentas de leitura: saúde, disco e fila,
   containers, erros recentes, logs da API e do worker, histórico de deploy. O `baba-db` continua
   local até a migração da policy `current_user <> 'baba_ro'` e uma ADR própria. `baba-caixa`,
   `baba-producao` e `baba-atendimento` continuam locais, porque usam o login nominal da pessoa.
2. **Container `ez-mcp`** com o código de `tools/mcp-baba` e o transporte Streamable HTTP do SDK
   oficial (já presente na versão fixada 1.32.2). O servidor vira fábrica (`criarServidorDiag()`),
   usada pelo stdio local e pelo HTTP, que cria um servidor por requisição (sem sessão). O modo stdio continua igual. O
   SHA chega por build-arg `GIT_SHA`, como nos outros serviços.
3. **Endereço:** `https://mcp.easystok.online/diag`, num bloco próprio do Caddyfile compartilhado,
   aplicado por script versionado no padrão do `console-cache.sh`: backup do arquivo, `caddy validate`,
   `caddy reload` e restauração do backup se algo falhar. Registro DNS A apontando para a VPS.
4. **Acesso ao host continua pelo `gate.sh`.** O container faz SSH para `baba-mcp` no host com uma
   chave própria, fora da imagem, em `/opt/stacks/mcp/ssh` (dono felipe, uid 1000 como o usuário do
   container, 600, montada só leitura). A linha no `authorized_keys` leva
   `from="172.16.8.0/24",command="/opt/baba-mcp/gate.sh --sem-banco",restrict`. O modo `--sem-banco`
   recusa o `psql_ro`: a chave do container só alcança `logs`, `containers` e `deploy_historico`. Sem
   `docker.sock`, sem acesso ao banco, sem grupo docker. `known_hosts` fixo, montado junto.
5. **Rede própria.** O `ez-mcp` fica só numa rede `mcp`, compartilhada com o Caddy, e não entra na
   `edge`. Sem acesso lateral a `shared-postgres`, `ez-redis` e à porta interna do `ez-api`. A saúde
   da API é lida pela URL pública.
6. **Token por pessoa.**
   - Emissão: `scripts/deploy/mcp-deploy.sh token-emitir <pessoa>`: 32 bytes aleatórios, mostrados uma
     única vez.
   - Guarda: só o SHA-256 do token, em `/opt/stacks/mcp/auth/tokens` (felipe 600, diretório montado
     somente leitura), uma linha `pessoa:sha256:validade`.
   - Validade de 90 dias, renovada pelo mesmo script.
   - Revogação: `mcp-deploy.sh token-revogar <pessoa>`. O servidor relê o arquivo a cada requisição, sem
     reinício.
   - Verificação: `Authorization: Bearer`, comparação de hash em tempo constante
     (`crypto.timingSafeEqual`), resposta 401 sem detalhe.
   - Limite: 60 requisições por minuto por token e bloqueio temporário do IP após 10 tokens inválidos.
7. **Host e Origin validados** contra `mcp.easystok.online`, contra DNS rebinding.
8. **Auditoria:** cada chamada registra data, pessoa (vinda do token), ferramenta, parâmetros
   validados e duração, sem a saída.
9. **Instalação no computador da pessoa**, só no Claude Code:

   ```
   claude mcp add --transport http --scope user baba-diag https://mcp.easystok.online/diag --header "Authorization: Bearer <token>"
   ```

   O conector do claude.ai e do Desktop exige OAuth e fica para outra ADR.
10. **Deploy:** stack própria `/opt/stacks/mcp` (`scripts/deploy/mcp/compose.yaml`), gerida por
    `scripts/deploy/mcp-deploy.sh`, separada do deploy do ERP. Repete a guarda do `vps-deploy.sh` (SHA
    igual ao `origin/master` ou descendente), a trava e o `deploy-history.log`; imagem
    `easystok-mcp:vps-<sha>`, healthcheck e volta à imagem anterior se não ficar healthy. A rede `mcp`
    (172.16.8.0/24) nunca é apagada, porque o `from=` aponta para ela.

## Segurança

| Risco | Tratamento |
|---|---|
| Token vazado | só leitura de diagnóstico, validade de 90 dias, revogação sem reinício, auditoria por pessoa |
| Furo no servidor HTTP | sem `docker.sock`, sem banco, rede própria; o único caminho ao host é o `gate.sh` com allowlist |
| Chave SSH do container | `from=` preso à subnet, `command=` no gate, `restrict`; segredo fora da imagem |
| Varredura do endpoint | 401 sem detalhe, limite por token e por IP, Host e Origin validados |
| Identidade por pessoa no SSH some | a chave do container é uma só; quem chamou vem do token e fica na auditoria |

## LGPD

Os logs da API e do worker podem trazer nome e endereço de cliente. A redação local cobre token, JWT,
senha, connection string, e-mail, CPF e telefone, mas não nome nem endereço. Tratamento:

- Tokens só para quem já lê esses logs pelo SSH hoje (operação da Casa da Baba).
- A saída das ferramentas não entra na auditoria e o servidor não guarda cache das respostas.
- A redação cobre nome, endereço e CEP por chave (JSON ou chave=valor) e CEP com hífen (#3062).

## Alternativas

| Opção | Por que não |
|---|---|
| Continuar local, com script de instalação | ainda pede Node, clone e chave SSH por máquina |
| Pacote baixável em `downloads/`, que o Caddy já serve | mantém a decisão 2, mas a chave SSH por pessoa continua |
| Rede privada (Tailscale, WireGuard) | cliente em cada aparelho e não serve ao conector do claude.ai depois |
| Publicar também o `baba-db` | a barreira real (policy) ainda não existe; token vazado viraria leitura do banco |

## Rollback

`scripts/deploy/mcp-deploy.sh descer` tira o bloco do Caddyfile e derruba a stack; apagar a linha da
chave do container no `authorized_keys`. O modo stdio local segue disponível o
tempo todo.

## Consequências

- Instalar em outro computador vira um comando e um token.
- A implementação é tier alto (Dockerfile, compose, script de deploy, `gate.sh`): #3062.
- A spec e o `tools/mcp-baba/README.md` apontam para esta ADR na decisão 2.
- Ficam fora: OAuth para claude.ai e Desktop; `baba-db` remoto depois da migração da policy.
