# Onda 5 — CRM leve e pós-venda (S24–S27)

Objetivo: a dona conhece o cliente pelo sistema, na Huggy e no console; reclamação vira ocorrência com
reembolso rastreado; avaliação é de um toque.
Cobre US-007, US-015 a US-019, US-046 a US-050, RN-08, RN-12 a RN-14, RN-34 a RN-38, D10.

---

### S24 · Tags, notas internas, bloqueio e preferências do cliente

**Problema.** `Cliente` só tem `Observacoes` (texto único) e `ConsentiuMarketing`. Faltam tags para campanha, nota datada por pedido, bloqueio que vale em todos os canais e a preferência de desligar avisos de status.
**Abordagem.** Aditivo sobre `Cliente`, sem tocar em `AtualizarCadastro` (7 parâmetros posicionais, risco R8).
**Escopo.**
- Criar `Domain/Entities/ClienteTag.cs` (`Id`, `EmpresaId`, `ClienteId`, `Tag` (lowercase, sem acento, ≤ 40), `Origem` (`dona`, `agente`, `sistema`), `CriadoEm`; único por `(ClienteId, Tag)`).
- Criar `Domain/Entities/ClienteNota.cs` (`Id`, `EmpresaId`, `ClienteId`, `Texto` (≤ 500), `PedidoId?`, `MensagemId?`, `Autor`, `CriadoEm`). Nunca é projetada para o cliente: teste de arquitetura garante que nenhum DTO de storefront ou de agente exponha `ClienteNota.Texto` (o dossiê do agente entrega as notas em campo `[interno]` que o prompt proíbe reproduzir; S06).
- `Cliente`: `Bloqueado` (bool), `BloqueadoEm?`, `MotivoBloqueio?`, `AvisosStatusAtivos` (bool, default true) + métodos `Bloquear(motivo)`, `Desbloquear()`, `DefinirAvisosStatus(bool)`, `DefinirConsentimentoMarketing(bool, quando)`.
- Migration `AddClienteTagNotaBloqueioPreferencias`.
- Endpoints em `Api/Controllers/ClientesController.cs` (ou controller novo `ClientesCrmController`): `GET|POST|DELETE api/clientes/{id}/tags`, `GET|POST api/clientes/{id}/notas`, `POST api/clientes/{id}/bloquear {motivo}`, `POST api/clientes/{id}/desbloquear`, `PUT api/clientes/{id}/preferencias {avisosStatusAtivos, consentiuMarketing}`.
- Efeitos: S05 (cliente bloqueado não recebe saudação; conversa vai para a dona); S13 (preparo e saída respeitam `AvisosStatusAtivos`; agradecimento sempre); S25 (sync na Huggy); ferramentas `registrar_restricao`, `registrar_nota` (S06).
- Tags sugeridas no seed: `intolerante_lactose`, `vegano`, `sem_gluten`, `risco`, `encomenda`.
**Fora.** Tag em pedido; histórico de bloqueio.
**Aceite.**
- [ ] Tag duplicada não cria segunda linha (409 na API, no-op no domínio).
- [ ] Nota com `PedidoId` de outro cliente → 400.
- [ ] Cliente bloqueado: `criar_pedido` recusa com `cliente_bloqueado`; `startedChat` escala para a dona.
- [ ] `AvisosStatusAtivos=false`: `Preparando` não enfileira; `Entregue` enfileira.
- [ ] Teste de arquitetura: nenhum tipo em `App/UseCases/Storefront/**` ou `App/Services/Atendimento/Ferramentas/**` referencia `ClienteNota.Texto` fora do dossiê marcado.
**Testes (Red).** `ClienteTagTests.NormalizaEUnica`, `ClienteTests.BloquearGravaMotivoEData`, `NotificarClienteStatusPedidoHandlerTests.RespeitaAvisosStatus`, `CriarPedidoAtendimentoUseCaseTests.RecusaBloqueado`, `ArchitectureTests.NotaInternaNaoVazaParaStorefront`.
**Rollback.** Migration `Down`.
**Depende de.** S13 (para o filtro), S06 (ferramentas).
**Leitura mínima.** `Domain/Entities/Cliente.cs`; `Api/Controllers/ClientesController.cs` (rotas e policies); `App/UseCases/AdicionarClienteTelefone/*.cs` (padrão de sub-recurso 1:N); `EasyStock.ArchitectureTests/` (um teste existente como modelo).
**Tamanho.** M. **Tier.** alto (migration).

---

### S25 · Dossiê do cliente e sincronização com o contato na Huggy

**Problema.** US-007, US-017, US-018, RN-12, RN-13: ver ao lado da conversa pedidos, notas, tags, última compra e o sinal de mesmo domicílio, sem abrir outra tela e sem fundir cadastros. Na Huggy, isso é campo personalizado do contato.
**Abordagem.** Uma projeção única `DossieClienteDto`, consumida pelo console (endpoint), pelo agente (resumo) e pela sincronização com a Huggy (campos curtos). Domicílio é derivado por endereço normalizado (`cep + numero + complemento`), sem tabela: devolve só nome e id dos outros cadastros, nunca o histórico deles (D10).
**Escopo.**
- Criar `App/UseCases/Cliente/Dossie/ObterDossieClienteUseCase.cs` → `{ Cliente (dados primários), Enderecos, Tags, Notas (autor, data, pedido), UltimosPedidos (10, com itens e status), ItemFavorito (mais pedido), UltimaCompraEm, TotalPedidos, Bloqueado, Preferencias, Domicilio [{ClienteId, Nome}], ConversasRecentes (S04, 5 últimas: canal, data, situação) }`.
- `GET api/clientes/{id}/dossie` (policy de clientes existente).
- Criar `App/Services/Atendimento/SincronizarContatoHuggyService.cs`: `PUT /contacts/{id}` com `custom_fields` {`ultima_compra`, `favorito`, `tags` (csv), `bloqueado` (sim/não), `ficha_url` (`App:ConsoleBaseUrl` + `/clientes/{id}`)}; chamado best-effort (sem falhar o caso de uso) após: pedido `Entregue`, tag/nota/bloqueio alterados (S24). Formato exato de `custom_fields` no `PUT` é confirmado no spike de S02 (registrar no arquivo).
- Domicílio: query `Postgre/Queries/DomicilioQueries.cs` com normalização igual à de `FreteZona` (CEP 8 dígitos; complemento lowercase sem acento).
**Fora.** Somar faturamento do domicílio (Q1 aberta); fusão de cadastros (proibida).
**Aceite.**
- [ ] Dossiê de cliente com 12 pedidos devolve os 10 mais recentes e `ItemFavorito` correto.
- [ ] Dois clientes com mesmo `cep+numero+complemento` aparecem um no `Domicilio` do outro só com nome e id; endereço diferente → lista vazia.
- [ ] Sync na Huggy chamado após `Entregue` com os 5 campos; falha da Huggy não falha o caso de uso (log warning).
**Testes (Red).** `ObterDossieClienteUseCaseTests.FavoritoEUltimos10`, `DomicilioQueriesTests.MesmoEnderecoSemHistorico`, `SincronizarContatoHuggyServiceTests.FalhaNaoPropaga`.
**Rollback.** Remover endpoint e serviço.
**Depende de.** S02, S04, S24.
**Leitura mínima.** `App/UseCases/ObterClienteDetalhes/*.cs`; `App/UseCases/ListarPedidosCliente/*.cs` (só a consulta); `Domain/Entities/Storefront/FreteZona.cs` (funções de normalização); `App/Ports/Output/Atendimento/IHuggyClient.cs` (S02).
**Tamanho.** M. **Tier.** alto.

---

### S26 · Avaliação em duas opções, 30 minutos após a entrega

**Problema.** US-046, RN-34, RN-35, UC-10: avaliação positiva ou negativa em um toque, 30 min após `Entregue`; negativa abre ocorrência. Hoje `PedidoAvaliacao` é 5 estrelas + comentário, pedida +24 h por link.
**Abordagem.** Manter a entidade e adicionar `Resultado` (`Positiva` | `Negativa`, nullable para as antigas). O pedido de avaliação sai pelo outbox de notificações (S13) com atraso de 30 min (`ProximaTentativaEm = entregueEm + 30 min`, sem campo novo). A resposta chega como mensagem na conversa: o agente (S06) reconhece 👍/👎 ou texto equivalente e chama `registrar_avaliacao(pedidoId, resultado, comentario?)`. Botões interativos do WhatsApp via Huggy: se o spike de S02 confirmar suporte, usar; senão, texto com as duas opções.
**Escopo.**
- `Domain/Entities/Storefront/PedidoAvaliacao.cs`: `Resultado` (enum), `Estrelas` passa a nullable; factory `CriarSimples(pedidoId, clienteId, empresaId, resultado, comentario, solicitadoEm)`. Migration `AddResultadoPedidoAvaliacao`.
- `TipoEventoNotificacao.AvaliacaoSolicitada = 42`; handler em `NotificarClienteStatusPedidoHandler` (S13): `Entregue` → enfileira avaliação com `ProximaTentativaEm = +30 min`, respeitando `AvisosStatusAtivos`? Não: avaliação é pós-venda; respeita `AvisosStatusAtivos` (a dona decidiu que só o agradecimento é incondicional).
- Ferramenta `registrar_avaliacao` + `App/UseCases/Storefront/Avaliacao/RegistrarAvaliacaoSimplesUseCase.cs` (1 por pedido; UNIQUE já existe). Negativa → S27 abre ocorrência.
- Endpoint existente de avaliação por link continua para o site.
**Fora.** Foto; resposta pública da dona (já existe).
**Aceite.**
- [ ] `Entregue` às 13:00 → outbox com `ProximaTentativaEm = 13:30` e template de avaliação.
- [ ] `registrar_avaliacao(Negativa)` cria `PedidoAvaliacao` e uma `Ocorrencia` aberta (S27) ligada ao pedido.
- [ ] Segunda avaliação do mesmo pedido → `AvaliacaoDuplicadaException` (mensagem amigável pelo agente).
**Testes (Red).** `NotificarClienteStatusPedidoHandlerTests.EntregueAgendaAvaliacaoEm30Min`, `RegistrarAvaliacaoSimplesUseCaseTests.NegativaAbreOcorrencia`, `...DuplicadaLanca`.
**Rollback.** Migration `Down`; rotina desativada.
**Depende de.** S06, S13, S27.
**Leitura mínima.** `Domain/Entities/Storefront/PedidoAvaliacao.cs`; `App/UseCases/Storefront/Avaliacao/CriarAvaliacaoPedidoUseCase.cs`; `App/Events/Storefront/Handlers/EnviarLinkAvaliacaoWhatsAppHandler.cs`; `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs` (campo `ProximaTentativaEm`).
**Tamanho.** M. **Tier.** alto (migration).

---

### S27 · Ocorrência e reembolso

**Problema.** US-049, US-050, RN-35, RN-36, UC-06: reclamação abre janela ligada ao pedido; a resolução é humana; comida não volta, devolve o dinheiro com motivo gravado no pedido e no cadastro. `IEfiPixService.EstornarAsync(e2eId, idSolicitacao, valor)` existe; `CobrancaPedido.E2eId` (S11) guarda o que ele precisa.
**Abordagem.** Entidade própria e enxuta. Não reaproveitar `AdminTicket` (suporte SaaS com SLA). Abertura automática (avaliação negativa ou agente detectando reclamação); resolução só por ação da dona no console.
**Escopo.**
- Criar `Domain/Entities/Atendimento/Ocorrencia.cs`: `Id`, `EmpresaId`, `PedidoId`, `ClienteId`, `ConversaId?`, `Origem` (`avaliacao`, `agente`, `dona`), `Categoria` (`produto_improprio`, `atraso`, `preferencia`, `outro`), `Relato`, `Status` (`Aberta`, `Resolvida`), `Resolucao?`, `ReembolsoValor?`, `ReembolsoIdSolicitacao?`, `ReembolsoEm?`, `CriadaEm`, `ResolvidaEm?`, `ResolvidaPorUsuarioId?`. Migration `AddOcorrencia`.
- `App/UseCases/Atendimento/Ocorrencias/AbrirOcorrenciaUseCase.cs` (também usado pela ferramenta `abrir_ocorrencia(motivo)` do agente quando classificar a mensagem como reclamação, S06), `ResolverOcorrenciaUseCase.cs` (com ou sem reembolso), `ReembolsarPedidoUseCase.cs` (busca `CobrancaPedido` paga do pedido; `EstornarAsync(E2eId, idSolicitacao = ocorrenciaId em formato N, valor)`; grava resultado; publica evento de notificação `ReembolsoEfetuado = 43` para avisar o cliente; cria `ClienteNota` automática "reembolso de R$ X: <motivo>").
- Ao abrir: escalar a conversa para a dona (S07) e publicar `ocorrencia.aberta` no SSE (S18).
- `Api/Controllers/OcorrenciasController.cs`: `GET api/ocorrencias?status=`, `GET api/ocorrencias/{id}`, `POST api/ocorrencias` (dona abre na mão), `POST api/ocorrencias/{id}/resolver {resolucao, reembolsar: bool, valor?}`.
- Pedido pago no Mercado Pago (site): reembolso fica manual; `ReembolsarPedidoUseCase` responde `reembolso_manual_necessario` e a ocorrência guarda o valor para conferência.
**Fora.** Devolução parcial por item; classificação automática de sentimento (US-051, Could: fica como ferramenta futura do agente).
**Aceite.**
- [ ] Avaliação negativa cria ocorrência `Aberta` com `Origem=avaliacao` e escala a conversa.
- [ ] Resolver com reembolso: `EstornarAsync` chamado com `E2eId` da cobrança paga e o valor; ocorrência `Resolvida` com `ReembolsoEm`; `ClienteNota` criada; cliente avisado.
- [ ] Resolver sem reembolso grava `Resolucao` e não chama a Efi.
- [ ] Reembolso maior que o pago → 400.
**Testes (Red).** `AbrirOcorrenciaUseCaseTests.EscalaConversa`, `ReembolsarPedidoUseCaseTests.ChamaEfiComE2eId`, `...ValorMaiorQuePagoRejeita`, `ResolverOcorrenciaUseCaseTests.SemReembolsoNaoChamaEfi`.
**Rollback.** Migration `Down`.
**Depende de.** S07, S11, S24.
**Leitura mínima.** `App/Ports/Output/IEfiPixService.cs` (método `EstornarAsync`); `App/UseCases/Financeiro/Pagamentos/EstornarPagamentoParcelaUseCase.cs` (padrão de estorno); `Domain/Entities/Pagamentos/CobrancaPedido.cs` (S11); `App/UseCases/Atendimento/EscalarConversaUseCase.cs` (S07).
**Tamanho.** M. **Tier.** alto (migration, pagamento).
