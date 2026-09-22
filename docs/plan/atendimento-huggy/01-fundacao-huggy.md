# Onda 1 — Fundação Huggy (S01–S09)

Objetivo: a Huggy conectada ao EasyStok, conversa persistida, agente respondendo em linguagem livre,
handoff para a dona funcionando e o WhatsApp de saída passando pela Huggy.
Cobre US-001 a US-010 (exceto US-007, na onda 5), RN-01 a RN-08, D2, D3, D4, D8.

Dependências externas: onda 0.1, 0.2, 0.3, 0.5 do README.

---

### S01 · Credencial da Huggy por empresa e troca de token OAuth

**Problema.** A Huggy usa OAuth 2.0 authorization code com token de 6 meses e refresh. Não há onde guardar isso por empresa.
**Abordagem.** Reusar `CredencialIntegracao` (AES-256-GCM, chave `(empresa_id, provider_key, ambiente)`) com `provider_key = "huggy"` e payload JSON `{ access_token, refresh_token, expires_at, company_id, departamento_humano_id }`. Um endpoint administrativo troca o `code` pelo token uma única vez. Refresh automático quando faltar menos de 30 dias ou em 401.
**Escopo.**
- Criar `Integrations/Huggy/HuggyOptions.cs` (`ClientId`, `ClientSecret`, `RedirectUri`, `BaseUrl = https://api.huggy.app/v3`, `AuthUrl = https://auth.huggy.app`).
- Criar `App/Ports/Output/Atendimento/IHuggyCredencialStore.cs` (`ObterAsync(empresaId)`, `SalvarAsync(empresaId, HuggyCredencial)`), implementado sobre `ICredencialIntegracaoRepository` existente.
- Criar `App/UseCases/Atendimento/Huggy/TrocarCodeOAuthHuggyUseCase.cs` e `RenovarTokenHuggyUseCase.cs`.
- Criar `Api/Controllers/IntegracoesHuggyController.cs`: `GET api/integracoes/huggy/autorizar` (devolve a URL de autorização), `POST api/integracoes/huggy/oauth/code { code }` (troca e grava). Policy `Admin`.
- Adicionar `Features.ModuloAtendimento = "ModuloAtendimento"` em `Domain/Constants/Features.cs`.
**Fora.** Múltiplas contas Huggy por empresa; UI.
**Aceite.**
- [ ] `POST oauth/code` com code válido grava credencial cifrada e responde 204; segunda chamada substitui.
- [ ] `RenovarTokenHuggyUseCase` troca `refresh_token` e atualiza `expires_at`.
- [ ] Empresa sem flag `ModuloAtendimento` recebe 404 nos dois endpoints (não vaza existência).
- [ ] Nenhum segredo em log (teste verifica que o logger não recebe `access_token`).
**Testes (Red).** `TrocarCodeOAuthHuggyUseCaseTests.GravaCredencialCifrada`, `RenovarTokenHuggyUseCaseTests.RenovaQuandoFaltaMenosDe30Dias`, `IntegracoesHuggyControllerTests.SemFlagRetorna404`.
**Rollback.** Remover controller e use cases; a linha em `CredenciaisIntegracao` pode ficar.
**Depende de.** Onda 0.1.
**Leitura mínima.** `Domain/Integration/CredencialIntegracao.cs`; `App/Ports/Output/Persistence/ITenantFeatureFlagRepository.cs`; `Domain/Constants/Features.cs`; `Integrations/DependencyInjection/IntegrationsServiceCollectionExtensions.cs`; `Integrations/Pagamentos/MercadoPago/MercadoPagoOptions.cs` (padrão de options).
**Tamanho.** M. **Tier.** alto (feat, auth).

---

### S02 · Cliente HTTP da Huggy (`IHuggyClient`) + spike de mensagem ativa

**Problema.** Nada fala com a API v3 da Huggy.
**Abordagem.** Cliente fino com Polly já configurado por `IntegrationCategories` (retry 3x, circuit breaker, timeout 30 s). Um método por operação usada pelas specs seguintes. Antes de codar, spike de leitura na doc (30 min, sem commit) para fechar a única lacuna: **como enviar mensagem a um contato sem chat aberto** (chat ativo ou template HSM). Registrar a resposta no topo do arquivo `HuggyClient.cs`.
**Escopo.**
- Criar `App/Ports/Output/Atendimento/IHuggyClient.cs`:
  `EnviarTextoAsync(empresaId, chatId, texto)`, `EnviarArquivoAsync(empresaId, chatId, url ou base64, legenda)`, `EnviarTemplateAsync(empresaId, chatId, templateId, params)`, `ObterChatAsync(empresaId, chatId)`, `ObterContatoAsync(empresaId, contatoId)`, `AtualizarContatoAsync(empresaId, contatoId, nome?, customFields)`, `AtribuirDepartamentoAsync(empresaId, chatId, departamentoId)`, `TransferirParaAgenteAsync(empresaId, chatId, agenteId, mensagem)`, `EncerrarChatAsync(empresaId, chatId, comentario?)`, `AbrirChatComContatoAsync(empresaId, contatoId, mensagemInicial)` (assinatura definida pelo spike).
- Criar `Integrations/Huggy/HuggyClient.cs`, `Integrations/Huggy/Dtos/*.cs`, `Integrations/Huggy/HuggyServiceCollectionExtensions.cs` (`AddHuggy(configuration)` com `HttpClient` nomeado, bearer vindo de `IHuggyCredencialStore`, renovação em 401 via S01).
- Criar `Integrations/Huggy/StubHuggyClient.cs` para testes e ambientes sem Huggy (`Huggy:UseStub`).
**Fora.** Cache de contatos; paginação de listagens.
**Aceite.**
- [ ] `EnviarTextoAsync` faz `POST /chats/{id}/messages` com `{ text }` e bearer da empresa; 401 dispara renovação e uma nova tentativa.
- [ ] Stub registrado quando `Huggy:UseStub=true`; produção falha no startup se `ClientId` estiver vazio e stub desligado.
- [ ] Resultado do spike documentado em comentário no topo de `HuggyClient.cs` com a URL da doc consultada.
**Testes (Red).** `HuggyClientTests.EnviaTextoComBearerDaEmpresa` (HttpMessageHandler fake), `HuggyClientTests.RenovaTokenEm401EUmaVez`.
**Rollback.** Remover pasta `Integrations/Huggy` e registro no DI.
**Depende de.** S01.
**Leitura mínima.** `Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs` (padrão de client); `Integrations/Resilience/IntegrationCategories.cs`; `Integrations/DependencyInjection/IntegrationsServiceCollectionExtensions.cs`; doc `developers.huggy.io/pt/API/api-v3.html`.
**Tamanho.** M. **Tier.** alto.

---

### S03 · Webhook inbound da Huggy

**Problema.** Não existe recepção de eventos da Huggy.
**Abordagem.** `POST api/webhooks/huggy/{segredo}` anônimo. Handshake: corpo com `validToken:true` responde o `token` recebido. Segurança em camadas: segredo no caminho (config `Huggy:WebhookSegredo`), comparação do `token` do payload com o token guardado no handshake, rate limit `public-post`, idempotência por `(provedor="huggy", EventIdExterno)` em `WebhookRecebido`. O controller só normaliza e persiste; o processamento pesado (agente) sai pela fila em processo `BackgroundQueueService` (Async), com a conversa já gravada, para responder 200 em menos de 1 s.
**Escopo.**
- Criar `Api/Controllers/Webhooks/WebhookHuggyController.cs`.
- Criar `App/UseCases/Atendimento/Webhook/ProcessarEventoHuggyUseCase.cs` com um `switch` por evento: `startedChat`, `receivedMessage`, `sentAllMessage`, `agentEntered`, `closedChat`, `createdCustomer`, `updatedCustomer`. Eventos desconhecidos: registrar e ignorar.
- Criar `App/UseCases/Atendimento/Webhook/EventoHuggy.cs` (record normalizado: `Tipo`, `EventoId`, `ChatId`, `ContatoId`, `Mobile` (E.164), `Nome`, `Canal`, `Situacao`, `Texto`, `ArquivoUrl`, `EhInterno`, `OcorridoEm`, `PayloadRaw`).
- Resolver `EmpresaId`: por `company.id` do payload → coluna nova `Empresa.HuggyCompanyId` (string, índice único parcial); migration `AddHuggyCompanyIdEmpresa`.
- Empresa sem flag `ModuloAtendimento`: registra em `WebhookRecebido` com `Sucesso=false, Erro="modulo_desligado"` e responde 200.
**Fora.** Anexos (só a URL é guardada); mensagens internas (`is_internal=true` são ignoradas).
**Aceite.**
- [ ] Handshake responde 200 com o token; segredo errado responde 404.
- [ ] Mesmo `EventoId` duas vezes processa uma vez (`WebhookRecebido` UNIQUE).
- [ ] `receivedMessage` de empresa com flag enfileira o turno do agente e responde 200 em menos de 1 s (teste mede sem chamar LLM: stub).
- [ ] Payload sem `company.id` conhecido responde 200 e grava `Erro="empresa_desconhecida"`.
**Testes (Red).** `WebhookHuggyControllerTests.HandshakeDevolveToken`, `...SegredoErrado404`, `...EventoDuplicadoNaoReprocessa`, `ProcessarEventoHuggyUseCaseTests.ModuloDesligadoIgnora`.
**Rollback.** Remover controller e use case; a coluna `HuggyCompanyId` fica nula.
**Depende de.** S01, S04.
**Leitura mínima.** `Api/Controllers/WebhookGatewayController.cs` (padrão de webhook + `WebhookRecebido`); `Domain/Entities/WebhookRecebido.cs`; `Async/BackgroundQueueService.cs`; `Api/Configuration/ApiServiceCollectionExtensions.cs` (só o bloco `AddRateLimiter`); `Domain/Entities/Empresa.cs`.
**Tamanho.** M. **Tier.** alto (migration).

---

### S04 · Agregado `Conversa` e `Mensagem`

**Problema.** O agente precisa de estado por conversa (assumida ou não, pedido em andamento, endereço já lido) e do histórico para o dossiê.
**Abordagem.** Duas entidades mínimas, tenant-scoped, sem espelhar toda a Huggy.
**Escopo.**
- Criar `Domain/Entities/Atendimento/Conversa.cs`: `Id`, `EmpresaId`, `ClienteId?`, `ChatExternoId` (string, único por empresa), `ContatoExternoId`, `Canal` (`whatsapp`, `instagram`, `widget`, `outro`), `Situacao` (enum `SituacaoConversa { Automatica, Assumida, Encerrada }`), `PedidoEmAndamentoId?`, `ContextoJson` (carrinho em montagem, endereço extraído, etapa; `jsonb`), `IniciadaEm`, `UltimaMensagemEm`, `EncerradaEm`. Métodos: `Assumir()`, `Encerrar()`, `RegistrarMensagem(...)`, `DefinirContexto(...)`.
- Criar `Domain/Entities/Atendimento/Mensagem.cs`: `Id`, `EmpresaId`, `ConversaId`, `ExternoId?`, `Direcao` (`Entrada`, `Saida`), `Autor` (`Cliente`, `Agente`, `Dona`, `Sistema`), `Texto`, `ArquivoUrl?`, `TipoArquivo?`, `EnviadaEm`, `ProcessadaEm?`, `Erro?`.
- Configurações EF em `Postgre/Data/Configurations/Atendimento/`, `DbSet`s, filtro global por tenant (reflexão já aplica), RLS herdada (verificar `RlsBypassAllowlistTests`).
- Repositório `App/Ports/Output/Atendimento/IConversaRepository.cs` (`ObterPorChatExternoAsync`, `ObterComMensagensAsync(id, ultimasN)`, `ListarPorClienteAsync`, `AddAsync`, `AddMensagemAsync`).
- Migration `AddAtendimentoConversaMensagem` (índices: `(EmpresaId, ChatExternoId)` único; `(EmpresaId, ClienteId, UltimaMensagemEm)`).
**Fora.** Anexos binários; leitura/entrega de mensagens.
**Aceite.**
- [ ] `Conversa.Assumir()` em conversa `Encerrada` lança `RegraDeDominioVioladaException`.
- [ ] Filtro de tenant ativo: `Conversa` de outra empresa não aparece em consulta (teste de integração com dois tenants).
- [ ] Migration sobe e desce limpa em Postgres real.
**Testes (Red).** `ConversaTests.AssumirEmEncerradaLanca`, `ConversaTests.RegistrarMensagemAtualizaUltimaMensagemEm`, `ConversaRepositoryTests.IsolamentoDeTenant` (IntegrationTests).
**Rollback.** Migration `Down`.
**Depende de.** Nada.
**Leitura mínima.** `Domain/Entities/Storefront/PedidoAvaliacao.cs` (padrão de entidade com factory e invariantes); `Postgre/Data/Configurations/Storefront/PedidoAvaliacaoConfiguration.cs` (ou equivalente na mesma pasta); `Postgre/Data/EasyStockDbContext.cs` (só a região dos `DbSet` de Storefront e o bloco `HasQueryFilter`, ~linha 522); `EasyStock.ArchitectureTests/EfCoreHasFilterValidationTests.cs`.
**Tamanho.** M. **Tier.** alto (migration).

---

### S05 · Identificar cliente ou lead pelo telefone

**Problema.** RN-02 e RN-03: cliente com telefone cadastrado é saudado pelo nome; lead é quem ainda não comprou. A saudação e o cardápio precisam sair em até 5 s (RN-01).
**Abordagem.** No `startedChat`: normalizar `contact.mobile` para E.164; procurar `Cliente` por `IClienteRepository.FindByTelefoneAsync` e por `TelefoneHash` (SHA-256 do E.164, já usado pelo OTP); não achou → `Cliente.Criar(empresaId, nome do contato)` com telefone e `HuggyContatoId`; `OrderCount == 0` define lead. Guardar `HuggyContatoId` no cliente. A saudação inicial (com cardápio) é enviada **antes** do agente, pelo próprio handler, com texto de `ConfiguracaoAtendimento` (S08): garante os 5 s sem depender do LLM.
**Escopo.**
- Adicionar `Cliente.HuggyContatoId` (string?, índice) e método `VincularHuggy(contatoId)`; migration `AddHuggyContatoIdCliente`.
- Criar `App/UseCases/Atendimento/IdentificarClientePorTelefoneUseCase.cs` (retorna `{ Cliente, EhLead, EhNovo }`).
- Criar `App/Services/Atendimento/NormalizadorTelefone.cs` (E.164 BR; reusar o que o OTP já faz em `App/UseCases/Storefront/Auth/SolicitarOtpUseCase.cs` sem duplicar: extrair para o serviço e apontar o OTP para ele).
- No `startedChat` (S03): identificar, criar/vincular, gravar `Conversa.ClienteId`, enviar saudação (retorno ou primeira vez) + link do cardápio (`Storefront.Slug`) + frase de espera, e enfileirar o agente.
- Cliente bloqueado (S24): não enviar saudação automática; atribuir ao departamento humano com mensagem interna "cliente bloqueado" e marcar `Conversa.Situacao = Assumida`. Até S24 existir, o campo não existe: deixar `TODO(S24)` com issue.
**Fora.** Merge de cadastros; detecção de duplicidade por nome.
**Aceite.**
- [ ] Telefone conhecido → saudação usa o nome e a conversa aponta para o `ClienteId` existente.
- [ ] Telefone desconhecido → cria `Cliente` com `OrderCount=0`, telefone e `HuggyContatoId`; saudação de primeiro contato.
- [ ] Tempo entre `startedChat` recebido e `EnviarTextoAsync` chamado < 5 s com stub (medido no teste).
**Testes (Red).** `IdentificarClientePorTelefoneUseCaseTests.ConhecidoRetornaExistente`, `...DesconhecidoCriaLead`, `ProcessarEventoHuggyUseCaseTests.StartedChatEnviaSaudacaoAntesDoAgente`.
**Rollback.** Migration `Down`; handler volta a só registrar a conversa.
**Depende de.** S02, S03, S04.
**Leitura mínima.** `Domain/Entities/Cliente.cs` (linhas 1-140); `App/Ports/Output/Persistence/IClienteRepository.cs`; `App/UseCases/Storefront/Auth/SolicitarOtpUseCase.cs` (só a normalização e o hash); `App/UseCases/CriarCliente/*.cs`.
**Tamanho.** M. **Tier.** alto (migration).

---

### S06 · Agente de atendimento (LLM com ferramentas)

**Problema.** US-003 e D3: interpretar linguagem livre, sem menu numérico; responder com base no estado real do pedido; nunca prometer prazo menor que o preparo (RN-06); não afirmar hábito (RN-07); nota interna nunca sai (RN-08).
**Abordagem.** Um serviço de turno: recebe `ConversaId`, monta o contexto (últimas 20 mensagens, cliente, pedido em andamento, `ContextoJson`, configuração de tom), chama a API Messages da Anthropic com `tools`, executa as ferramentas contra a Application (nunca contra o banco direto), repete até `stop_reason != "tool_use"` (máximo 6 iterações), grava a resposta como `Mensagem(Saida, Agente)` e envia pela Huggy. Modelo por configuração (`Anthropic:ModeloAgente`, padrão `claude-sonnet-5`). Uso registrado em `UsoIa`.
**Ferramentas (nome, entrada → saída).**
| Ferramenta | Faz | Reusa |
|---|---|---|
| `consultar_cardapio` | itens visíveis e disponíveis com preço, porções, linha, tempo de preparo | `ListarCardapioPublicoUseCase` |
| `enviar_cardapio_imagem` | envia a imagem do cardápio (`Storefront.CardapioImagemUrl`, campo novo) ou o link `/menu/imprimir` | `IHuggyClient.EnviarArquivoAsync` |
| `validar_endereco(rua, numero, complemento, bairro, cep)` | normaliza, checa área, devolve taxa e tempo | S14 |
| `confirmar_endereco(...)` | grava `ClienteEndereco` padrão | `AdicionarClienteEndereco` |
| `listar_janelas(data?)` | janelas com vaga respeitando antecedência | S16 |
| `criar_pedido(itens[], janela_id, data_entrega, observacao?)` | pedido + cobrança Pix; devolve resumo, total, copia-e-cola e QR | S10, S11 |
| `consultar_pedido(pedido_id?)` | status e previsão do último pedido | `ObterPedidoDetalhes` |
| `registrar_restricao(tag)` / `registrar_interesse(item)` / `registrar_nota(texto)` | CRM | S24, S31 |
| `escalar_para_dona(motivo)` | departamento humano + `Conversa.Assumir()` | S07 |
| `encerrar_conversa()` | fecha o chat | `IHuggyClient.EncerrarChatAsync` |
**Escopo.**
- Criar `Integrations/Ia/AnthropicMessagesClient.cs` (`POST /v1/messages`, headers `x-api-key` e `anthropic-version: 2023-06-01`, corpo `{ model, max_tokens, system, messages, tools }`, resposta com blocos `text` e `tool_use`; a continuação envia `tool_result` com `tool_use_id`). Copiar o padrão HTTP de `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs`; não mover o arquivo antigo.
- Criar `App/Services/Atendimento/AgenteAtendimentoService.cs`, `App/Services/Atendimento/Ferramentas/*.cs` (uma classe por ferramenta implementando `IFerramentaAgente { Nome, Descricao, SchemaJson, ExecutarAsync }`), `App/Services/Atendimento/PromptAtendimento.cs` (system prompt com as regras RN-01 a RN-08 e o tom de S08).
- Criar `App/UseCases/Atendimento/ProcessarTurnoAgenteUseCase.cs` (chamado pela fila de S03). Guarda: `Conversa.Situacao != Automatica` → não responde.
- Registrar `UsoIa` por turno (tokens de entrada/saída).
**Fora.** Voz; imagens enviadas pelo cliente (responder que só lê texto por enquanto); classificador de sentimento (S27); memória longa além do dossiê.
**Aceite.**
- [ ] Pergunta fora de intenção mapeada ("quanto tempo falta?") gera resposta em texto natural com base em `consultar_pedido` (teste com LLM fake que devolve `tool_use` e depois texto).
- [ ] Prompt contém as 8 regras e o tom; teste de snapshot do system prompt.
- [ ] `Conversa.Assumida` → turno não chama o LLM nem envia nada.
- [ ] Limite de 6 iterações de ferramenta; ao estourar, envia mensagem de espera e escala.
- [ ] Nota interna do cliente nunca entra no contexto enviado ao LLM como texto reproduzível (o dossiê entra resumido, com as notas marcadas como `[interno]` e a regra proíbe citar).
**Testes (Red).** `AgenteAtendimentoServiceTests.ExecutaFerramentaEResponde`, `...NaoRespondeQuandoAssumida`, `...LimiteDeIteracoesEscala`, `PromptAtendimentoTests.ContemRegrasRN01aRN08`, `AnthropicMessagesClientTests.MontaCorpoComTools`.
**Rollback.** Desligar `Anthropic:Enabled`; o webhook continua persistindo mensagens e o handoff manual funciona.
**Depende de.** S02, S04, S05, S08; ferramentas de pedido dependem da onda 2 (entregar o agente primeiro só com cardápio, endereço, escalar e encerrar; ligar as demais quando S10/S11/S14/S16 existirem).
**Leitura mínima.** `Infra.Postgre/Services/GeradorAutoPreenchimentoClaude.cs`; `App/UseCases/Storefront/Menu/ListarCardapioPublicoUseCase.cs`; `App/UseCases/ObterPedidoDetalhes/*.cs`; `Domain/Entities/UsoIa.cs`; `App/Ports/Output/Ai/IGeradorAutoPreenchimento.cs` (só para o padrão de porta).
**Tamanho.** G. **Tier.** alto.

---

### S07 · Handoff: a dona assume e devolve

**Problema.** US-004, RN-04, RN-05, US-013: qualquer mensagem da dona suspende o automático daquela conversa; a retomada é decisão dela; avisos transacionais continuam.
**Abordagem.** Mapear estados da Huggy para `Conversa.Situacao`: `agentEntered` ou `sentAllMessage` (autor agente humano) → `Assumida`; `closedChat` → `Encerrada` (a próxima mensagem abre chat novo em `Automatica`). Escalada pelo agente (`escalar_para_dona`) = `PUT /chats/{id}/department` para `departamento_humano_id` + `Assumir()`. Notificação em todos os dispositivos é da Huggy (fila do departamento). Avisos de status (S13) saem pelo outbox e não consultam `Situacao`.
**Escopo.**
- Completar os ramos `agentEntered`, `sentAllMessage`, `closedChat` em `ProcessarEventoHuggyUseCase`.
- Criar ferramenta `EscalarParaDonaFerramenta` (S06) e `App/UseCases/Atendimento/EscalarConversaUseCase.cs` (motivo gravado em `Mensagem(Sistema)`).
- Endpoint `POST api/atendimento/conversas/{id}/liberar-automatico` (policy `Operador`) para a dona devolver ao agente sem encerrar o chat; grava `Mensagem(Sistema, "automático liberado por <usuário>")`.
**Fora.** Transferência para agente específico; horário de atendimento.
**Aceite.**
- [ ] `sentAllMessage` com autor humano → `Situacao=Assumida`; `receivedMessage` seguinte não aciona o agente.
- [ ] `closedChat` → `Encerrada`; `startedChat` do mesmo contato cria conversa nova `Automatica`.
- [ ] `escalar_para_dona` chama `AtribuirDepartamentoAsync` com o id configurado e grava o motivo.
- [ ] Aviso de status enfileirado no outbox é enviado mesmo com a conversa `Assumida` (teste em S13 referencia este aceite).
**Testes (Red).** `ProcessarEventoHuggyUseCaseTests.SentAllMessageHumanoAssume`, `...ClosedChatEncerra`, `EscalarConversaUseCaseTests.AtribuiDepartamentoEGravaMotivo`.
**Rollback.** Remover ramos e endpoint.
**Depende de.** S03, S04, S06.
**Leitura mínima.** `App/UseCases/Atendimento/Webhook/ProcessarEventoHuggyUseCase.cs` (S03); `Domain/Entities/Atendimento/Conversa.cs` (S04).
**Tamanho.** P. **Tier.** alto.

---

### S08 · Configuração do atendimento por empresa

**Problema.** US-005: limite de autonomia e tom configuráveis; saudações; mensagem fora de área; imagem do cardápio.
**Abordagem.** Entidade pequena, uma por empresa, lida pelo agente e pelo handler de saudação.
**Escopo.**
- Criar `Domain/Entities/Atendimento/ConfiguracaoAtendimento.cs`: `EmpresaId` (PK), `Tom` (texto livre curto, ex.: "acolhedor, direto, sem gíria"), `NivelSugestao` (`Discreto` | `Ativo`), `SaudacaoPrimeiroContato`, `SaudacaoRetorno` (com `{nome}`), `FraseEspera`, `MensagemForaArea`, `RespiroMinutos` (padrão 40), `Ativo`. Migration `AddConfiguracaoAtendimento`.
- Adicionar `Storefront.CardapioImagemUrl` (string?) + método; migration na mesma fatia.
- `GET|PUT api/atendimento/configuracao` (policy `Admin`).
- Seed padrão para a Casa da Baba em `Api/Data/` (mesmo mecanismo de `NotificacoesGlobaisSeed.cs`), com textos tirados do doc 03 (RN-01, US-002).
**Fora.** Versionamento de prompt; A/B.
**Aceite.**
- [ ] `PUT` valida `RespiroMinutos >= 0` e `NivelSugestao` válido; `GET` de empresa sem registro devolve o padrão (não 404).
- [ ] `PromptAtendimento` (S06) muda quando `Tom` e `NivelSugestao` mudam (teste de snapshot com dois valores).
**Testes (Red).** `ConfiguracaoAtendimentoTests.PadraoValido`, `AtendimentoConfiguracaoControllerTests.PutValida`, `PromptAtendimentoTests.RefleteTomENivel`.
**Rollback.** Migration `Down`.
**Depende de.** Nada (S05 e S06 consomem).
**Leitura mínima.** `Domain/Entities/ConfiguracaoLoja.cs` (padrão); `Domain/Entities/Storefront/Storefront.cs` (linhas 41-110); `Api/Data/NotificacoesGlobaisSeed.cs` (só a assinatura e o registro no startup).
**Tamanho.** P. **Tier.** alto (migration).

---

### S09 · Provider `whatsapp:huggy` no módulo de notificações

**Problema.** O número de WhatsApp passa a viver na Huggy. O outbox de notificações precisa entregar por ela; os providers Meta e Twilio ficam mortos.
**Abordagem.** `HuggyWhatsAppProvider : IProvedorWhatsApp` (chave `whatsapp:huggy`). `MensagemPronta.Destinatario` é o telefone E.164 → resolve `Cliente.HuggyContatoId` (S05) → chat aberto do contato → `EnviarTextoAsync`; sem chat aberto → mensagem ativa conforme o spike de S02 (`AbrirChatComContatoAsync` ou `EnviarTemplateAsync` quando `MensagemPronta` trouxer `TemplateId` em metadados). Configuração `Notifications:WhatsApp:Provider=huggy`.
**Escopo.**
- Criar `Notifications/WhatsApp/HuggyWhatsAppProvider.cs` e registrar em `NotificationsInfraServiceCollectionExtensions.cs` (keyed `whatsapp:huggy`).
- Estender `MensagemPronta` com `IReadOnlyDictionary<string,string>? Metadados` (opcional, aditivo) para `template_id` e `params`. R8: atualizar todos os construtores de `MensagemPronta` no mesmo commit (`git grep "new MensagemPronta"`).
- Remover nada ainda: Meta e Twilio saem em P06.
**Fora.** Campanhas (S30) e avaliação (S26) só usam este provider.
**Aceite.**
- [ ] Com chat aberto, envia texto no chat; sem chat, usa o caminho do spike; sem `HuggyContatoId`, devolve `ResultadoEnvio(Sucesso=false, ErroDetalhado="contato_nao_vinculado")` e o outbox registra falha sem retry infinito (respeita `MaxTentativas`).
- [ ] `LogEnvioNotificacao.Provider == "huggy"` no envio bem-sucedido.
**Testes (Red).** `HuggyWhatsAppProviderTests.EnviaEmChatAberto`, `...SemContatoFalhaSemLancar`, `MensagemProntaTests.MetadadosOpcionais`.
**Rollback.** `Notifications:WhatsApp:Provider=stub`.
**Depende de.** S02, S05.
**Leitura mínima.** `App/Ports/Output/Notifications/IProvedorWhatsApp.cs`; `App/Ports/Output/Notifications/ICanalNotificacao.cs`; `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs` (padrão); `Notifications/DependencyInjection/NotificationsInfraServiceCollectionExtensions.cs` (linhas 40-60).
**Tamanho.** P. **Tier.** alto.
