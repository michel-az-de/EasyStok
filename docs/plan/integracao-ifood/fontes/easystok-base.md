# Base do EasyStok para a integração com o iFood

Levantamento somente leitura, 2026-11-16, no worktree `estudo-ifood`. Convenções:

- Fonte no formato `arquivo:linha`. Quando só o símbolo foi lido, cito arquivo e símbolo, sem inventar linha.
- "Não encontrado (busca: termos)" quer dizer que a busca textual no repositório não achou. Não prova que a capacidade não exista por outro nome.
- **Inferência** marca o que concluí sem ler a prova direta.
- Caminhos de código partem da raiz do repositório.

## 1. Stack e arquitetura

| Item | Fato | Fonte |
|---|---|---|
| Plataforma | .NET 10, PostgreSQL, EF Core, Clean Architecture | `global.json`, `AGENTS.md:7` |
| README | Desatualizado (cita .NET 9 e Azure). Vale o AGENTS.md | `README.md`, `AGENTS.md:9` |
| Solução | `EasyStok.sln`: Api, Application, Domain, Infra.Postgre, Infra.Integrations, Infra.Async, Infra.Notifications, Worker, Console, Web, Contracts, Observabilidade, testes | `EasyStok.sln` |
| Front | Console React (ADR-0054); Web Razor congelado (ADR-0059); PWA em `EasyStock.Api/wwwroot/pwa/`; API mobile em `EasyStock.Api/Mobile` (ADR-0019) | `docs/adr/` |
| Deploy | VPS `/opt/stacks/easystok`, `scripts/deploy/vps-deploy.sh` (serviços api, worker, web) e `console-deploy.sh`. Fly.io, Render e Azure desligados | `AGENTS.md:9`, `docker-compose.yml` |
| Segredos no compose | KEK por env `EZ_CRYPTO_KEK_ID` e `EZ_CRYPTO_KEK` | `docker-compose.yml` |

### 1.1 Multi-tenant

| Mecanismo | Fato | Fonte |
|---|---|---|
| Chave | `EmpresaId` em toda entidade de negócio. Regra: usar o `EmpresaId` do JWT em toda leitura e escrita | `AGENTS.md` |
| Filtro EF | Filtro global `IsSuperAdmin \|\| EmpresaId == CurrentTenantId` | `EasyStock.Infra.Postgre/EasyStockDbContext.cs` (`ApplyTenantQueryFilters`, ~535-559) |
| RLS | Policy `tenant_isolation` por `app.empresa_id` e `app.bypass_rls` (ADR-0010) | migration `20260511120000_AddRowLevelSecurity`; `20261114213146_RlsTabelasFilhasBabaRo` é posterior |
| Conexão | `SetTenantOnConnectionInterceptor`; `UseRowLevelSecurityBypass` para job cross-tenant | `EasyStock.Infra.Postgre` |

Consequência: um webhook anônimo do iFood não tem JWT. Precisa descobrir a empresa antes de qualquer leitura (ver 2.2).

### 1.2 Jobs e mensageria

| Item | Fato | Fonte |
|---|---|---|
| Agendador | Não há Hangfire nem Quartz (busca: Hangfire, Quartz, sem resultado) | grep no repositório |
| Jobs na API | `BackgroundService` ligados por flags `BackgroundJobs:Enable*`: CobrancaPedidoJob (60 s), CorridasLalamoveJob (2 min), PedidoAtrasoJob, VigiaOperacaoJob, VigiaIntegracoesBackgroundService (15 min) | `EasyStock.Api/BackgroundServices/BackgroundJobServiceCollectionExtensions.cs` |
| Exclusão entre instâncias | `pg_try_advisory_lock` | `CorridasLalamoveJob.cs:16` e `:53` |
| Worker | Hospeda `IntegrationOutboxBackgroundService` (polling 30 s, lote 50), dispatcher de notificações e relatórios | `EasyStock.Worker/Program.cs` |
| Outbox transacional (ADR-0030) | Tabela `outbox_evento_integracao`: `tipo_evento`, `aggregate_type`, `payload_json` (jsonb), `status`, `tentativas`, `proxima_tentativa_em`, `idempotency_key`, `shard_key` | `OutboxEventoIntegracaoConfiguration.cs` |
| Publicação | `IPublicadorEventoIntegracao.PublicarAsync(empresaId, tipoEvento, aggregateType, aggregateId, payload, correlationId, ct)` | `CancelarPedidoUseCase.cs:92-95` |
| Eventos de pedido já publicados | `pedido.mudou_status` (`CancelarPedidoUseCase.cs:92`), `pedido.pago` (`QuitacaoPedido.cs`, `PedidoPagoEvent`), `estoque.desacerto` (`EstoqueDesacertadoEvent`) | arquivos citados |
| Eventos SSE de operação | `pedido.mudou_status`, `pedido.pago`, `pedido.atrasado`, `pedido.confirmado`, `pedido.reagendado`, `pedido.pagamento_atualizado`, `pedido.estorno_atualizado` | `EventosOperacao` |

**Inferência:** o outbox serve de gancho para empurrar ao iFood as mudanças de status que a loja fizer (aceite, pronto, despacho, cancelamento). Hoje não há consumidor do iFood.

## 2. Integrações existentes como modelo

### 2.1 Adapters

| Provedor | Onde | Observação |
|---|---|---|
| Mercado Pago | `Infra.Integrations`: `MercadoPagoClient`, `MercadoPagoEstornoPedidoGateway` | Pix e checkout; webhook com HMAC |
| WhatsApp (Meta, WppConnect, Waha) | `Infra.Integrations` | Gerido pela FMA no catálogo |
| CEP, geocoding, rotas (Google, TomTom), IA | `Infra.Integrations` | |
| Lalamove | `LalamoveLogisticaGateway`, `AssinadorLalamove` | Melhor molde para um provedor com webhook, polling e credencial por loja |
| Uber Direct | contratos apenas | |
| Resiliência | Polly e `IntegrationCategories` | |
| Mercado Livre | só na enum `CanalVenda` e em comentários; sem adapter | busca: MercadoLivre |

### 2.2 Modelo de webhook reaproveitável

| Passo | Fato | Fonte |
|---|---|---|
| Rota | `POST api/webhooks/{provedor}`, `[AllowAnonymous]`, `RequestSizeLimit(2_000_000)`, rate limit "webhook-meta" | `EasyStock.Api/Controllers/WebhookGatewayController.cs` |
| 1 | Lê o corpo bruto | idem |
| 2 | Resolve `IWebhookSignatureValidator` por provedor: 404 se não existe, 401 se assinatura inválida | idem |
| 3 | Hash SHA-256 do corpo e `eventId` (campo `id` na raiz, ou o hash) | idem |
| 4 | `webhookRepo.TryRegistrarAsync`: já existe com sucesso, responde 200; existe sem sucesso, reprocessa (#787) | idem |
| 5 | `IGatewayWebhookProcessor.ProcessarAsync(rawBody, headers)` | idem |
| 6 | `MarcarProcessadoAsync` | idem |
| Validadores existentes | `MercadoPagoSignatureValidator` (HMAC com `MercadoPago:WebhookSecret` global; falha segura em Production), `EfiPixSignatureValidator` | `Infra.Integrations` |

Tabela de idempotência `webhook_recebidos` (`WebhookRecebido`):

| Campo | Tipo | Nulo | Observação | Fonte |
|---|---|---|---|---|
| `Provedor` | varchar(40) | não | | `WebhookRecebidoConfiguration.cs` |
| `EventIdExterno` | varchar(200) | não | | idem |
| `RawBodyHash` | varchar(64) | não | | idem |
| `RecebidoEm` | timestamptz | não | | idem |
| `ProcessadoEm` | timestamptz | sim | | idem |
| `Sucesso` | bool | não | | idem |
| `Erro` | varchar(2000) | sim | | idem |
| índice | único `ux_webhook_recebidos_provedor_eventid` | | **Sem `EmpresaId`** | idem |

Webhook com identificação da loja (modelo mais próximo do iFood):

| Item | Fato | Fonte |
|---|---|---|
| Controller | `WebhookLalamoveController` e `ProcessarWebhookLalamoveUseCase` | `EasyStock.Api/Controllers`, `Application/UseCases` |
| Resolução do tenant | Acha a loja pela corrida (id externo), chama `tenant.SetCurrentTenant`, valida a assinatura **da conexão da loja** | idem |
| Deduplicação | `WebhookRecebido`, e reconsulta o provedor antes de mudar estado | idem |
| Resposta | 200 a tudo que não é falha transitória | idem |
| Contingência | `CorridasLalamoveJob` faz polling a cada 2 min | `CorridasLalamoveJob.cs` |
| Regra | ADR-0061 D3: identificar a loja nunca substitui a assinatura; o webhook reconsulta o provedor | `docs/adr/0061-conexoes-de-integracao.md` |

### 2.3 Cofre de credenciais e catálogo

| Campo de `credencial_integracao` | Observação | Fonte |
|---|---|---|
| `EmpresaId`, `ProviderKey`, `Categoria`, `Ambiente` | `Categoria` é `CategoriaIntegracao` (Payments=1, Fiscal=2, Marketplace=3, Logistics=4, Mensageria=5, Mapas=6); `Ambiente` é Sandbox=1, Production=2. O comentário de `ProviderKey` cita "mercadolivre", "ifood" | `EasyStock.Domain/Integration/CredencialIntegracao.cs:36` |
| `PayloadCifrado`, `KekId`, `Iv`, `Tag` | AES-256-GCM | idem |
| `Mascara` | últimos 4 | idem |
| `UltimoTesteEm`, `UltimoTesteOk`, `UltimoTesteMensagem` | resultado do teste | idem |
| `ValidoDe`, `ValidoAte`, `Ativo`, `UltimoUsoEm` | | idem |
| Índices | `ix_credencial_integracao_empresa_provider_ambiente_ativo`, `ix_credencial_integracao_kek_id` | snapshot `EasyStockDbContextModelSnapshot.cs` (~11212) |

| Componente | Fato | Fonte |
|---|---|---|
| Resolver | `IntegrationCredentialResolver.ObterAsync<T>(empresaId, providerKey, ambiente)`; cache de 5 min com geração global; KEKs em `Crypto:Keks` | `EasyStock.Infra.Postgre/Integration/IntegrationCredentialResolver.cs` |
| Catálogo | Só 4 providers: mercadopago {accessToken}, whatsapp (gerido pela FMA), googlemaps {apiKey}, lalamove {apiKey, apiSecret}; ambiente sandbox ou producao | `EasyStock.Application/UseCases/Integracoes/CatalogoIntegracoes.cs` |
| Endpoints | `api/integracoes` (policy Admin): PUT `{provider}`, POST `{provider}/testar`, POST `{provider}/desativar`, GET | `IntegracoesController` |
| Testadores | `Infra.Integrations/Conexao`; `VigiarIntegracoesUseCase` testa a cada 15 min | idem |
| Tabela `conexao_integracao` (ADR-0061 D2) | **Não encontrada** (busca: `conexao_integracao`, `ConexaoIntegr`, `class Conexao`). Só portas em `Application/Ports/Output/Integration/Conexao` e testadores. **Inferência:** a spec I5.1 de `docs/plan/integracoes/01-plug-and-play.md` ainda não foi implementada | snapshot; `docs/plan/integracoes/01-plug-and-play.md` |

ADRs de apoio: 0030 (outbox), 0061 (conexões, webhook por provedor), 0064 (despacho externo, Lalamove).

## 3. Pedido

### 3.1 Entidade `Pedido` (tabela `pedidos`)

| Campo | Tipo | Nulo | Observação | Fonte |
|---|---|---|---|---|
| `Id` | Guid | não | | `EasyStock.Domain/Entities/Pedido.cs` |
| `EmpresaId` | Guid | não | tenant | idem |
| `LojaId` | Guid | sim | | idem |
| `ClienteId` | Guid | sim | | idem |
| `ClienteNome` | varchar(150) | | snapshot | idem |
| `ClienteApt` | varchar(32) | | snapshot | idem |
| `ClienteTelefone` | varchar(32) | | snapshot | idem |
| `EnderecoEntrega` | text | | | idem |
| `BairroEntrega` | varchar(150) | | | idem |
| `CepEntrega` | varchar(8) | | | idem |
| `EntregaLat`, `EntregaLng` | double | sim | | idem |
| `EnderecoConfirmadoEm` | timestamptz | sim | | idem |
| `Status` | varchar(32) | não | string do `StatusPedido` | idem |
| `Total` | `Dinheiro` numeric(14,2) | não | soma dos subtotais (`Pedido.TotalDe`) | idem |
| `Observacoes` | text | sim | | idem |
| `Origem` | varchar(20) | não | string livre, default "web" | `Pedido.cs` (~89) |
| `MobileOrderId` | varchar(64) | sim | id do pedido no aparelho; **Inferência:** reutilizável como id externo, com risco (ver 9) | idem |
| `VendaId` | Guid | sim | | idem |
| `CriadoEm`, `AlteradoEm`, `EntreguEm`, `CanceladoEm`, `AgendadoParaEm`, `AvaliacaoSolicitadaEm` | timestamptz | alguns sim | | idem |
| `AprovadoEm`, `AprovadoPorUsuarioId`, `RecusadoEm`, `RecusadoPorUsuarioId` | | sim | fluxo de aprovação | idem |
| `MotivoRecusa` | varchar(40) | sim | | idem |
| `MensagemRecusaCliente` | varchar(280) | sim | | idem |
| `RequerAprovacao`, `MotivoRequerAprovacao` (200) | | | | idem |
| `InicioPrevistoEm`, `AtrasoNotificadoEm`, `ConfirmadoPeloClienteEm` | timestamptz | sim | | idem |
| `NumeroDoDia`, `DataNumero` | int, date | | número de exibição; índice único `ux_pedidos_empresa_numero_do_dia` (empresa, data, número); gerado por `NumeradorPedidoDoDia.cs` com `SequenciaDiariaPedido` | `PedidoConfiguration.cs` |
| `xmin` | | | concorrência otimista | idem |
| Índices | `ix_pedidos_empresa_status_criado_em`, `ix_pedidos_atraso_pendente` | | | idem |

Tabela no snapshot: `pedidos` (~8190), `pedido_itens` (~8555), `pedido_pagamentos` (~8596).

### 3.2 `PedidoItem` (tabela `pedido_itens`, `Pedido.cs:425+`)

| Campo | Tipo | Nulo | Observação |
|---|---|---|---|
| `ProdutoId` | Guid | sim | sem ele a baixa de estoque ignora o item (ver 5) |
| `Nome` | varchar(150) | não | |
| `Emoji`, `Unidade` | | | |
| `Quantidade` | numeric(14,3) | não | |
| `PrecoUnitario`, `Subtotal` | numeric(14,2) | não | |
| `Observacao` | text | sim | único lugar para texto livre do item |
| `CardapioItemId` | Guid | sim | |
| `CardapioItemVariacaoId` | Guid | sim | porção |
| `ProdutoVariacaoId` | Guid | sim | |
| `VariacaoRotuloSnapshot` | varchar(60) | | |
| `SkuSnapshot` | varchar(100) | | |
| `LinhaSnapshot` | varchar(30) | | |
| `ConservacaoSnapshot` | varchar(20) | | |

### 3.3 `PedidoPagamento` (tabela `pedido_pagamentos`, `Pedido.cs:527+`)

| Campo | Observação |
|---|---|
| `Metodo` | "pix", "dinheiro", "credito", "debito", "transferencia", "outro"; válidos em `MetodosPagamentoCaixa.Todos` |
| `Valor`, `PagoEm` | |
| `Referencia` | txid ou NSU |
| `Observacao` | |
| `RegistradoPorUserId`, `RegistradoPorNome` | |

`CobrancaPedido` (`EasyStock.Domain/Entities/Pagamentos/CobrancaPedido.cs`): `Provedor`, `ReferenciaExterna`, `LinkPagamento`, `Valor`, `Status`, `PagaEm`, `ValorPago`, `PagamentoExternoId`, `MetodoPagamento`, `ContaRecebedoraId`, `PixCopiaCola`.

### 3.4 Atributos de pedido que o iFood traz e a base não tem ou modela de outro jeito

| Atributo | Situação | Fonte / busca |
|---|---|---|
| Frete | Não é campo. É uma linha de item com nome "Entrega — {zona}" (`PedidoItem.PrefixoDoFrete`, `EhLinhaDeFrete`, `Pedido.Frete`) | `Pedido.cs` |
| Desconto no pedido | Não encontrado (busca: desconto em Pedido.cs, Storefront, Cupom.cs; só `Cupom` com `TipoDesconto`) | `Cupom.cs` |
| Complementos, adicionais, grupos de opção | Não encontrados (busca: complemento, adicional, grupoopc, opcaoitem, modificador). Existe só `Observacao` do item e as porções (`CardapioItemVariacao`) | Domain |
| Entrega versus retirada | Sem campo (busca: retirada, TipoEntrega, ModoEntrega). Retirada aparece só como "fora do raio = retirada" | `FreteRaioCalculadora.cs:40,64`; `Storefront.cs:93` |
| Troco | Não encontrado (busca: troco) | |
| Pagamento online já feito pelo marketplace | Não há. Há `na_entrega`, `online`, `pix_direto` | `TrocarFormaPagamentoPedidoUseCase.cs:66-68`; ADR-0061 emenda de 15/11/2026 (#2233) |
| Taxa de serviço, comissão | Não encontrado (ver 6) | |
| Id externo do pedido, canal | Não há coluna. `Origem` é texto livre de 20 caracteres | `Pedido.cs` |

### 3.5 Valores de `Origem` encontrados

"web" (default), "mobile", "storefront", "balcao" (`FinalizarVendaBalcaoUseCase.cs:71`), "whatsapp" (`PerfilClienteWhatsApp.cs:15`), "api", "console", "kds", "comanda_cliente", "mercadopago", "sistema", "viagem", "webhook". Não existe catálogo. `PedidoEvento.Origem` é outro campo. No PWA há canal opcional com "ifood" (`EasyStock.Api/wwwroot/pwa/index.html:7662` e `:10811`), mas a coluna `mobile_orders.channel` não existe (comentário em `index.html:22720`).

### 3.6 Estados e transições (`EasyStock.Domain/Sales/StatusPedido.cs`, `PedidoStateMachine.cs:38-65`)

Enum: Rascunho=0, Aguardando=1, Preparando=2, Pronto=3, Entregue=4, Cancelado=5, AguardandoPagamento=6, AguardandoAprovacaoBaba=7, AprovadoBaba=8, SaiuParaEntrega=9.

| De | Para |
|---|---|
| Rascunho | AguardandoPagamento, Cancelado |
| AguardandoPagamento | Aguardando, AguardandoAprovacaoBaba, Cancelado |
| AguardandoAprovacaoBaba | AprovadoBaba, Cancelado |
| AprovadoBaba | Preparando, Cancelado |
| Aguardando | Preparando, Cancelado |
| Preparando | Pronto, Cancelado |
| Pronto | SaiuParaEntrega, Entregue, Cancelado |
| SaiuParaEntrega | Entregue, Cancelado |
| Entregue | Cancelado |

Conjuntos: `Entradas` (`PedidoStateMachine.cs:85-89`) = Aguardando, Rascunho, AguardandoAprovacaoBaba. `ComEstoqueDescontado` (`:100-101`) = Pronto, SaiuParaEntrega, Entregue. `Finais` = Entregue, Cancelado. Há também `PreOperacionais` e `DemandaAConsumir` no mesmo arquivo.

### 3.7 Como um canal externo cria pedido hoje

| Caminho | Fato | Fonte |
|---|---|---|
| `CriarPedidoUseCase` | `CriarPedidoCommand`: EmpresaId, LojaId, ClienteId, ClienteNomeAdHoc, ClienteAptAdHoc, ClienteTelefoneAdHoc, Observacoes, Origem, MobileOrderId, Itens, AgendadoParaEm. `CriarPedidoItemInput`: Nome, Quantidade, PrecoUnitario, ProdutoId?, Emoji, Unidade, Observacao | `EasyStock.Application/UseCases/CriarPedido/CriarPedidoUseCase.cs` |
| Checkout do site | `CheckoutCoreService.CriarPedidoComReservaAsync`: Rascunho, reserva de vaga (`VagaOcupada`, janela), AguardandoPagamento. Frete entra como item. `GarantirLojaAbertaAsync` recusa só com `ForcarFechada` | `EasyStock.Application/Services/Storefront/CheckoutCoreService.cs:150-262`, `:310-315` |
| Balcão | `FinalizarVendaBalcaoUseCase` percorre Aguardando, Preparando, Pronto, Entregue | `FinalizarVendaBalcaoUseCase.cs` |
| Endpoints | `api/pedidos` (`PedidosController`: balcão, PATCH status, cancelar, agendamento, itens, pagamentos); `api/kds` (`KdsController.cs:16-65`, policy Operador); `api/storefront/{slug}/checkout`, `/menu`, `/frete`, `/janelas` | Controllers |
| Mudança de status | `AtualizarStatusPedidoUseCase` | `UseCases/AtualizarStatusPedido/` |
| Cancelamento | `CancelarPedidoUseCase`: trava o pedido, devolve estoque, cancela cobranças pendentes, grava `PedidoEvento` "cancelado", registra "devolucao_a_fazer" se havia pagamento, publica `pedido.mudou_status` | `CancelarPedidoUseCase.cs:39-113` |
| Canhoto | `ImpressaoPendente.CriarCanhoto` na quitação (`QuitacaoPedido.cs:111` via `PrepararCanhotoAsync`); `TipoImpressao.Canhoto=1`, `Documento=2`; alerta por `ImpressaoPendenteAlertaJob` | `Services/Pedidos/QuitacaoPedido.cs` |

Cancelamento: o fluxo existente parte de usuário com `NivelAcesso` (`CancelarPedidoCommand`, `CancelarPedidoUseCase.cs:14-22`). Cancelamento exige permissão elevada quando há pagamento (`EfeitosCancelamentoPedido.ExigirPermissao`, `:61-63`). Não há comando para cancelamento iniciado por sistema externo (**Inferência**: seria preciso usar `Origem` e um nível de acesso de sistema).

### 3.8 Cliente (`Cliente.cs`) e entrega

| Entidade | Campos | Observação |
|---|---|---|
| `Cliente` | Nome, Apt, Endereco, Telefone, Email, Documento, Cpf, TipoPessoa, Cep, Bairro, Cidade, Complemento, TelefoneHash, ConsentiuMarketing, Bloqueado, OrderCount, LastOrderAt | filhas `ClienteEndereco`, `ClienteTelefone`, `ClienteDocumento` |
| Entrega | `Viagem`, `ParadaViagem`, `Entregador`, `ChamadoEntregador`, `CorridaExterna` | `CorridaExterna` guarda custo (ADR-0061 D5), índice único `ux_corridas_externas_provedor_id_externo` |
| `EmpresaEntregador` | Propria=1, NoveNove=2, Lalamove=3, **Ifood=4**, Outra=5, Uber=6 | `EasyStock.Domain/Enums/Atendimento/EmpresaEntregador.cs`. É rótulo de cadastro manual; o comentário diz que a integração fica fora |

## 4. Cardápio e produto

### 4.1 `Produto` e derivados

| Entidade / campo | Observação | Fonte |
|---|---|---|
| `Produto` | `Nome`, `CategoriaId`, `SubcategoriaId`, `Tipo` (FISICO, ALIMENTO, SERVICO), `SkuBase` (`CodigoSku?`), `CodigoBarras`, `PrecoReferencia` (`Dinheiro?`), `CustoReferencia`, `FotosJson`, `Status` | `EasyStock.Domain/Entities/Produto.cs`; snapshot `produtos` (~8897) |
| Receita | `EhInsumo`, `RendimentoBase`, `RendimentoUnidade`, `UnidadeMedidaBase`; `BaixaInsumoAutomatica` (D-M2-01); `EhEmbalagem` (D-M2-03) | idem |
| Coleções | `Variacoes`, `Composicoes`, `Embalagens` | idem |
| `ProdutoVariacao` | `Nome`, `Sku`, `CodigoBarras`, `Ativa` | idem |
| `ProdutoComposicao` (ficha técnica) | `ProdutoFinalId`, `InsumoId`, `LojaId?`, `Quantidade`, `Unidade`, `OrdemExibicao` | idem |

### 4.2 Cardápio público (`EasyStock.Domain/Entities/Storefront/`)

| Entidade | Campos | Observação |
|---|---|---|
| `CardapioItem` | `StorefrontId`, `ProdutoId?`, `NomePublico`, `SecaoId`, `Visivel` (default false), `Disponivel`, `OrdemExibicao`, `DescricaoPublica`, `Ingredientes`, `Alergenos`, `TempoPreparoMinutos`, `FotoUrl`, `FotosJson`, `PrecoStorefront` (decimal?; nulo usa `Produto.PrecoReferencia`), `Tag`, `ArquivadoEm`, `EmValidacao`, `Linha` (`LinhaProduto`), `Conservacao` | `EstaOfertado()` = `Visivel && !EstaArquivado && Disponivel && TemDisponibilidade() && !EmSecaoOculta` (`CardapioItem.cs:190`) |
| `CardapioItemVariacao` (porção) | `Rotulo`, `PrecoStorefront`, `Sku`, `ProdutoVariacaoId`, `EmbalagemProdutoId`, `Disponivel`, `EhPadrao` | |
| `CardapioSecao` | hierárquica: `SecaoPaiId`, `Nivel`, `Visivel` | |

ADRs: 0031 e 0035 (cardápio agnóstico de canal, variações, seções).

| Tema | Situação | Fonte / busca |
|---|---|---|
| Código externo por canal | Não existe. Há só `Sku`, `CodigoBarras` e `SkuSnapshot` | busca: CodigoExterno, IdExterno por canal |
| Disponibilidade | Separada do saldo. `POST api/atendimento/comanda/cardapio/{itemId}/disponivel` e `/saldo` | `AtendimentoCardapioDoDiaController`; gestão em `AtendimentoItensCardapioController` |
| Leitura pública | `api/storefront/{slug}/menu` | `MenuController.cs:45` |
| Regra da casa sobre pausa | "o iFood pausa o item; a casa mantém o item visível" (RN-16) | `EasyStock.Console/src/dominio/cardapio.js:78` |
| Canal novo no catálogo do Console | entra como linha de dado | `EasyStock.Console/src/infra/catalogo.js:7` |

## 5. Estoque

| Aspecto | Fato | Fonte |
|---|---|---|
| Serviço | `PedidoEstoqueIntegrationService` | `EasyStock.Application/Services/PedidoEstoqueIntegrationService.cs` |
| Granularidade da baixa | Por produto acabado (item do pedido com `ProdutoId`), **não** pela ficha técnica. Insumos baixam na produção (`BaixaDeInsumosDaProducao`, natureza `UsoInterno`) | idem |
| Lotes | `ConsumoDeLotes`, FEFO, ou FIFO se `FifoAtivo` da loja; lock `FOR UPDATE` | idem |
| Idempotência | `ReferenciaDocumento = "{pedidoId}:{itemId}"`, natureza `Venda` | idem |
| Estoque negativo | `PermiteEstoqueNegativo` default true (`:20` e `:189`). A falta vira `QuantidadeDescoberta` e emite `estoque.desacerto` | idem |
| Item sem `ProdutoId` | Ignorado com log. `ReligarAoEstoqueAsync` liga item avulso ao produto do prato (#1619) | idem |
| Porção | Usa `ProdutoVariacaoId` | idem |
| Estorno | `DevolverAsync` (`:280`); também devolve a baixa do aparelho (`MobileOrderId`) | idem |
| Gatilho de baixa | Na transição para status de `ComEstoqueDescontado` (Pronto), antes do `MudarStatus` | `AtualizarStatusPedidoUseCase.cs` (~126-132) |
| Gatilho de devolução | Cancelamento devolve se o estoque estava descontado ou se há `MobileOrderId` | `CancelarPedidoUseCase.cs:66-71` |
| Reserva | Não há reserva de estoque no pedido. Demanda agendada entra na sugestão de produção (`DemandaAConsumir`, M2.5) | `PedidoStateMachine.cs` |

**Inferência:** pedido do iFood que entre em Aguardando só baixaria estoque ao chegar em Pronto. Entre o aceite e o pronto, o saldo exposto ao iFood ficaria desatualizado.

## 6. Financeiro

| Item | Fato | Fonte |
|---|---|---|
| Recebimento de pedido | Pagamento de pedido, inclusive o do webhook do Mercado Pago, **não** grava `MovimentoCaixa` (#926). O Extrato lê via `ListarLinhasCaixaUseCase`; `RealizadoDoCaixa` (#2268) soma isso | `ListarLinhasCaixaUseCase` |
| `MovimentoCaixa` | `Tipo`, `Valor`, `Metodo`, `Categoria`, `Referencia`, `Origem`, estorno | `EasyStock.Domain/Entities/MovimentoCaixa.cs` |
| Contas | `ContaReceber`, `ParcelaReceber`; `OrigemContaFinanceira` (Manual, Pedido, PedidoFornecedor, Importacao) | Domain |
| Taxa de marketplace ou gateway | Não encontrada (busca: taxa marketplace, TaxaGateway, fee_details) | |
| Canal de venda | `Venda.Canal` é `CanalVenda` (LojaPropria, MercadoLivre, Shopee, WhatsApp, Instagram, Outro). **Sem iFood**. `ObterVendasPorCanal` usa esse enum | `EasyStock.Domain/Enums/CanalVenda.cs` |
| Devolução | Cancelamento com dinheiro recebido grava evento "devolucao_a_fazer"; a devolução é registrada depois em Devoluções | `CancelarPedidoUseCase.cs:115-134` |

## 7. Loja e horário

| Item | Fato | Fonte |
|---|---|---|
| `Loja` | nome, `Ativa`, Pix da loja | `Loja.cs` |
| `ExpedienteLoja` (tabela `expedientes_loja`) | `HorariosJson` (um turno por dia), `ControleManual`, mensagens de fora do horário e de loja fechada | snapshot (~9877) |
| `ControleManualLoja` | Automatico, ForcarAberta, ForcarFechada | Domain |
| Endpoint | `api/atendimento/expediente`, POST `controle` | `AtendimentoExpedienteController` |
| Fuso | America/Sao_Paulo (`OperacionalFuso`) | |
| Efeito no checkout | Recusa só com `ForcarFechada` (o horário em si não bloqueia) | `CheckoutCoreService.cs:310-315` |
| Pausa por item | `CardapioItem.Disponivel` | ver 4 |
| Propagação para canal externo | Não há | ver 9 |

## 8. Menções a iFood no repositório (`git grep -ni ifood`)

| Local | Conteúdo | Leitura |
|---|---|---|
| `docs/plan/erp-casa-da-baba/04-m3-atendimento.md` (~95, 97, 174) | Canais: iFood não existe no sistema (`CategoriaIntegracao.Marketplace`, `EmpresaEntregador.Ifood` são só rótulos). A Thati fala do iFood no futuro. Propõe catálogo de `Origem` (`site`, `conversa`, `balcao`, `ifood` futuro) | **Inferência** do próprio doc: a casa não opera iFood hoje |
| mesmo arquivo, **M3.9** (293-300) | Porta `ICanalPedidoExterno`; adaptador cria `Pedido` com origem `ifood`, itens mapeados ao cardápio, entra no KDS, confirma e muda status de volta ao iFood. Chat só se a API permitir. Credencial por loja na F16 (`Marketplace`). Tamanho G, tier alto. Rollback: desligar o canal. **A API do iFood não foi lida** | |
| mesmo arquivo, **D3-02** (319-322) | A) recomendado: fora até a casa ter conta e volume no iFood; B) só o pedido; C) pedido e chat | |
| `docs/plan/erp-casa-da-baba/README.md:120` | D3-02 decidido: "fora até ter conta e volume" | decisão vigente |
| `docs/plan/erp-casa-da-baba/11-levantamento-e-ondas-2026-10-09.md` (1033, 1041, 1049) | M3.9 condicionado a D3-02 e ao contrato do iFood | |
| `docs/plan/erp-casa-da-baba/00-estudo.md` (35, 156); `09-m8-entregas.md:84` | citações de contexto | |
| `docs/plan/atendimento-whatsapp/homologacao-2026-10-07.md` (54, 187); `09-sistema-completo.md` (349, 363, 432) | "iFood fica fora (D3-02)" | |
| `docs/plan/balcao-ux/banca/C-pesquisa.md` (11, 81-82, 85, 278) | Pesquisa do Gestor de Pedidos iFood: aceitar, em preparo, despachar, Pronto para retirada; conclusão após confirmação ou 2 h; aceite automático | referência de fluxo operacional |
| `docs/reports/ANALISE-CONCORRENTES-FOODSERVICE.md` (7, 13, 19, 21, 34, 54) | iFood como diferencial de concorrentes; "Integracao ainda a consolidar" | |
| Console | `src/dominio/cardapio.js:78`, `src/infra/catalogo.js:7`, `despacho.js`, `entregasApi.js:32`, `entregadoresSemente.js`, `provedoresDeEntrega.js`, `EscolhaDeEntregador.jsx`, `TelaCardapioLink.jsx` e `cardapioLink.*` (vitrine estilo iFood), `ferramentas/prova-*.mjs` | rótulos e UX, nenhum cliente HTTP do iFood |
| Backend | `EasyStock.Api/Controllers/AtendimentoEntregasController.cs:11`; `EmpresaEntregador.Ifood`; comentário em `CredencialIntegracao.cs:36` | só rótulos |
| Web legado | `EasyStock.Web/Services/SaidasService.cs` cita marketplace | irrelevante |

Planos de integração: `docs/plan/integracoes/README.md` (levantamento de 30/09, superado em parte em 12/10) e `docs/plan/integracoes/01-plug-and-play.md` (épico #1633; specs I0.1 a I11.3, com I5.1 conexão mínima, I6.2 webhook para várias contas, I11.2 vigia no Worker).

Cliente HTTP, enum, tabela ou endpoint do iFood: **não encontrado** (busca: ifood em `*.cs`, migrations e snapshot, só rótulos listados acima).

## 9. Lacunas da base para receber o iFood

| # | Lacuna | Evidência | Impacto |
|---|---|---|---|
| 1 | Sem id externo e sem canal no `Pedido` | 3.1, 3.5 | Idempotência por pedido iFood e consulta por id exigem coluna nova ou reuso de `MobileOrderId` (varchar 64), que dispara devolução de estoque por outro caminho (`CancelarPedidoUseCase.cs:70`). **Inferência:** reutilizar mistura semânticas |
| 2 | `Origem` é texto livre de 20 caracteres, sem catálogo | 3.5 | "ifood" cabe, mas sem validação |
| 3 | Sem desconto, taxa de serviço, troco, pago-online-pelo-marketplace | 3.4 | Total do iFood não fecha com o total do pedido sem campos novos |
| 4 | Frete modelado como item | 3.4 | Mapear a taxa de entrega do iFood a uma linha "Entrega" |
| 5 | Sem complementos, adicionais ou grupos de opção | 3.4 | Pedido do iFood com opções só cabe em `Observacao` ou em itens avulsos |
| 6 | Sem campo entrega versus retirada | 3.4 | |
| 7 | Sem código externo por item de cardápio | 4.2 | Mapear item iFood para `CardapioItem` ou `Produto` exige tabela de mapeamento (M3.9 já prevê "provável migração") |
| 8 | Estados de entrada | 3.6 | `Entradas` admite Aguardando, Rascunho, AguardandoAprovacaoBaba. Não há estado "confirmado pelo marketplace" nem passo de aceite com prazo |
| 9 | Baixa de estoque só em Pronto, sem reserva | 5 | Disponibilidade enviada ao iFood pode estar defasada |
| 10 | Item sem `ProdutoId` não baixa estoque | 5 | Item iFood não mapeado vira venda sem baixa |
| 11 | Sem sincronização de cardápio, preço e disponibilidade | 4.2 | Há `PrecoStorefront` e `Disponivel`, mas nada empurra ao iFood; pausa é manual por item |
| 12 | Sem propagação do status da loja | 7 | `ExpedienteLoja`/`ControleManual` existem; nenhum consumidor externo |
| 13 | Catálogo de credenciais sem "ifood" | 2.3 | Adicionar provider em `CatalogoIntegracoes`. O cofre guarda payload estático por loja; **Inferência:** não há ciclo de refresh de token OAuth, que o iFood normalmente exige |
| 14 | `conexao_integracao` (ADR-0061 D2) não implementada | 2.3 | Estado de conexão por loja, que o iFood precisa (merchant id), depende da spec I5.1 |
| 15 | `webhook_recebidos` sem `EmpresaId`; validador por provedor com segredo global | 2.2 | iFood precisa achar a loja pelo merchant id e validar a assinatura com credencial da loja (molde: Lalamove) |
| 16 | Sem job genérico de polling | 1.2 | Só o molde `CorridasLalamoveJob` (advisory lock, 2 min). **Inferência:** polling de eventos do iFood seguiria esse molde |
| 17 | Sem taxa, comissão ou recebível por canal | 6 | Conciliação do repasse do iFood não tem onde cair |
| 18 | `CanalVenda` e canal de conversa sem iFood | 6 | Relatórios por canal não separam o iFood |
| 19 | Cancelamento iniciado pelo marketplace | 3.7 | Só existe cancelamento por usuário com nível de acesso e regra de devolução |
| 20 | Decisão de negócio | 8 | D3-02: iFood fora até a casa ter conta e volume; M3.9 é tier alto (migração, integração externa, credencial) e exige `aprovado` do Felipe. A API do iFood não foi lida neste levantamento |

Reaproveitável como está: outbox transacional, cofre de credenciais cifrado, molde de webhook com tenant por id externo (Lalamove), idempotência em `webhook_recebidos`, `CriarPedidoUseCase`, KDS, impressão de canhoto e eventos `pedido.*`.
