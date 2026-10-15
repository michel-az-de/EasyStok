# ADR-0061 · Conexões de integração: moradia, modo e saúde, webhook por provedor e Pix pelo Mercado Pago

- Status: Aceito (2026-10-12; registra as decisões do Felipe de 12/10, seção 2 do plano de integrações)
- Data: 2026-10-12
- Decisor: Felipe
- Emenda: revoga a D3 de 30/09 (`docs/plan/integracoes/README.md`, "Pix de pedido pelo PSP da loja") e
  volta à decisão A de 22/09 (`docs/plan/atendimento-whatsapp/08-mercado-pago.md`).
- Relacionados: épico #1633, issue #1664 (spec I1.2), issue #1665 (poda), plano
  `docs/plan/integracoes/01-plug-and-play.md`, ADR-0050 (WhatsApp direto na Meta), ADR-0055 (tier),
  ADR-0057 (notificações), ADR-0010 (RLS), ADR-0014 (`PedidoPagamento`)

## Contexto

Não havia ADR de integrações. O plano `01-plug-and-play.md` foi aprovado em 12/10 depois de duas
bancas adversariais; este ADR fixa as decisões de arquitetura dele para que cada spec não as reabra.
Fatos medidos no master em 12/10:

| # | O que existe hoje | Medida |
|---|---|---|
| 1 | Adapter HTTP de terceiro em quatro projetos | `Infra.Integrations` (Mercado Pago, WhatsApp, Meta, CEP, geocodificação, rotas, IA), `Infra.Async` (`EfiPixService`, `EfiBoletoService`, `Pagamentos/*GatewayAdapter`), `Infra.Postgre/Services` (`GeradorAutoPreenchimentoClaude` e `OpenAI`), `Infra.Notifications` (Twilio, Zenvia, `MetaCloudWhatsAppProvider`) |
| 2 | O mesmo provedor em dois projetos | Mercado Pago: cliente em `Infra.Integrations/Pagamentos/MercadoPago`, adapter e webhook em `Infra.Async/Pagamentos`. WhatsApp Meta: `Infra.Integrations/WhatsApp` e `Infra.Notifications/WhatsApp` |
| 3 | Dois modelos de pagamento | Pedido: `CobrancaPedido` com `IMercadoPagoClient` e `IEstornoPedidoGateway`, que é o caminho vivo. Fatura: `FaturaPagamento`, `PaymentAttempt`, `GatewayRoutingRule` com `IPagamentoGateway`, Router e Orchestrator |
| 4 | A cadeia do modelo de fatura não tem chamador | `IPagamentoOrchestrator`: zero injeções e zero testes. `IPagamentoGatewayRouter`: só o Orchestrator consome. `IGatewayHealthStore`: única implementação é `NoopGatewayHealthStore`. `MercadoPagoGatewayAdapter.CriarAsync` e `StripeGatewayAdapter.CriarAsync` lançam `NotImplementedException` |
| 5 | Duas decisões de Pix em sentidos opostos | 22/09: "Mercado Pago como gateway único de pedidos". 30/09 (D3): "PSP próprio do tenant", com certificado `.p12`. Nenhuma das duas estava em ADR |
| 6 | Custo de corrida não tem onde morar | `Viagem`, `ParadaViagem`, `Entregador` e `ChamadoEntregador` não têm campo de custo. `CorridaExterna` só existe no plano |
| 7 | Credencial e configuração são globais | `.env` e restart; a tela de integrações do console é simulada |

Não foi verificada resolução dos tipos do item 4 por reflexão; a poda (#1665) confere antes de apagar.

## Decisão

### D1 · Moradia de adapter

| O que é | Onde mora |
|---|---|
| HTTP para provedor externo | `EasyStock.Infra.Integrations/<família>` |
| O que lê tenant, cofre ou banco para montar a credencial de uma loja | `EasyStock.Infra.Postgre/Services/<família>` (molde: `RemetenteWhatsAppDoTenant`) |
| Canal de notificação de plataforma (ADR-0057) | `EasyStock.Infra.Notifications`, como está |

Nada novo em `EasyStock.Infra.Async/Pagamentos` nem atrás de `IPagamentoGateway`. O que já está nesses
lugares e tem chamador fica onde está até a fatia que o tocar; mover por mover não é trabalho deste ADR.

### D2 · Uma conexão por loja, com dois eixos

A conexão de uma loja com um provedor (`conexao_integracao`) guarda dois eixos que não se misturam.

| Eixo | Valores | Quem escreve |
|---|---|---|
| Modo | `NaoConfigurado -> Producao <-> Desconectado`; `Teste` só onde o provedor tem sandbox | A loja, por ação na tela |
| Saúde | resultado do último teste e da vigia | O vigia, por `ExecuteUpdate`, sem regravar a linha |

Regras que valem para toda família:

- Conexão que já foi real nunca usa simulador. Em Production nenhum simulador é construído para conexões
  operacionais; a única exceção é o laboratório de tenant dedicado descrito na D2.1.
- `Desconectado` nunca cai no token global. Desconectar vira `SomenteConsulta` até a última cobrança ou
  corrida aberta; depois revoga e apaga.
- Credencial ausente, revogada ou ilegível nunca equivale a "sem pagamento": a cobrança vai a
  `AguardandoCredencial` e volta quando a conexão volta.
- A credencial fica no cofre por loja (RLS, KEK, cifra versionada) e chega ao cliente HTTP por
  `ICredencial<Família>DaLoja`, scoped. Mapas usam credencial de plataforma, sem cartão para a dona.

### D2.1 · Exceção explícita: laboratório de logística #1693

Felipe autorizou em 12/10/2026 a publicação de uma demonstração navegável do sprint de entregas em
**tenant dedicado**, com backend e persistência reais e simulação limitada às integrações externas.
O propósito é testar cotação, agrupamento, contratação, acompanhamento, devolução e caixa sem contratar
motoristas nem usar clientes reais.

- O único tenant elegível é `Logistica:EmpresaDemonstracaoId`, configurado explicitamente. Empresa vazia,
  configuração ausente ou empresa diferente deve ser recusada no servidor.
- Em Production os simuladores deste laboratório só atendem esse tenant. Não há fallback de credencial,
  indisponibilidade ou erro para simulação; **nenhuma conexão real usa simulador**.
- Os dez pedidos, contatos, destinos e saldo inicial são sintéticos e identificados como demonstração.
  Não importar clientes reais nem misturar lançamentos com o Caixa de uma loja operacional.
- Mensagens externas permanecem bloqueadas para esses contatos. O ensaio não envia WhatsApp, não chama
  entregador real, não cobra cartão e não produz link de rastreio externo fictício como se fosse válido.
- O indicador de simulação vem da API e é mostrado pelo Console. Registro manual/em mãos é um evento
  local do tenant de demonstração, não comprovação de serviço prestado fora do laboratório.
- A entidade de despacho e seu custo neste laboratório não substituem a decisão D5 para integrações
  operacionais. A ativação comercial continua sujeita ao contrato, credencial, gate e revisão da fatia dona.

QA automatizado, inspeção visual, publicação verificada e aceite humano são evidências separadas.
Personas encenadas de Thatiane/atendente não contam como aceite humano. Homologar o laboratório não
homologa Uber, Lalamove, 99, WhatsApp ou cobrança real. O relatório e suas pendências ficam na
[seção 7 do plano M8](../plan/erp-casa-da-baba/09-m8-entregas.md#7-sprint-de-logística-1693-12102026).

Para retirar a exceção operacionalmente, remover a configuração do tenant demonstrativo e bloquear
suas rotas; preservar os registros para auditoria. Não transformar a demonstração em conexão real por
troca de token ou retirada do indicador visual.

### D3 · Webhook por provedor

A identificação da loja nunca substitui a assinatura.

| Provedor | Quem autentica | Como acha a loja |
|---|---|---|
| Mercado Pago | HMAC com o segredo da aplicação (um só) | `user_id` do corpo; reconsulta com o token dessa loja; só confirma se o `collector_id` do pagamento for o `user_id` da conexão |
| Meta | `X-Hub-Signature-256` da aplicação | `phone_number_id` |
| Lalamove | Assinatura com a chave e o segredo da loja | Chave opaca no caminho da URL; o cadastro da URL exige 200 na sondagem |

O webhook reconsulta o provedor antes de mudar estado. 401 ou 404 com pendência responde 5xx. A entrada
continua em `WebhookGatewayController`, com `IWebhookSignatureValidator` e `WebhookRecebido`.

### D4 · Pix do pedido pelo Mercado Pago

Todo pagamento de pedido, Pix incluído, passa pelo Mercado Pago. A D3 de 30/09 fica revogada: não há
PSP próprio da loja para pedido nem certificado `.p12` no cofre. A loja conecta por OAuth ("Conectar com
Mercado Pago"), com a aplicação modelo Marketplace na conta da plataforma.

Pix não é pagável no ambiente de teste do Mercado Pago. O aceite do Pix é um Pix real de R$ 1 em
produção, com estorno. Efí continua onde já está vivo (`IEfiPixService` em contas a receber); este ADR
não o toca.

### D5 · `CorridaExterna` é o lugar único do custo de corrida

O custo de uma corrida feita por terceiro (99 registrada à mão, Lalamove pela API) mora em uma entidade
só, `CorridaExterna`, ligada à parada. `Viagem`, `ParadaViagem` e `Entregador` não ganham campo de custo.
A 99 entra por registro manual com custo; adapter da 99 só com a API confirmada por escrito. A Lalamove
entra com adapter no sandbox e flag desligada em produção; o gate de produção do plano de logística
(#1597) continua valendo.

### D6 · `IPagamentoGateway` está fechado para adapter novo

Nenhum provedor novo implementa `IPagamentoGateway`. Pagamento de pedido usa `IMercadoPagoClient` com a
credencial da loja. Router, Orchestrator e `IGatewayHealthStore` saem pela issue #1665, uma fatia por PR;
o que tem tabela (`GatewayRoutingRule`, `PaymentAttempt`) sai com migration, em tier alto, pela fila
única de migration do plano.

## Consequências

- Uma sessão nova sabe onde pôr um adapter sem ler o plano inteiro.
- Os dois projetos que hoje dividem Mercado Pago e WhatsApp continuam divididos até a fatia dona; a
  dívida fica visível na tabela do contexto, não escondida.
- A regra dos dois eixos custa uma tabela e um vigia, e em troca a tela diz "funcionando, testado às
  18:45" sem depender de restart.
- Sem `.p12`, o cofre guarda só token e segredo; a rotação fica mais simples.
- O Pix só fecha com dinheiro real. Sandbox não prova Pix.
- Podar a cadeia de fatura remove código sem chamador, mas `PaymentAttempt` e `GatewayRoutingRule` têm
  tabela: sair exige migration e decisão própria na #1665.

## Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| Framework genérico de integrações antes das fatias | Reprovado pela banca da v1: framework antes de valor |
| Pix pelo PSP da loja (D3 de 30/09) | Exige `.p12` com validade e rotação no cofre e um segundo modelo de cobrança; revogado pelo dono em 12/10 |
| Achar a loja do webhook do Mercado Pago pelo token OAuth | Contrário à documentação oficial; reprovado pela banca da v2 |
| Um campo de custo em `ParadaViagem` | Mistura o snapshot de quem levou com o que foi pago a terceiro, e não comporta corrida refeita |
| Novo adapter atrás de `IPagamentoGateway` | A cadeia não tem chamador; cresceria o lado morto do código |

## Rollback

Marcar este ADR como Superseded pelo que o substituir. A D4 volta atrás reabrindo a D3 em ADR novo.
