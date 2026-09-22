# Onda 2 — Pedido e cobrança na conversa (S10–S16)

Objetivo: o agente cria o pedido antes do pagamento, a cobrança Pix é conciliada sem a dona, o pedido
pago entra direto na fila e o cliente recebe os avisos de status pelo WhatsApp.
Cobre US-011, US-012, US-014, US-021 (lado API), US-024, US-026, US-027, US-028, US-031, US-032,
US-039, US-040, US-045, RN-06, RN-09, RN-10, RN-21 a RN-24, RN-27, RN-32, D9.

Dependências externas: onda 0.4, 0.7, 0.8.

---

### S10 · Núcleo do checkout compartilhado

**Problema.** `IniciarCheckoutUseCase` (355 linhas) faz três coisas: cria o pedido em `Rascunho` com itens, reserva a vaga da janela e cria a preferência do Mercado Pago. O agente precisa das duas primeiras com Pix no lugar da terceira. Duplicar seria o pior caminho.
**Abordagem.** Extrair as fases 1 e 2 para `App/Services/Storefront/CheckoutCoreService.cs` (`CriarPedidoComReservaAsync(input) → Pedido em AguardandoPagamento`), sem mudar comportamento do storefront. O use case atual passa a chamar o serviço e segue com o Mercado Pago. Novo `CriarPedidoAtendimentoUseCase` chama o serviço com `Origem="whatsapp"` e devolve o pedido para S11 cobrar.
**Escopo.**
- Criar `CheckoutCoreService` movendo o código das fases 1-2 (idempotência `CheckoutIdempotencyService`, validação de janela e dia, bloqueio, frete por CEP, criação do pedido, `VagaOcupada.Ocupar`, rollback para `Cancelado` se a vaga falhar).
- `IniciarCheckoutUseCase` e `IniciarCheckoutGuestUseCase` passam a delegar (diff só de remoção).
- Criar `App/UseCases/Atendimento/CriarPedidoAtendimentoUseCase.cs` com input `(EmpresaId, ConversaId, ClienteId, Itens[(CardapioItemId, VariacaoId?, Qtd, Observacao?)], JanelaId, DataEntrega, EnderecoId, Observacoes?)`; grava `Conversa.PedidoEmAndamentoId`; `Origem = "whatsapp"`.
- `Pedido.Origem` aceita `"whatsapp"` (é string; adicionar constante em `CanalOrigem`? Não: `CanalOrigem` é enum de outra coisa. Adicionar constante `OrigemPedido.WhatsApp` ao lado das usadas em `CriarPedidoCommand`).
- Observação por item já existe (`PedidoItem.Observacao`); o serviço deve aceitá-la também no storefront (hoje só há `Observacoes` do pedido). RN-20.
**Fora.** Cupons; múltiplos endereços por pedido.
**Aceite.**
- [ ] Testes existentes de `IniciarCheckoutUseCase` continuam verdes sem alteração (só o `using` do serviço).
- [ ] `CriarPedidoAtendimentoUseCase` cria pedido `AguardandoPagamento` com vaga ocupada, `Origem="whatsapp"`, itens com snapshot de cardápio e observação por item.
- [ ] Janela lotada → `JanelaSemVagasException` e nenhum pedido fica em `Rascunho` (rollback para `Cancelado`).
- [ ] Chamada repetida com a mesma `IdempotencyKey` devolve o mesmo pedido.
**Testes (Red).** `CheckoutCoreServiceTests.CriaPedidoEReservaVaga`, `...JanelaLotadaCancelaRascunho`, `CriarPedidoAtendimentoUseCaseTests.OrigemWhatsappEObservacaoPorItem`.
**Rollback.** Reverter o commit de extração; o storefront não muda de comportamento.
**Depende de.** S04.
**Leitura mínima.** `App/UseCases/Storefront/Checkout/IniciarCheckoutUseCase.cs`; `IniciarCheckoutInput.cs`; `IniciarCheckoutGuestUseCase.cs` (só a parte que repete as fases 1-2); `App/UseCases/CriarPedido/CriarPedidoUseCase.cs` (linhas 16-30 e 85-100); `Domain/Entities/Storefront/VagaOcupada.cs`.
**Tamanho.** M. **Tier.** alto (refactor).

---

### S11 · Cobrança Pix do pedido, webhook e expiração

**Problema.** US-031, US-032, RN-23, RN-24, UC-01 E2: o pedido é gerado antes do pagamento; o pagamento é conciliado sem ação da dona; link expirado é reenviado uma vez. Hoje o Pix da Efi só serve parcela a receber e assinatura, e `AceitaPagamento` recusa registrar pagamento em `AguardandoPagamento`.
**Abordagem.** Entidade `CobrancaPedido` com `txid` prefixado `pd`; o webhook Pix existente roteia por prefixo (já faz isso com `cr`). Ao confirmar: transitar `AguardandoPagamento → Aguardando` (transição já permitida), registrar `PedidoPagamento(metodo="pix", referencia=txid)`, publicar `PedidoPagoEvent` (S18 consome). Job de expiração: primeira expiração reenvia cobrança nova e avisa; segunda cancela o pedido (o handler existente libera a vaga).
**Escopo.**
- Criar `Domain/Entities/Pagamentos/CobrancaPedido.cs`: `Id`, `EmpresaId`, `PedidoId`, `Txid` (único), `Valor`, `CopiaCola`, `QrCodeBase64`, `ExpiraEm`, `Status` (`Pendente`, `Paga`, `Expirada`, `Cancelada`), `PagaEm?`, `ValorPago?`, `E2eId?` (necessário para estorno), `Tentativa` (1 ou 2), `CriadaEm`. Migration `AddCobrancaPedido`.
- Criar `App/UseCases/Pedidos/Cobranca/GerarCobrancaPixPedidoUseCase.cs` (txid `pd{empresa6}{pedido18}` + sufixo da tentativa, ≤ 35 chars, mesmo padrão de `GerarPixQrParcelaReceberUseCase.cs:50`; expiração 30 min; idempotente por pedido+tentativa).
- Criar `ConfirmarPagamentoPixPedidoUseCase.cs` (lock em `Txid` via `GetByTxidComLockAsync` no repositório novo; valida `valorPago >= Valor`; transição; `RegistrarPagamentoPedidoUseCase`; `PedidoPagoEvent` no outbox antes do commit).
- `Api/Controllers/WebhookPixController.cs`: em `ProcessarPagamentoAsync`, `txid.StartsWith("pd")` → `ConfirmarPagamentoPixPedidoUseCase`. Extrair `endToEndId` do item do payload. **Corrigir #953 no mesmo PR:** falha em um txid não pode responder 200 silencioso; gravar `WebhookRecebido` com `Sucesso=false` e responder 500 para a Efi reenviar.
- Criar `Api/BackgroundServices/CobrancaPedidoJob.cs` (a cada 60 s): `ConsultarCobrancaAsync` para pendentes com `ExpiraEm < agora + 2 min` (reconciliação de webhook perdido); expiradas: tentativa 1 → gera nova cobrança e envia mensagem na conversa (via `IWhatsAppCloudClient`, texto de S08); tentativa 2 → `CancelarPedidoUseCase` com motivo `pagamento_expirado` e mensagem ao cliente.
- `Api/Controllers/PedidosController.cs`: `POST api/pedidos/{id}/cobranca-pix` (policy `Operador`) para o console e para reenviar na mão.
- Ferramenta `criar_pedido` (S06) passa a chamar S10 + este use case e a responder com resumo, total, copia-e-cola e QR (imagem via `EnviarArquivoAsync`).
**Fora.** Cartão (Mercado Pago continua no site como está); pagamento parcial.
**Aceite.**
- [ ] Webhook com `txid` `pd…` e valor igual → pedido `Aguardando`, pagamento registrado com `Referencia=txid`, `CobrancaPedido.Status=Paga`, `E2eId` gravado, `PedidoPagoEvent` no outbox.
- [ ] Webhook duplicado não registra segundo pagamento (lock + status).
- [ ] Valor menor que o esperado → não confirma, grava motivo, responde 200 (subpagamento é decisão humana).
- [ ] Falha interna ao processar → responde 500 e `WebhookRecebido.Sucesso=false` (regressão de #953 coberta).
- [ ] Job: expirada na tentativa 1 gera tentativa 2 e envia mensagem; expirada na tentativa 2 cancela e libera a vaga.
**Testes (Red).** `GerarCobrancaPixPedidoUseCaseTests.TxidComPrefixoPdEIdempotente`, `ConfirmarPagamentoPixPedidoUseCaseTests.ConfirmaETransita`, `...DuplicadoNaoRegistraDuasVezes`, `...SubpagamentoNaoConfirma`, `WebhookPixControllerTests.FalhaInternaResponde500`, `CobrancaPedidoJobTests.SegundaExpiracaoCancela`.
**Rollback.** Migration `Down`; remover o ramo `pd` do controller; desligar o job por `BackgroundJobs:EnableCobrancaPedido=false`.
**Depende de.** S10; onda 0.4.
**Leitura mínima.** `App/Ports/Output/IEfiPixService.cs`; `App/UseCases/Financeiro/Pagamentos/GerarPixQrParcelaReceberUseCase.cs`; `Api/Controllers/WebhookPixController.cs`; `Api/BackgroundServices/ContaReceberPixReconciliacaoJob.cs` (padrão de job); `App/UseCases/RegistrarPagamentoPedido/RegistrarPagamentoPedidoCommand.cs`; `Domain/Sales/PedidoStateMachine.cs`; `App/Events/Storefront/Handlers/LiberarVagaOnPedidoCanceladoHandler.cs`.
**Tamanho.** G. **Tier.** alto (migration, pagamento).

---

### S12 · Esteira: estado "saiu para entrega" e fim da aprovação no caminho feliz

**Problema.** RN-27 e UC-04: pedido pago vai direto para a fila e imprime; "saiu para entrega" é um passo real (US-040) e hoje é só rótulo de `Pronto`. A aprovação manual (`AguardandoAprovacaoBaba`) fica apenas para exceção de área (UC-02).
**Abordagem.** Estender a máquina de estados (ADR-0042, #864 parcial, sem os dois eixos ainda). Adicionar `SaiuParaEntrega = 9`. Transições: `Pronto → {SaiuParaEntrega, Entregue, Cancelado}`; `SaiuParaEntrega → {Entregue, Cancelado}`. `ComEstoqueDescontado` inclui `SaiuParaEntrega`. Webhooks de pagamento (Pix S11 e Mercado Pago) vão para `Aguardando`, salvo `Pedido.RequerAprovacao == true`.
**Escopo.**
- `Domain/Sales/StatusPedido.cs`, `PedidoStateMachine.cs`, `StatusPedidoMapper.cs` (`"saiu_para_entrega"`), `StatusPedidoVocabulario.cs` (rótulo lojista "Saiu para entrega", rótulo cliente "Saiu para entrega", bucket Operacional). `Pronto` volta a ser "Pronto" para o cliente.
- `Pedido.RequerAprovacao` (bool, default false) + `MarcarRequerAprovacao(motivo)`; migration `AddRequerAprovacaoPedido`. Setado por S14 quando a dona aceitou fora de área e por `CheckoutCoreService` quando o storefront aceitar fora de área (hoje o storefront recusa; manter).
- Handler do webhook Mercado Pago (`Async/Pagamentos/Webhooks/MercadoPagoGatewayAdapter` ou o processor que hoje leva a `AguardandoAprovacaoBaba`): destino `Aguardando` quando `!RequerAprovacao`.
- R8: `EasyStock.Web/Helpers/StatusHelper.cs` ganha o status (o teste de drift `StatusHelperCobreTodosOsStatusPedido` obriga); `Api/Mobile/Controllers/KdsController.cs` `StatusesPermitidos` ganha `saiu_para_entrega` (some em S19). O site casadababa.com lê `schemaVersion` do contrato; o rótulo novo é aditivo, mas avisar no PR.
**Fora.** Eixo financeiro derivado (#864 completo); retirada no balcão como estado.
**Aceite.**
- [ ] `PedidoStateMachineTests` cobre 100% das transições novas e rejeições.
- [ ] Teste de drift das projeções passa nas três superfícies.
- [ ] Pedido pago sem `RequerAprovacao` fica `Aguardando`; com `RequerAprovacao` fica `AguardandoAprovacaoBaba`.
- [ ] Cancelar em `SaiuParaEntrega` devolve estoque (usa `ComEstoqueDescontado`).
**Testes (Red).** `PedidoStateMachineTests.ProntoParaSaiuParaEntrega`, `...SaiuParaEntregaParaEntregue`, `StatusPedidoVocabularioTests.CobreSaiuParaEntrega`, `ConfirmarPagamentoPixPedidoUseCaseTests.RequerAprovacaoVaiParaAprovacao`.
**Rollback.** Migration `Down`; remover o valor do enum (nenhum pedido em produção terá o status se o rollback for antes do deploy do KDS).
**Depende de.** S11.
**Leitura mínima.** `Domain/Sales/StatusPedido.cs`, `PedidoStateMachine.cs`, `StatusPedidoMapper.cs`, `StatusPedidoVocabulario.cs`; `EasyStock.Web/Helpers/StatusHelper.cs`; `EasyStock.Domain.Tests/**/PedidoStateMachineTests.cs`; `docs/adr/0042-esteira-pedidos-canonica.md` (só "Decisão").
**Tamanho.** M. **Tier.** alto (migration, domínio).

---

### S13 · Avisos de status ao cliente pelo WhatsApp

**Problema.** #585 aberta: transição de status não avisa o cliente. US-039, US-040, US-045, RN-32 (só quando a dona marca), RN-05 (sai mesmo com a conversa assumida), RN-37 (agradecimento nunca desliga).
**Abordagem.** Consumidor de `PedidoMudouStatusEvent` (outbox `"pedido.mudou_status"`, já existe com `PedidoMudouStatusLogHandler`) que publica no **outbox de notificações** (retry, log, janela) um evento por status relevante, canal WhatsApp, provider Meta (S09; texto na janela de 24 h, template fora). Templates Scriban seedados. Preferência do cliente (S24) filtra preparo e saída; agradecimento sempre. Até S24 existir, o filtro é `Cliente.ConsentiuMarketing`? Não: avisos são transacionais; até S24, enviar sempre.
**Escopo.**
- `Domain/Enums/Notifications/TipoEventoNotificacao.cs`: `PedidoPagoConfirmado = 38`, `PedidoEmPreparo = 39`, `PedidoSaiuParaEntrega = 40`, `PedidoEntregue = 41`.
- Criar `App/Events/Pedidos/Handlers/NotificarClienteStatusPedidoHandler.cs`: `Preparando` → `PedidoEmPreparo` com `{previsao}` = label da janela do pedido (`VagaOcupada` → `JanelaEntrega.Label` + data); `SaiuParaEntrega` → `PedidoSaiuParaEntrega`; `Entregue` → `PedidoEntregue` (agradecimento + convite às redes, `Storefront` ganha `LinksRedesJson`? Não: usar `Storefront.SubtituloPublico` como texto e um campo novo `Storefront.InstagramUrl`; migration pequena).
- S11 publica `PedidoPagoConfirmado` ("recebemos seu pagamento, pedido nº … agendado para …").
- Seeds de `TemplateNotificacao` (canal WhatsApp, `Categoria=Transacional`) em `Api/Data/NotificacoesGlobaisSeed.cs` com os textos do doc 03 (áudio 06). `RotinaNotificacao` por evento (`TriggerTipo=Evento`, canal `[WhatsApp]`, sem janela de horário: aviso de status não espera).
- Destinatário: `Cliente.Telefone` principal em E.164; sem telefone → não enfileira e loga.
**Fora.** Push para o console (S18); SMS.
**Aceite.**
- [ ] `Preparando` gera exatamente uma mensagem no outbox de notificações, canal WhatsApp, com a previsão da janela.
- [ ] `Entregue` gera agradecimento mesmo com `AvisosStatusAtivos=false` (S24) e mesmo com a conversa `Assumida`.
- [ ] Status sem template (ex.: `Cancelado`) não gera nada.
- [ ] Reprocessar o mesmo evento (at-least-once) não duplica: `IdempotencyKey` do outbox = `pedidoId + statusNovo`.
**Testes (Red).** `NotificarClienteStatusPedidoHandlerTests.PreparandoEnfileiraComPrevisao`, `...EntregueSempreEnfileira`, `...IdempotentePorPedidoEStatus`.
**Rollback.** Desativar as `RotinaNotificacao` (flag `Ativa=false`); nenhum schema além do enum.
**Depende de.** S09, S12.
**Leitura mínima.** `App/Events/Pedidos/PedidoMudouStatusEvent.cs`; `App/Events/Pedidos/Handlers/PedidoMudouStatusLogHandler.cs`; `App/UseCases/Notifications/PublicarEventoNotificacaoCommand.cs`; `App/Services/Notifications/NotificadorService.cs` (só assinaturas públicas); `Api/Data/NotificacoesGlobaisSeed.cs` (um template e uma rotina de exemplo); `Domain/Entities/Notifications/RotinaNotificacao.cs`.
**Tamanho.** M. **Tier.** alto.

---

### S14 · Endereço em texto livre e área de entrega

**Problema.** US-011, US-012, US-013, RN-09 a RN-11: endereço completo com CEP antes de fechar; CEP fora de área responde na hora; lead que insiste vai para a dona.
**Abordagem.** A extração de campos fica no LLM (é a entrada da ferramenta `validar_endereco`). O backend normaliza e decide: ViaCEP para completar logradouro/bairro/cidade (flag ligada em 0.7); `CalcularFreteUseCase` (zona por CEP/bairro ou raio via Nominatim) para dentro/fora, taxa e tempo. Fora de área: resposta imediata com `ConfiguracaoAtendimento.MensagemForaArea`; se o lead insistir, `escalar_para_dona("fora_de_area")` e, se ela liberar no console, `Pedido.RequerAprovacao=true` (S12).
**Escopo.**
- Criar `App/UseCases/Atendimento/ValidarEnderecoUseCase.cs` → `{ DentroDaArea, EnderecoNormalizado, TaxaEntrega, TempoEstimadoMin, Motivo }`.
- Criar `ConfirmarEnderecoClienteUseCase.cs` → grava `ClienteEndereco` (padrão) e campos primários de `Cliente` (`Endereco`, `Cep`, `Bairro`, `Cidade`, `Complemento`, `Apt`) via `AdicionarClienteEnderecoUseCase` existente.
- Ferramentas `validar_endereco` e `confirmar_endereco` (S06) sobre esses use cases; `listar_endereco_salvo` devolve os endereços do cliente para US-014 (mais de um → o agente pede para escolher).
- Endpoint `POST api/atendimento/conversas/{id}/liberar-fora-de-area` (policy `Operador`) grava decisão na conversa (`ContextoJson.foraDeAreaLiberado=true`, motivo) para o próximo `criar_pedido` marcar `RequerAprovacao`.
**Fora.** Geocodificação do número; mapa.
**Aceite.**
- [ ] CEP em zona ativa → `DentroDaArea=true` com taxa da zona; CEP fora → `false` e `Motivo="fora_area"`, sem exceção.
- [ ] ViaCEP indisponível → valida só pela zona por CEP e devolve `EnderecoNormalizado` parcial (não bloqueia).
- [ ] `confirmar_endereco` cria `ClienteEndereco` padrão e atualiza os campos primários.
**Testes (Red).** `ValidarEnderecoUseCaseTests.DentroDaZona`, `...ForaDaZonaSemExcecao`, `...ViaCepIndisponivelDegrada`, `ConfirmarEnderecoClienteUseCaseTests.GravaPadraoEPrimarios`.
**Rollback.** Remover use cases e ferramentas.
**Depende de.** S06; onda 0.7.
**Leitura mínima.** `App/UseCases/Storefront/Frete/CalcularFreteUseCase.cs`; `Domain/Entities/Storefront/FreteZona.cs` (linhas 1-80); `Integrations/Cep/ViaCepLookupClient.cs`; `App/UseCases/AdicionarClienteEndereco/*.cs`; `Domain/Entities/Cliente.cs` (linhas 20-45 e `ClienteEndereco`).
**Tamanho.** M. **Tier.** alto.

---

### S15 · Linha de produto e tempo de preparo numérico

**Problema.** US-024, US-028, RN-06, RN-17, RN-22: todo item declara a linha (para servir ou para preparar em casa); o agente nunca promete prazo menor que o preparo; a faixa informada tem respiro de 40 min. Hoje `CardapioItem.TempoPreparo` é string de exibição.
**Abordagem.** Campos aditivos no cardápio (é o que o cliente vê e o que o pedido copia).
**Escopo.**
- `Domain/Entities/Storefront/CardapioItem.cs`: `Linha` (enum `LinhaProduto { ParaServir = 1, PrepararEmCasa = 2 }`, default `ParaServir`), `TempoPreparoMinutos` (int?, default null = usa `ConfiguracaoAtendimento.TempoPreparoPadraoMinutos`, campo novo em S08 com default 60), `InstrucaoFinalizacao` (string?, RN-18). Manter `TempoPreparo` string. Migration `AddLinhaETempoPreparoCardapioItem`.
- `PedidoItem`: `LinhaSnapshot` (string) copiado na criação (S10 e checkout). Migration na mesma fatia.
- Contrato público v2 do menu ganha `linha` e `tempoPreparoMinutos` (aditivo; `docs/contracts/menu-publico.v2.schema.json` afrouxado já aceita); `ListarCardapioPublicoUseCase` projeta.
- `App/Services/Pedidos/CalculadoraPrazoPedido.cs`: `PrazoMinimo(itens) = max(TempoPreparoMinutos) + RespiroMinutos`; usado por S16 e pela ferramenta `consultar_pedido` (previsão).
- Endpoints de cardápio existentes (`AdminStorefrontCardapioController` / `TenantVitrineCardapioController`) aceitam os campos (R8).
**Fora.** Capacidade por linha (Q2 aberta); adicionais (convenção: item avulso na seção "Adicionais", sem código).
**Aceite.**
- [ ] Item novo sem linha → `ParaServir`; contrato público emite `linha` e `tempoPreparoMinutos`.
- [ ] `PedidoItem.LinhaSnapshot` preenchido no checkout e no atendimento.
- [ ] `CalculadoraPrazoPedido` com itens de 40 e 60 min e respiro 40 → 100.
**Testes (Red).** `CardapioItemTests.LinhaPadraoParaServir`, `ListarCardapioPublicoUseCaseTests.EmiteLinhaETempo`, `CalculadoraPrazoPedidoTests.MaxMaisRespiro`.
**Rollback.** Migration `Down`.
**Depende de.** S08, S10.
**Leitura mínima.** `Domain/Entities/Storefront/CardapioItem.cs` (só propriedades e factory); `App/UseCases/Storefront/Menu/ListarCardapioPublicoUseCase.cs`; `docs/contracts/menu-publico.v2.schema.json`; `Domain/Entities/Pedido.cs` (linhas 236-270).
**Tamanho.** M. **Tier.** alto (migration).

---

### S16 · Janelas com antecedência mínima e capacidade para o agente

**Problema.** US-026, US-027, RN-21, UC-03 E1: o agente só oferece janelas com vaga e nunca uma janela cujo início seja antes de agora + prazo mínimo.
**Abordagem.** Estender `ListarJanelasDisponiveisInput` com `PrazoMinimoMinutos?`; o use case já calcula vagas; adicionar o corte por horário (fuso da loja via `HorarioBrasil`).
**Escopo.**
- `App/UseCases/Storefront/Agendamento/ListarJanelasDisponiveisInput.cs` + `UseCase`: parâmetro opcional; janela cujo `HoraInicio` na data seja `< agora + prazo` sai da lista.
- Ferramenta `listar_janelas(data?)` (S06) chama com `PrazoMinimo` de `CalculadoraPrazoPedido` para o carrinho em `ContextoJson`.
- `CheckoutCoreService` (S10) revalida o mesmo corte ao criar o pedido (defesa contra janela que ficou inválida entre listar e criar).
**Fora.** Capacidade por linha de produto.
**Aceite.**
- [ ] Hoje 11:00, prazo 100 min: janela 12:00-13:00 de hoje não aparece; 13:00-14:00 aparece; amanhã aparece tudo.
- [ ] Criar pedido em janela abaixo do prazo mínimo → `RegraDeDominioVioladaException("janela_abaixo_do_prazo")`.
**Testes (Red).** `ListarJanelasDisponiveisUseCaseTests.CortaPorPrazoMinimo`, `CheckoutCoreServiceTests.RejeitaJanelaAbaixoDoPrazo`.
**Rollback.** Parâmetro opcional; sem migration.
**Depende de.** S10, S15.
**Leitura mínima.** `App/UseCases/Storefront/Agendamento/ListarJanelasDisponiveisUseCase.cs`, `ListarJanelasDisponiveisInput.cs`; `App/Common/HorarioBrasil.cs`.
**Tamanho.** P. **Tier.** alto.
