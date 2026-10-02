# M4 · WhatsApp de plataforma (N6)

Plano: [README](README.md) · Decisão: [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md) · Issue-mãe: #1344 · Data: 2026-10-01
Base medida: master `c2eb85ca`, .NET 10. Regras comuns das specs: [README](README.md#regras-que-valem-para-todas-as-specs).

Legenda da coluna **Prova**: `caminho:linha` lido nesta data; **doc Meta** = página oficial lida em 01/10/2026 (URLs na
Leitura mínima).

## Veredito

**O ÚNICO PROVIDER DA META FOI FEITO PARA A LOJA E NÃO SERVE AO SISTEMA.** Ele conversa com a `Conversa` do atendimento, só
tem um remetente, usa a Graph v19.0 (vencida), não sabe mandar código de verificação e, quando a rede falha, manda a mesma
mensagem paga duas vezes. A N6 cria o remetente de **Plataforma**: o 2º número da mesma WABA, só template, nunca toca a
`Conversa`, com o status fechado pelo webhook.

```
Evento ─► NotificadorService: Remetente = f(tipo do evento) ─► outbox.Remetente
Dispatcher ─► MensagemPronta.ProviderOverride ─► WhatsAppCanal
  Loja (sem override) ─► MetaCloudWhatsAppProvider ─► número da empresa ─► grava Conversa            (como hoje)
  Plataforma          ─► MetaCloudWhatsAppPlataformaProvider ─► número fixo da plataforma ─► NUNCA grava Conversa
Meta ─► POST api/webhooks/whatsapp-plataforma   mensagens e status do 2º número (override por número)
Meta ─► POST api/webhooks/whatsapp              template_category_update (a Meta não deixa sobrescrever)
```

---

## N6 · WhatsApp de plataforma: 2º número, só template, sem Conversa · issue aberta ao iniciar

**Problema.**

| # | Achado | Prova |
|---|---|---|
| 1 | O provider da Meta depende do atendimento: `ResolvedorCanal` do Atendimento, `IConversaRepository` e `ITenantContextAccessor` no construtor. Consulta a conversa aberta e grava a saída como `Mensagem` | `MetaCloudWhatsAppProvider.cs:28-33,61,129-137,160-167` |
| 2 | Um remetente por canal: `WhatsAppCanal` injeta um provider só (`whatsapp:active`). `MensagemPronta.ProviderOverride` existe, mas ninguém escreve nem lê (a propriedade é do record `MensagemPronta`, não da interface; o nome aparece 1 vez no código) | `WhatsAppCanal.cs:9-20`; `ICanalNotificacao.cs:13`; `NotificacoesDispatcherOrchestrator.cs:125-131`; `NotificationsInfraServiceCollectionExtensions.cs:64-67` |
| 3 | O número sai da empresa do tenant. Com tenant e sem número a chamada falha, e o global só vale sem tenant. Não há como mandar por um número explícito | `WhatsAppCloudClient.cs:203-222`; `RemetenteWhatsAppDoTenant.cs:17-34` |
| 4 | A Graph v19.0 saiu do ar em 21/05/2026 e a v20.0 em 24/09/2026. A Meta encaminha a chamada para a versão mais antiga que resta (hoje v21.0, até 21/01/2027). A versão mora em 3 lugares que divergem: `BaseUrl` ×2 e `ApiVersion`, que só enfeita a tela de status. O S35 já usa v25.0 | `WhatsAppCloudOptions.cs:12`; `WhatsAppProviderOptions.cs:14,23`; `IntegracoesWhatsAppController.cs:35`; `MetaMensageriaOptions.cs:14`; **doc Meta** (versões e versionamento) |
| 5 | O envio de template só monta cabeçalho de imagem, corpo e resposta rápida. Falta o botão URL, que é o formato do copy code do template de autenticação: o código vai no corpo **e** no botão `sub_type: url`, índice `"0"`, com no máximo 15 caracteres | `WhatsAppCloudClient.cs:93-144`; **doc Meta** (copy code) |
| 6 | Timeout vira falha transitória e o outbox reenvia. O cliente já não repete o POST (#1292), mas o `catch` do provider devolve `permanente: false` para toda exceção que não seja `OperationCanceledException`, e o `TaskCanceledException` de um timeout do `HttpClient` escapa desse filtro e deixa a mensagem `Pendente`. Nos dois casos o outbox reenvia (1, 5 e 30 min), e a Meta pode ter aceitado a primeira. O dispatcher também nunca grava `EmEnvio` antes de chamar (`MarcarEmEnvio` não tem chamador). Um teste fixa o comportamento errado. **Dono do conserto: N2** (desfecho `Indeterminado`) **e N1** (`EmEnvio`); a N6 aplica o mesmo desfecho ao provider de plataforma | `MetaCloudWhatsAppProvider.cs:92-96`; `IntegrationsServiceCollectionExtensions.cs:58-70` (timeout de 30 s); `NotificacoesDispatcherOrchestrator.cs:134,156-163`; `OutboxMensagemNotificacao.cs:106,123-129`; `MetaCloudWhatsAppProviderTests.cs:171-181` |
| 7 | Só 3 códigos de erro da Meta são permanentes. `132001` (template não existe), `132000` (parâmetros), `131050` e `131042` (pagamento), entre outros, são reenviados 3 vezes | `WhatsAppCloudClient.cs:27`; **doc Meta** (códigos de erro) |
| 8 | O `wamid` volta do envio e se perde: `ResultadoEnvio` não tem id externo, o outbox não tem coluna, e o provider só o copia para a `Conversa`. **Dono: N2** (`IdExterno` no resultado) **e N1** (coluna `ProviderMensagemId`); a N6 os usa no webhook | `ICanalNotificacao.cs:23-30`; `OutboxMensagemNotificacaoConfiguration.cs:9-34`; `MetaCloudWhatsAppProvider.cs:76-80` |
| 9 | O webhook só conhece número de empresa: número sem empresa vira `empresa_desconhecida` com 200, e o status procura o `wamid` só dentro de `Conversa`. A Meta **não** deixa sobrescrever webhook de template: `template_category_update` chega sempre ao callback do app, e o controller entrega o corpo inteiro ao atendimento sem olhar o `field` | `ProcessarEventoWhatsAppUseCase.cs:69-89,276-292`; `WebhookWhatsAppParser.cs:20-26`; `WebhookWhatsAppController.cs:15,58`; **doc Meta** (override) |
| 10 | Consentimento: `Operacional` sem registro é permitido e `Transacional` ignora o opt-in. A política da Meta exige opt-in antes de mensagem iniciada pela empresa | `Services/Notifications/ResolvedorCanal.cs:66-85`; **doc Meta** (opt-in) |
| 11 | Nada protege a fronteira: `Infra.Notifications.csproj` só referencia Domain e Application, e o acoplamento está em namespaces (`Application.Services.Atendimento`), que o `csproj` não enxerga | `EasyStock.Infra.Notifications.csproj:37-38`; `MetaCloudWhatsAppProvider.cs:2-8` |
| 12 | O Admin pode vincular o número de plataforma a uma empresa (o índice só barra dono duplicado), e aí o webhook do atendimento criaria `Conversa` para ele | `VincularWhatsAppDoTenantUseCase.cs:38-40`; `EmpresaConfiguration.cs:19-21` |

**Abordagem.**

1. **Remetente por mensagem.** `OrigemRemetente { Loja = 1, Plataforma = 2 }` (Domain) e `RemetentePorTipoEvento.De(tipo)`.
   **Loja** só quando quem recebe é cliente final (`PedidoPagoConfirmado` a `PedidoEntregue`, `AvaliacaoSolicitada`,
   `ReembolsoEfetuado`, `Campanha*`: valores 38 a 45). **Plataforma** para os demais, cujo destinatário é usuário interno (a
   sessão confere cada valor ao classificar), e para os tipos novos da N13. Sem valor padrão: um teste falha quando entra valor
   novo no enum sem classificação. O remetente é propriedade do tipo, não de tela: nenhum Admin troca. O `NotificadorService`
   grava `OutboxMensagemNotificacao.Remetente` ao enfileirar, e o dispatcher copia para `MensagemPronta.ProviderOverride`
   (`"plataforma"` ou nulo).
   - **Plataforma nunca cai no número da loja.** `WhatsAppCanal` escolhe o provider pela chave do override. Chave sem provider,
     ou provider desligado, é falha permanente `provider_nao_configurado`.
2. **Provider de plataforma.** `MetaCloudWhatsAppPlataformaProvider` em `Infra.Notifications/WhatsApp/Plataforma/`, chave
   `whatsapp:plataforma` (stub quando `Notifications:WhatsApp:Plataforma:Provider` não é `meta`). Depende só de
   `IClienteWhatsAppPlataforma`, de `ITemplateMetaEstadoRepository` e de log. Só template: sem `Metadados["template"]`, falha permanente
   `template_obrigatorio`. `Categoria == Marketing` é recusada (`marketing_nao_sai_pela_plataforma`): a plataforma só usa
   autenticação e utilidade, porque a escalada da Meta por uso indevido de categoria (aviso, teto de volume de utilidade e, por
   fim, todos os templates de utilidade da WABA viram marketing) vale para a **WABA inteira** e alcançaria os templates do
   atendimento (**doc Meta**, categorização). Código de verificação só em template de autenticação: a Meta proíbe OTP em
   utilidade e proíbe URL em autenticação.
3. **Cliente com remetente explícito.** Porta `IClienteWhatsAppPlataforma` (Application, namespace de Notifications, não de
   Atendimento): enviar template e enviar texto (só a resposta automática, dentro da janela de serviço). A implementação é a
   mesma classe `WhatsAppCloudClient`: mesmo `HttpClient` (o token é o da WABA), mesmo pipeline sem retry, mesma leitura de
   erro, em métodos que **não** usam `IRemetenteWhatsApp`. O `phone_number_id` vem de
   `Notifications:WhatsApp:Plataforma:PhoneNumberId`. O JSON do template sai de um ponto só (`MetaTemplatePayload`),
   compartilhado com o envio da loja, e ganha o botão URL (`botaoUrl0` e `botaoUrl1` nos `Metadados`, valor do parâmetro) e o
   `biz_opaque_callback_data`. Copy code: o mesmo código no corpo e no botão `url`, índice `"0"`; com mais de 15 caracteres
   falha antes da rede.
4. **No máximo 1 vez, no WhatsApp de plataforma.** O desfecho é o da N2 (`Desfecho` tipado e `IdExterno`), e o cliente de
   plataforma o devolve sem lançar exceção: aceito (com `wamid`), recusado (código e classe) ou `Indeterminado`. Sem resposta
   (timeout, queda depois do envio) e 5xx são `Indeterminado`, como na N2: a mudança do provider da loja é dela
   (`MetaCloudWhatsAppProviderTests.cs::Timeout_de_http_vira_Indeterminado`), e a N6 só aplica a mesma regra ao provider novo. A
   N6 refina duas coisas. (a) Só é transitório o que prova que nada saiu: `HttpRequestError` de resolução de nome, de conexão
   ou de TLS, e disjuntor aberto, em vez de tratar toda queda como `Indeterminado` (perder mensagem que nunca saiu é custo
   sem ganho). (b) Resposta 4xx com código da Meta se classifica pela tabela abaixo, que **substitui** `CodigosPermanentes`
   (`WhatsAppCloudClient.cs:27`) e vale também para o provider da loja: o `EhPermanente` da exceção sai da mesma tabela.

   | Classe | Códigos (**doc Meta**) | Resultado no motor |
   |---|---|---|
   | Permanente | 100, 131008, 131009, 131021, 131026, 131047, 131049, 131050, 131051, 132000, 132001, 132005, 132007, 132012, 132015, 132016, 132018 | `Falhado`, sem retentativa |
   | Permanente com alerta de operação | 0, 3, 10, 190, 131005 (token e permissão); 131042 (pagamento); 131031, 368, 130497 (conta restrita); 131037, 131045, 133010 (número); 131048 (restrição de volume) | `Falhado`, contador `notifications.whatsapp.plataforma.alerta{codigo}` e log de erro sem telefone; o health do motor (N1) acusa |
   | Transitório | 130429, 80007, 4, 131056, 131016, 133004, 131057, 131000, 135000; código fora da tabela | reagenda pelo recuo do dispatcher, até `MaxTentativas` |
   | Indeterminado | sem resposta | `Indeterminado`, nunca reenvia sozinho |

5. **Wamid e fechamento.** Migração aditiva da N6: `notif_outbox_mensagens.Remetente varchar(20) NOT NULL DEFAULT 'Loja'` e a
   tabela `notif_templates_meta_estado (Nome, Idioma, CategoriaAtual, AtualizadoEm)` com chave `(Nome, Idioma)`, sem
   `EmpresaId` (a WABA é uma só). A coluna do id do provider **não é daqui**: a N1 cria `ProviderMensagemId` (varchar(128),
   índice parcial) e o dispatcher grava nela o `IdExterno` que a N2 devolve. O envio de plataforma leva
   `biz_opaque_callback_data = "{EmpresaId:N}.{OutboxId:N}"` (só GUIDs, sem dado pessoal, limite da Meta de 512 caracteres).
   Com isso o status acha a linha **sem bypass de RLS** e sem depender do `wamid`, que não existe no `Indeterminado`; o
   webhook grava o `wamid` em `ProviderMensagemId` quando a linha ainda não o tem. O índice da N1 serve ao suporte e a um
   fallback por `wamid` sob a porta de bypass, que só entra se a métrica `notifications.whatsapp.plataforma.status_sem_opaco`
   mostrar que a Meta deixou de devolver o opaco.
6. **Webhook de plataforma.** `GET` e `POST api/webhooks/whatsapp-plataforma`: `[AllowAnonymous]`, política `webhook-meta`,
   assinatura `X-Hub-Signature-256` com o `AppSecret` do app (a mesma `AssinaturaWebhookMeta`) e **verify token próprio**
   (`Notifications:WhatsApp:Plataforma:VerifyToken`). Use cases em `Application/UseCases/Notifications/Plataforma/`:
   - `statuses[]` do `phone_number_id` da plataforma (outro número é ignorado e logado): acha a linha pelo opaco, fixa o
     tenant dessa empresa e aplica a transição **monotônica** `EmEnvio` ou `Indeterminado` para `Enviado` (em `sent`,
     `delivered` ou `read`, gravando `ProviderMensagemId`) e `Enviado` ou `Indeterminado` para `Falhado` (em `failed`, com o
     código e a classe da tabela, sem telefone no log). Status sem opaco é ignorado e contado. Status repetido ou fora de
     ordem não regride: a Meta reenvia por até 7 dias e o `delivered` pode faltar quando a leitura é imediata. Uma linha de
     categoria `Seguranca` já foi purgada pela N2 ao ficar `Indeterminado`, e o webhook só muda o status, nunca devolve o
     segredo.
   - Cobrança: `pricing.category == "marketing"` num template de plataforma soma
     `notifications.whatsapp.plataforma.cobrado_como_marketing` e loga o nome do template.
   - `messages[]` (alguém respondeu ao número): uma resposta automática por remetente a cada 24 h ("Este número só envia
     avisos do EasyStok e não é monitorado"), texto livre dentro da janela de serviço, controlada por `ICacheService` com o
     hash do número. Não grava `Conversa`, `Mensagem` nem `webhook_recebido`, e não responde `reaction`, `system` nem
     `unsupported`.
   - 200 quando processou ou descartou de propósito; 503 só em falha transitória, para a Meta reenviar.
   - **Override por número**, na Meta, depois do deploy: `POST /{PHONE_NUMBER_ID}` com
     `{"webhook_configuration":{"override_callback_uri":"https://<API_HOST>/api/webhooks/whatsapp-plataforma","verify_token":"<segredo>"}}`
     (URL de até 200 caracteres; o `verify_token` é o do GET de verificação, então o endpoint já precisa estar no ar antes do
     POST: **inferência** do fluxo padrão de webhooks da Meta, a conferir na primeira configuração). Conferir com
     `GET /{PHONE_NUMBER_ID}?fields=webhook_configuration`.
7. **`template_category_update`** (a Meta não deixa sobrescrever webhook de template). `WebhookWhatsAppController.Receber` lê
   os `field` do corpo (`CamposWebhookMeta`): `template_category_update` vai ao `ProcessarCategoriaTemplateWhatsAppUseCase`,
   `messages` vai ao atendimento como hoje, e outro `field` devolve 200 com contagem e **sem** `empresa_desconhecida`. O
   parser do atendimento não muda. Categoria `MARKETING`, **no aviso** (`correct_category`, 24 h antes) ou **consumada**
   (`new_category`), grava o template em `notif_templates_meta_estado`, e todo envio seguinte com ele é falha permanente
   `template_recategorizado_marketing`, sem chamar a Meta (a Meta mantém o template `APPROVED` e passa a cobrar como
   marketing). O idioma do webhook vem com hífen (`pt-BR`) e o do envio com sublinhado (`pt_BR`): comparar normalizado.
   Template de autenticação não entra nessa recategorização; o inverso (utilidade ou marketing usado como código) a Meta rejeita
   no 1º dia do mês seguinte. O tratamento vale para qualquer categoria.
8. **Opt-in.** A regra principal é da N4: o `IResolvedorAudiencia` só entrega WhatsApp a telefone verificado **e** com opt-in
   explícito em `ConsentimentoNotificacao` (categoria da rotina), gravado na verificação para `Seguranca` e `Operacional`. A N6
   fecha o que a audiência não cobre: `ResolvedorCanal.ResolverCanaisPermitidos` (o das notificações) ganha o remetente como
   parâmetro opcional, com padrão `Loja`, sobre o resolvedor que a N5 entrega (os 7 testes de hoje seguem valendo, e o
   `NotificadorService`, única chamada de produção, passa o remetente do tipo). A N2 trata `Seguranca` como `Transacional`: ignora opt-out, certo para e-mail. Para WhatsApp **de plataforma** a N6 desfaz isso, porque a política da
   Meta exige opt-in antes da mensagem: o canal só sai para usuário identificado (`usuarioDestinoId`) com
   `ConsentimentoNotificacao(WhatsApp, categoria, OptIn = true)`, inclusive em `Seguranca`. Telefone solto no payload, sem
   usuário, ou sem registro: o canal é pulado e o e-mail segue (no modo `todos` da N5). Marketing nunca sai pela plataforma.
   Loja: igual a hoje.
9. **Uma versão da Graph.** `Notifications:WhatsApp:Meta:ApiVersion`, padrão `v26.0` (a mais recente em 01/10/2026, lançada
   em 29/07/2026, validade ainda sem data). `BaseUrl` vira derivada (`https://graph.facebook.com/{ApiVersion}`), com override
   só para a homologação, e as duas options leem o mesmo padrão. A `v25.0` (S35, vale até 29/07/2028) é o plano B, trocado só
   por configuração. Messenger e Instagram (`MetaMensageriaOptions`) ficam como estão.
10. **Fronteira testada.** `PlataformaNaoDependeDoAtendimentoTests` (NetArchTest, molde de `ArchitectureTests.cs`): os tipos
    de `Infra.Notifications.WhatsApp.Plataforma` e de `Application.UseCases.Notifications.Plataforma` não dependem dos 6
    namespaces `*.Atendimento` (`Ports.Output.Atendimento`, `Services.Atendimento`, `UseCases.Atendimento`,
    `Ports.Output.Persistence.Atendimento`, `Domain.Entities.Atendimento`, `Domain.Enums.Atendimento`). Canário: o conjunto
    varrido tem ao menos 5 tipos, senão o teste passa sem provar nada (molde: a "sanidade do varredor" de
    `NotaInternaNaoVazaParaStorefront`, `ArchitectureTests.cs:173-222`). Guarda de vínculo: `VincularWhatsAppDoTenantUseCase`
    recusa o `phone_number_id` da plataforma (`NumeroReservadoDaPlataforma`, 409), lido de `IConfiguration`.
11. **Homologação.** `scripts/homologacao/fake_meta.py` passa a: (a) deduzir o `phone_number_id` do caminho e mandar os status
    do número de plataforma a `api/webhooks/whatsapp-plataforma`, devolvendo o `biz_opaque_callback_data`; (b) validar o JSON
    do template de autenticação (mesmo código no corpo e no botão `url` índice `"0"`, até 15 caracteres) e responder `132000`
    quando não bater; (c) tratar o telefone final `7777` como "aceita e demora mais que o timeout", e mandar o `sent` depois;
    (d) injetar `template_category_update` no callback do app. `compose.homologacao.yml` ganha as chaves da plataforma e a
    mesma versão da Graph (linha 26).
12. **Validação no startup** nas duas hospedagens (API e Worker), no molde de `StartupHardening.cs:80-112` e do S37: com
    `Plataforma:Provider=meta`, falta de `PhoneNumberId` (só dígitos, até 32) ou de `VerifyToken` derruba o startup nomeando a
    chave, e a plataforma passa a contar como "usa a Meta" para exigir token e `AppSecret`.

**Leitura mínima.**
- Provider e canal: `Infra.Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs`, `WhatsAppCanal.cs`,
  `DependencyInjection/NotificationsInfraServiceCollectionExtensions.cs`, `Options/WhatsAppProviderOptions.cs`;
  `Application/Ports/Output/Notifications/ICanalNotificacao.cs`.
- Dispatcher e motor: `Infra.Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs:101-182`;
  `Application/Services/Notifications/NotificadorService.cs:117-291` e `ResolvedorCanal.cs`; `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs`
  e a configuração EF dele.
- Cliente: `Infra.Integrations/WhatsApp/WhatsAppCloudClient.cs`, `WhatsAppCloudOptions.cs`,
  `DependencyInjection/WhatsAppCloudClientServiceCollectionExtensions.cs` e `IntegrationsServiceCollectionExtensions.cs:58-70`.
- Webhook: `Api/Controllers/Webhooks/WebhookWhatsAppController.cs`, `AssinaturaWebhookMeta.cs`;
  `Application/UseCases/Atendimento/Webhook/ProcessarEventoWhatsAppUseCase.cs:69-99,276-292` e `WebhookWhatsAppParser.cs`.
- Moldes de teste: `Infra.Integrations.UnitTests/WhatsApp/WhatsAppCloudClientTests.cs` (handler fake),
  `Notifications/MetaCloudWhatsAppProviderTests.cs`, `Api.UnitTests/Controllers/Webhooks/WebhookWhatsAppControllerTests.cs`,
  `Infra.Postgre.IntegrationTests/Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs`, `ArchitectureTests/ArchitectureTests.cs:173-222`.
- Homologação: `scripts/homologacao/fake_meta.py`, `compose.homologacao.yml`, `README.md`.
- Fontes Meta (`developers.facebook.com`, lidas em 01/10/2026): `/docs/graph-api/changelog/versions/` e
  `/docs/graph-api/guides/versioning/`; em `/documentation/business-messaging/whatsapp/`: `webhooks/override`,
  `webhooks/reference/messages/status`, `webhooks/reference/template_category_update`,
  `templates/authentication-templates/copy-code-button-authentication-templates`, `templates/time-to-live`,
  `templates/template-categorization`, `support/error-codes`, `getting-opt-in`.

**Testes Red.** Projetos na CI (`EasyStok.CI.slnf`). Integração em `Infra.Postgre.IntegrationTests`, nunca em
`Api.IntegrationTests`.

| Projeto/arquivo::Método | Falha hoje porque |
|---|---|
| `Domain.Tests/Entities/Notifications/RemetentePorTipoEventoTests.cs::TodoValorDoEnumTemRemetenteDeclarado`, `::AvisosAoClienteSaoDaLoja`, `::TiposDeSistemaSaoDaPlataforma` | `RemetentePorTipoEvento` não existe. O primeiro também falha no dia em que entrar valor novo no enum sem classificação |
| `Domain.Tests/Entities/Notifications/OutboxMensagemNotificacaoTests.cs::CriarGuardaORemetente`, `::RemetentePadraoEhLoja` | a entidade não tem `Remetente` |
| `Application.Tests/Services/Notifications/NotificadorServiceRemetenteTests.cs::OutboxNasceComORemetenteDoTipo` | o serviço não grava remetente (`NotificadorService.cs:268-280`) |
| `Application.Tests/Services/Notifications/ResolvedorCanalTests.cs::WhatsAppDePlataformaSemOptInPulaOCanalMesmoEmSeguranca`, `::WhatsAppDePlataformaSemUsuarioEhPulado`, `::EmailDePlataformaSegueIgnorandoOptOutEmSeguranca`, `::WhatsAppDaLojaSegueComoHoje` | `ConsentimentoPermite` libera `Transacional` e `Operacional` sem registro (`ResolvedorCanal.cs:72-82`) e não conhece o remetente |
| `Infra.Integrations.UnitTests/Notifications/WhatsAppCanalTests.cs::OverrideDePlataformaEscolheOProviderDePlataforma`, `::OverrideSemProviderFalhaPermanenteSemCairNoDaLoja` | `WhatsAppCanal` ignora o override (`WhatsAppCanal.cs:9-20`) |
| `Infra.Integrations.UnitTests/WhatsApp/WhatsAppCloudClientPlataformaTests.cs::TemplateDeAutenticacaoLevaOCodigoNoCorpoENoBotaoUrlIndiceZero`, `::BotaoUrlDeConviteLevaSoOSufixo`, `::CodigoComMaisDe15CaracteresNaoChamaARede`, `::UsaOPhoneNumberIdDaPlataformaNuncaODoTenant`, `::EnviaOpacoComEmpresaEOutbox`, `::TimeoutDoPollyViraIndeterminadoComUmaSoChamada`, `::QuedaDepoisDoEnvioViraIndeterminado`, `::Http5xxViraIndeterminado`, `::FalhaDeConexaoAntesDoEnvioEhTransitoria`, `::DisjuntorAbertoEhTransitorio`, `::CodigosDaMetaSaoClassificadosPelaTabela` (Theory), `::CodigoForaDaTabelaEhTransitorio` | não há botão URL nem envio com número explícito (`WhatsAppCloudClient.cs:93-144,203-222`) e só 3 códigos são permanentes (`:27`). Contrato JSON com `HttpMessageHandler` fake |
| `Infra.Integrations.UnitTests/WhatsApp/WhatsAppCloudClientTests.cs::Erro132001EhPermanenteSemRetry`, `::Erro131056NaoEPermanente` (o caminho da loja usa a mesma tabela; molde `Erro131047EhPermanenteSemRetry`, `:197`) | `132001` não está na lista de permanentes (`WhatsAppCloudClient.cs:27`) |
| `Infra.Integrations.UnitTests/WhatsApp/WhatsAppCloudOptionsTests.cs::AsDuasOptionsNascemNaMesmaVersao`, `::BaseUrlDerivaDaVersao`, `::ClienteUsaABaseDaVersaoConfigurada` | versão fixa em 3 lugares (`WhatsAppCloudOptions.cs:12`; `WhatsAppProviderOptions.cs:14,23`) |
| `Infra.Integrations.UnitTests/Notifications/MetaCloudWhatsAppPlataformaProviderTests.cs::SemTemplateFalhaPermanente`, `::MarketingNaoSaiPelaPlataforma`, `::TemplateRecategorizadoNaoChamaAMeta`, `::AceitoDevolveIdExternoEProviderMetaPlataforma`, `::NaoTocaConversaNemTenant` (NSubstitute: `IRemetenteWhatsApp` e `IConversaRepository` nunca chamados) | o provider não existe |
| `Application.Tests/UseCases/Notifications/Plataforma/ProcessarStatusWhatsAppPlataformaUseCaseTests.cs::SentConfirmaOIndeterminadoEGuardaOProviderMensagemId`, `::FailedMarcaFalhadoComCodigoSemTelefoneNoLog`, `::StatusRepetidoOuForaDeOrdemNaoRegride`, `::PhoneNumberIdDeOutroNumeroEhIgnorado`, `::SemOpacoEhIgnoradoSemErro`, `::PrecoComoMarketingSomaContador` | o use case não existe; hoje o status só procura `Conversa` (`ProcessarEventoWhatsAppUseCase.cs:276-292`) |
| `Application.Tests/UseCases/Notifications/Plataforma/ProcessarCategoriaTemplateWhatsAppUseCaseTests.cs::AvisoComCorrectCategoryMarketingBloqueia`, `::NovaCategoriaMarketingBloqueia`, `::UtilityNaoBloqueia`, `::IdiomaComHifenCasaComSublinhado`, `::PayloadSemCamposNaoQuebra` | hoje o evento cai em `empresa_desconhecida` (`WebhookWhatsAppParser.cs:23-26`; `ProcessarEventoWhatsAppUseCase.cs:75-80`) |
| `Application.Tests/UseCases/Notifications/Plataforma/ResponderMensagemRecebidaPlataformaUseCaseTests.cs::RespondeUmaVezPorRemetenteEm24h`, `::NaoRespondeReacaoNemSistema`, `::NaoGravaConversaNemMensagem` | não existe |
| `Api.UnitTests/Controllers/Webhooks/WebhookWhatsAppPlataformaControllerTests.cs::VerificacaoDevolveChallengeComOTokenDaPlataforma`, `::TokenDoAtendimentoNaoVerificaAPlataforma`, `::AssinaturaInvalida403`, `::AssinaturaValidaDevolve200`, `::FalhaTransitoriaDevolve503`, `::UsaRateLimitDaMeta` (molde `WebhookWhatsAppControllerTests.cs:70-126`) | a rota não existe |
| `Api.UnitTests/Controllers/Webhooks/WebhookWhatsAppControllerTests.cs::TemplateCategoryUpdateVaiParaAPlataformaESemEmpresaDesconhecida`, `::MessagesContinuaNoAtendimento`, `::FieldDesconhecidoDevolve200SemRegistro` | `Receber` entrega o corpo inteiro ao atendimento (`WebhookWhatsAppController.cs:58`) |
| `Application.Tests/UseCases/Admin/VincularWhatsAppDoTenantUseCaseTests.cs::NumeroDaPlataformaNaoVinculaAEmpresa` | o use case só confere dono entre empresas (`VincularWhatsAppDoTenantUseCase.cs:38-40`) |
| `Api.UnitTests/Startup/StartupHardeningTests.cs::PlataformaMetaExigePhoneNumberIdEVerifyToken`, `::PlataformaStubNaoExigeNada` | `ValidateWhatsAppMeta` não conhece a plataforma (`StartupHardening.cs:80-112`) |
| `Infra.Postgre.IntegrationTests/Notifications/StatusWhatsAppPlataformaTenantTests.cs::OpacoFixaOTenantEFechaSoALinhaDaEmpresa`, `::OpacoDeOutraEmpresaNaoAtualizaNada`, `::MigracaoAdicionaRemetenteComDefaultLoja`, `::EstadoDeTemplateEhGlobalELegivelSemTenant` (`[SkippableFact]`, papel `rls_test_client`, molde `Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs`, com o canário anti-skip da N1) | colunas e tabela não existem, e sem tenant a RLS zera a leitura do outbox |
| `ArchitectureTests/PlataformaNaoDependeDoAtendimentoTests.cs::TiposDePlataformaNaoDependemDoAtendimento`, `::CanarioVeOsTiposDePlataforma` | teste novo; o canário falha se o varredor não achar ao menos 5 tipos |

**Aceite.**
- [ ] **Remetente.** Dado evento de tipo de plataforma e usuário com opt-in, quando o Dispatcher envia, então o outbox sai
  `Enviado` com `ProviderMensagemId`, o log do envio diz `meta-plataforma`, o fake recebe o POST no número da plataforma e
  nenhuma `Conversa` nem `Mensagem` foi criada (consulta ao banco).
- [ ] **Loja intacta.** Dado aviso S13 ao cliente, então sai pelo número da empresa e grava na `Conversa` como hoje (testes
  atuais do provider da loja verdes).
- [ ] **Sem opt-in.** Dado usuário sem opt-in de WhatsApp, ou telefone solto no payload sem usuário, quando um evento
  `Seguranca` é avaliado, então o WhatsApp é pulado e o e-mail segue (a regra da N5 de ignorar opt-out não vale para o WhatsApp
  de plataforma).
- [ ] **Nunca cai na loja.** Dado override de plataforma sem provider configurado, então falha permanente
  `provider_nao_configurado`, sem chamada à Meta.
- [ ] **Código.** Dado template de autenticação com o código `482913`, então corpo e botão `url` índice `"0"` levam o mesmo
  valor, o fake valida o formato e recusa código com mais de 15 caracteres.
- [ ] **No máximo 1 vez.** Dado timeout depois de a Meta aceitar (telefone final `7777` no fake), então o outbox fica
  `Indeterminado`, o fake registra **1** POST, e o `sent` pelo webhook de plataforma fecha como `Enviado`.
- [ ] **Falha de entrega.** Dado `failed` 131026 pelo webhook, então `Falhado` com o código e sem telefone nos logs.
- [ ] **Recategorização.** Dado `template_category_update` para marketing (aviso e consumada) no callback do app, então o
  template é bloqueado, o envio seguinte falha permanente sem chamar a Meta, e o atendimento não registra
  `empresa_desconhecida`.
- [ ] **Resposta automática.** Dado mensagem enviada ao número da plataforma, então volta 1 resposta "não monitorado" a cada
  24 h e nenhuma `Conversa` é criada.
- [ ] **Reserva do número.** Dado o `phone_number_id` da plataforma, quando o Admin tenta vinculá-lo a uma empresa, então 409.
- [ ] **Versão.** Dada a `ApiVersion` configurada, então toda chamada usa a mesma versão, e `v19.0` não aparece em código de
  produção nem no compose de homologação (`grep` anexado à PR).
- [ ] **Fronteira.** Arch-test verde, com o canário.
- [ ] **Disparo de teste da N13.** Dado `POST api/admin/notificacoes/disparo-teste` de um tipo de plataforma por superadmin
  com telefone verificado e opt-in, então a perna de WhatsApp chega ao fake pelo número de plataforma, e a N13 fecha a perna
  que ela deixou para esta spec.
- [ ] **(Felipe, depois do deploy)** modelos aprovados no WhatsApp Manager: os cinco da N13 (`codigo_redefinir_senha`, com copy
  code, `convite_acesso_link`, `incidente_sistema`, `prazo_estourado` e `resumo_diario`), que a N13 já pede antes desta spec, e os
  que a N8 e a N9 acrescentam (`senha_alterada`), cada um com `message_send_ttl_seconds` (autenticação
  em 600 s; utilidade alinhada à quarentena do tipo na N1); `.env` da API e do Worker com
  `Notifications__WhatsApp__Plataforma__Provider=meta`, `__PhoneNumberId` e `__VerifyToken`; `template_category_update`
  assinado no painel do app; override do número e `GET ...?fields=webhook_configuration` mostrando a URL de plataforma; 1º
  status real chegando com assinatura válida (a página do override não diz qual segredo assina: conferir).

**Fora.**
- SMS e Twilio (ADR-0057, item 10). Messenger e Instagram (`MetaMensageriaOptions`, v25.0).
- Opt-out por palavra (`PARAR`): exigiria achar o usuário por telefone entre tenants (bypass de RLS, ADR-0010). A resposta
  automática orienta desligar em Preferências.
- Botão "Não pedi este código" (`DID_NOT_REQUEST_CODE`, beta na Meta) e one-tap ou zero-tap (exigem app Android).
- Fallback de canal depois de falha informada só pelo webhook: a mensagem fica `Falhado` e visível no health (N1 e N10).
- Teto global de envios do número: a N10 deduplica incidente e a N8 limita por conta.
- Webhooks de conta e de status de template (`phone_number_quality_update`, `account_update`,
  `message_template_status_update`): chegam ao callback do app, e o consumo é do vigia Meta da F16 e da N10.
- Criar e aprovar os templates na Meta: é operação do Felipe, e o catálogo é da N13.
- Verificação do telefone por código do próprio usuário. A N4 a deixou para esta etapa, mas ela precisa do segredo de uso único
  com tentativas que a N8 cria. Hoje a equipe se verifica por `POST api/admin/usuarios/{id}/telefone/verificar` (N4) e o
  convidado por WhatsApp no aceite do convite (N9); a verificação por código vira uma spec seguinte, sem desfazer nada daqui.
- Aviso ao WhatsApp antigo na troca de telefone (N4): o canal passa a existir, e ligá-lo na rotina `contato_alterado_global`
  pede um modelo novo da Meta, numa fatia pequena depois desta. O aviso por e-mail já sai.

**Depende de.**
- N2: `ResultadoEnvio.Desfecho` (`Simulado`, `Indeterminado`) e `IdExterno`, categoria `Seguranca` e o `Indeterminado` do
  provider da loja. N1: o dispatcher grava `EmEnvio` antes de chamar, a coluna `ProviderMensagemId`, a quarentena por TTL, a
  porta de bypass e a allowlist (esta spec não precisa de bypass novo).
- N5 e N13: o `ResolvedorCanal` com `Seguranca`, o modo `todos`, o template por tipo e canal, e os tipos e modelos da Meta do
  catálogo (`codigo_redefinir_senha`, `convite_acesso`, `incidente_sistema` e os outros). A N13 já grava `botaoUrl0` nos `Metadados`; aqui o
  cliente passa a enviá-lo à Meta.
- N4: `Usuario.Telefone`, `TelefoneVerificadoEm`, o `IResolvedorAudiencia` (WhatsApp exige telefone verificado e opt-in
  explícito) e o registro dos opt-ins `Seguranca` e `Operacional` na verificação administrativa.
- Onda 0: 2º número na WABA, forma de pagamento na WABA, segredos no `.env`. ADR-0057.

**Tamanho.** G, fatiado em 4 commits: (1) domínio, migração, remetente, `WhatsAppCanal`, opt-in e guarda de vínculo;
(2) cliente e provider de plataforma, tabela de classificação (também para a loja) e versão da Graph; (3) webhooks
(plataforma, roteamento por `field`, categoria, resposta automática); (4) arch-test com canário, `fake_meta.py` e compose.

**Tier.** ALTO. ADR-0055, item 2: migração EF (coluna `Remetente` e tabela de estado de template), endpoint público novo (webhook
anônimo) e mudança de autorização do canal (opt-in). R5 do `CLAUDE.md`: mais de 5 arquivos e mais de 100 linhas, com migração e
DI. ADR-0057, item 11: na divergência vale a regra mais restritiva. A PR espera a label `aprovado`.

**Rollback.** Do menos para o mais invasivo: (1) `Notifications__WhatsApp__Plataforma__Provider=stub` desliga o envio de
plataforma sem deploy (as mensagens passam a `Simulado`); (2) esvaziar o override do número na Meta
(`override_callback_uri` vazio) devolve o webhook ao app, e a versão da Graph volta por `Notifications__WhatsApp__Meta__ApiVersion`;
(3) revert do squash. **Antes do revert**, suprimir o que ficou na fila
(`UPDATE notif_outbox_mensagens SET "Status" = 'Suprimido' WHERE "Remetente" = 'Plataforma' AND "Status" IN ('Pendente', 'EmEnvio')`),
senão o código antigo mandaria o código de verificação pelo número da loja. A migração é aditiva: o `Down` remove a coluna e a
tabela, e sem `Down` elas ficam ociosas, sem dano. A `ProviderMensagemId` é da N1 e não sai com esta reversão.

---

## Rollback do marco

O M4 é só a N6, então o rollback do marco é o da N6, na ordem acima. O que depende dele cai para o e-mail sem perder função: o
código de redefinição por WhatsApp (N8) e o convite por WhatsApp (N9) deixam de existir, e o e-mail segue sozinho. Desligar o
canal não apaga o catálogo nem os consentimentos já gravados.
