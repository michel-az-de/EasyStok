# Onda 2 — Pedido e cobrança na conversa (S10–S16)

Objetivo: o agente cria o pedido antes do pagamento, a cobrança do Mercado Pago é conciliada sem a
dona, o pedido pago entra direto na fila e o cliente recebe os avisos de status pelo WhatsApp.
Cobre US-011, US-012, US-014, US-021 (lado API), US-024, US-026, US-027, US-028, US-031, US-032,
US-039, US-040, US-045, RN-06, RN-09, RN-10, RN-21 a RN-24, RN-27, RN-32, D9.

Decisão A (22/09/2026, doc 08): **Mercado Pago é o gateway único de pedidos**, no site e na conversa.
A Efi fica só para contas a receber da FMA. S11 aqui define o lado do pedido; o processor do webhook
que confirma o pagamento é S32 (doc 08) e entra logo depois de S11.

Dependências externas: onda 0.7, 0.8, 0.9.

---

### S10 · Núcleo do checkout compartilhado

**Problema.** `IniciarCheckoutUseCase` (355 linhas) faz três coisas: cria o pedido em `Rascunho` com itens, reserva a vaga da janela e cria a preferência do Mercado Pago. O agente precisa das duas primeiras e da terceira pelo mesmo caminho. Duplicar seria o pior caminho.
**Abordagem.** Extrair as fases 1 e 2 para `App/Services/Storefront/CheckoutCoreService.cs` (`CriarPedidoComReservaAsync(input) → Pedido em AguardandoPagamento`), sem mudar comportamento do storefront. O use case atual passa a chamar o serviço e, na fase 3, o `GerarCobrancaPedidoUseCase` de S11. Novo `CriarPedidoAtendimentoUseCase` chama o serviço com `Origem="whatsapp"` e devolve o pedido para S11 cobrar.
**Escopo.**
- Criar `CheckoutCoreService` movendo o código das fases 1-2 (idempotência `CheckoutIdempotencyService`, validação de janela e dia, bloqueio, frete por CEP, criação do pedido, `VagaOcupada.Ocupar`, rollback para `Cancelado` se a vaga falhar).
- `IniciarCheckoutUseCase` e `IniciarCheckoutGuestUseCase` passam a delegar (diff só de remoção).
- Criar `App/UseCases/Atendimento/CriarPedidoAtendimentoUseCase.cs` com input `(EmpresaId, ConversaId, ClienteId, Itens[(CardapioItemId, VariacaoId?, Qtd, Observacao?)], JanelaId, DataEntrega, EnderecoId, Observacoes?)`; grava `Conversa.PedidoEmAndamentoId`; `Origem = "whatsapp"`.
- Constante `OrigemPedido.WhatsApp = "whatsapp"` ao lado das usadas em `CriarPedidoCommand` (`Pedido.Origem` é string).
- Observação por item já existe (`PedidoItem.Observacao`); o serviço aceita também no storefront (hoje só há `Observacoes` do pedido). RN-20.
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

### S11 · Cobrança do pedido pelo Mercado Pago, confirmação e expiração

**Problema.** US-031, US-032, RN-23, RN-24, UC-01 E2: o pedido é gerado antes do pagamento; o pagamento é conciliado sem ação da dona; link expirado é reenviado uma vez. Hoje a preferência é criada e nada confirma o pagamento (doc 08, seção 1), e `AceitaPagamento` recusa registrar pagamento em `AguardandoPagamento`.
**Abordagem.** Entidade `CobrancaPedido` com a preferência do Mercado Pago (`external_reference = PedidoId`, expiração de 30 min). O processor de S32 recebe o webhook, consulta o pagamento e chama `ConfirmarPagamentoPedidoUseCase`, que transita `AguardandoPagamento → Aguardando` (transição já permitida), registra `PedidoPagamento` e publica `PedidoPagoEvent` (S18 consome). Job de expiração: primeira expiração gera cobrança nova e avisa; segunda cancela o pedido (o handler existente libera a vaga). Um caminho só para site e conversa.
**Escopo.**
- Criar `Domain/Entities/Pagamentos/CobrancaPedido.cs`: `Id`, `EmpresaId`, `PedidoId`, `Provedor` (`"mercadopago"`; string para não fechar a porta), `ReferenciaExterna` (id da preferência), `LinkPagamento` (`init_point`), `Valor`, `ExpiraEm`, `Status` (`Pendente`, `Paga`, `Expirada`, `Cancelada`, `Estornada`), `PagaEm?`, `ValorPago?`, `PagamentoExternoId?` (id do pagamento no MP; necessário para estorno), `MetodoPagamento?` (`pix`, `credito`, `debito`, `outro`, de `payment_method_id`), `Tentativa` (1 ou 2), `CriadaEm`. Migration `AddCobrancaPedido`; índice único `(Provedor, ReferenciaExterna)`; índice `(PedidoId, Status)`.
- Criar `App/UseCases/Pedidos/Cobranca/GerarCobrancaPedidoUseCase.cs`: monta a preferência com itens do pedido e frete, `external_reference = PedidoId`, `expires = true`, `expiration_date_to = agora + 30 min`, `notification_url` e `back_urls` das opções, `X-Idempotency-Key = PedidoId + tentativa`; grava `CobrancaPedido`; devolve o link. Idempotente por pedido e tentativa (segunda chamada devolve a mesma cobrança pendente).
- Criar `ConfirmarPagamentoPedidoUseCase.cs` (chamado pelo processor de S32): lock por pedido (`SELECT FOR UPDATE`, padrão de `AprovarPedidoStorefrontUseCase`); pagamento `approved` com `valorPago >= Valor`; transição para `Aguardando` (ou `AguardandoAprovacaoBaba` se `Pedido.RequerAprovacao`, S12); `RegistrarPagamentoPedidoUseCase(metodo, referencia = PagamentoExternoId)`; `CobrancaPedido.Status = Paga`; `PedidoPagoEvent` no outbox antes do commit; enfileira impressão (S20). Idempotente: cobrança já `Paga` → no-op.
- `IniciarCheckoutUseCase` (fase 3) passa a chamar `GerarCobrancaPedidoUseCase` (mesmo caminho do site e da conversa; `CobrancaPedido` registrada para pedidos do site também).
- Criar `Api/BackgroundServices/CobrancaPedidoJob.cs` (a cada 60 s): pendentes com `ExpiraEm < agora + 2 min` → consulta `GET v1/payments/search?external_reference={PedidoId}` (S32) para pegar webhook perdido; expiradas sem pagamento: tentativa 1 e pedido com conversa → gera nova cobrança e envia o link na conversa (via `IWhatsAppCloudClient`, texto de S08); tentativa 1 sem conversa (site) ou tentativa 2 → `CancelarPedidoUseCase` com motivo `pagamento_expirado` (o handler existente libera a vaga) e aviso ao cliente quando houver telefone.
- `Api/Controllers/PedidosController.cs`: `POST api/pedidos/{id}/cobranca` (policy `Operador`) para reemitir na mão; resposta traz o link.
- Ferramenta `criar_pedido` (S06) chama S10 + este use case e responde com resumo, total e o link ("Pix ou cartão, como preferir").
**Fora.** Pagamento parcial; cartão salvo; Bricks.
**Aceite.**
- [ ] Criar cobrança gera preferência com `external_reference` do pedido, itens, frete e expiração de 30 min, e grava `CobrancaPedido(Pendente)` com o link.
- [ ] `ConfirmarPagamentoPedidoUseCase` com pagamento aprovado e valor igual → pedido `Aguardando`, `PedidoPagamento` com `Referencia = PagamentoExternoId` e método do pagamento, cobrança `Paga`, `PedidoPagoEvent` no outbox, impressão enfileirada.
- [ ] Confirmação repetida → no-op (lock + status).
- [ ] Valor menor que o esperado → não confirma, grava motivo, não lança (o processor responde 200).
- [ ] Job: expirada na tentativa 1 com conversa gera tentativa 2 e envia o link; expirada na tentativa 2, ou sem conversa, cancela e libera a vaga.
- [ ] Checkout do site passa pelo mesmo use case e registra `CobrancaPedido`.
**Testes (Red).** `GerarCobrancaPedidoUseCaseTests.PreferenciaComExternalReferenceEExpiracao`, `...IdempotentePorTentativa`, `ConfirmarPagamentoPedidoUseCaseTests.ConfirmaETransita`, `...RepetidoNoOp`, `...SubpagamentoNaoConfirma`, `CobrancaPedidoJobTests.SegundaExpiracaoCancela`, `...SemConversaCancelaNaPrimeira`, `IniciarCheckoutUseCaseTests.RegistraCobrancaPedido`.
**Rollback.** Migration `Down`; job desligado por `BackgroundJobs:EnableCobrancaPedido=false`; o site volta ao caminho anterior (preferência sem `CobrancaPedido`).
**Depende de.** S10; onda 0.9. S32 (doc 08) é o consumidor imediato: implementar em seguida.
**Leitura mínima.** `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs` e `MercadoPagoOptions.cs`; `App/UseCases/Storefront/Checkout/IniciarCheckoutUseCase.cs` (linhas 239-290, fase 3); `App/UseCases/Storefront/Aprovacao/AprovarPedidoStorefrontUseCase.cs` (padrão de lock); `App/UseCases/RegistrarPagamentoPedido/RegistrarPagamentoPedidoCommand.cs`; `Domain/Sales/PedidoStateMachine.cs`; `App/Events/Storefront/Handlers/LiberarVagaOnPedidoCanceladoHandler.cs`; `Api/BackgroundServices/ContaReceberPixReconciliacaoJob.cs` (padrão de job).
**Tamanho.** G. **Tier.** alto (migration, pagamento).

---

### S12 · Esteira: estado "saiu para entrega" e fim da aprovação no caminho feliz

**Problema.** RN-27 e UC-04: pedido pago vai direto para a fila e imprime; "saiu para entrega" é um passo real (US-040) e hoje é só rótulo de `Pronto`. A aprovação manual (`AguardandoAprovacaoBaba`) fica apenas para exceção de área (UC-02). Hoje nenhum código leva `AguardandoPagamento` a `AguardandoAprovacaoBaba` (doc 08); quem transita passa a ser `ConfirmarPagamentoPedidoUseCase` (S11).
**Abordagem.** Estender a máquina de estados (ADR-0042, #864 parcial, sem os dois eixos ainda). Adicionar `SaiuParaEntrega = 9`. Transições: `Pronto → {SaiuParaEntrega, Entregue, Cancelado}`; `SaiuParaEntrega → {Entregue, Cancelado}`. `ComEstoqueDescontado` inclui `SaiuParaEntrega`. Pagamento confirmado vai para `Aguardando`, salvo `Pedido.RequerAprovacao == true`.
**Escopo.**
- `Domain/Sales/StatusPedido.cs`, `PedidoStateMachine.cs`, `StatusPedidoMapper.cs` (`"saiu_para_entrega"`), `StatusPedidoVocabulario.cs` (rótulo lojista "Saiu para entrega", rótulo cliente "Saiu para entrega", bucket Operacional). `Pronto` volta a ser "Pronto" para o cliente.
- `Pedido.RequerAprovacao` (bool, default false) + `MarcarRequerAprovacao(motivo)`; migration `AddRequerAprovacaoPedido`. Setado por S14 quando a dona aceitou fora de área e por `CheckoutCoreService` quando o storefront aceitar fora de área (hoje o storefront recusa; manter).
- `ConfirmarPagamentoPedidoUseCase` (S11) usa `RequerAprovacao` para escolher o destino.
- R8: `EasyStock.Web/Helpers/StatusHelper.cs` ganha o status (o teste de drift `StatusHelperCobreTodosOsStatusPedido` obriga); `Api/Mobile/Controllers/KdsController.cs` `StatusesPermitidos` ganha `saiu_para_entrega` (some em S19). O site casadababa.com lê `schemaVersion` do contrato; o rótulo novo é aditivo, mas avisar no PR.
**Fora.** Eixo financeiro derivado (#864 completo); retirada no balcão como estado.
**Aceite.**
- [ ] `PedidoStateMachineTests` cobre 100% das transições novas e rejeições.
- [ ] Teste de drift das projeções passa nas três superfícies.
- [ ] Pedido pago sem `RequerAprovacao` fica `Aguardando`; com `RequerAprovacao` fica `AguardandoAprovacaoBaba`.
- [ ] Cancelar em `SaiuParaEntrega` devolve estoque (usa `ComEstoqueDescontado`).
**Testes (Red).** `PedidoStateMachineTests.ProntoParaSaiuParaEntrega`, `...SaiuParaEntregaParaEntregue`, `StatusPedidoVocabularioTests.CobreSaiuParaEntrega`, `ConfirmarPagamentoPedidoUseCaseTests.RequerAprovacaoVaiParaAprovacao`.
**Rollback.** Migration `Down`; remover o valor do enum (nenhum pedido em produção terá o status se o rollback for antes do deploy do KDS).
**Depende de.** S11.
**Leitura mínima.** `Domain/Sales/StatusPedido.cs`, `PedidoStateMachine.cs`, `StatusPedidoMapper.cs`, `StatusPedidoVocabulario.cs`; `EasyStock.Web/Helpers/StatusHelper.cs`; `EasyStock.Domain.Tests/**/PedidoStateMachineTests.cs`; `docs/adr/0042-esteira-pedidos-canonica.md` (só "Decisão").
**Tamanho.** M. **Tier.** alto (migration, domínio).

---

### S13 · Avisos de status ao cliente pelo WhatsApp

**Problema.** #585 aberta: transição de status não avisa o cliente. US-039, US-040, US-045, RN-32 (só quando a dona marca), RN-05 (sai mesmo com a conversa assumida), RN-37 (agradecimento nunca desliga).
**Abordagem.** Consumidor de `PedidoMudouStatusEvent` (outbox `"pedido.mudou_status"`, já existe com `PedidoMudouStatusLogHandler`) que publica no **outbox de notificações** (retry, log, janela) um evento por status relevante, canal WhatsApp, provider Meta (S09; texto na janela de 24 h, template fora). Templates Scriban seedados. Preferência do cliente (S24) filtra preparo e saída; agradecimento sempre. Até S24 existir: avisos são transacionais, enviar sempre.
**Escopo.**
- `Domain/Enums/Notifications/TipoEventoNotificacao.cs`: `PedidoPagoConfirmado = 38`, `PedidoEmPreparo = 39`, `PedidoSaiuParaEntrega = 40`, `PedidoEntregue = 41`.
- Criar `App/Events/Pedidos/Handlers/NotificarClienteStatusPedidoHandler.cs`: `Preparando` → `PedidoEmPreparo` com `{previsao}` = label da janela do pedido (`VagaOcupada` → `JanelaEntrega.Label` + data); `SaiuParaEntrega` → `PedidoSaiuParaEntrega`; `Entregue` → `PedidoEntregue` (agradecimento + convite às redes: `Storefront.InstagramUrl`, campo novo, migration pequena).
- S11 publica `PedidoPagoConfirmado` ("recebemos seu pagamento, pedido nº … agendado para …").
- Seeds de `TemplateNotificacao` (canal WhatsApp, `Categoria=Transacional`) em `Api/Data/NotificacoesGlobaisSeed.cs` com os textos do doc 03 (áudio 06) e `Metadados` com o nome do template da Meta correspondente (onda 0.3). `RotinaNotificacao` por evento (`TriggerTipo=Evento`, canal `[WhatsApp]`, sem janela de horário: aviso de status não espera).
- Destinatário: `Cliente.Telefone` principal em E.164; sem telefone → não enfileira e loga.
**Fora.** Push para o console (S18); SMS.
**Aceite.**
- [ ] `Preparando` gera exatamente uma mensagem no outbox de notificações, canal WhatsApp, com a previsão da janela.
- [ ] `Entregue` gera agradecimento mesmo com `AvisosStatusAtivos=false` (S24) e mesmo com a conversa `Assumida`.
- [ ] Status sem template (ex.: `Cancelado`) não gera nada.
- [ ] Reprocessar o mesmo evento (at-least-once) não duplica: `IdempotencyKey` do outbox = `pedidoId + statusNovo`.
**Testes (Red).** `NotificarClienteStatusPedidoHandlerTests.PreparandoEnfileiraComPrevisao`, `...EntregueSempreEnfileira`, `...IdempotentePorPedidoEStatus`.
**Rollback.** Desativar as `RotinaNotificacao` (`Ativa=false`); nenhum schema além do enum.
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
- Ferramentas `validar_endereco` e `confirmar_endereco` (S06) sobre esses use cases; `listar_endereco_salvo` devolve os endereços do cliente para US-014 (mais de um → o agente pede para escolher, com botões `acao:escolher_endereco:<id>`).
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
- `Domain/Entities/Storefront/CardapioItem.cs`: `Linha` (enum `LinhaProduto { ParaServir = 1, PrepararEmCasa = 2 }`, default `ParaServir`), `TempoPreparoMinutos` (int?, default null = usa `ConfiguracaoAtendimento.TempoPreparoPadraoMinutos`, S08), `InstrucaoFinalizacao` (string?, RN-18). Manter `TempoPreparo` string. Migration `AddLinhaETempoPreparoCardapioItem`.
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
- Ferramenta `listar_janelas(data?)` (S06) chama com `PrazoMinimo` de `CalculadoraPrazoPedido` para o carrinho em `ContextoJson`; oferece as opções com botões `acao:escolher_janela:<id>:<data>` (até 3 por mensagem).
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
