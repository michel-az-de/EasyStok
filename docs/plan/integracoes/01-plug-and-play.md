# Integrações plug and play: Mercado Pago, WhatsApp Meta, geolocalização e entregas

Épico: #1633 · Aprovado pelo Felipe em 2026-10-12 · Supera em parte o `README.md` desta pasta (30/09)

Este documento é a fonte de trabalho. Cada fatia abaixo é uma spec citável (ADR-0055): `I<etapa>.<n>`.
O estado de cada etapa vive no épico #1633, não aqui.

## 1. Onde estamos

O dono quer clicar, conectar, configurar e funcionar. O fluxo de negócio existe; a configuração é global, por `.env`, com restart, e a tela de integrações é simulada.

| Integração | Negócio | Clicar e conectar | O que trava de verdade |
|---|---|---|---|
| WhatsApp Meta | Pronto | Existe (Embedded Signup, #1417) | Sem desconectar, reconectar nem revogação |
| Mercado Pago | Pronto (Checkout Pro, webhook, estorno, conciliação) | Não existe | Credencial global; nunca rodou contra o MP real |
| Geolocalização | Google, Nominatim e ViaCEP prontos; TomTom na PR #1323 | Não se aplica (chave da plataforma) | Frete por raio sem endpoint nem tela |
| Entregas e 99 | Só entregador próprio | Simulado em JS no console | Sem adapter; API da 99 não confirmada |

O plano passou por duas rodadas de banca adversarial. A v1 foi reprovada por 3 a 0 (framework antes de valor, pagamento confirmando pedido de outra loja, deploy da chave-mestra derrubando a API). A v2 foi reprovada de novo (invariantes sem etapa dona, desenho do webhook do Mercado Pago por OAuth contrário à documentação oficial). Esta é a v3.

## 2. Decisões do dono (2026-10-12)

| # | Tema | Decisão | Efeito |
|---|---|---|---|
| 1 | Pix do pedido | Tudo pelo Mercado Pago | Revoga a D3 de 30/09. Sem certificado `.p12`. Pix não é pagável no ambiente de teste do MP: o aceite é um Pix real de R$ 1 em produção, com estorno |
| 2 | Conexão do MP | OAuth "Conectar com Mercado Pago" | Aplicação modelo Marketplace na conta MP da plataforma; segredos da aplicação no `.env` |
| 3 | 99 | Registro manual de corrida com custo | Sem cartão simulado; adapter só com API confirmada por escrito |
| 4 | Lalamove | Adapter já, no sandbox | Diverge do plano de logística (#1597): a ordem de código muda, o gate de produção fica. Flag desligada em produção |

Assumido, reversível: simulação sem chave serve a desenvolvimento e homologação, não à dona.

## 3. O que plug and play NÃO cobre

Estes passos continuam fora do sistema depois do plano.

| Integração | Passo externo | Quem |
|---|---|---|
| Mercado Pago | Duas aplicações (produção e teste) modelo Marketplace, PKCE ligado, redirect HTTPS, webhook com os tópicos Pagamentos e Vinculação de aplicações; `client_id`, `client_secret` e segredo de webhook no `.env`; conta da loja com chave Pix cadastrada | Felipe e dona |
| WhatsApp | Portfólio verificado, Tech Provider, AppId, ConfigId v4, AppSecret | Felipe |
| Mapas | Conta e chave TomTom no `.env` | Felipe |
| Lalamove | Conta Business, chaves de sandbox, recarga da carteira para produção | Felipe e dona |
| Cofre | Chave-mestra (KEK) no `.env` e no compose da VPS, para `api` e `worker`. O compose fica em `/opt/stacks/easystok`, fora do repositório | Felipe |

Depois do plano a dona conecta, testa, vê a saúde, reconecta e desconecta sozinha.

## 4. Desenho

```
Console
  Configurações > Canais        WhatsApp (conectar, reconectar, desconectar)
  Configurações > Integrações   Pagamento: Mercado Pago | Entrega: Lalamove, corrida manual
        |
api/integracoes -> IEnumerable<ITestadorIntegracao> (a definição mora no testador; só provedor com adapter vivo)
        |
conexao_integracao (dois eixos: Modo e Saúde)  +  cofre por loja (RLS, KEK, cifra versionada)
        |
ICredencial<Família>DaLoja (scoped, molde RemetenteWhatsAppDoTenant) injetada no cliente HTTP real
```

| Tema | Regra |
|---|---|
| Moradia | HTTP em `EasyStock.Infra.Integrations`. O que lê tenant ou banco em `EasyStock.Infra.Postgre/Services/<família>`. Nada novo em `EasyStock.Infra.Async/Pagamentos` nem em `IPagamentoGateway` |
| Modo | `NaoConfigurado -> Producao <-> Desconectado`. `Teste` só onde há sandbox. Conexão que já foi real nunca usa simulador |
| Saúde | Eixo separado do modo. O vigia só escreve saúde, por `ExecuteUpdate`, sem regravar a linha |
| Desconectar | Vira `SomenteConsulta` até a última cobrança ou corrida aberta; depois revoga e apaga |
| Credencial indisponível | A cobrança vai a `AguardandoCredencial`, sai da varredura do job e volta quando a conexão volta. Falha conta por loja |
| Token global | Cobrança antiga recebe origem `global`. A transição encerra ao remover o token global com zero cobrança `global`, medido |
| Mapas | Credencial de plataforma, sem cartão para a dona |

Webhook por provedor:

| Provedor | Quem autentica | Como acha a loja |
|---|---|---|
| Mercado Pago | HMAC com o segredo da aplicação (um só) | `user_id` do corpo; depois reconsulta com o token dessa loja; só confirma se `collector_id` do pagamento for o `user_id` da conexão |
| Meta | `X-Hub-Signature-256` da aplicação (já existe) | `phone_number_id` (já existe) |
| Lalamove | Assinatura com a chave e o segredo da loja | Chave opaca no caminho da URL. O cadastro da URL exige 200 na sondagem |

Reuso obrigatório, já no master: `CredencialIntegracao`, `IIntegrationCredentialResolver`, `RemetenteWhatsAppDoTenant`, `ConectarWhatsAppCoexistenciaUseCase`, `WebhookGatewayController`, `IWebhookSignatureValidator`, `WebhookRecebido`, `IPublicadorEventoIntegracao`, `AddEasyStockIntegrationResilience`, `IdempotencyMiddleware`, `PostgresAdvisoryLock`, `AuditLog`, `EasyStock.TestHelpers`, `scripts/homologacao/fake_mp.py`, `fake_meta.py`.

Da PR #1304, portar (não mergear; 78% das 14.977 linhas é `Designer.cs`): colunas e `Mascara`, `DesativarAsync`, `ChaveMestraAusenteException`, `ChaveMestraConfiguracao` (para os dois hosts), SQL de RLS, `TestadorMercadoPago`, `TestadorWhatsApp`, `IntegracoesController`, `AbaIntegracoesApi.jsx`, testes. Descartar: Designer, `ChavesGlobaisIntegracao`, estado em memória, `TipoLembrete` 4 e 5, peças de Lalamove fora da E10.

## 5. Invariantes

Cada invariante nasce como teste Red na etapa dona.

| # | Invariante | Dona |
|---|---|---|
| 1 | Em Production nenhum simulador é construído, sem exceção (`ISimulador` e fábrica que lança) | E3, E7 |
| 2 | Simulador devolve `Simulado`, nunca `approved`, `wamid` ou corrida criada | E3, E7 |
| 3 | Pagamento só confirma pedido da mesma loja e da mesma conta recebedora (`collector_id`) | E3 grava, E6 exige |
| 4 | Credencial ausente, revogada ou ilegível nunca equivale a "sem pagamento" | E3, E6 |
| 5 | Cobrança e corrida guardam a conta e a aplicação que as criaram | E3, E10 |
| 6 | Conexão criada pela aplicação de teste nunca muda estado de pedido de loja real (não depende de `live_mode`) | E6 |
| 7 | Webhook reconsulta o provedor; 401 ou 404 com pendência responde 5xx | E3 |
| 8 | Dedupe por loja, provedor e evento, `NULLS NOT DISTINCT`, em coluna fora do filtro global de tenant (`LojaResolvidaId`) | E6 |
| 9 | Identificação da loja nunca substitui a assinatura; segue a tabela de webhook por provedor | E6, E10 |
| 10 | `Desconectado` nunca cai no token global; encerrada a transição, sem credencial da loja não envia nem cobra | E6, E7 |
| 11 | Desconectar vale em API e Worker em até 30 s e não cria novas cobranças nem envios | E4, E6, E7 |
| 12 | Escrita em provedor sem retry; estorno e corrida com chave de idempotência explícita | E3, E10 |
| 13 | Timeout de escrita é `Indeterminado`, nunca "recusado" | E3, E10 |
| 14 | Bypass de RLS só lista ids, pela porta e na allowlist; segredo só é decifrado com tenant fixado | E4, E11 |
| 15 | Todo `kek_id` de linha ativa resolve na subida, na API e no Worker | E4 |
| 16 | Segredo-canário ausente de todos os sinks, span, métrica, resposta, `WebhookRecebido.Erro` e auditoria | E4 |
| 17 | Id de loja não é rótulo de métrica | E11 |
| 18 | Health anônimo não chama provedor, não identifica loja e fica fora de `/health` | E11 |
| 19 | KEK antiga só sai com zero linha ativa ou referenciada por cobrança aberta, medido | E4 |
| 20 | "Parada" só após 3 falhas seguidas; lembrete só na transição; 429 não é falha | E11 |
| 21 | `state` do OAuth: uso único, 10 min, preso a loja e usuária, guardado no banco | E6 |
| 22 | Refresh com advisory lock; token novo persistido antes do uso; `invalid_grant` leva a `Reconectar` | E6 |
| 23 | Corrida de sandbox não muda estado de pedido de loja real; dois cliques criam uma corrida; aceita `ORDER_REPLACED` | E10 |

## 6. Etapas

Tier só existe baixo ou ALTO (ADR-0055). Corte mínimo que já funciona: PR0 + E2 + E8 + E3 + E4 + E5.

| Etapa | Entrega | Valor visível | Tier | Depende |
|---|---|---|---|---|
| E0 | Pré-condições do Felipe, sem código | Destrava o resto | n/a | Felipe |
| PR0 | Kit de teste de integrações | Próximo adapter não começa copiando handler | baixo | nada |
| E1 | Este plano (E1a) e o ADR-0061 (E1b) | Qualquer sessão sabe onde continuar | baixo e ALTO | nada |
| E2 | Frete por raio: endpoint e tela | A dona digita um endereço e vê o frete | baixo | E1a |
| E8 | Mapas: TomTom e cadeia de geocodificação | Frete calcula sem Google | ALTO | chave TomTom |
| E3 | Mercado Pago vivo com a chave atual, já seguro | Pedido pago avança sozinho | ALTO | contas de teste |
| E4 | Cofre seguro | Nenhum; pré-requisito de segurança | ALTO | E0 |
| E5 | Conexão mínima e aba Integrações | "Mercado Pago funcionando, testado às 18:45" | ALTO | E4 |
| E6 | Mercado Pago por OAuth e por loja | A dona conecta e troca de conta sozinha | ALTO | E5 |
| E7 | WhatsApp: ciclo de vida da conexão | A dona reconecta sozinha | ALTO | E5 |
| E9 | Corrida manual com custo | Custo por entrega na tela; 99 atendida sem API | ALTO | E1b |
| E10 | Lalamove no sandbox | Botão "Chamar corrida" | ALTO | E5, E9 |
| E11 | Telemetria, vigia e saúde | Felipe vê a saúde por provedor | ALTO | E6 a E8 |

Só fecham com dinheiro ou corrida real, não com sandbox: o Pix (E6) e as regras R1, R2, R7 e R8 da Lalamove, que são o gate para ligar a flag em produção (as 20 corridas reais do plano de logística).

### E0 · Pré-condições do Felipe

Cada item tem a saída colada no épico #1633.

| Item | Como provar |
|---|---|
| ConfigId do Embedded Signup é v4 (a Meta desliga v2 e v3 em 15/10/2026) | Captura do painel |
| KEK gerada e presente nos dois contêineres | `printenv` da variável não vazio em `ez-api` e `ez-worker` |
| Credenciais já gravadas no cofre | `SELECT kek_id, ativo, count(*) FROM credencial_integracao GROUP BY 1, 2` |
| Papel do banco usado pela API | `rolsuper` e `rolbypassrls` do usuário |
| Duas aplicações MP e três contas de teste (integradora, vendedora, compradora) | Ids no épico, sem segredo |
| Endereço da cozinha (CEP e número) | Texto no épico |
| Chave TomTom e chaves de sandbox da Lalamove | Presentes no `.env`, sem valor no épico |

## 7. Specs

### I0.1 · Kit de teste de integrações (PR0, #1634)

**Problema.** Há 27 `HttpMessageHandler` falsos redeclarados nos testes e a assinatura de webhook é montada à mão em cada arquivo.
**Abordagem.** `HttpFalso` roteável e fábricas `AssinaturaMercadoPago` e `AssinaturaMeta` em `EasyStock.TestHelpers`, provadas contra os validadores reais. Três arquivos refatorados como prova de uso. Roteiro em `docs/dev/integracoes-novo-provedor.md`.
**Aceite.**
- [ ] `HttpFalso` cobre resposta, sequência, exceção, demora, gravação de chamadas e rota não prevista
- [ ] Fábricas aceitas pelos validadores reais; assinatura adulterada é recusada
- [ ] `MercadoPagoClientTests`, `GoogleGeocodingClientTests` e `GoogleRotasClientTests` sem handler próprio e com a mesma contagem de testes
**Testes (Red).** `HttpFalsoTests.Rota_nao_prevista_lanca_com_a_url`, `AssinaturaMercadoPagoTests.Assinatura_gerada_passa_no_validador_real`, `AssinaturaMetaTests.Corpo_adulterado_e_recusado`.
**Leitura mínima.** `EasyStock.TestHelpers/`, `EasyStock.Infra.Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs`, `EasyStock.Api/Controllers/Webhooks/AssinaturaWebhookMeta.cs`, ADR-0023.
**Depende de.** Nada. **Tier.** baixo. **Rollback.** Revert; não toca código de produção.

### I1.1 · Plano publicado (E1a, #1635)

**Problema.** O plano aprovado mora fora do repositório e o `README.md` desta pasta está desatualizado.
**Abordagem.** Este arquivo, o `README.md` como índice marcando o que foi superado e a label `aguardando-credencial`.
**Aceite.**
- [ ] Uma spec por fatia de PR0 a E11
- [ ] Toda invariante com etapa dona
- [ ] `README.md` com a D3 riscada e a coluna "substituída por" em F1 a F10
**Depende de.** Nada. **Tier.** baixo. **Rollback.** Revert.

### I1.2 · ADR-0061: conexões de integração (E1b)

**Problema.** Não há ADR de integrações. Existem três lugares para adapters, dois modelos de pagamento e duas decisões de Pix que se contradizem.
**Abordagem.** ADR com: regra de moradia; modo e saúde como eixos; webhook por provedor; Pix pelo Mercado Pago (revoga a D3); `CorridaExterna` como lugar único do custo de corrida; proibição de adapter novo em `IPagamentoGateway`; issue de poda de Router, Orchestrator e `IGatewayHealthStore`.
**Aceite.**
- [ ] ADR-0061 aceito, citando as decisões de 12/10
- [ ] Issue de poda aberta
**Depende de.** Nada. **Tier.** ALTO (ADR). **Rollback.** Marcar como Superseded.

### I2.1 · Frete por raio: endpoint

**Problema.** `Storefront.ConfigurarFreteRaio` só é chamado em testes. Nenhum controller grava cozinha, raio e faixas, então a loja não consegue cotar frete por distância.
**Abordagem.** `PUT api/minha-vitrine/entrega/raio` recebe CEP e número da cozinha, raio máximo, faixa grátis, fator de rota e faixas. Geocodifica no servidor (o backend nunca aceita coordenada do cliente) e só grava com resultado `Confiavel`. Sem geocodificador ligado responde 422 com mensagem em português. Faixas validadas no use case. `POST .../raio/testar` recebe CEP e número do cliente e devolve modo, distância, faixa e se a distância é estimada. Sem migration: as colunas vêm de `20260624145051_AddStorefrontFreteRaioConfig`.
**Aceite.**
- [ ] Endereço sem geocodificação confiável é recusado e nada é gravado
- [ ] Faixas sobrepostas ou fora de ordem são recusadas
- [ ] `testar` devolve a mesma conta que o checkout faria
- [ ] Sem geocodificador: 422, nunca 500
**Testes (Red).** `ConfigurarFreteRaioUseCaseTests.Endereco_sem_geocode_confiavel_recusa_e_nao_grava`, `...Faixas_sobrepostas_recusa`, `TestarFreteRaioUseCaseTests.Mesma_conta_do_checkout`.
**Leitura mínima.** `EasyStock.Domain/Entities/Storefront/Storefront.cs`, `FreteRaioCalculadora.cs`, `CalcularFreteUseCase.cs`, `TenantVitrineEntregaController.cs`, `IGeocodingClient.cs`.
**Depende de.** I1.1. **Tier.** baixo (sem migration; vira ALTO se guardar o endereço em coluna nova). **Rollback.** Revert; a configuração gravada fica inerte.

### I2.2 · Frete por raio: tela e contrato com o site

**Problema.** `CadastroEntregaApi.jsx` não tem campo de cozinha nem de raio. O site lê `geocodeConfiavel`, `distanciaMetros`, `faixaId`, `gratis` e `foraDeCobertura`, que o `FreteCalculadoDto` não envia.
**Abordagem.** Aba Frete em Entregas > Janelas com "Cozinha, raio e faixas" e o campo "Testar um endereço". Campos aditivos no DTO. Issue irmã no repositório do site.
**Aceite.**
- [ ] A dona grava a cozinha, digita um endereço e vê distância e frete
- [ ] DTO com os cinco campos, sem remover nenhum existente
- [ ] Prova do console em `EasyStock.Console/ferramentas/`
**Testes (Red).** `FreteControllerTests.Dto_traz_campos_de_distancia`, prova `prova-frete-raio.mjs`.
**Leitura mínima.** `EasyStock.Console/src/features/entregas/CadastroEntregaApi.jsx`, `infra/api/entregasApi.js`, `FreteCalculadoDto.cs`, `docs/plan/erp-casa-da-baba/09-m8-entregas.md` (M8.6).
**Depende de.** I2.1. **Tier.** baixo. **Rollback.** Revert.

### I8.1 · TomTom e cadeia de geocodificação

**Problema.** O faturamento do Google Cloud está bloqueado (#1322). A PR #1323 traz os clientes TomTom e está mergeável, mas a escolha de provedor é um único valor de startup, sem queda para o seguinte.
**Abordagem.** Trazer a #1323 revalidada contra o master. Cadeia TomTom, Nominatim, NoOp: falha ou resultado não confiável do primeiro cai no seguinte. Chave de plataforma, sem cartão.
**Aceite.**
- [ ] Com chave TomTom, o frete por raio calcula sem Google
- [ ] TomTom fora do ar cai em Nominatim sem erro para o checkout
- [ ] Sem chave nenhuma, comportamento atual (NoOp) preservado
**Testes (Red).** `CadeiaGeocodingTests.Cadeia_TomTom_falha_cai_em_Nominatim`, `...Resultado_nao_confiavel_tenta_o_seguinte`, `GeocodingProviderSelecaoTests.Sem_chave_e_NoOp`.
**Leitura mínima.** `GeocodingServiceCollectionExtensions.cs`, diff da #1323, `GoogleGeocodingClient.cs`, `NominatimGeocodingClient.cs`.
**Depende de.** Chave TomTom; issue #1322. **Tier.** ALTO. **Rollback.** Voltar o provedor por configuração.

### I8.2 · Endereço fora do log e teste do ViaCEP

**Problema.** TomTom e ViaCEP põem endereço e CEP no caminho da URL, que vai para span e log. `ViaCepLookupClient` não tem teste.
**Abordagem.** Filtrar `url.full` dos hosts de geocodificação no OTel e no log de saída. Testes do cliente de CEP com `HttpFalso`.
**Aceite.**
- [ ] Endereço-canário ausente de log e span em chamada de geocodificação
- [ ] `ViaCepLookupClient` coberto (achou, não achou, timeout)
**Testes (Red).** `TelemetriaGeocodingTests.Endereco_canario_nao_aparece`, `ViaCepLookupClientTests.Cep_inexistente_devolve_vazio`.
**Depende de.** I0.1. **Tier.** ALTO. **Rollback.** Revert.

### I3.1 · Mercado Pago seguro com a chave atual

**Problema.** O Mercado Pago nunca rodou contra o provedor real, e o caminho atual falha em silêncio: sem token o webhook responde 404; 404 na reconsulta responde 200 e o MP para de reenviar; o stub não tem trava de produção; o estorno do stub devolve `approved`.
**Abordagem.** `ISimulador` no `StubMercadoPagoClient`, com fábrica que lança em Production. Validador e processor registrados sempre, separados do `if` do token. 401 ou 404 na reconsulta com cobrança pendente responde 500. Em Production recusar token `TEST-` e `BaseUrl` diferente de `api.mercadopago.com`. Estado "não configurado" explícito no checkout e na tela. Timeout de estorno vira `Indeterminado` e a chave de idempotência do estorno passa a ser obrigatória.
**Aceite.**
- [ ] Production com `UseStub=true` não sobe
- [ ] Stub devolve desfecho `Simulado`
- [ ] Reconsulta 404 com pendência responde 500
- [ ] Dois estornos parciais de mesmo valor geram dois reembolsos
- [ ] Timeout de estorno não vira "recusado"
**Testes (Red).** `Production_com_UseStub_true_nao_sobe`, `Webhook_404_com_pendente_responde_500`, `Dois_parciais_iguais_geram_2_refunds`, `Estorno_com_timeout_e_Indeterminado`, `Post_com_timeout_chama_1_vez`.
**Leitura mínima.** `MercadoPagoServiceCollectionExtensions.cs`, `StubMercadoPagoClient.cs`, `MercadoPagoClient.cs`, `MercadoPagoWebhookProcessor.cs`, `WebhookGatewayController.cs`, `EasyStock.Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs` (145 a 153), `MercadoPagoEstornoPedidoGateway.cs`.
**Depende de.** I0.1. **Tier.** ALTO (pagamento). **Rollback.** Revert; volta ao comportamento atual.

### I3.2 · Cobrança sabe de qual conta é

**Problema.** A cobrança não guarda a conta recebedora. Trocar a credencial com cobrança pendente faz a busca voltar vazia e o job cancela pedido pago. O tenant do webhook vem do `external_reference`, sem conferir a conta.
**Abordagem.** `CobrancaPedido` grava `collector_id` e a origem da credencial (`global`). `ConfirmarPagamentoPedidoUseCase` exige que a conta do pagamento seja a da cobrança. `ProcessarCobrancaVencidaUseCase` não expira cobrança cuja conta difere da credencial atual: marca `AguardandoCredencial` e tira da varredura. Falha do job conta por loja.
**Aceite.**
- [ ] Pagamento de outra conta com `external_reference` de pedido alheio não confirma
- [ ] Credencial trocada com cobrança pendente não cancela o pedido
- [ ] Três cobranças presas de uma loja não travam a fila das demais
**Testes (Red).** `Pagamento_de_outra_conta_nao_confirma`, `Cobranca_pendente_com_credencial_trocada_nao_cancela`, `Fila_segue_com_loja_sem_credencial`.
**Leitura mínima.** `CobrancaPedido.cs`, `ConfirmarPagamentoPedidoUseCase.cs` (145 a 152, 338 a 340), `ProcessarCobrancaVencidaUseCase.cs`, `CobrancaPedidoJob.cs`, `CobrancaPedidoRepository.cs`.
**Depende de.** I3.1; fila de migration. **Tier.** ALTO (migration). **Rollback.** Colunas ficam nulas; o código anterior as ignora.

### I3.3 · Roteiro de homologação refeito

**Problema.** O roteiro de 8 passos usa credencial `TEST-`, que não dispara webhook (documentação do Checkout Pro). Pix não é pagável em teste.
**Abordagem.** Reescrever `08-mercado-pago.md` seção 6 com contas de teste e credencial `APP_USR` delas, túnel público e compose separado para o Worker. Executar e colar a evidência.
**Aceite.**
- [ ] Pedido criado, pago com cartão de teste, webhook recebido e pedido avançado, com captura do `WebhookRecebido`
- [ ] Estorno total e parcial executados
**Depende de.** I3.1, I3.2; contas de teste. **Tier.** baixo (docs). **Rollback.** n/a.

### I4.1 · KEK chega aos dois hosts (só avisa)

**Problema.** A chave-mestra só é lida de `Crypto:*`, sem variável mapeada, e o Worker decifra o token do WhatsApp. O deploy só confere se o Worker está rodando, então um Worker em laço de restart passa como verde.
**Abordagem.** Mapear `EZ_CRYPTO_KEK_ID`, `EZ_CRYPTO_KEK` e as anteriores para `Crypto:*` em extensão compartilhada chamada por API e Worker. Na subida, só registrar aviso se faltar. `scripts/deploy/vps-deploy.sh` reprova Worker com `RestartCount` maior que zero. Versionar uma cópia do compose da VPS como referência.
**Aceite.**
- [ ] Worker só com `EZ_CRYPTO_*` resolve e decifra
- [ ] Sem KEK, os dois hosts sobem e avisam
- [ ] Deploy falha com Worker em laço de restart
**Testes (Red).** `Worker_so_com_EZ_resolve_e_decifra`, `Sem_KEK_sobe_e_avisa`.
**Leitura mínima.** `IntegrationCredentialResolver.cs` (272 a 280), `EasyStock.Worker/Program.cs`, `EasyStock.Api/Startup/StartupHardening.cs`, worktree da #1304 `ChaveMestraConfiguracao.cs`, `scripts/deploy/vps-deploy.sh`.
**Depende de.** E0. **Tier.** ALTO. **Rollback.** Voltar a tag da imagem.

### I4.2 · Fail-fast da KEK

**Problema.** Uma linha ativa com `kek_id` que o processo não conhece deixa a API verde e o envio quebrado.
**Abordagem.** Só em Production: API e Worker não sobem sem KEK corrente, nem se algum `kek_id` de linha ativa não resolver e decifrar.
**Aceite.**
- [ ] Prova do `printenv` nos dois contêineres colada na issue ANTES do merge
- [ ] Subida falha com linha ativa de KEK desconhecida
**Testes (Red).** `Worker_Production_sem_KEK_nao_sobe`, `Startup_com_linha_ativa_de_kek_desconhecida_falha`.
**Depende de.** I4.1 publicada e verificada. **Tier.** ALTO. **Rollback.** Voltar a tag; o deploy volta sozinho se `/health/ready` não ficar verde.

### I4.3 · Toda leitura do cofre fixa o tenant

**Problema.** A RLS que entra em I4.4 devolve zero linhas para quem lê sem tenant (rotação de KEK, jobs, webhooks), e a imagem anterior ficaria cega depois de um rollback.
**Abordagem.** Num deploy próprio, antes da RLS: toda leitura do cofre passa por tenant fixado; a rotação de KEK vira comando administrativo pela porta de bypass, listando ids e decifrando loja a loja. TTL do cache cai de 5 min para 30 s.
**Aceite.**
- [ ] Nenhuma leitura do cofre sem tenant fora da porta de bypass
- [ ] Desativar na API deixa o Worker sem a credencial em até 30 s
- [ ] Rotação sem bypass falha alto, não registra "0 recifradas"
**Testes (Red).** `Rotacao_sem_bypass_falha_alto`, `Desconectar_na_API_Worker_nao_envia`, entrada nova em `RlsBypassAllowlistTests`.
**Leitura mínima.** `IntegrationCredentialResolver.cs` (55, 189 a 258), `CredencialIntegracaoRepository.cs`, `RlsBypassAllowlistTests.cs`.
**Depende de.** I4.2 publicada. **Tier.** ALTO (RLS). **Rollback.** Voltar a tag.

### I4.4 · RLS, colunas e cifra versionada

**Problema.** `credencial_integracao` não tem policy de RLS e já guarda token. O AES-GCM não usa dado associado, então uma linha copiada para outra loja decifra.
**Abordagem.** Migration aditiva: policy `tenant_isolation`; colunas `mascara`, `ultimo_teste_*`, `versao_cifra` (1 = sem dado associado). Leitura aceita 1 e 2; escrita só 2, com loja, provedor, ambiente e `kek_id` como dado associado. Job recifra com tenant fixado. `DesativarAsync` e gravação parcial por `ExecuteUpdate`.
**Aceite.**
- [ ] Linha gravada antes do deploy continua legível
- [ ] Sob papel sem bypass, uma loja não lê a credencial da outra (cache frio)
- [ ] Linha v2 copiada para outra loja não decifra
- [ ] O vigia não reativa credencial desativada no intervalo do teste
**Testes (Red).** `Linha_v1_continua_legivel_apos_deploy`, `RlsEscondeACredencialDeOutraEmpresa`, `Linha_copiada_para_outra_empresa_nao_decifra`, `Teste_intercalado_com_desativar_nao_reativa`.
**Leitura mínima.** `IntegrationCredentialResolver.cs` (302 a 320), worktree da #1304 `20261001004039_AddChavesIntegracaoPorLoja.cs`, `MotorNotificacoesRlsTests.cs` (263).
**Depende de.** I4.3 publicada; fila de migration. **Tier.** ALTO (migration, RLS). **Rollback.** Nunca `Down()` em produção; emergência documentada: `ALTER TABLE credencial_integracao DISABLE ROW LEVEL SECURITY`.

### I4.5 · Redação em todos os sinks

**Problema.** Só o sink de arquivo redige segredo. O Worker tem console e OTLP. O corpo de erro do Mercado Pago vai para log e para `WebhookRecebido.Erro`.
**Abordagem.** Redação aplicada a todos os sinks dos dois hosts, com padrões novos (`access_token` em JSON, `key=` em URL). Corpo de endpoint de autenticação nunca é registrado. Webhook registra o molde da rota, não o caminho.
**Aceite.**
- [ ] Segredo-canário ausente de console, arquivo, OTLP, span, métrica, resposta, `WebhookRecebido.Erro` e auditoria
**Testes (Red).** `Canario_ausente_de_todos_os_sinks`, `Erro_do_provedor_persistido_sem_segredo`.
**Leitura mínima.** `docs/dev/seguranca-logging.md`, `SensitivePatterns.cs`, `RedactingTextFormatter.cs`, `EasyStock.Worker/Program.cs`, `PipelineExtensions.cs` (72).
**Depende de.** Nada (pode correr em paralelo com I4.1). **Tier.** ALTO. **Rollback.** Revert.

### I5.1 · Conexão mínima: API e estado

**Problema.** Não existe lugar único para o estado de uma integração. Derivar o modo de "o resolvedor devolveu nulo" confunde inexistente, desativada e vencida.
**Abordagem.** Tabela `conexao_integracao` (loja, provedor, modo, saúde, último teste, falhas seguidas). `ITestadorIntegracao` carrega a própria definição; o catálogo é o `IEnumerable<>` do DI. `IntegracoesController`: listar, testar (limite por loja e provedor), desconectar. O cartão do Mercado Pago testa a MESMA fonte que o cliente usa hoje e a rotula "chave da plataforma". `AuditLog` explícito e fail-closed em conectar, testar e desconectar, sem valor de segredo.
**Aceite.**
- [ ] `GET api/integracoes` devolve modo, saúde, hora do último teste e origem, nunca segredo
- [ ] Testar grava saúde sem alterar modo
- [ ] Toda ação gera linha de auditoria; falha de auditoria falha a ação
- [ ] Teste de composição: todo testador registrado tem definição válida e provedor único
**Testes (Red).** `Todo_testador_registrado_tem_definicao_valida`, `Testar_grava_saude_sem_mudar_modo`, `Auditoria_de_credencial_sem_payload_iv_tag_mascara`, `LimiteDeTaxaPorLojaTests`.
**Leitura mínima.** `IntegracoesWhatsAppController.cs` (molde), worktree da #1304 (`IntegracoesController.cs`, `TestadorMercadoPago.cs`, `ExecutorTesteIntegracao.cs`), `EntityChangeInterceptor.cs`, `AuditLog.cs`.
**Depende de.** I4.4 publicada; fila de migration; issue #1246. **Tier.** ALTO (migration). **Rollback.** Desligar a rota por flag; tabela fica inerte.

### I5.2 · Aba Integrações no console

**Problema.** A aba "Integrações de entrega" é simulada e abre desabilitada no modo API. O WhatsApp já mora em Canais.
**Abordagem.** Antes do código: tabela estado x provedor x texto x ação aprovada pelo Felipe (não configurado, funcionando, parada, vencendo, reconectar, somente consulta, sem saldo, sem chave Pix). Aba renomeada, com grupos Pagamento e Entrega. Cada cartão traz a contingência que já existe ("Recebi" manual, app do celular, chamado manual). Faixa "Integração parada". WhatsApp continua só em Canais.
**Aceite.**
- [ ] Tabela de estados aprovada e anexada à issue
- [ ] Cartão do Mercado Pago mostra estado real e hora do último teste
- [ ] Prova do console percorre listar, testar e ver a faixa
**Testes (Red).** `prova-integracoes-mercadopago.mjs`.
**Leitura mínima.** `EasyStock.Console/src/dominio/modulos.js`, `features/gestao/integracoes/AbaIntegracoes.jsx`, `features/gestao/canais/TelaCanais.jsx`, worktree da #1304 `AbaIntegracoesApi.jsx`, `app/FaixaApi.jsx`.
**Depende de.** I5.1. **Tier.** baixo (console, sem migration). **Rollback.** Revert.

### I6.1 · Experimento no sandbox do Mercado Pago

**Problema.** Sete pontos do desenho dependem de comportamento que a documentação não confirma.
**Abordagem.** Com contas de teste e credencial `APP_USR`, provar: (1) em qual URL o webhook chega e com qual segredo; (2) `user_id` do corpo, do token e `collector_id` são iguais; (3) token da loja A lendo pagamento da loja B devolve 403 ou 404; (4) o reenvio traz `ts` novo (o validador recusa `ts` com mais de 5 min); (5) desvincular gera `mp-connect` e o refresh repetido funciona ou não; (6) a mesma chave de idempotência em duas preferências; (7) vencimento de 30 min é aceito.
**Aceite.**
- [ ] As sete respostas em PR de docs que atualiza I6.2 a I6.4
- [ ] O que não se confirmar reabre a spec afetada
**Depende de.** Aplicações MP e contas de teste. **Tier.** baixo (docs). **Rollback.** n/a.

### I6.2 · Webhook pronto para várias contas

**Problema.** O validador é síncrono e singleton, então não consulta nada. O dedupe é por provedor e evento, sem loja. O processor descarta tudo que não é `payment`, inclusive a desvinculação.
**Abordagem.** Validador assíncrono (toca a Efí: regressão obrigatória). Dedupe por `LojaResolvidaId`, provedor e evento, com índice novo ao lado do antigo até a fase de contração. Loja resolvida pelo `user_id` do corpo depois da assinatura. Tratar `mp-connect` (`application.deauthorized`): marca desconectado, para de cobrar, não cancela pendentes.
**Aceite.**
- [ ] O mesmo id de evento em duas lojas processa as duas
- [ ] Evento sem loja duplicado segue no-op (Efí)
- [ ] `user_id` sem conexão ativa responde 5xx e marca `Reconectar`
**Testes (Red).** `Mesmo_eventId_em_duas_lojas_processa_as_duas`, `Evento_sem_loja_duplicado_segue_no_op`, `Desautorizacao_marca_desconectado_sem_cancelar`.
**Leitura mínima.** `IGatewayWebhookProcessor.cs`, `WebhookGatewayController.cs`, `WebhookRecebidoConfiguration.cs`, `WebhookRecebidoRepository.cs`, `EasyStockDbContext.cs` (filtro global, 512 a 584), `EfiPixWebhookProcessor.cs`.
**Depende de.** I6.1, I5.1; fila de migration. **Tier.** ALTO (migration, pagamento). **Rollback.** O índice antigo permanece; desligar o caminho novo por flag.

### I6.3 · Conectar com Mercado Pago

**Problema.** Não existe OAuth. Sem `state` amarrado, um atacante liga a loja da vítima à própria conta e recebe as vendas.
**Abordagem.** `state` de 256 bits, uso único, 10 min, guardado em hash no banco, preso a loja e usuária. PKCE S256. `redirect_uri` fixa. O callback troca o `code`, mostra tela de confirmação (apelido, documento, aviso se a conta não tem chave Pix) e só então grava no cofre tokens, expiração, `user_id` e id da aplicação. Índice único por `user_id` ativo. Refresh aos 150 dias com advisory lock; `invalid_grant` leva a `Reconectar`. OAuth no `fake_mp.py`. `StartupHardening`: em Production exigir `ClientId`, `ClientSecret`, `WebhookSecret` e `RedirectUri` e recusar `WebhookAllowUnsigned`.
**Aceite.**
- [ ] Callback com `state` de outra loja, reusado ou vencido é recusado
- [ ] A mesma conta MP em duas lojas é recusada
- [ ] Dois refresh concorrentes geram uma chamada HTTP
- [ ] Token nunca aparece em log, resposta ou auditoria
**Testes (Red).** `Callback_com_state_de_outra_loja_ou_reusado_recusa`, `Mesma_conta_em_duas_lojas_recusa`, `Dois_refresh_concorrentes_uma_chamada`, `Invalid_grant_leva_a_Reconectar`.
**Leitura mínima.** `ConectarWhatsAppCoexistenciaUseCase.cs` (molde), `MetaEmbeddedSignupClient.cs`, `MercadoPagoOptions.cs`, `PostgresAdvisoryLock.cs`, `SecaoWhatsApp.jsx`.
**Depende de.** I6.2. **Tier.** ALTO (auth, migration). **Rollback.** Esconder o botão por flag; conexões gravadas ficam inertes.

### I6.4 · Mercado Pago usa a credencial da loja

**Problema.** `MercadoPagoClient` lê token e URLs de `IOptions`. O Worker nem registra o cliente.
**Abordagem.** `ICredencialMercadoPagoDaLoja` scoped e memoizado em `EasyStock.Infra.Postgre/Services/Pagamentos`, lido a cada chamada pelo cliente real. Cobrança sem credencial disponível vai a `AguardandoCredencial` (I3.2). Desconectar vira `SomenteConsulta` enquanto houver cobrança aberta e pede confirmação se houver pendência de outra conta. O validador aceita os dois segredos enquanto existir cobrança de origem `global`.
**Aceite.**
- [ ] Cobrar, consultar, estornar e conciliar com o token da loja, em API e Worker
- [ ] Conexão criada pela aplicação de teste não confirma pedido de loja real
- [ ] `Desconectado` não cai no token global
**Testes (Red).** `Webhook_da_loja_B_com_pedido_da_loja_A_nao_confirma`, `Conexao_do_app_de_teste_nao_confirma_pedido_real`, `Desconectado_nao_usa_token_global`, `Desconectar_com_pendencia_vira_SomenteConsulta`.
**Leitura mínima.** `MercadoPagoClient.cs` (59 a 65, 220), `RemetenteWhatsAppDoTenant.cs`, `MercadoPagoServiceCollectionExtensions.cs` (26 a 33), `EasyStock.Worker/Program.cs`.
**Depende de.** I6.3. **Tier.** ALTO. **Rollback.** Flag por loja volta à chave da plataforma; cobranças abertas ficam `AguardandoCredencial`, nunca canceladas.

### I6.5 · Encerrar a transição do token global e provar o Pix

**Problema.** Enquanto o token global existir no ambiente, uma loja sem conexão ainda cobra por ele.
**Abordagem.** Medir zero cobrança de origem `global` aberta; Felipe remove o token global do `.env`; o validador deixa de aceitar o segredo antigo. Pix real de R$ 1 em produção com a conexão OAuth, incluindo estorno.
**Aceite.**
- [ ] Consulta com zero cobrança `global` aberta colada na issue
- [ ] Pix de R$ 1 pago, confirmado pelo webhook e estornado, com evidência
**Depende de.** I6.4 em produção. **Tier.** ALTO. **Rollback.** Recolocar o token global.

### I7.1 · WhatsApp: desconectar e reconectar

**Problema.** Só o superadmin desvincula. `account_update` é ignorado. `RegistrarWebhookVerificado` nunca é chamado, então a tela nunca mostra "Webhook verificado".
**Abordagem.** Botões Desconectar e Reconectar em Canais. Desconectar desvincula o número, desativa a credencial e desinscreve a aplicação. Tratar revogação vinda de `account_update`: estado `Reconectar`. Chamar `RegistrarWebhookVerificado` na verificação GET.
**Aceite.**
- [ ] A dona desconecta e reconecta sem o Felipe
- [ ] Token revogado na Meta aparece como "Reconectar" na tela
- [ ] "Webhook verificado" acende
**Testes (Red).** `Desconectar_desativa_credencial_e_desvincula`, `AccountUpdate_de_revogacao_marca_Reconectar`, `Verificacao_GET_registra_webhook_verificado`.
**Leitura mínima.** `IntegracoesWhatsAppController.cs`, `WebhookWhatsAppController.cs` (25 a 77), `ConfiguracaoAtendimento.cs` (126), `VincularWhatsAppTenant`, `TelaCanais.jsx`, `dominio/canais.js`.
**Depende de.** I5.1. **Tier.** ALTO. **Rollback.** Esconder os botões.

### I7.2 · Sem token da loja não envia

**Problema.** Sem token da loja o envio cai no token global. Pendentes antigos saem ao reconectar em outro número.
**Abordagem.** Loja `Desconectado` nunca usa o token global. Ao desconectar, pendentes viram `Cancelada`. Ao reconectar em outro número, nada antigo sai.
**Aceite.**
- [ ] Loja sem token próprio e desconectada não envia
- [ ] Reconectar não envia pendente antigo
**Testes (Red).** `Loja_desconectada_nunca_usa_global`, `Reconectar_nao_envia_pendente_antigo`.
**Leitura mínima.** `RemetenteWhatsAppDoTenant.cs`, `WhatsAppCloudClient.cs` (324 a 352), `ReenvioMensagensBackgroundService.cs`.
**Depende de.** I7.1. **Tier.** ALTO. **Rollback.** Revert.

### I7.3 · Worker sem stub fixo e limitador de vazão

**Problema.** `EasyStock.Worker/appsettings.json` fixa `"stub"`. O stub do atendimento devolve `wamid` e a mensagem aparece como enviada. O envio sai de dois hosts, então limitador em memória não limita.
**Abordagem.** `ISimulador` no `StubWhatsAppCloudClient`, com desfecho `Simulado` e trava de Production. Remover o padrão `stub` do Worker. Limitador por número no Postgres.
**Aceite.**
- [ ] Production nunca constrói o stub
- [ ] Mensagem simulada nunca aparece como enviada
- [ ] Dois hosts respeitam o mesmo teto por número
**Testes (Red).** `Production_nao_constroi_stub_de_whatsapp`, `Stub_devolve_Simulado`, `Limite_por_numero_vale_entre_hosts`.
**Leitura mínima.** `WhatsAppCloudClientServiceCollectionExtensions.cs`, `StubWhatsAppCloudClient.cs`, `StubWhatsAppProvider.cs` (30), `EasyStock.Worker/appsettings.json`.
**Depende de.** I7.2. **Tier.** ALTO. **Rollback.** Configuração explícita do provider.

### I7.4 · Modelos padrão com clique humano

**Problema.** Conectar o número não deixa a loja operante: os modelos precisam existir e estar aprovados na conta dela. Submeter a cada reconexão duplica e afeta a qualidade da conta.
**Abordagem.** Botão "Enviar modelos para aprovação" em Canais, idempotente por conta, nome e idioma. Estado "modelo em aprovação" na tela. Ligar `modulo.atendimento` passa a ser pergunta explícita no fim da conexão.
**Aceite.**
- [ ] Clicar duas vezes não duplica modelo
- [ ] A tela mostra quais modelos estão aprovados, em análise ou recusados
**Testes (Red).** `Submeter_modelos_e_idempotente`, `Modelo_recusado_aparece_na_tela`.
**Leitura mínima.** `CatalogoModelosWhatsApp.cs`, `NotificacoesGlobaisSeed.cs` (592 a 649), `WhatsAppCloudClient.cs`.
**Depende de.** I7.1. **Tier.** ALTO. **Rollback.** Esconder o botão.

### I9.1 · Corrida manual com custo: backend

**Problema.** Não há campo de custo em viagem, parada, chamado nem entregador. A corrida pedida pelo app da 99, da Lalamove ou da Uber não deixa rastro, então não existe custo por entrega.
**Abordagem.** `CorridaExterna` (viagem, provedor, origem `Manual`, custo em centavos, motorista em texto, quem registrou), a mesma tabela que a E10 estende. `EmpresaEntregador` ganha `Uber`. A viagem manual sai com o entregador genérico do provedor (RN-32 intacta). Custo registrado e corrigido em Montando, EmRota ou Concluída. Use case em arquivo novo, sem tocar `SairParaEntregaUseCase`.
**Aceite.**
- [ ] Registrar e corrigir o custo de uma corrida da 99
- [ ] Custo negativo ou em viagem desfeita é recusado
- [ ] Relatório custo contra frete cobrado por viagem
**Testes (Red).** `RegistrarCorrida_custo_negativo_recusa`, `RegistrarCorrida_em_viagem_desfeita_recusa`, `Corrigir_custo_guarda_quem_corrigiu`.
**Leitura mínima.** `Viagem.cs`, `ChamadoEntregador.cs`, `EmpresaEntregador.cs`, `AtendimentoEntregasController.cs`.
**Depende de.** I1.2; fila de migration. **Tier.** ALTO (migration). **Rollback.** Tabela fica inerte.

### I9.2 · Corrida manual com custo: tela

**Problema.** O console simula 99 e Lalamove em JS no modo demonstração e não tem onde anotar a corrida no modo API.
**Abordagem.** Na viagem: "Registrar corrida" (empresa, motorista, custo). Custo por entrega visível. Apagar o 99 simulado de `provedoresDeEntrega.js`.
**Aceite.**
- [ ] A atendente registra a corrida em até três toques
- [ ] Nenhuma tela mostra cotação inventada da 99
**Testes (Red).** `prova-corrida-manual.mjs`.
**Leitura mínima.** `ConteudoEntregasApi.jsx`, `PainelViagem.jsx`, `infra/provedoresDeEntrega.js`, `dominio/integracoes.js`.
**Depende de.** I9.1. **Tier.** baixo. **Rollback.** Revert.

### I10.1 · Lalamove: domínio de corrida externa

**Problema.** A viagem só sai com entregador cadastrado e ativo (RN-32), o pedido em rota só vai a Entregue ou Cancelado, e o pedido só tem o comprador, sem destinatário.
**Abordagem.** `Viagem.SairComCorrida` (R1), desfecho "não entregue" que devolve o pedido ao despacho (R2), nome e telefone do destinatário no pedido, no formato com DDI (R9).
**Aceite.**
- [ ] Viagem sai com retrato de motorista externo, sem entregador cadastrado
- [ ] "Não entregue" devolve o pedido ao despacho com motivo
- [ ] Pedido sem telefone válido não despacha por provedor
**Testes (Red).** `SairComCorrida_sem_entregador_cadastrado_sai_com_retrato_externo`, `NaoEntregue_devolve_ao_despacho`, `Sem_telefone_valido_nao_despacha`.
**Leitura mínima.** `Viagem.cs` (82 a 93), `ParadaViagem.cs`, `PedidoStateMachine.cs` (47), `Pedido.cs` (41 a 50), plano de logística (R1, R2, R9).
**Depende de.** I9.1; plano de logística no master com a seção Reconciliação; trabalho não commitado de `m8-troca-endereco` commitado pela sessão dona. **Tier.** ALTO (migration, domínio). **Rollback.** Caminho novo atrás de flag.

### I10.2 · Lalamove: adapter no sandbox

**Problema.** Não existe adapter. `POST /v3/orders` não tem idempotência, o `orderId` pode mudar (`ORDER_REPLACED`), a categoria `logistics` nasce com retry e a URL de webhook é desativada após 10 falhas em 24 h.
**Abordagem.** Um adapter, sem porta genérica (a porta só é extraída com o segundo provedor). Cotar; a atendente vê o preço e confirma; tentativa gravada antes do POST; índice "uma corrida ativa por viagem" que aceita substituição; POST com timeout é reconciliado pelo evento `ORDER_CREATED`; cancelar; webhook como gatilho com reconsulta; consulta de contingência com advisory lock; `scheduleAt` da janela convertida de America/Sao_Paulo para UTC; `fake_lalamove.py`. Categoria de resiliência de escrita sem retry.
**Aceite.**
- [ ] R3, R4, R5, R6, R8, R10 e R12 verdes contra o fake
- [ ] Cotar, criar e cancelar no sandbox real
- [ ] Dois cliques criam uma corrida; webhook forjado não muda estado
- [ ] Corrida de sandbox não muda estado de pedido de loja real
**Testes (Red).** `Dois_cliques_criam_1_corrida`, `Webhook_COMPLETED_forjado_nao_muda_estado`, `Post_com_timeout_reconcilia_por_ORDER_CREATED`, `Order_replaced_troca_o_id_sem_duplicar`, `Cancelar_pedido_com_corrida_ativa_cancela_ou_alerta`.
**Leitura mínima.** Plano de logística (L3, R1 a R12), worktree da #1304 `AssinadorLalamove.cs`, `IntegrationCategories.cs`, `WebhookGatewayController.cs` (61 a 74).
**Depende de.** I10.1, I5.1; chaves de sandbox; issue #1247. **Tier.** ALTO. **Rollback.** Quatro camadas: flag, cancelar corridas, remover a URL do webhook, revert.

### I10.3 · Lalamove: cartão e botão

**Problema.** Sem tela, o adapter não é usado.
**Abordagem.** Cartão Lalamove em Integrações (conectar com chave e segredo, Testar por `GET /v3/cities`, sandbox ou produção, saldo insuficiente como estado). Botão "Chamar corrida" na viagem, uma parada por corrida (R7), com a contingência manual visível (R11).
**Aceite.**
- [ ] Conectar, testar, cotar e chamar pela tela no sandbox
- [ ] Flag desligada em produção esconde o botão
**Testes (Red).** `prova-lalamove-sandbox.mjs`.
**Depende de.** I10.2, I5.2. **Tier.** baixo. **Rollback.** Flag.

### I11.1 · Telemetria por provedor

**Problema.** Chamada externa é um span de HttpClient sem provedor. Não há métrica de latência ou erro por provedor.
**Abordagem.** `DelegatingHandler` único nos clientes de integração: tags de provedor e operação, métricas `integracao_chamadas_total` e `integracao_latencia_ms`, propagação de `X-Correlation-Id`. Loja só em span e log, nunca em rótulo de métrica.
**Aceite.**
- [ ] Toda chamada a provedor tem provedor e operação no span
- [ ] Nenhuma métrica tem id de loja como rótulo
**Testes (Red).** `Chamada_externa_tem_tag_de_provedor`, `Metrica_nao_tem_rotulo_de_loja`.
**Depende de.** E6 a E8. **Tier.** baixo. **Rollback.** Remover o handler.

### I11.2 · Vigia no Worker

**Problema.** Chave revogada só é descoberta quando a venda falha.
**Abordagem.** Vigia no Worker (ADR-0057): bypass só para listar ids, cada loja em escopo próprio com tenant fixado; mapas uma vez ao dia, demais a cada 15 min; "parada" após 3 falhas seguidas; 429 não é falha; lembrete e incidente só na transição.
**Aceite.**
- [ ] Um teste falho isolado não acende a faixa
- [ ] 429 não cria lembrete
- [ ] O vigia não decifra segredo sem tenant fixado
**Testes (Red).** `Uma_falha_isolada_nao_marca_parada`, `Vigia_429_nao_cria_lembrete`, entrada em `RlsBypassAllowlistTests`.
**Depende de.** I5.1. **Tier.** ALTO (RLS). **Rollback.** Flag `BackgroundJobs`.

### I11.3 · Saúde agregada e painel

**Problema.** Não há visão de saúde das integrações, e um health check por loja contaminaria o `/health` da plataforma.
**Abordagem.** `/health/integracoes` anônimo, só contagem agregada por provedor lida do estado persistido, sem chamar provedor e fora do `/health`. Painel por provedor no OpenObserve.
**Aceite.**
- [ ] `/health` ignora integrações
- [ ] `/health/integracoes` não traz id de loja nem mensagem de exceção
**Testes (Red).** `Health_raiz_ignora_integracoes`, `Health_integracoes_sem_empresaId_nem_excecao`.
**Leitura mínima.** `PipelineExtensions.cs` (252 a 306), `HealthCheckResponseWriter.cs`.
**Depende de.** I11.2. **Tier.** baixo. **Rollback.** Remover o endpoint.

## 8. Regras de execução

| Tema | Regra |
|---|---|
| Trilhas (worktrees próprios, sem branch empilhada) | A: E4, E5, E6, E11. B: PR0, E1a, E2. C: E3. D: E1b, E9, E10. E: E8, depois E7 |
| Label | No máximo 2 PRs ALTO abertas sem `aprovado` por trilha; trilha parada cede a vez |
| Migration | Fila única: uma PR com migration aberta por vez, marcada no épico. A migration é o último commit, gerada depois de `git merge origin/master`. Conflito de snapshot nunca se resolve à mão: fica o do master, apagam-se os arquivos próprios da migration e gera-se de novo |
| Credencial não chegou | A fatia fecha contra o fake; abre issue irmã "homologar com credencial real" com a label `aguardando-credencial`. Nenhuma PR mergeia com caixa de aceite desmarcada |
| Deploy | Editar `.env` e compose da VPS é do Felipe. Publicar é da sessão coordenadora, pelo script oficial. Cada PR da E4 só recebe `aprovado` com o deploy da anterior verificado |
| Pronto por PR | Gate, `changelog.d/<issue>.md`, revisão automatizada, `npm run qualidade` se tocar o console, `mergeable` conferido no instante do relatório |
| PRs e branches alheias | Fechar #1304 ou #1323, apagar branch ou worktree de outra sessão: só com GO do Felipe |

## 9. Colisões conhecidas (12/10)

| Onde | Estado | Trato |
|---|---|---|
| Plano de logística, branch `docs/plano-logistica-entregas-1597` | No origin, sem PR | A decisão 4 inverte a ordem de código; o gate de produção fica. Edições pedidas em comentário na #1597 |
| Worktree `m8-troca-endereco` | 7 arquivos fora do commit, branch só em disco | Não tocar. Bloqueia I10.1. I9.1 contorna com arquivo novo |
| Worktree `testes-whatsapp-1007` | `compose.homologacao.yml` e `fake_meta.py` fora do commit | Não copiar. I3.3 usa compose separado para o Worker |
| PR #1304 | Em conflito | Superada por E4 e E5 |
| PR #1323 | Mergeável | Insumo de I8.1 |
| PRs #1433, #1431, #1293 | Em conflito, com snapshot | Entram na fila única de migration |

## 10. Tamanho

| Medida | Valor |
|---|---|
| PRs estimadas a cerca de 400 linhas úteis | cerca de 52, das quais cerca de 37 ALTO |
| Ritmo medido em 12/10 | 60 merges em 59 h; label em 13 a 75 min com o Felipe presente; CI de 14 a 20 min |
| Prazo | 10 dias úteis otimista; 25 a 30 realista; corte mínimo em 8 a 10 |

O gargalo não é código: são as PRs ALTO esperando `aprovado`, os deploys em série da E4 e as credenciais externas.

## 11. Fontes externas conferidas em 12/10

| Tema | Fonte |
|---|---|
| OAuth do Mercado Pago (PKCE opcional, token de 180 dias, refresh) | mercadopago.com.br/developers/pt/docs/security/oauth |
| Webhook do Mercado Pago (um segredo por aplicação, `user_id` no corpo) | mercadopago.com.br/developers/pt/docs/your-integrations/notifications/webhooks |
| Credencial de teste não envia notificação | mercadopago.com.br/developers/pt/docs/checkout-pro/payment-notifications |
| Contas de teste | mercadopago.com.br/developers/pt/docs/checkout-pro/additional-content/your-integrations/test/accounts |
| Lalamove v3 (sandbox, cotação de 5 min, webhook) | developers.lalamove.com |
| Embedded Signup v4 e descontinuação de v2 e v3 em 15/10/2026 | developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/versions |

Não confirmado na documentação, a provar em I6.1: se a notificação da `notification_url` vem assinada e com qual segredo; se o Pix aparece no Checkout Pro com preferência criada por token OAuth; se a dona da aplicação pode autorizar a si mesma; faixa aceita de vencimento no Checkout Pro. Na Lalamove: fórmula exata da assinatura do webhook e se o sandbox simula motorista.
