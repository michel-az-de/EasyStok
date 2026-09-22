# Onda 1 — Fundação WhatsApp na Meta Cloud API (S01–S09)

Objetivo: o número da Casa da Baba conectado direto à Cloud API, conversa persistida, agente
respondendo em linguagem livre, a dona assumindo e devolvendo pelo console, e todo WhatsApp de saída
passando pelo mesmo provider com a regra da janela de 24 h.
Cobre US-001 a US-010 (exceto US-007, na onda 5), RN-01 a RN-08, D2, D3, D4, D5, D8.

Dependências externas: onda 0.1, 0.2, 0.3, 0.5, 0.8 do README.

---

### S01 · Credenciais da Meta, roteamento do número e flag do módulo

**Problema.** `MetaCloudWhatsAppOptions` só tem `AccessToken`, `PhoneNumberId` e `BaseUrl`, globais. Faltam o `AppSecret` (assinatura do webhook), o `VerifyToken`, a versão da API e o vínculo número → empresa para o webhook rotear.
**Abordagem.** Manter configuração global (há um número hoje; credencial por empresa só se aparecer um segundo número). Roteamento por `Empresa.WhatsAppPhoneNumberId`. Startup falha cedo se faltar credencial com o provider `meta` ligado.
**Escopo.**
- `Notifications/Options/WhatsAppProviderOptions.cs`: `MetaCloudWhatsAppOptions` ganha `AppSecret`, `VerifyToken`, `ApiVersion` (default o que está no código hoje, `v19.0`; revisar para a versão vigente na doc no momento da implementação) e `WabaId?`.
- `Domain/Entities/Empresa.cs`: `WhatsAppPhoneNumberId` (string?) + `VincularWhatsApp(phoneNumberId)`; migration `AddWhatsAppPhoneNumberIdEmpresa` com índice único parcial (`WHERE whats_app_phone_number_id IS NOT NULL`).
- `Domain/Constants/Features.cs`: `ModuloAtendimento = "ModuloAtendimento"`.
- `Api/Startup/StartupHardening.cs`: com `Notifications:WhatsApp:Provider=meta`, `AccessToken`, `AppSecret` e `VerifyToken` vazios derrubam o startup com mensagem nomeando a chave.
- `Api/Controllers/IntegracoesWhatsAppController.cs`: `GET api/integracoes/whatsapp/status` (policy `Admin`) → `{ phoneNumberId, apiVersion, webhookVerificadoEm, ultimaMensagemRecebidaEm, provider }` (os dois carimbos vêm de S03; até lá, nulos).
**Fora.** Múltiplos números; troca de token pela API.
**Aceite.**
- [ ] Startup com provider `meta` e `AppSecret` vazio falha citando `Notifications:WhatsApp:Meta:AppSecret`.
- [ ] Duas empresas com o mesmo `WhatsAppPhoneNumberId` → violação de índice.
- [ ] `GET status` de empresa sem flag `ModuloAtendimento` → 404.
**Testes (Red).** `StartupHardeningTests.MetaSemAppSecretFalha`, `EmpresaConfigurationTests.PhoneNumberIdUnico` (IntegrationTests), `IntegracoesWhatsAppControllerTests.SemFlag404`.
**Rollback.** Migration `Down`; opções novas são ignoradas.
**Depende de.** Onda 0.2.
**Leitura mínima.** `Notifications/Options/WhatsAppProviderOptions.cs`; `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs`; `Api/Startup/StartupHardening.cs`; `Domain/Entities/Empresa.cs`; `Domain/Constants/Features.cs`; `App/Ports/Output/Persistence/ITenantFeatureFlagRepository.cs`.
**Tamanho.** P. **Tier.** alto (migration).

---

### S02 · Cliente da Cloud API (`IWhatsAppCloudClient`) e mídia

**Problema.** O provider existente só manda texto (template fora da interface). O agente e o console precisam de imagem, botões interativos, marcar como lida e baixar a mídia que o cliente envia.
**Abordagem.** Extrair um cliente em `Integrations` e fazer o provider de notificações delegar a ele. Erros da Meta viram exceção tipada com o código numérico (`131047` fora da janela, `131026` destinatário indisponível, `130429` limite de taxa). Mídia recebida: `GET /{media_id}` → URL temporária → download com bearer → `IFileStorage` (S3 compatível, existe) → chave interna servida pela API com autenticação.
**Escopo.**
- Criar `App/Ports/Output/Atendimento/IWhatsAppCloudClient.cs`: `EnviarTextoAsync(waId, texto, responderA?)`, `EnviarImagemAsync(waId, urlPublica, legenda?)`, `EnviarBotoesAsync(waId, corpo, botoes[(id, titulo)])` (máximo 3, título ≤ 20, id ≤ 256), `EnviarTemplateAsync(waId, nome, idioma, parametrosCorpo[], botoesQuickReply[]?)`, `MarcarComoLidaAsync(wamid)`, `BaixarMidiaAsync(mediaId) → (Stream, mime)`. Retorno de envio: `EnvioWhatsAppResult(wamid)`; falha: `WhatsAppCloudException(codigo, mensagem, ehPermanente)`.
- Criar `Integrations/WhatsApp/WhatsAppCloudClient.cs` (`HttpClient` nomeado `whatsapp-cloud`, bearer de `MetaCloudWhatsAppOptions`, Polly da categoria existente em `IntegrationCategories`; sem retry em `131047`), `Integrations/WhatsApp/Dtos/*.cs`, `StubWhatsAppCloudClient.cs` (registrado quando `Notifications:WhatsApp:Provider=stub`, grava chamadas em memória para os testes).
- Refatorar `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs` para delegar ao cliente (R8: `git grep -n MetaCloudWhatsAppProvider` e ajustar todos os call-sites e o registro keyed `whatsapp:meta`).
- Criar `App/Services/Atendimento/ArmazenadorMidiaWhatsApp.cs`: salva em `atendimento/{empresaId}/{conversaId}/{wamid}.{ext}` e devolve a chave; `GET api/atendimento/midias/{mensagemId}` (policy `Operador`) serve o binário com `Content-Type` e cache privado.
**Fora.** Upload de mídia para a Meta (imagens de saída usam `link` público); áudio transcrito; localização.
**Aceite.**
- [ ] `EnviarTextoAsync` monta `{ messaging_product: "whatsapp", to, type: "text", text: { body } }` e devolve o `wamid` da resposta (HttpMessageHandler fake).
- [ ] `EnviarBotoesAsync` com 4 botões lança antes de chamar a rede; título de 21 caracteres idem.
- [ ] Resposta da Meta com erro `131047` vira `WhatsAppCloudException(131047, ehPermanente: true)` sem retry.
- [ ] `BaixarMidiaAsync` faz as duas chamadas (metadados e binário) com bearer e devolve o mime.
- [ ] `MetaCloudWhatsAppProvider` continua passando nos testes existentes (só delega).
**Testes (Red).** `WhatsAppCloudClientTests.EnviaTextoEDevolveWamid`, `...BotoesValidaLimites`, `...Erro131047EhPermanenteSemRetry`, `...BaixaMidiaComBearer`, `ArmazenadorMidiaWhatsAppTests.SalvaComChavePrevisivel`.
**Rollback.** Provider volta ao código anterior; remover pasta `Integrations/WhatsApp/WhatsAppCloud*`.
**Depende de.** S01.
**Leitura mínima.** `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs`; `Notifications/DependencyInjection/NotificationsInfraServiceCollectionExtensions.cs` (linhas 49-60); `Integrations/DependencyInjection/WhatsAppServiceCollectionExtensions.cs`; `Integrations/Resilience/IntegrationCategories.cs`; `Async/Storage/S3CompatibleFileStorage.cs` e `FileStorageOptions.cs` (só assinaturas); doc `developers.facebook.com/docs/whatsapp/cloud-api/messages` e `/media`.
**Tamanho.** M. **Tier.** alto.

---

### S03 · Webhook da Meta

**Problema.** Não existe recepção de mensagens nem de status de entrega.
**Abordagem.** `GET api/webhooks/whatsapp` responde à verificação (`hub.mode=subscribe`, `hub.verify_token` igual ao configurado → `hub.challenge` em texto puro, 200; senão 403). `POST api/webhooks/whatsapp` anônimo, valida `X-Hub-Signature-256` contra o HMAC-SHA256 do corpo bruto com o `AppSecret` (comparação em tempo constante); assinatura inválida → 403 sem processar; válida → 200 sempre (a Meta reenvia em não-200; reprocessamento é seguro pela idempotência). Rate limit `public-post`. O controller normaliza, persiste e enfileira; o agente roda fora da requisição pela fila em processo (`BackgroundQueueService`, existe) para responder em menos de 1 s.
**Escopo.**
- Criar `Api/Controllers/Webhooks/WebhookWhatsAppController.cs` (GET e POST). Ler o corpo bruto antes do model binding (necessário para o HMAC).
- Criar `App/UseCases/Atendimento/Webhook/ProcessarEventoWhatsAppUseCase.cs`: para cada `entry[].changes[].value`: resolver empresa por `metadata.phone_number_id` (`Empresa.WhatsAppPhoneNumberId`); empresa sem flag `ModuloAtendimento` → `WebhookRecebido(Sucesso=false, Erro="modulo_desligado")`. Para cada `messages[]`: idempotência por `(Provedor="meta_whatsapp", EventIdExterno=wamid)` em `WebhookRecebido`; abrir ou obter `Conversa` (S04) por `wa_id`; gravar `Mensagem(Entrada, Cliente)` com `TipoConteudo` por `type` (`text`, `image`, `audio`, `document`, `sticker`, `location`, `interactive`, `button`, `reaction`, `unsupported`); `MarcarComoLidaAsync` best-effort; mídia → enfileirar `ArmazenadorMidiaWhatsApp` (S02); `interactive.button_reply.id` ou `button.payload` com prefixo `acao:` → `RoteadorAcoesBotao` (S06) sem LLM; demais → enfileirar `ProcessarTurnoAgenteUseCase` (S06). Para cada `statuses[]`: atualizar `Mensagem.Status` por `wamid` (`sent → Enviada`, `delivered → Entregue`, `read → Lida`, `failed → Falhou` com `Erro`), ignorando wamid desconhecido.
- Criar `App/UseCases/Atendimento/Webhook/EventoWhatsApp.cs` (record normalizado) e o parser `WebhookWhatsAppParser.cs` (tolerante a campos novos).
- Gravar em `Empresa` (ou `ConfiguracaoAtendimento`, S08) `WebhookVerificadoEm` e `UltimaMensagemRecebidaEm` para o status de S01.
- Publicar `conversa.mensagem_recebida` no SSE (S18) após o commit; até S18 existir, deixar a chamada atrás de `IOperacaoEventPublisher` com implementação no-op.
**Fora.** Mensagens de grupo; reações (só registrar); mensagens enviadas pelo app (não chegam pelo webhook).
**Aceite.**
- [ ] `GET` com token correto devolve o challenge; token errado → 403.
- [ ] `POST` sem assinatura ou com assinatura errada → 403 e nada persistido.
- [ ] Mesmo `wamid` duas vezes processa uma vez.
- [ ] Mensagem de texto grava `Mensagem`, atualiza `Conversa.UltimaMensagemEntradaEm`, enfileira o agente e responde 200 em menos de 1 s com stub.
- [ ] Status `read` atualiza a `Mensagem` de saída correspondente; `failed` grava o erro.
- [ ] Imagem: `Mensagem` com `TipoConteudo=imagem` e mídia armazenada após o processamento da fila.
- [ ] `phone_number_id` desconhecido → 200 e `WebhookRecebido(Erro="empresa_desconhecida")`.
**Testes (Red).** `WebhookWhatsAppControllerTests.VerificacaoDevolveChallenge`, `...TokenErrado403`, `...AssinaturaInvalida403`, `...WamidDuplicadoNaoReprocessa`, `ProcessarEventoWhatsAppUseCaseTests.TextoEnfileiraAgente`, `...StatusAtualizaMensagem`, `...BotaoAcaoNaoChamaAgente`, `...ImagemArmazena`.
**Rollback.** Remover controller e use case; desassinar o webhook no app da Meta.
**Depende de.** S01, S02, S04.
**Leitura mínima.** `Api/Controllers/WebhookGatewayController.cs` (padrão de webhook e `WebhookRecebido`); `Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs` (padrão de HMAC); `Domain/Entities/WebhookRecebido.cs`; `Async/BackgroundQueueService.cs`; `Api/Configuration/ApiServiceCollectionExtensions.cs` (só o bloco `AddRateLimiter`).
**Tamanho.** M. **Tier.** alto.

---

### S04 · Agregado `Conversa` e `Mensagem`

**Problema.** O agente precisa de estado por conversa (assumida ou não, pedido em andamento, endereço já lido, janela de 24 h) e o console e o dossiê precisam do histórico.
**Abordagem.** Duas entidades mínimas, tenant-scoped. Uma conversa aberta por contato; encerrar abre espaço para a próxima.
**Escopo.**
- Criar `Domain/Entities/Atendimento/Conversa.cs`: `Id`, `EmpresaId`, `ClienteId?`, `ContatoWaId` (dígitos E.164, sem `+`), `ContatoNome?` (perfil do WhatsApp), `Canal` (`whatsapp`; enum aberto para o futuro), `Situacao` (`SituacaoConversa { Automatica, Assumida, Encerrada }`), `PedidoEmAndamentoId?`, `ContextoJson` (`jsonb`: carrinho em montagem, endereço extraído, etapa), `IniciadaEm`, `UltimaMensagemEm`, `UltimaMensagemEntradaEm` (janela de 24 h), `NaoLidas` (int), `EncerradaEm?`, `AssumidaPorUsuarioId?`. Métodos: `Assumir(usuarioId?)`, `LiberarAutomatico()`, `Encerrar()`, `RegistrarEntrada(...)`, `RegistrarSaida(...)`, `DefinirContexto(...)`, `DentroDaJanela24h(agora)`.
- Criar `Domain/Entities/Atendimento/Mensagem.cs`: `Id`, `EmpresaId`, `ConversaId`, `ExternoId?` (wamid), `Direcao` (`Entrada`, `Saida`), `Autor` (`Cliente`, `Agente`, `Dona`, `Sistema`), `TipoConteudo` (`texto`, `imagem`, `audio`, `documento`, `botao`, `localizacao`, `outro`), `Texto?`, `BotaoId?`, `MidiaChave?`, `MidiaMime?`, `Status` (`Pendente`, `Enviada`, `Entregue`, `Lida`, `Falhou`), `Erro?`, `EnviadaEm`, `ProcessadaEm?`.
- Configurações EF em `Postgre/Data/Configurations/Atendimento/`, `DbSet`s, filtro global por tenant (reflexão já aplica), RLS herdada (conferir `RlsBypassAllowlistTests`). Índices: `(EmpresaId, ContatoWaId) WHERE situacao <> 'encerrada'` único; `(EmpresaId, UltimaMensagemEm DESC)`; `(EmpresaId, ExternoId)` único parcial em `Mensagem`.
- Repositório `App/Ports/Output/Atendimento/IConversaRepository.cs` (`ObterAbertaPorContatoAsync`, `ObterComMensagensAsync(id, ultimasN)`, `ListarAsync(filtro, paginacao)`, `ListarPorClienteAsync`, `ObterMensagemPorExternoIdAsync`, `AddAsync`, `AddMensagemAsync`).
- Migration `AddAtendimentoConversaMensagem`.
**Fora.** Anexos binários no banco; leitura por usuário.
**Aceite.**
- [ ] `Assumir()` em conversa `Encerrada` lança `RegraDeDominioVioladaException`.
- [ ] Segunda conversa aberta para o mesmo contato viola o índice; após `Encerrar()` é permitida.
- [ ] `DentroDaJanela24h` verdadeiro 23 h após a última entrada e falso 25 h depois.
- [ ] Filtro de tenant ativo: conversa de outra empresa não aparece (dois tenants, IntegrationTests).
- [ ] Migration sobe e desce limpa em Postgres real.
**Testes (Red).** `ConversaTests.AssumirEmEncerradaLanca`, `ConversaTests.Janela24h`, `ConversaRepositoryTests.UmaAbertaPorContato`, `ConversaRepositoryTests.IsolamentoDeTenant`.
**Rollback.** Migration `Down`.
**Depende de.** Nada.
**Leitura mínima.** `Domain/Entities/Storefront/PedidoAvaliacao.cs` (padrão de entidade com factory e invariantes); uma configuração em `Postgre/Data/Configurations/Storefront/`; `Postgre/Data/EasyStockDbContext.cs` (região dos `DbSet` de Storefront e o bloco `HasQueryFilter`, ~linha 522); `EasyStock.ArchitectureTests/EfCoreHasFilterValidationTests.cs`.
**Tamanho.** M. **Tier.** alto (migration).

---

### S05 · Identificar cliente ou lead pelo telefone

**Problema.** RN-02 e RN-03: cliente com telefone cadastrado é saudado pelo nome; lead é quem ainda não comprou. Saudação e cardápio em até 5 s (RN-01).
**Abordagem.** Na primeira mensagem de uma conversa nova: `wa_id` já é E.164; procurar `Cliente` por `FindByTelefoneAsync` e por `TelefoneHash` (SHA-256 do E.164, já usado pelo OTP); não achou → `Cliente.Criar(empresaId, nome do perfil)` com `ClienteTelefone(Whatsapp=true, Principal=true)`; `OrderCount == 0` define lead. A saudação (com link do cardápio e frase de espera) sai **antes** do agente, pelo próprio handler, com texto de `ConfiguracaoAtendimento` (S08): garante os 5 s sem depender do LLM.
**Escopo.**
- Criar `App/Services/Atendimento/NormalizadorTelefone.cs` (E.164 BR; extrair de `App/UseCases/Storefront/Auth/SolicitarOtpUseCase.cs` sem duplicar e apontar o OTP para ele).
- Criar `App/UseCases/Atendimento/IdentificarClientePorTelefoneUseCase.cs` → `{ Cliente, EhLead, EhNovo }`.
- No fluxo de S03, quando a conversa acabou de ser aberta: identificar, vincular `Conversa.ClienteId`, enviar saudação (primeiro contato ou retorno, com `{nome}`) + link `https://casadababa.com/cardapio` (de `Storefront.Slug`/domínio) + frase de espera, gravar como `Mensagem(Saida, Sistema)`, e só então enfileirar o agente.
- Cliente bloqueado (S24): sem saudação automática; `Conversa.Assumir()` + `Mensagem(Sistema, "cliente bloqueado: <motivo>")` + notificação à dona (S07). Até S24 existir, deixar `TODO(S24)` com o número da issue.
**Fora.** Merge de cadastros; detecção de duplicidade por nome.
**Aceite.**
- [ ] Telefone conhecido → saudação com o nome; `Conversa.ClienteId` aponta para o cadastro existente.
- [ ] Telefone desconhecido → `Cliente` novo com `OrderCount=0`, telefone marcado como WhatsApp; saudação de primeiro contato.
- [ ] Tempo entre a mensagem recebida e `EnviarTextoAsync` da saudação < 5 s com stub (medido no teste).
- [ ] Conversa já aberta não repete a saudação.
**Testes (Red).** `IdentificarClientePorTelefoneUseCaseTests.ConhecidoRetornaExistente`, `...DesconhecidoCriaLead`, `ProcessarEventoWhatsAppUseCaseTests.PrimeiraMensagemSaudaAntesDoAgente`, `...ConversaAbertaNaoRepeteSaudacao`.
**Rollback.** Handler volta a só registrar a conversa.
**Depende de.** S02, S03, S04, S08.
**Leitura mínima.** `Domain/Entities/Cliente.cs` (linhas 1-140 e `ClienteTelefone`); `App/Ports/Output/Persistence/IClienteRepository.cs`; `App/UseCases/Storefront/Auth/SolicitarOtpUseCase.cs` (só normalização e hash); `App/UseCases/CriarCliente/*.cs`; `App/UseCases/AdicionarClienteTelefone/*.cs`.
**Tamanho.** M. **Tier.** alto.

---

### S06 · Agente de atendimento (LLM com ferramentas) e roteador de botões

**Problema.** US-003 e D3: interpretar linguagem livre, sem menu numérico; responder com base no estado real do pedido; nunca prometer prazo menor que o preparo (RN-06); não afirmar hábito (RN-07); nota interna nunca sai (RN-08).
**Abordagem.** Um serviço de turno: recebe `ConversaId`, monta o contexto (últimas 20 mensagens, cliente, pedido em andamento, `ContextoJson`, tom de S08), chama a API Messages da Anthropic com `tools`, executa as ferramentas contra a Application (nunca contra o banco), repete até `stop_reason != "tool_use"` (máximo 6 iterações), grava `Mensagem(Saida, Agente)` e envia pela Cloud API. Modelo por configuração (`Anthropic:ModeloAgente`, padrão `claude-sonnet-5`). Uso registrado em `UsoIa`. Botões com id `acao:<nome>:<payload>` são resolvidos por um roteador sem LLM (usado por S26 e por confirmações simples).
**Ferramentas (nome, entrada → saída).**
| Ferramenta | Faz | Reusa |
|---|---|---|
| `consultar_cardapio` | itens visíveis e disponíveis com preço, porções, linha e tempo de preparo | `ListarCardapioPublicoUseCase` |
| `enviar_cardapio_imagem` | envia `Storefront.CardapioImagemUrl` (S08) como imagem, com o link do cardápio na legenda | `IWhatsAppCloudClient.EnviarImagemAsync` |
| `validar_endereco(rua, numero, complemento, bairro, cep)` | normaliza, checa área, devolve taxa e tempo | S14 |
| `confirmar_endereco(...)` | grava `ClienteEndereco` padrão | S14 |
| `listar_janelas(data?)` | janelas com vaga respeitando a antecedência | S16 |
| `criar_pedido(itens[], janela_id, data_entrega, observacao?)` | pedido + cobrança Pix; devolve resumo, total, copia-e-cola e QR | S10, S11 |
| `consultar_pedido(pedido_id?)` | status e previsão do último pedido | `ObterPedidoDetalhes` |
| `registrar_restricao(tag)` / `registrar_interesse(item)` / `registrar_nota(texto)` | CRM | S24, S31 |
| `escalar_para_dona(motivo)` | `Conversa.Assumir()` + aviso à dona | S07 |
| `encerrar_conversa()` | `Conversa.Encerrar()` | S04 |
**Escopo.**
- Criar `Integrations/Ia/AnthropicMessagesClient.cs` (`POST /v1/messages`, headers `x-api-key` e `anthropic-version: 2023-06-01`, corpo `{ model, max_tokens, system, messages, tools }`; resposta com blocos `text` e `tool_use { id, name, input }`; a continuação envia mensagem `user` com `tool_result { tool_use_id, content }`). Copiar o padrão HTTP de `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs`; não mover o arquivo antigo.
- Criar `App/Services/Atendimento/AgenteAtendimentoService.cs`, `App/Services/Atendimento/Ferramentas/*.cs` (uma classe por ferramenta implementando `IFerramentaAgente { Nome, Descricao, SchemaJson, ExecutarAsync }`), `App/Services/Atendimento/PromptAtendimento.cs` (system prompt com RN-01 a RN-08, D3 e o tom de S08), `App/Services/Atendimento/RoteadorAcoesBotao.cs` (`acao:avaliacao:...` em S26; `acao:confirmar_endereco:...`, `acao:escolher_janela:...` para confirmações de um toque).
- Criar `App/UseCases/Atendimento/ProcessarTurnoAgenteUseCase.cs` (chamado pela fila de S03). Guardas: `Conversa.Situacao != Automatica` → não responde; fora da janela de 24 h → não responde (o cliente acabou de escrever, então isso só acontece em reprocessamento tardio).
- Mensagem com mídia do cliente entra no contexto como `[imagem recebida]`; o prompt orienta a responder que por enquanto só lê texto.
- Registrar `UsoIa` por turno (tokens de entrada e saída).
**Fora.** Voz; classificador de sentimento (US-051, Could); memória longa além do dossiê.
**Aceite.**
- [ ] "quanto tempo falta?" gera `tool_use consultar_pedido` e depois texto natural com a previsão (LLM fake).
- [ ] Prompt contém as 8 regras e o tom; teste de snapshot do system prompt.
- [ ] `Conversa.Assumida` → turno não chama o LLM nem envia nada.
- [ ] Limite de 6 iterações; ao estourar, envia mensagem de espera e escala.
- [ ] Botão `acao:confirmar_endereco:<id>` executa a confirmação sem chamar o LLM.
- [ ] Notas internas entram no contexto marcadas `[interno]` e o prompt proíbe reproduzi-las; teste verifica a marcação.
**Testes (Red).** `AgenteAtendimentoServiceTests.ExecutaFerramentaEResponde`, `...NaoRespondeQuandoAssumida`, `...LimiteDeIteracoesEscala`, `RoteadorAcoesBotaoTests.ConfirmaEnderecoSemLlm`, `PromptAtendimentoTests.ContemRegrasRN01aRN08`, `AnthropicMessagesClientTests.MontaCorpoComTools`.
**Rollback.** `Anthropic:Enabled=false`: o webhook continua persistindo mensagens e o handoff manual funciona.
**Depende de.** S02, S04, S05, S08; ferramentas de pedido dependem da onda 2 (entregar o agente primeiro com cardápio, endereço, escalar e encerrar; ligar as demais quando S10, S11, S14 e S16 existirem).
**Leitura mínima.** `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs`; `App/UseCases/Storefront/Menu/ListarCardapioPublicoUseCase.cs`; `App/UseCases/ObterPedidoDetalhes/*.cs`; `Domain/Entities/UsoIa.cs`; `App/Ports/Output/Ai/IGeradorAutoPreenchimento.cs` (padrão de porta).
**Tamanho.** G. **Tier.** alto.

---

### S07 · Handoff pelo console: assumir, responder, devolver, encerrar

**Problema.** US-004, US-009, US-013, RN-04, RN-05, D4: qualquer mensagem da dona suspende o automático daquela conversa; a retomada é decisão dela; avisos transacionais continuam; ela precisa ser avisada em todos os dispositivos quando o agente escala.
**Abordagem.** A inbox é do console; o backend expõe a API de conversas. Enviar pelo console marca `Assumida`. Escalada = `Assumir()` + Web Push (existe) + SSE. Respostas rápidas são do front, preenchidas com dados do dossiê (S25).
**Escopo.**
- `Api/Controllers/AtendimentoConversasController.cs` (policy `Operador`): `GET api/atendimento/conversas?situacao=&q=&pagina=` (cliente, última mensagem, `NaoLidas`, situação, pedido em andamento); `GET api/atendimento/conversas/{id}/mensagens?antesDe=&limite=`; `POST api/atendimento/conversas/{id}/mensagens { texto }` e `POST .../mensagens/imagem` (multipart → `UploadsController` guarda → `EnviarImagemAsync` com URL pública) → grava `Mensagem(Saida, Dona)`, chama `Conversa.Assumir(usuarioId)`; `POST .../assumir`, `POST .../liberar-automatico`, `POST .../encerrar`, `POST .../marcar-lida` (zera `NaoLidas`).
- Fora da janela de 24 h: `EnviarTextoAsync` devolve `131047` → API responde 409 `{ erro: "fora_da_janela_24h", sugestao: "template" }`; o console oferece o template `avaliacao`? Não: oferece `pedido_*` só quando fizer sentido. Nesta versão, 409 e mensagem clara.
- Criar `App/UseCases/Atendimento/EscalarConversaUseCase.cs` (usado pela ferramenta `escalar_para_dona`): `Assumir()`, `Mensagem(Sistema, motivo)`, evento de notificação `ConversaEscalada = 46` (InApp + Push para os usuários da empresa com `WebPushSubscription`), SSE `conversa.escalada`.
- `TipoEventoNotificacao.ConversaEscalada = 46` + template seed ("<cliente> precisa de você: <motivo>").
**Fora.** Transferência entre atendentes; horário de atendimento; áudio de saída.
**Aceite.**
- [ ] `POST mensagens` envia pela Cloud API, grava `Mensagem(Dona)` com o `wamid` e a conversa fica `Assumida`; a mensagem seguinte do cliente não aciona o agente.
- [ ] `liberar-automatico` volta a `Automatica` e grava `Mensagem(Sistema)` com o usuário.
- [ ] `encerrar` fecha; nova mensagem do contato abre conversa nova `Automatica` com saudação (S05).
- [ ] `escalar_para_dona` gera push para cada subscription da empresa e SSE.
- [ ] Aviso de status enfileirado no outbox sai mesmo com a conversa `Assumida` (S13 referencia este aceite).
- [ ] Envio fora da janela → 409 e nada gravado como enviado.
**Testes (Red).** `AtendimentoConversasControllerTests.EnviarMarcaAssumida`, `...LiberarVoltaAutomatica`, `...EncerrarPermiteNovaConversa`, `EscalarConversaUseCaseTests.PushParaTodasSubscriptions`, `...ForaDaJanela409`.
**Rollback.** Remover controller e use case.
**Depende de.** S02, S04, S06.
**Leitura mínima.** `Notifications/Push/WebPushCanal.cs`; `Domain/Entities/Notifications/WebPushSubscription.cs`; `Api/Controllers/PwaPushController.cs` (como a subscription é criada); `Api/Controllers/UploadsController.cs` (rotas); `App/UseCases/Notifications/PublicarEventoNotificacaoCommand.cs`.
**Tamanho.** M. **Tier.** alto.

---

### S08 · Configuração do atendimento por empresa

**Problema.** US-005: limite de autonomia e tom configuráveis; saudações; mensagem fora de área; imagem do cardápio; tempo de preparo padrão.
**Abordagem.** Entidade pequena, uma por empresa, lida pelo agente e pelo handler de saudação.
**Escopo.**
- Criar `Domain/Entities/Atendimento/ConfiguracaoAtendimento.cs`: `EmpresaId` (PK), `Tom` (texto curto, ex.: "acolhedor, direto, sem gíria"), `NivelSugestao` (`Discreto` | `Ativo`), `SaudacaoPrimeiroContato`, `SaudacaoRetorno` (com `{nome}`), `FraseEspera`, `MensagemForaArea`, `RespiroMinutos` (padrão 40), `TempoPreparoPadraoMinutos` (padrão 60), `WebhookVerificadoEm?`, `UltimaMensagemRecebidaEm?`, `Ativo`. Migration `AddConfiguracaoAtendimento`.
- `Storefront.CardapioImagemUrl` (string?, pública) + método; migration na mesma fatia.
- `GET|PUT api/atendimento/configuracao` (policy `Admin`).
- Seed padrão para a Casa da Baba em `Api/Data/` (mesmo mecanismo de `NotificacoesGlobaisSeed.cs`), com textos tirados do doc 03 (RN-01, US-002).
**Fora.** Versionamento de prompt; A/B.
**Aceite.**
- [ ] `PUT` valida `RespiroMinutos >= 0`, `TempoPreparoPadraoMinutos > 0` e `NivelSugestao` válido; `GET` de empresa sem registro devolve o padrão (não 404).
- [ ] `PromptAtendimento` (S06) muda quando `Tom` e `NivelSugestao` mudam (snapshot com dois valores).
**Testes (Red).** `ConfiguracaoAtendimentoTests.PadraoValido`, `AtendimentoConfiguracaoControllerTests.PutValida`, `PromptAtendimentoTests.RefleteTomENivel`.
**Rollback.** Migration `Down`.
**Depende de.** Nada (S05 e S06 consomem).
**Leitura mínima.** `Domain/Entities/ConfiguracaoLoja.cs` (padrão); `Domain/Entities/Storefront/Storefront.cs` (linhas 41-110); `Api/Data/NotificacoesGlobaisSeed.cs` (assinatura e registro no startup).
**Tamanho.** P. **Tier.** alto (migration).

---

### S09 · Provider da Meta no outbox de notificações e regra da janela de 24 h

**Problema.** O outbox de notificações vai entregar avisos de status (S13), avaliação (S26) e campanhas (S30). Fora da janela de 24 h a Meta exige template; hoje o provider só manda texto e o template está fora da interface.
**Abordagem.** `MetaCloudWhatsAppProvider` (já `whatsapp:meta`) passa a ser o padrão e ganha a decisão: `MensagemPronta.Metadados["template"]` presente e (sem conversa aberta ou fora da janela) → template com parâmetros; dentro da janela → texto renderizado. Erro `131047` sem template → falha permanente (sem retry). Toda saída com conversa aberta é copiada como `Mensagem(Saida, Sistema)` para o histórico.
**Escopo.**
- Estender `MensagemPronta` com `IReadOnlyDictionary<string,string>? Metadados` (aditivo; chaves `template`, `idioma`, `param1..paramN`, `botao1..botao3`). R8: `git grep "new MensagemPronta"` e ajustar todos os construtores no mesmo commit.
- `ResultadoEnvio` ganha `FalhaPermanente` (bool, default false) e o dispatcher (`NotificadorService`) não reagenda quando verdadeiro (verificar onde `MaxTentativas` é aplicado e curto-circuitar).
- Provider: resolve `Conversa` aberta por `Destinatario` (E.164) via `IConversaRepository`; decide texto ou template; grava `Mensagem(Sistema)` com o `wamid`; `LogEnvioNotificacao.Provider = "meta"`.
- `Notifications:WhatsApp:Provider` default `meta` no `appsettings.json` da Api e do Worker; `stub` nos testes.
**Fora.** Campanhas (S30) e avaliação (S26) só consomem.
**Aceite.**
- [ ] Dentro da janela, envia texto; fora, envia o template com os parâmetros na ordem `param1..n`.
- [ ] Fora da janela sem `template` → `ResultadoEnvio(Sucesso=false, FalhaPermanente=true, ErroDetalhado="fora_da_janela_24h_sem_template")` e o outbox não reagenda.
- [ ] Envio com conversa aberta grava `Mensagem(Sistema)` com `wamid`; sem conversa não grava nada além do log.
**Testes (Red).** `MetaCloudWhatsAppProviderTests.DentroDaJanelaTexto`, `...ForaDaJanelaTemplate`, `...SemTemplateFalhaPermanente`, `NotificadorServiceTests.FalhaPermanenteNaoReagenda`, `MensagemProntaTests.MetadadosOpcionais`.
**Rollback.** `Notifications:WhatsApp:Provider=stub`.
**Depende de.** S02, S04.
**Leitura mínima.** `App/Ports/Output/Notifications/IProvedorWhatsApp.cs`; `App/Ports/Output/Notifications/ICanalNotificacao.cs`; `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs`; `Notifications/DependencyInjection/NotificationsInfraServiceCollectionExtensions.cs` (linhas 40-60); `App/Services/Notifications/NotificadorService.cs` (tratamento de `ResultadoEnvio` e `MaxTentativas`).
**Tamanho.** M. **Tier.** alto.
