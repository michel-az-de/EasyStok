# Bateria de homologação do atendimento (#1318)

Simula clientes reais contra uma API local isolada e mede a aderência do atendimento. Nada sai para a Meta nem para o Mercado Pago de verdade.

| Peça | Função |
|---|---|
| `compose.homologacao.yml` | Sobrepõe `docker-compose.local.yml`: API na porta 18510, Meta falsa, Mercado Pago falso, agente pelo proxy. Segredos vêm do ambiente (`FW_KEY`, `META_APPSECRET`), nunca do arquivo |
| `fake_meta.py` | Graph API falsa (porta 18620): grava envios, devolve `wamid`, manda `sent`, `delivered`, `read` por webhook assinado, serve mídia. Telefone terminado em 9999 ou 8888 injeta erro |
| `fake_mp.py` | Mercado Pago falso (18630): preferência, consulta, busca, estorno; `POST /_pagar` aprova o pagamento e dispara o webhook assinado |
| `proxy_modelo.py` | Proxy do agente (18610): registra latência e tokens por chamada, sem gravar cabeçalhos |
| `sim.py` | Cliente WhatsApp (`wa`, `btn`, `img`) e chat do site (`site`) |
| `bateria1.py`, `bateria2.py`, `seg.py`, `fluxo_pedido.py` | Cenários de cliente, casos difíceis, segurança do webhook, pedido completo |
| `matriz.py`, `render.py`, `spec_corpo.md` | Matriz de cenários, nota de aderência e geração de `docs/plan/atendimento-whatsapp/13-homologacao-organica.md` |

Pré-requisitos: Docker, Python 3.12, o segredo de teste do app da Meta em `~/.easystok/meta-appsecret-teste-local` e a chave do modelo em `FW_KEY`. Suba o Worker junto (sem ele o outbox não anda). O agente é estocástico: para aceite, repita 5 rodadas.

## Onda 0: ciclo manual em API real local

`onda0-http.ps1` exercita login, abertura da loja e caixa, pedido avulso fracionado, cozinha, pagamento manual, entrega, cancelamento de outro pedido pago e fechamento. Cada etapa relê a API. Só aceita URL loopback, uma loja chamada `Cozinha de Teste`, nenhum pedido anterior e caixa ainda não movimentado. Altera dados de teste e não é um roteiro para produção. Use uma base nova a cada execução; ele não apaga nem reseta bases.

Preparação usada em 09/10/2026: PostgreSQL 17 Alpine em container exclusivo, porta aleatória publicada em `127.0.0.1`; API compilada desta árvore; console com `VITE_FONTE_DADOS=api` e `API_ALVO=http://127.0.0.1:18540`. Não subir `compose.homologacao.yml` sobre stacks existentes: seus nomes e portas são fixos.

Para a API, utilizar as configurações já existentes de `docker-compose.local.yml`, com conexão apontando exclusivamente à base descartável e estas substituições no processo local:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:18540'
$env:Database__Provider = 'PostgreSql'
# ConnectionStrings__DefaultConnection deve apontar à base local NOVA.
$env:ConnectionStrings__Redis = ''
$env:Anthropic__Enabled = 'false'
$env:OpenAI__Enabled = 'false'
$env:SEED_DEMO_DATA = 'false'
$env:SEED_ADMIN_EMAIL = 'onda0@easystock.local'
$env:SEED_ADMIN_PASSWORD = 'Onda0Local@123' # somente ambiente descartável
$env:SEED_EMPRESA_NOME = 'Casa da Baba Homologacao Onda 0'
$env:SEED_LOJA_NOME = 'Cozinha de Teste'
$env:GIT_SHA = git rev-parse HEAD
# Com JWT local configurado como em docker-compose.local.yml, iniciar no diretório EasyStock.Api:
dotnet bin/Debug/net10.0/EasyStock.Api.dll
```

Em outro terminal, na raiz do repositório:

```powershell
./scripts/homologacao/onda0-http.ps1 -ExpectedSha (git rev-parse HEAD) -Senha 'Onda0Local@123'
```

O resultado esperado é R$ 100 de abertura + R$ 25 recebidos = R$ 125. O segundo pedido, de R$ 15, é recebido e cancelado; seu valor deixa de compor o caixa. O cenário avulso não prova baixa de produto: estoque/lotes são cobertos separadamente por `PedidoVendaCaixaIntegrationTests`, `EstoqueWorkflowsIntegrationTests` e `PedidoEstoqueIntegrationServiceTests`.

Para a versão servida, reutilizar `verificar-http.mjs` com as quatro variáveis `HOMOLOGACAO_API_URL`, `HOMOLOGACAO_CONSOLE_URL`, `HOMOLOGACAO_API_SHA` e `HOMOLOGACAO_CONSOLE_SHA`. O HTML compilado precisa do marcador `easystok-console-sha`, como no script existente `scripts/deploy/console-deploy.sh`. Não carimbar um SHA em fontes de outra revisão.

Limites: HTTP não comprova usabilidade, IndexedDB, impressão física, WhatsApp, Mercado Pago ou transportadora reais. A restauração de backup da produção usa os scripts separados em `scripts/infra/backup-externo/`; restauração local de massa sintética não comprova o backup externo da VPS.
