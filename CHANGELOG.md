# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Fixed
- Criar usuário em cliente pelo SuperAdmin (`POST api/admin/usuarios-tenant` e "Novo usuário" do
  Admin) respondia 500: o RLS recusava o `Perfil` da empresa alvo porque o token do SuperAdmin não
  tem empresa. O caso de uso agora define o tenant da empresa alvo antes de gravar. (#1159)

### Added
- **Canhoto e fila de impressão** (S20): o pedido pago entra em `impressoes_pendentes` na mesma
  transação do `ConfirmarPagamentoPedidoUseCase` e sai `impressao.pendente` no SSE depois do commit.
  `GET api/pedidos/{id}/canhoto?formato=html|texto` (html de 80 mm com o CSS do recibo; texto de 42
  colunas sem acentos para ESC/POS), agrupado por linha com porção, molho e observação por item.
  Consumidor agnóstico por polling: `GET api/impressao/pendentes`, `POST api/impressao/{id}/impressa`
  (idempotente) e `POST .../falhou`, com JWT de operador ou header `X-Impressao-Api-Key`
  (`Impressao:ApiKey` + `Impressao:EmpresaId`). `POST api/pedidos/{id}/reimprimir` só para operador.
  `ImpressaoPendenteAlertaJob` publica `impressao.atrasada` para pendente há mais de 3 min
  (`BackgroundJobs:EnableImpressaoPendenteAlerta`). Migration `AddImpressaoPendente` com RLS. (#1156)
- **Início previsto e atraso** (S21): `Pedido.InicioPrevistoEm` = início da janela da vaga ativa (hora
  de Brasília) ou `AgendadoParaEm`, menos o prazo mínimo dos itens do cardápio (S15); gravado em
  `ConfirmarPagamentoPedidoUseCase` e recalculado em `AlterarAgendamentoPedidoUseCase`, que também zera
  `AtrasoNotificadoEm`. O KDS devolve `inicioPrevistoEm` e deriva `atrasado` (aguardando e início vencido;
  sem início previsto, vale a regra da S19). `PedidoAtrasoJob` (60 s, `BackgroundJobs:EnablePedidoAtraso`)
  publica `pedido.atrasado {pedidoId, numero, cliente, inicioPrevistoEm}` uma vez por pedido. Migration
  `AddInicioPrevistoPedido`. (#1165)
- **Dossiê do cliente ao lado da conversa** (S25): `GET api/clientes/{id}/dossie` e `GET
  api/atendimento/conversas/{id}/dossie` devolvem a mesma projeção (cadastro, endereços, tags, notas
  internas, 10 últimos pedidos com itens, item favorito, última compra, bloqueio, preferências,
  conversas recentes e pedido em andamento). Conversa de lead devolve o dossiê mínimo (nome do perfil e
  telefone). Sinal de mesmo domicílio por `cep+numero+complemento` normalizado, só com id e nome do
  outro cadastro (D10). O agente recebe um resumo com as notas marcadas `[interno]`. (#1161)
- **CRM leve do cliente** (S24): tags normalizadas e únicas por cliente (`GET|POST|DELETE
  api/clientes/{id}/tags`, repetida → 409, com sugeridas), notas internas datadas (`GET|POST
  api/clientes/{id}/notas`, pedido de outro cliente → 400), bloqueio em todos os canais (`POST
  bloquear|desbloquear`: sem saudação nem agente, conversa vai para a dona, `criar_pedido` recusa com
  `cliente_bloqueado`) e preferências (`PUT preferencias`: `AvisosStatusAtivos`, `ConsentiuMarketing`).
  Ferramentas do agente `registrar_restricao` e `registrar_nota`. Migration
  `AddClienteTagNotaBloqueioPreferencias` com RLS. O filtro de avisos de status por
  `AvisosStatusAtivos` fica para depois da S13. (#1148)
- **SSE de operação do console** (S18): `GET api/operacao/eventos` com JWT no header `Authorization`
  (sem token, 401; token sem empresa, 403), eventos nomeados só da empresa da claim e heartbeat a cada
  25 s. `ConfirmarPagamentoPedidoUseCase` publica `pedido.pago {pedidoId, numero, cliente, total, janela}`
  e `AtualizarStatusPedidoUseCase` publica `pedido.mudou_status`, sempre depois do commit. O broker
  in-memory do mobile virou `Api/Services/Operacao/OperacaoEventBroker` e atende os dois canais; o SSE
  mobile não muda. `IOperacaoEventPublisher` deixa de ser no-op na Api (Worker segue no-op). (#1146)
- **Instagram Direct e Messenger** (S35, ADR-0051): webhook `api/webhooks/meta/mensageria` (objetos
  `instagram` e `page`, mesmo HMAC do App Secret), roteado por `Empresa.InstagramAccountId` e
  `Empresa.FacebookPageId`, idempotente pelo `mid`. Adaptadores `CanalInstagram` e `CanalMessenger` na
  porta de canal (Send API v25.0, page token em `Atendimento:MetaMensageria`, stub sem provider `meta`).
  Fora da janela o console responde com `HUMAN_AGENT` até 7 dias; depois, 409. A conversa entra na
  fila humana e o agente não responde fora do WhatsApp. Flags `atendimento.canal.instagram` e
  `atendimento.canal.messenger`. (#1103)
- **Cadastro de entrega pela loja** (S45, parte 1): `api/minha-vitrine/entrega` com janelas (listar,
  criar, editar, ativar e desativar), zonas de frete por faixa de CEP ou por bairros (inclusive trocar
  a cobertura) e bloqueios de dia ou de janela (listar por período, criar, remover). A loja é sempre a
  da empresa do token e id de outra loja devolve 404. Canais por empresa ficam para a parte 2. (#1095)
- **Chat do site** (S36, ADR-0051): canal `ChatSite` público por loja em `api/public/chat/{slug}`.
  `POST sessoes` devolve um token (só o hash fica no banco, vale 24 h renovadas no uso), `POST|GET
  mensagens` com header `X-Chat-Token` e `GET stream` (SSE que lê do banco a cada 1 s, funciona
  com várias instâncias). A conversa entra na fila humana sem responsável; o agente ainda não responde
  no site. Flag `atendimento.canal.chatsite`, rate limit por IP e por sessão, tabela
  `sessoes_chat_site` com RLS e limpeza das vencidas de hora em hora. (#1097)

### Security
- CSRF do storefront: POST/PUT/PATCH/DELETE com cookie `__Host-cdb_*` só da mesma origem
  (`Sec-Fetch-Site` `same-origin`/`none`, ou `Origin` igual ao `Host`); outro site recebe 403.
  O front é same-origin e não muda; chamadas bearer do Web e do Admin seguem livres (ADR-0053). (#1088)
- CodeQL: `cs/web/missing-token-validation` filtrada na Api bearer (CSRF não se aplica a JWT) e
  mantida nos 4 controllers do storefront que usam cookie; `CodeQlCsrfFilterTests` amarra a lista
  do `codeql.yml` ao código. Web e Admin seguem com a regra inteira (ADR-0052). (#1089)

### Added
- **Núcleo do checkout compartilhado** (S10): `CheckoutCoreService` concentra as fases 1 e 2 do
  checkout (validação de CEP, janela, bloqueio e cardápio, frete por zona, pedido em `Rascunho`,
  vaga ocupada, `AguardandoPagamento`, rollback para `Cancelado` com janela lotada).
  `IniciarCheckoutUseCase` e `IniciarCheckoutGuestUseCase` passam a delegar; o site não muda de
  comportamento. Novo `CriarPedidoAtendimentoUseCase` cria o pedido da conversa pelo mesmo caminho
  (`Origem = "whatsapp"`, observação por item, `Conversa.PedidoEmAndamentoId`); a cobrança é da S11. (#1101)
- **Cobrança do pedido pelo Mercado Pago** (S11): `CobrancaPedido` (migration `AddCobrancaPedido`,
  RLS) guarda a preferência com `external_reference = PedidoId`, link e expiração de 30 min; site e
  conversa passam pelo mesmo `GerarCobrancaPedidoUseCase`. `ConfirmarPagamentoPedidoUseCase` (ponto de
  entrada do webhook da S32) leva o pedido de `AguardandoPagamento` a `Aguardando`, registra o
  pagamento e publica `pedido.pago`; repetido é no-op e valor menor não confirma. `CobrancaPedidoJob`
  (60 s, `BackgroundJobs:EnableCobrancaPedido`) reemite uma vez para pedido da conversa e cancela o
  resto liberando a vaga. Operadora: `POST api/pedidos/{id}/cobranca`, `.../cobranca/forma`
  (`online` ou `na_entrega`) e `.../pagamento-manual/desfazer`. A ferramenta `criar_pedido` do agente
  fecha o pedido e devolve resumo, total e link. (#1115)
- **Mensagem programada ao cliente em todos os canais** (S39, ADR-0051): `MensagemProgramada`
  (texto ou modelo aprovado, agendada, enviando, enviada, cancelada ou falhou). Ao agendar e de
  novo no disparo: horário no passado é recusado; fora da janela no horário do envio, o WhatsApp
  exige modelo e Instagram e Messenger recusam; marketing sem consentimento no canal é recusado
  (S38). O destino vem da conversa aberta ou do cadastro (telefone para WhatsApp e SMS, e-mail
  para e-mail). Disparador no processo da API (`MensagensProgramadasBackgroundService`, desliga
  com `BackgroundJobs:EnableMensagensProgramadas=false`), com reserva `FOR UPDATE SKIP LOCKED` que
  não duplica entre processos. A saída entra no histórico com `Mensagem.Programada`.
  `POST|GET|DELETE api/atendimento/mensagens-programadas` (Operador). (#1082)
- **WhatsApp por empresa** (#1102): a resposta sai pelo `phone_number_id` da empresa do tenant
  (`IRemetenteWhatsApp`), com fallback para `Notifications:WhatsApp:Meta:PhoneNumberId` e erro
  permanente, sem chamar a Meta, quando não há nenhum. Chave nova `Atendimento__WhatsApp__Cliente=meta`
  liga o cliente real da Cloud API sem trocar o provider de notificações (que segue `stub`);
  `Provider=meta` continua ligando o cliente real. O `StartupHardening` exige AccessToken, AppSecret
  e VerifyToken com qualquer uma das duas em `meta`. `PUT api/admin/tenants/{id}/whatsapp`
  (`{ phoneNumberId }`, `null` desvincula, 409 se outra empresa já usa) e card "WhatsApp" na aba
  Features do detalhe do tenant no Admin.
- **Consentimento do cliente final por canal e finalidade** (S38, ADR-0051): `ConsentimentoContato`
  (transacional ou marketing, concedido ou revogado, com origem), `PoliticaConsentimento` (marketing
  só com opt-in no canal; transacional passa salvo revogação) e `PoliticaEnvioCliente` para a
  mensagem programada e as campanhas. "SAIR", "PARAR" ou "STOP" sozinhos no WhatsApp revogam o
  marketing daquele canal, confirmam ao cliente e não acionam o agente.
  `GET|PUT api/atendimento/clientes/{id}/consentimentos` (Admin). Tabela `consentimentos_contato`
  com RLS e backfill de `ConsentiuMarketing=true` para WhatsApp e e-mail. (#1078)
- **E-mail e SMS na porta de canal do atendimento** (S37, ADR-0051): `CanalSms` (texto pelo
  `IProvedorSms` ativo, Twilio em produção, com `+` no número) e `CanalEmail` (texto, ou imagem por
  link em HTML escapado, pelo `IEmailService`). O `ResolvedorCanal` passa a achar `Sms` e `Email`.
  Falha do provedor vira `EnvioCanalFalhouException`; operação que o canal não suporta lança
  `NotSupportedException`. (#1080)
- **Atendentes e atribuição de conversa** (S41, ADR-0051): permissão `AtenderConversas` (Operador,
  Gerente e Admin pelo nível; perfil com permissões explícitas só se a tiver). Assumir, responder,
  transferir, liberar e encerrar devolvem 403 sem ela. `POST api/atendimento/conversas/{id}/transferir`
  (422 se o destino não atende na empresa), `GET api/atendimento/atendentes` e filtro
  `?responsavel=eu|ninguem|{id}` na inbox. Mensagem do console grava `EnviadaPorUsuarioId`. A regra
  de permissão efetiva foi para o domínio (`PoliticaPermissao`). (#1085)
- **Expediente da loja** (S40, ADR-0051): `ExpedienteLoja` por empresa com horário por dia (virada
  da meia-noite), controle manual que vence o relógio e não volta sozinho, e mensagens de "fora do
  horário" (`{abre}` vira "amanhã às 08:00") e "loja fechada". `GET|PUT api/atendimento/expediente`
  e `POST api/atendimento/expediente/controle` (Admin), evento `expediente.alterado`. O checkout do
  site devolve 409 só com a loja fechada na mão (o pedido é agendado; o horário governa o
  atendimento). Tabela `expedientes_loja` com RLS. (#1074)
- Provider da Meta no outbox de notificações e regra da janela de 24 h (S09): `MensagemPronta.Metadados`
  (`template`, `idioma`, `param1..N`); `MetaCloudWhatsAppProvider` envia pela porta de canal (S34) texto
  dentro da janela e template fora dela ou sem conversa aberta; fora da janela sem template (ou 131047
  da Meta) vira `ResultadoEnvio.FalhaPermanente` e o outbox não reagenda; com conversa aberta a saída
  entra no histórico como `Mensagem(Saida, Sistema)` com o `wamid`. O provider continua `stub` por
  padrão: liga com `Notifications__WhatsApp__Provider=meta` junto das credenciais da Meta.
- Handoff pelo console do atendimento (S07, ADR-0050): `api/atendimento/conversas` (policy
  `Operador`) com inbox (última mensagem, não lidas, busca por nome ou contato), histórico
  paginado por `antesDe`, envio de texto e imagem pela porta do canal (S34) como
  `Mensagem(Saida, Dona)` que deixa a conversa `Assumida` e cala o agente, `assumir`,
  `liberar-automatico` (nota interna com o usuário), `encerrar` e `marcar-lida`. Fora da janela de
  24 h (domínio ou erro 131047 da Meta): 409 `{ erro: "fora_da_janela_24h", sugestao: "template" }`
  e nada gravado. `EscalarConversaUseCase` completa a porta `IEscaladorConversa` da S06: evento
  `ConversaEscalada = 46` no outbox, Web Push para todas as subscriptions da empresa (destinatário
  `empresa:{id}`) e SSE `conversa.escalada` (no-op até S18). (#1068)
- Identidade da conversa **por canal** e porta de envio (S34, ADR-0051): `CanalConversa` ganha
  Instagram, Messenger, ChatSite, Email e Sms; `CapacidadesCanal` diz o que cada um aceita (janela,
  modelo, tag fora da janela, mídia, botões). `Conversa.ContatoWaId` vira `ContatoIdExterno`
  normalizado por canal, com índice aberto `(EmpresaId, Canal, ContatoIdExterno)`; a migration é
  rename (não perde dados) e o Down recusa com conversa de outro canal. `GarantirPodeEnviarTextoLivre`
  recusa texto livre fora da janela sem tag válida. `ICanalMensageria` + `ResolvedorCanal` +
  adaptador `CanalWhatsApp`. (#1065)
- Agente de atendimento por WhatsApp com LLM e ferramentas (S06, onda 1, ADR-0050):
  `AgenteAtendimentoService` responde fora da requisição (fila `TurnoAgente`, consumida na Api)
  com a API Messages da Anthropic (`AnthropicMessagesClient`, modelo em `Anthropic:ModeloAgente`,
  padrão `claude-sonnet-5`), até 6 iterações de ferramenta; ao estourar, envia a frase de espera e
  escala. Ferramentas desta fatia: `consultar_cardapio`, `enviar_cardapio_imagem`,
  `consultar_pedido`, `escalar_para_dona` (mínimo; aviso por push/SSE na S07) e
  `encerrar_conversa`; pedido, endereço, janelas e CRM entram com a onda 2. Prompt com RN-01 a
  RN-08 e D3 (snapshot), notas internas marcadas `[interno]`, consumo em `UsoIa`.
  `RoteadorAcoesBotao` resolve `acao:<nome>:<payload>` sem LLM. Desligado com
  `Anthropic:Enabled=false`, `Anthropic:AgenteAtendimentoEnabled=false` ou sem chave. (#1064)
- Identificação do cliente ou lead pelo telefone no atendimento por WhatsApp (S05, onda 1,
  ADR-0050): na primeira mensagem de uma conversa nova, `IdentificarClientePorTelefoneUseCase`
  procura o `Cliente` pelo `TelefoneHash` do OTP e pelo `Telefone` do cadastro (E.164 e dígitos
  nacionais, com a variante do nono dígito que a Meta omite em celulares antigos) ou cria o lead
  com o telefone marcado como WhatsApp; a conversa é vinculada ao cadastro. A saudação
  (`SaudacaoAtendimento`: primeiro contato ou retorno com `{nome}`, link do cardápio e frase de
  espera) sai antes do agente e fica gravada como `Mensagem(Saida, Sistema)`.
  `NormalizadorTelefone` passa a ser a normalização E.164 única do OTP e do atendimento. (#1062)
- Webhook da Meta Cloud API (S03, onda 1, ADR-0050): `GET|POST api/webhooks/whatsapp` — GET
  responde a verificação, POST valida `X-Hub-Signature-256` (HMAC-SHA256, tempo constante) e
  devolve 200 quando a assinatura é válida, ou 503 quando uma mensagem falhou por motivo que um
  reenvio da Meta resolve (corrida na 1ª mensagem do contato, banco); regra de domínio não pede reenvio. `ProcessarEventoWhatsAppUseCase` resolve o
  tenant por `phone_number_id`, idempotência por `wamid` (reaproveitando `WebhookRecebido`), abre
  `Conversa`/grava `Mensagem`, marca como lida (best-effort), enfileira mídia e turno do agente
  (stub — S06 ainda não existe). `ITenantContextAccessor` liga o filtro de tenant e a RLS para o
  webhook, que não tem claim JWT — mesmo mecanismo do módulo Mobile. Fila de mídia com consumidor
  real (`AtendimentoFilaMidiaBackgroundService`, no processo da API porque a fila é em memória, com o
  tenant definido no job); fila do agente ainda sem consumidor. A janela de 24 h conta do instante
  da mensagem na Meta, não do processamento; wamid já gravado não é reinserido.
  (#1052)
- Plano do atendimento ampliado para **multicanal e sistema completo do protótipo** (ADR-0051,
  que amplia o ADR-0050): onda 8 com S34–S47 em `docs/plan/atendimento-whatsapp/09-sistema-completo.md`
  (canal como porta, Instagram, Messenger, chat do site, e-mail e SMS, consentimento por canal,
  mensagem programada, expediente da loja, atendentes, entregadores e viagens) e Onda 0 com os itens
  0.10 a 0.12. (#1060)
- Configuração do atendimento por WhatsApp por empresa (S08, onda 1, ADR-0050):
  `ConfiguracaoAtendimento` (tom, nível de sugestão, saudações, frase de espera, mensagem fora de
  área, respiro e tempo de preparo padrão), `GET|PUT api/atendimento/configuracao` (Admin — GET
  nunca 404, devolve o padrão) e `Storefront.CardapioImagemUrl`. `PromptAtendimento` compõe o
  cabeçalho do system prompt do agente a partir de tom e nível de sugestão (S06 estende). (#1050)
- Cliente da Cloud API da Meta para o atendimento WhatsApp (S02, onda 1, ADR-0050):
  `IWhatsAppCloudClient` (texto, imagem, botões interativos, template, marcar como lida, baixar
  mídia) com categoria de resiliência Polly própria (`whatsapp`) — erro de aplicação da Meta
  (ex.: `131047`, fora da janela de 24h) nunca é reenviado automaticamente. `MetaCloudWhatsAppProvider`
  passa a delegar a esse cliente em vez de falar HTTP direto. `ArmazenadorMidiaWhatsApp` guarda a
  mídia recebida no storage privado. (#1048)
- Fundacao da integracao com a **Meta Cloud API** (S01, onda 1 do atendimento WhatsApp,
  ADR-0050): `MetaCloudWhatsAppOptions` ganha `AppSecret`, `VerifyToken`, `ApiVersion` e
  `WabaId`, com o startup falhando cedo (nomeando a chave) quando o provider `meta` esta ligado
  sem credencial. `Empresa.WhatsAppPhoneNumberId` (indice unico parcial) roteia o webhook (S03)
  ate o tenant. Flag `modulo.atendimento` em `FeatureCatalogo` (ADR-0048) e
  `GET api/integracoes/whatsapp/status` (Admin) expondo o status por tenant, 404 sem a flag.
  (#1046)
- Agregado **Conversa/Mensagem** do atendimento por WhatsApp (S04, ADR-0050): entidades com
  situacao (automatica, assumida, encerrada), janela de 24 h contada da ultima mensagem do cliente,
  contexto JSON e status de entrega monotono; tabelas `atendimento_conversas` e
  `atendimento_mensagens` com indice parcial "uma conversa aberta por contato", `wamid` unico por
  empresa, filtro global de tenant e policy de RLS; `IConversaRepository` + repositorio Postgres.
  Nada consome ainda: e a fundacao de S03/S05/S06. (#1044)
- Plano do atendimento revisado para **Meta Cloud API direto** (ADR-0050 supersede ADR-0049):
  pasta renomeada para `docs/plan/atendimento-whatsapp/`, onda 1 reescrita (credenciais da Meta,
  cliente Graph com botoes e midia, webhook com `hub.challenge` e HMAC, conversa por `wa_id`,
  handoff pelo console, provider com regra da janela de 24 h) e ajustes nas ondas 2, 5, 6 e 7.
  Somente documentacao. (#1042)
- Plano executavel do backend para o atendimento da Casa da Baba via **Huggy** (ADR-0049,
  `docs/plan/atendimento-huggy/`): 31 specs em 6 ondas (fundacao Huggy, pedido e cobranca Pix,
  esteira e cozinha, estoque minimo, CRM e pos-venda, campanhas) + 6 specs de poda, cada uma com
  Problema/Abordagem/Escopo/Aceite/Rollback, testes Red e leitura minima, escritas para execucao
  por um modelo mais barato, uma spec por sessao. Somente documentacao. (#1040)
- Cadastro de cliente **pessoa juridica** (ADR-0048, item 1 do epico #1013): `TipoPessoa`,
  `NomeFantasia` e `InscricaoEstadual` no `Cliente`, com o formulario do Web alternando entre
  PF e PJ (rotulo, placeholder e campos condicionais). CNPJ ja era aceito antes — o que
  faltava era o discriminador e os dois campos. Nao ha campo `RazaoSocial`: seguindo o
  vocabulario da entidade `Empresa`, o **`Nome` E a razao social** quando o cliente e PJ.
  A busca passa a encontrar PJ tambem pelo nome fantasia. Migration aditiva com default
  `"fisica"` no banco — todo cadastro existente segue pessoa fisica sem UPDATE. (#1018)
- `TenantFeatureFlag` passa a ser lido e escrito em runtime (ADR-0048): repository com filtro
  explicito de `EmpresaId`, endpoint `GET /api/feature-flags` para o tenant logado, e servico
  no BFF Web com cache de 5min por empresa. O portal e o menu lateral escondem modulo que o
  tenant nao tem, com decisao **fail-closed** (Api fora do ar esconde, nao mostra). Item sem
  feature exigida nunca e afetado. Pre-requisito de todo o epico B2B (#1013). (#1016)
- Back-office ganha os endpoints `GET`/`PATCH api/admin/tenants/{id}/features`, que **acendem
  a aba "Features" ja existente** no Admin — ela chamava rotas inexistentes e o `catch`
  engolia a falha, exibindo "Nenhuma feature especial habilitada" como se fosse estado
  normal. O GET devolve o catalogo inteiro com o estado de cada feature, senao um tenant sem
  linha gravada nao teria como ligar o primeiro modulo. (#1016)
- ADR-0048: decisao de **plataforma unica** para o ERP B2B da FMA Informatica — ela entra como
  segundo tenant (`Empresa`) do EasyStok, com modulos novos gated por `TenantFeatureFlag`, em
  vez de fork, greenfield ou segundo backend. Epico com as speks em #1013. (#1014)
- Shell modular no Web: portal de modulos em `/launcher` como home autenticada (tela cheia,
  com pulso do dia, missoes e "Meu dia"), e menu lateral filtrado pelo modulo em que o
  usuario esta. O modulo e DERIVADO DA ROTA — sem querystring, cookie ou sessao —, entao o
  filtro sobrevive a redirect, formulario e paginacao. Nenhuma rota interna muda; o Dashboard
  continua como ancora, visivel dentro de qualquer modulo. Ver ADR-0046. (#1007)
- Missoes do dia no portal: pendencias computadas de dados que o sistema ja tem (pedidos em
  aberto, lotes vencidos, estoque critico, caixa do dia, parcelas vencidas). Sem tabela nova
  e sem migracao; missao sem fonte de dado nao e exibida em vez de aparecer como concluida. (#1007)
- Login em duas etapas com selecao de empresa: usuario com 2+ empresas ativas agora escolhe
  com qual entrar. Ver ADR-0047. (#1007)
- Telemetria OpenTelemetry (traces + metricas + logs) nos 4 hosts (Api/Web/Admin/Worker),
  exportada via OTLP pro otel-collector da VM consolidada -> OpenObserve. Opt-in por
  `OpenTelemetry:OtlpEndpoint` (vazio = desligado; dev/CI intactos). Api ganha Npgsql
  tracing, sink Serilog OTLP e sampler ParentBased (preserva trace distribuido). (#1002)

### Fixed
- `SeedSchemaBootstrap` rodava `ALTER TABLE "Empresas"` (a tabela e `empresas`) e falhava com 42P01
  a cada startup, quebrando todo seed pelo painel admin no passo 1. O `ALTER` sai: `IsSeedData` foi
  dropada pela migration `20260507011959` e e `[NotMapped]` no dominio. (#1092)
- `ToString` de `Quantidade`, `Dinheiro` e `Dimensoes` dependia da cultura do host (`1,5` no
  Windows pt-BR, `1.5` no CI Linux) e passa a ser invariante, como a Api ja produzia no container.
  O teste de wiring de metricas coleta so o proprio reader InMemory: o `ForceFlush` do provider
  esperava ~4 s pelo OTLP sem coletor e, sob carga, pulava o InMemory. (#1075)
- `POST /api/empresas/registrar` respondia 500 sob role sem `BYPASSRLS`: registrar empresa e
  cross-tenant por definicao (CRIA o tenant), entao a requisicao anonima nao tem `app.empresa_id`
  e a policy `tenant_isolation` recusava os INSERTs com `42501` em `assinaturas_empresa`. O
  `RegistrarEmpresaUseCase` passa a rodar sob `IRowLevelSecurityBypass.Begin()`, escopado por
  `using`. O escopo cobre o use case inteiro e nao so os INSERTs: `perfis` tambem tem RLS, e sem
  bypass `GetPadroesAsync` voltava vazio — o registro criava um perfil Admin por empresa em vez de
  reusar o padrao, **sem erro nenhum**. Coberto por teste de integracao com role
  `NOSUPERUSER NOBYPASSRLS` mais um controle negativo que prova que a RLS esta viva no ambiente
  (sem ele, um verde poderia significar apenas "a policy nao estava valendo"). (#1024)
- Usuario com 2+ empresas ativas nao conseguia entrar no Web: a Api emite token sem o claim
  `empresaId` nesse caso e o Web tratava como erro terminal ("entre em contato com o suporte").
  O caminho ja existia na Api (`auth/lista-empresas` + `login` aceitando `empresaId`) e nunca
  fora consumido. (#1007)
- `GET /` derrubava a landing publica com `AmbiguousMatchException`: o portal reivindicava o
  mesmo template de rota que o `SiteController`. (#1007)
- Card Financeiro do portal exibia texto de negocio fabricado ("2 contas a vencer hoje",
  variando por tenant) sem consultar nada; passa a usar as parcelas vencidas hoje do
  `financeiro/dashboard`, ou nenhum numero quando a fonte nao responde. (#1007)
- Cards do portal geravam URL malformada (`/estoque?status=vencido?modulo=producao`), que
  quebrava ao mesmo tempo o filtro de vencidos e o modulo. (#1007)

### Changed
- Plano do atendimento: a spec S11 passa a cobrir a troca da forma de pagamento com a cobrança já
  enviada e o desfazer de pagamento registrado à mão, defeito achado pela operadora no protótipo. A S44
  ganha veículo, placa e empresa do entregador, retrato do entregador por parada e relatório de
  entregas por bairro; a S48 nova liga o cardápio que o cliente marca à conversa. (#1072)
- Home autenticada passa a ser `/launcher` em todos os pontos (landing logada, onboarding,
  cardapio, lojas, 404 e primeiro slot do bottom nav mobile). Deep link continua vencendo:
  `returnUrl` valido tem precedencia sobre o portal. (#1007)
- Homolog consolidada na VM unica `hiram-demo-vm` (eastus2, `*.20.98.234.200.sslip.io`), host
  compartilhado com jornada + levante atras de um Caddy central; stack sobe sem caddy proprio
  (`docker-compose.azure.noedge.yml` + `EASYSTOK_COMPOSE_EXTRA` no vm-deploy.sh). VM antiga
  (westus2) descomissionada; observabilidade (Dozzle/Uptime Kuma/OpenObserve) no ar. (#997)

### Security
- `RlsBypassAllowlistTests`: allowlist de quem pode referenciar `IRowLevelSecurityBypass` em
  codigo. O port ja existia e ja tinha consumidores; o que faltava era o portao — injetar uma
  interface e barato demais para uma decisao que desliga a segunda camada de defesa do ADR-0010.
  Agora consumidor novo exige editar a lista, e isso aparece no diff da PR. Mencao em `<see cref>`
  nao conta: falso positivo em guarda de seguranca treina quem mantem o codigo a engordar a
  allowlist ate calar o teste. (#1024)
- Bump `System.Security.Cryptography.Xml` 10.0.7 -> 10.0.10 (5 advisories high / NU1903 que
  quebravam todo PR no CI com `-warnaserror`). (#999)
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Supersede a v3.1
  (master-first). Ver ADR-0043 e CLAUDE.md. (#881)
