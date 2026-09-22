# Mercado Pago — cadastro da integração e o que falta no código (S32–S33)

Objetivo: deixar o Mercado Pago cadastrado (conta, aplicação, credenciais, webhook) e funcionando de
ponta a ponta no EasyStok, porque hoje ele **nunca esteve vivo**: a preferência é criada, o webhook é
validado e gravado, e nada confirma o pagamento.

## 1. Estado medido em 22/09/2026

| Peça | Estado | Evidência |
|---|---|---|
| Criar preferência (Checkout Pro) | Existe, mas chama `POST v1/payments/preferences`; o endpoint documentado do Checkout Pro é `POST checkout/preferences`. Tratar como defeito até prova em sandbox | `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs:48` |
| Configuração | Seção `MercadoPago` com `AccessToken`, `BaseUrl`, `NotificationUrl`, `BackUrlSuccess`, `BackUrlFailure`, `BackUrlPending`; `MercadoPago:UseStub` (true em Development); `MercadoPago:WebhookSecret`; `MercadoPago:WebhookAllowUnsigned` | `MercadoPagoOptions.cs`, `MercadoPagoServiceCollectionExtensions.cs:17-26`, `Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs:26-30` |
| Validação da assinatura do webhook | Existe: `x-signature` (`ts=…,v1=…`) + `x-request-id`, HMAC-SHA256 sobre `id:{data.id};request-id:{x-request-id};ts:{ts};` com `MercadoPago:WebhookSecret`. Registrada só quando `AccessToken` está preenchido ou `WebhookAllowUnsigned=true` | `MercadoPagoSignatureValidator.cs:12-16`; `Async/DependencyInjection/ServiceCollectionExtensions.cs:175-181` |
| Processador do webhook | **Não existe.** Só `EfiPixWebhookProcessor` implementa `IGatewayWebhookProcessor`. Sem processor, o controller grava `WebhookRecebido(Sucesso=false)` e responde **500** | `Api/Controllers/WebhookGatewayController.cs:108-115`; `ServiceCollectionExtensions.cs:101` |
| Consulta do pagamento (`GET v1/payments/{id}`) | Não existe | grep em Api, Application, Async, Integrations |
| Transição `AguardandoPagamento → AguardandoAprovacaoBaba` | Nenhum código faz; só `Aprovar`/`Recusar` leem esse estado | grep em `AprovarPedidoStorefrontUseCase.cs:75`, `RecusarPedidoStorefrontUseCase.cs:92` |
| Estorno | Stub que devolve falha | `Async/Pagamentos/MercadoPagoGatewayAdapter.cs:66-67` |
| Adapter de gateway (`IPagamentoGateway`) | `CriarAsync` lança `NotImplementedException`; `ConsultarAsync` devolve `Desconhecido` | `MercadoPagoGatewayAdapter.cs:44-64` |
| Credenciais em produção | Nenhum `MercadoPago__*` em `render.yaml`, `fly.toml` ou `docker-compose.azure.yml` | grep nos três |
| Checkout do site | `IniciarCheckoutUseCase` usa MP; `IniciarCheckoutGuestUseCase` **não** (vai para aprovação sem cobrar) | contagem de `IMercadoPagoClient` nos dois arquivos |
| Bloqueio externo | casa-da-baba #10 `[HUM-004] Conta Mercado Pago + credenciais sandbox + producao`, aberta, com os passos já escritos | `gh issue view 10 --repo michel-az-de/casa-da-baba` |

## 2. Decisão de gateway (só o Felipe)

| Opção | O que é | A favor | Contra |
|---|---|---|---|
| **A. Mercado Pago como gateway único de pedidos** (site e conversa) | O agente manda o link `init_point` da preferência; o cliente escolhe Pix ou cartão na página do MP. Efi fica só para contas a receber (FMA) | Uma integração, um webhook, um estorno, uma conciliação. Cartão de graça. Assinatura HMAC pronta. O site já usa | Um toque a mais no chat (abre página). Taxa de Pix maior que na Efi |
| **B. Mercado Pago no site, Efi Pix na conversa** (como o plano está em S11) | Duas integrações vivas | Pix copia-e-cola direto no chat | Dois webhooks, dois estornos, duas conciliações, duas credenciais para manter |

Recomendação: **A**. A justificativa é a mesma do ADR-0048: operação solo, resultado mais rápido. O
código que falta para o MP (S32) tem de ser escrito de qualquer jeito para o site funcionar; com ele
pronto, a conversa custa uma spec pequena (S33). Se A for a escolha, S11 muda de "cobrança Pix Efi" para
"preferência MP" e `CobrancaPedido` guarda `PreferenceId` e `PaymentId` em vez de `Txid`/`E2eId`.

## 3. Cadastro no Mercado Pago (onda 0.9, fora do código)

Tudo em gerenciador de senhas. Nada em issue, commit ou chat.

| # | Passo | Onde | Resultado |
|---|---|---|---|
| 1 | Conta Mercado Pago da Casa da Baba (CNPJ ou CPF da Tatiana; conta de pessoa jurídica facilita conciliação e nota) | mercadopago.com.br | conta verificada, dados bancários para saque |
| 2 | Aplicação "Casa da Baba" em **Suas integrações**, produto **Checkout Pro**, modelo de integração "Pagamentos online" | mercadopago.com.br/developers | `Client ID`, `Client Secret` |
| 3 | Credenciais de **teste**: `Access Token` `TEST-…` e `Public Key` `TEST-…` | aplicação → Credenciais de teste | para sandbox e E2E |
| 4 | Credenciais de **produção**: `Access Token` `APP_USR-…` e `Public Key` `APP_USR-…` (exige conta verificada e, para Checkout Pro, homologação da integração) | aplicação → Credenciais de produção | para o ambiente que serve `casadababa.com` |
| 5 | Webhook: URL `https://<host da API em produção>/api/webhooks/mercadopago`, evento **Pagamentos** (`payment`), modo produtivo; repetir em modo de teste com a URL de homologação. Copiar a **assinatura secreta** | aplicação → Webhooks | `MercadoPago:WebhookSecret` |
| 6 | Usuários de teste (comprador e vendedor) para o sandbox | aplicação → Contas de teste (ou `POST /users/test_user` com `{"site_id":"MLB"}` usando o token de produção) | dois logins de teste |
| 7 | Cartões de teste: nome do titular `APRO` aprova, `OTHE` recusa; CVV `123`; validade futura | documentação do MP ("Cartões de teste") | roteiro do smoke |
| 8 | Confirmar qual host serve a API em produção hoje (o `render.yaml`, os `fly.*.toml` e o `docker-compose.azure.yml` divergem; ver P06) antes de cadastrar a URL do webhook | repositório | URL única |

Sem os passos 4 e 5, nada em produção; sem 3 e 6, nada em sandbox. Fecha casa-da-baba #10.

## 4. Configuração no EasyStok (nomes exatos)

Variáveis de ambiente no alvo de deploy da API (formato `Secao__Chave`), nunca em `appsettings.json`:

| Chave | Valor | Obrigatória |
|---|---|---|
| `MercadoPago__UseStub` | `false` | sim (o default de Development é `true`) |
| `MercadoPago__AccessToken` | `APP_USR-…` em produção; `TEST-…` em homologação | sim |
| `MercadoPago__BaseUrl` | `https://api.mercadopago.com/` (default; só sobrescrever em teste) | não |
| `MercadoPago__NotificationUrl` | `https://<host da API>/api/webhooks/mercadopago` (vai dentro da preferência) | sim |
| `MercadoPago__BackUrlSuccess` | `https://casadababa.com/pedido-status.html?resultado=sucesso` (definir com o front) | sim |
| `MercadoPago__BackUrlFailure` | `https://casadababa.com/pedido-status.html?resultado=falha` | sim |
| `MercadoPago__BackUrlPending` | `https://casadababa.com/pedido-status.html?resultado=pendente` | sim |
| `MercadoPago__WebhookSecret` | assinatura secreta do passo 5 | sim |
| `MercadoPago__WebhookAllowUnsigned` | `false` (**nunca** `true` em produção; hoje `true` liga o validador sem segredo) | sim |

No site (`C:\rep\casa-da-baba`): `VITE_MP_PUBLIC_KEY` só é usada por Bricks (futuro); o Checkout Pro
por redirecionamento não precisa dela.

## 5. Specs de código

### S32 · Webhook do Mercado Pago de ponta a ponta

**Problema.** Seção 1: sem processor, o webhook responde 500 e o pedido fica em `AguardandoPagamento` para sempre; o endpoint da preferência está errado; não há consulta nem estorno.
**Abordagem.** Espelhar o `EfiPixWebhookProcessor`: idempotência, lock, consulta à fonte (`GET v1/payments/{id}`), confirmação do pedido pelo `external_reference` (= `PedidoId`), transição para `Aguardando` (S12) e registro do pagamento. Corrigir o endpoint da preferência. Implementar estorno.
**Escopo.**
- `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs`: `POST checkout/preferences` (confirmar na doc oficial de Checkout Pro no momento da implementação e registrar a URL no PR); `X-Idempotency-Key` = `PedidoId`; novo `ConsultarPagamentoAsync(paymentId) → { Id, Status (approved | pending | rejected | cancelled | refunded), StatusDetail, ExternalReference, TransactionAmount, DateApproved, PaymentMethodId (pix | credit_card | …) }` via `GET v1/payments/{id}`; novo `EstornarAsync(paymentId, valor?)` via `POST v1/payments/{id}/refunds` (com `X-Idempotency-Key`). Atualizar `IMercadoPagoClient` e `StubMercadoPagoClient` (R8).
- Criar `Async/Pagamentos/Webhooks/MercadoPagoWebhookProcessor.cs : IGatewayWebhookProcessor` (`Provedor = "MercadoPago"`): aceita `type == "payment"` (ignora os demais com log); lê `data.id`; consulta o pagamento; `external_reference` → `Pedido`; lock por pedido (`SELECT FOR UPDATE`, mesmo padrão de `AprovarPedidoStorefrontUseCase`); `approved` e valor ≥ `Pedido.Total` → `ConfirmarPagamentoPedidoUseCase` (S11, generalizado com `Provedor="mercadopago"`, `ReferenciaExterna=paymentId`): transição para `Aguardando` (ou `AguardandoAprovacaoBaba` se `RequerAprovacao`, S12), `RegistrarPagamentoPedido(metodo = pix | credito | debito conforme `payment_method_id`, referencia = paymentId)`, `PedidoPagoEvent`, impressão (S20). `rejected`/`cancelled` → `Mensagem` na conversa (se houver) e nada mais; `refunded` → registra estorno na `CobrancaPedido`.
- Registrar no DI ao lado do validador (`ServiceCollectionExtensions.cs:175-181`). Falha interna → exceção → o controller responde 500 e o MP reenvia (comportamento já existente para a Efi).
- `MercadoPagoGatewayAdapter`: `ConsultarAsync` e `EstornarAsync` passam a delegar ao client; `CriarAsync` continua não implementado (é o caminho de faturas SaaS, que sai em P02).
- Job `CobrancaPedidoJob` (S11) ganha o ramo MP: pendente há mais de 30 min → consulta a preferência/pagamentos por `external_reference` (`GET v1/payments/search?external_reference=`) antes de expirar.
- S27: `ReembolsarPedidoUseCase` usa `EstornarAsync` do MP quando `CobrancaPedido.Provedor == "mercadopago"`; o caso `reembolso_manual_necessario` some.
**Fora.** Bricks (pagamento na própria página do site); assinatura recorrente; split.
**Aceite.**
- [ ] Webhook `payment` com pagamento `approved` e `external_reference` de pedido `AguardandoPagamento` → pedido `Aguardando`, `PedidoPagamento` com referência do pagamento, `PedidoPagoEvent` no outbox, `WebhookRecebido(Sucesso=true)`, resposta 200.
- [ ] Mesmo `data.id` duas vezes → segundo é no-op (idempotência do `WebhookRecebido` + status do pedido).
- [ ] Assinatura inválida → 401 (comportamento atual) e nada gravado como sucesso.
- [ ] Pagamento `approved` com valor menor que o total → não confirma; grava motivo; 200.
- [ ] `EstornarAsync` chama `POST v1/payments/{id}/refunds` com `X-Idempotency-Key`.
- [ ] Sandbox: pedido criado no site com credencial de teste, pago com cartão `APRO`, webhook de teste recebido, pedido em `Aguardando` (roteiro na seção 6, evidência no PR com captura do `WebhookRecebido`).
**Testes (Red).** `MercadoPagoWebhookProcessorTests.ApprovedConfirmaPedido`, `...DuplicadoNoOp`, `...ValorMenorNaoConfirma`, `...RejectedNaoMudaStatus`, `MercadoPagoClientTests.PreferenceUsaCheckoutPreferences`, `...EstornoComIdempotencyKey`, `WebhookGatewayControllerTests.MercadoPagoComProcessorRetorna200` (regressão do 500).
**Rollback.** Remover o processor do DI: volta ao 500 atual (não confirma nada).
**Depende de.** S11 (entidade `CobrancaPedido` e `ConfirmarPagamentoPedidoUseCase`), S12; onda 0.9.
**Leitura mínima.** `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs`, `MercadoPagoOptions.cs`, `StubMercadoPagoClient.cs`; `Async/Pagamentos/Webhooks/EfiPixWebhookProcessor.cs` (padrão); `Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs`; `Api/Controllers/WebhookGatewayController.cs`; `Async/DependencyInjection/ServiceCollectionExtensions.cs` (linhas 95-110 e 170-182); `App/UseCases/Storefront/Aprovacao/AprovarPedidoStorefrontUseCase.cs` (padrão de lock).
**Tamanho.** M. **Tier.** alto (pagamento).

### S33 · Cobrança do pedido da conversa pelo Mercado Pago (só na opção A)

**Problema.** Com A, `criar_pedido` (S06) precisa devolver o link de pagamento do MP em vez do Pix da Efi.
**Abordagem.** `GerarCobrancaPedidoUseCase` (S11) ganha o provedor `mercadopago`: cria a preferência com `external_reference = PedidoId`, `expires = true`, `expiration_date_to = agora + 30 min`, itens do pedido e frete, e devolve `init_point`. A mensagem ao cliente traz o link e a frase "Pix ou cartão, como preferir". `CobrancaPedido` guarda `Provedor`, `PreferenceId`, `InitPoint`, `ExpiraEm`; `PaymentId` chega pelo webhook (S32).
**Escopo.** `CobrancaPedido` com `Provedor` e `ReferenciaExterna` (migration aditiva); `GerarCobrancaPedidoUseCase` com estratégia por provedor (`ConfiguracaoAtendimento.GatewayPedido`, default `mercadopago` na opção A); ferramenta `criar_pedido` monta a resposta com o link; `CobrancaPedidoJob` expira pela `expiration_date_to`.
**Aceite.**
- [ ] `criar_pedido` devolve `init_point` e o cliente recebe o link na conversa.
- [ ] Preferência criada com `external_reference` do pedido, itens, frete e expiração de 30 min.
- [ ] Pagamento aprovado no MP confirma o pedido pelo mesmo caminho de S32 (nenhum código específico do chat).
**Testes (Red).** `GerarCobrancaPedidoUseCaseTests.MercadoPagoCriaPreferenciaComExternalReference`, `CriarPedidoFerramentaTests.RespostaTrazLink`.
**Rollback.** `GatewayPedido=efi`.
**Depende de.** S11, S32.
**Leitura mínima.** `App/UseCases/Pedidos/Cobranca/GerarCobrancaPixPedidoUseCase.cs` (S11); `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs`.
**Tamanho.** P. **Tier.** alto.

## 6. Roteiro de validação em sandbox (antes de produção)

1. API rodando com `MercadoPago__UseStub=false`, `MercadoPago__AccessToken=TEST-…`, `NotificationUrl` apontando para um túnel público (ngrok ou similar) `/api/webhooks/mercadopago`, `WebhookSecret` do modo de teste, `WebhookAllowUnsigned=false`.
2. No site em homologação, fechar um pedido; conferir no banco `pedidos.status = aguardando_pagamento` e a preferência criada (log com `init_point`).
3. Pagar com usuário comprador de teste e cartão `APRO`.
4. Conferir: `webhooks_recebidos` com `Sucesso=true`; `pedidos.status = aguardando`; `pedido_pagamentos` com `referencia = <payment id>`; evento `pedido.pago` no outbox.
5. Pagar outro pedido com `OTHE`: status permanece `aguardando_pagamento`, `WebhookRecebido` com sucesso e motivo `rejected`.
6. Reenviar a notificação pelo painel do MP: nada muda (idempotência).
7. Estornar pelo endpoint de ocorrência (S27): `refunds` chamado, `CobrancaPedido` marcada.
8. Só então trocar para credenciais de produção, cadastrar o webhook produtivo e repetir 2 a 4 com um pedido real de R$ 1,00.

Evidência mínima no PR de S32: as 8 etapas com data, e a captura da linha de `webhooks_recebidos`.
