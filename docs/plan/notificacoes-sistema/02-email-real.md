# M1 · E-mail real pelo motor (N3)

Marco M1 do plano de notificações de plataforma · issue #1344 · [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md) ·
linhas medidas em `c2eb85ca` (.NET 10) em 2026-10-01; as specs anteriores da ordem movem código, então a PR confere
pelo nome do método. Convenções do executor: [README do plano](README.md#regras-que-valem-para-todas-as-specs) e
[README do atendimento](../atendimento-whatsapp/README.md#convenções-do-executor-vinculantes-resumo-do-claudemd-v40).

Legenda da coluna **Prova**: **código** = lido na linha citada; **medido** = contagem ou execução de script
descartável; **inferência** = deduzido do protocolo ou do código, não reproduzido. `ServiceCollectionExtensions.cs`
sem pasta é o de `EasyStock.Infra.Async/DependencyInjection/`.

## Ordem

```
N2 (Simulado, desfecho tipado, uma só camada de retentativa, categoria Seguranca) ─► N1 (Worker é o único host) ─► N3 ─► N13 ─► N5
```

---

## N3 · MailKit e uma só fábrica de e-mail para API e Worker · issue aberta ao iniciar

**Problema.** O serviço de e-mail atual não aguenta o que o plano pede: duas caixas (`seguranca@` e `avisos@` em
`easystok.online`), porta 465 ou 587, envio pelo motor e remetente próprio. Hoje, com o mesmo `.env`, a API e o
Worker se comportam de forma oposta.

| # | Achado | Prova |
|---|---|---|
| 1 | O serviço usa `System.Net.Mail.SmtpClient`, que só fala TLS explícito (`EnableSsl`). Não há modo implícito, então a 465 não funciona: o servidor espera o handshake TLS e o cliente espera o `220` em claro | **código** `SmtpEmailService.cs:3,24-30`; **inferência** do protocolo SMTPS |
| 2 | Um único `SmtpClient` guardado num singleton. O `SmtpClient` não admite dois envios ao mesmo tempo, e o serviço atende a API e o Dispatcher | **código** `SmtpEmailService.cs:14,24`; `Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs:54`; `Worker/Program.cs:72` |
| 3 | O `Timeout` nunca é ajustado (padrão de 100 s do `SmtpClient`) e não há `CancellationToken`: `SendMailAsync(mailMessage)` vai sem token, e a porta não tem token | **código** `SmtpEmailService.cs:100`; `IAsyncInfrastructure.cs:54-67` |
| 4 | No HEAD há retentativa em três camadas (serviço, Polly do canal e outbox) e o 550 é tratado como transitório. A N2 remove o laço do serviço e o Polly do canal e tira o 550 da lista de transitórios **antes** desta spec. A N3 reescreve o serviço sobre MailKit e precisa manter isso: uma tentativa por chamada, 5xx permanente | **código** `SmtpEmailService.cs:17,45-58,103-108`; `SmtpEmailCanal.cs:18-32,66-70`; `OutboxMensagemNotificacao.cs:22`; N2 em [01-verdade-do-motor.md](01-verdade-do-motor.md) (achados 4 e 5) |
| 5 | Duas fábricas com regras diferentes. A da API escolhe por `Email:Provider` (smtp, sendgrid, console); a do Worker só olha se a seção `Smtp` existe. O `appsettings.json` da API traz `Smtp:Host=smtp.gmail.com` com credencial vazia, então lá a seção sempre existe e a API nunca cai no console; o Worker não tem seção `Smtp` e cai no console | **código** `ServiceCollectionExtensions.cs:35-68`; `Worker/Program.cs:69-84`; `EasyStock.Api/appsettings.json:110-118`; `EasyStock.Worker/appsettings.json:12-19` |
| 6 | Remetente padrão `noreply@easystock.com`, domínio que não é do produto (`easystok.online`), no SMTP e no SendGrid | **código** `ServiceCollectionExtensions.cs:48,59`; `Worker/Program.cs:77` |
| 7 | A porta não conhece remetente: reset de senha, confirmação de cadastro e conta criada pelo admin, que carregam credencial, sairiam da mesma caixa do relatório de diagnóstico | **código** `EsqueciSenhaUseCase.cs:84`; `CadastrarUsuarioUseCase.cs:63`; `CriarTenantPorAdminUseCase.cs:164`; `DiagnosticoEmailReportJob.cs:103` |
| 8 | O canal grava `ProviderUsado = "smtp"` fixo, mesmo quando o serviço injetado é o `ConsoleEmailService`, que devolve sucesso sem enviar. A N2 resolve com uma interface marcadora no canal; a N3, que troca a chamada por `EnviarAsync`, passa o desfecho direto e a marcadora fica sem uso | **código** `SmtpEmailCanal.cs:54-57`; `ServiceCollectionExtensions.cs:201-229`; N2 (achado 2 e abordagem) |
| 9 | `ConsoleEmailService` é contrato por nome: o diagnóstico decide "SMTP configurado" comparando `GetType().Name` | **código** `DiagnosticoController.cs:233-237`; `DiagnosticoEmailReportJob.cs:60` |
| 10 | Não há MailKit no repositório nem captador de e-mail no compose de dev | **medido** `grep` por MailKit, MimeKit, mailpit, mailhog e smtp4dev em `*.cs`, `*.csproj` e `docker-compose*.yml`: zero ocorrências |
| 11 | A CI já tem o que o teste de integração precisa: Docker no runner, `Testcontainers.PostgreSql` no projeto e um canário (`HarnessCanaryTests`) que usa `EASYSTOCK_IT_PG` como sinal de "estou na CI" | **código** `.github/workflows/ci.yml:74`; `EasyStock.Infra.Postgre.IntegrationTests.csproj:16`; `HarnessCanaryTests.cs:19-31`; `EasyStok.CI.slnf` lista o projeto |

**Abordagem.**
- **Pacote.** `MailKit` em `EasyStock.Infra.Async.csproj`, última 4.x estável na execução. O build roda com
  `-warnaserror`, então `dotnet list package --vulnerable` precisa sair limpo. O `SendGrid` fica como está.
- **Porta.** `IEmailService` ganha `EnviarAsync(MensagemEmail, CancellationToken)`, que devolve o `ResultadoEnvio`
  da N2 (`Desfecho`: `Enviado`, `Simulado`, `FalhaTransitoria` ou `FalhaPermanente`, mais o provider e um detalhe
  curto). `MensagemEmail` leva destinatário, assunto, corpo, HTML ou texto, anexos e `RemetenteEmail` (`Avisos`, o
  padrão, ou `Seguranca`). Os quatro métodos antigos continuam e passam a delegar para `EnviarAsync` com `Avisos`
  (lançam quando falha, como hoje), então `CanalEmail` e o relatório de diagnóstico não mudam.
- **`SmtpEmailService`**: mesmo nome e arquivo, reescrito sobre MailKit (o provider `smtp` do catálogo de canais
  continua verdadeiro).
  - Uma conexão por envio (`ConnectAsync`, `AuthenticateAsync` quando há `Username`, `SendAsync`,
    `DisconnectAsync`), sem estado compartilhado: o singleton passa a ser seguro.
  - Teto de `Smtp:TimeoutSegundos` (padrão 20) por envio, aplicado em `client.Timeout` e num `CancelAfter` de um
    token encadeado ao do chamador. Estourar o teto é falha transitória; cancelar o token do chamador propaga
    `OperationCanceledException`.
  - **Uma tentativa por chamada**, sem retentativa interna (a N2 já deixou o outbox como única camada).
  - Segurança por porta: 465 usa TLS implícito (`SslOnConnect`); 587 e qualquer outra porta usam `StartTls`
    obrigatório. Nunca `Auto` nem `StartTlsWhenAvailable`, que aceitam rebaixar para texto puro. `Smtp:Modo`
    permite forçar; `Nenhum` só vale fora de Production.
  - Classificação da resposta, por protocolo: 5xx do servidor (550, 551, 552, 553, 554) é falha permanente; 4xx
    (421, 450, 451, 452), erro de rede, de protocolo e teto estourado são transitórios; autenticação recusada
    (530, 535) é permanente e vira erro de configuração no log, nomeando `Smtp:*` e nunca a senha.
  - Mensagem MIME com `From` da categoria, `Auto-Submitted: auto-generated` e `Message-ID` no domínio do remetente.
  - Log sem endereço de e-mail (LGPD, a mesma regra de `SmtpEmailCanal.cs:49`): só `OutboxId`, categoria,
    duração e código SMTP. O `ProtocolLogger` do MailKit nunca liga fora de teste, porque imprime as credenciais
    do AUTH.
- **Configuração única** (seção `Smtp`, lida por `IOptions<SmtpOpcoes>`; os nomes legados valem, então o `.env`
  da Onda 0 não muda):

| Chave | Efeito | Padrão |
|---|---|---|
| `Smtp:Host`, `Smtp:Port` | servidor | sem padrão (some o `smtp.gmail.com`) |
| `Smtp:Modo` | `Auto`, `SslImplicito`, `StartTls` ou `Nenhum` | `Auto`: 465 implícita, o resto `StartTls` |
| `Smtp:TimeoutSegundos` | teto por envio | 20 |
| `Smtp:Username`, `Smtp:Password`, `Smtp:FromEmail`, `Smtp:FromName` | remetente `Avisos` | sem padrão |
| `Smtp:Seguranca:Username`, `:Password`, `:FromEmail`, `:FromName` | remetente `Seguranca`; cada caixa autentica com a própria credencial | cai no `Avisos`, com aviso na subida |
| `Smtp:EnableSsl` (legado) | só lido quando `Modo` não existe; `false` equivale a `Nenhum` | `true` |

  Trocar por SES ou outro provedor é só mudar `Host`, `Port` e credenciais no `.env`, porque o protocolo é SMTP.
- **Fábrica única.** `AddEasyStockEmail(IConfiguration)` em
  `Infra.Async/DependencyInjection/EmailServiceCollectionExtensions.cs`. Escolha do provider: `Email:Provider`
  explícito (`smtp`, `sendgrid`, `console`); sem ele, `smtp` quando houver `Host` e um remetente utilizável
  (`FromEmail`, ou `Username` com `@`), senão `console` com aviso nomeando a chave que falta. Nenhum remetente é
  inventado. `AddEasyStockAsyncInfrastructure` e `EasyStock.Worker/Program.cs` (no lugar das linhas 68 a 84)
  chamam a mesma extensão. `ConsoleEmailService` mantém o nome e passa a devolver `Simulado` com provider
  `console` no `EnviarAsync` (a fábrica `ResultadoEnvio.Simulado` da N2). `Smtp:Modo=Nenhum` com
  `ASPNETCORE_ENVIRONMENT=Production` recusa subir e nomeia a chave; porta e timeout inválidos também nomeiam a
  chave (hoje o `int.Parse` cru derruba a subida com `FormatException` sem dizer qual).
- **Canal.** `SmtpEmailCanal` usa `EnviarAsync` com o `ct` recebido e a categoria (`Seguranca` vira
  `RemetenteEmail.Seguranca`, o resto `Avisos`), repassa o desfecho e o provider do resultado e deixa de classificar
  exceção. A interface marcadora que a N2 pôs no console sai se ficar sem uso.
- **Fluxos de auth.** Reset de senha, confirmação de cadastro e conta criada pelo admin passam a chamar
  `EnviarAsync` com `Seguranca`, uma linha por call-site; falha vira log sem endereço. Mocks e fakes de
  `IEmailService` acompanham no mesmo commit (R8): `AsyncInfrastructureTests.cs:170`,
  `LogsDeEnvioSemDadoPessoalTests.cs:75-77`, `EsqueciSenhaUseCaseTests.cs:19,94-141`, `CanaisEmailSmsTests.cs:57,69,81`.
- **Dev.** Serviço `mailpit` (tag fixa, nunca `latest`) no `docker-compose.local.yml`, interface em
  `http://localhost:8025`, e no serviço `api` as variáveis `Smtp__Host=mailpit`, `Smtp__Port=1025` e
  `Smtp__Modo=Nenhum`. O cabeçalho do arquivo ganha a linha do Mailpit. O bloco `Smtp` de
  `EasyStock.Api/appsettings.json:110-118` perde `smtp.gmail.com` e `noreply@easystock.com`.
- **Integração.** `MailpitFixture` com o `ContainerBuilder` do Testcontainers (já transitivo pelo pacote do
  Postgres), portas 1025 e 8025, leitura pela API HTTP do Mailpit (`GET /api/v1/messages`,
  `GET /api/v1/message/{id}`, `DELETE /api/v1/messages` entre os testes). Fica em
  `EasyStock.Infra.Postgre.IntegrationTests/Email/`, que está na CI. **Canário anti-skip** no molde do
  `HarnessCanaryTests`, com `EASYSTOCK_IT_PG` como sinal de CI (o mesmo Docker serve aos dois), para o `ci.yml`
  não mudar.
- **TLS no Mailpit.** O Mailpit aceita certificado próprio (`MP_SMTP_TLS_CERT`, `MP_SMTP_TLS_KEY`) e
  `MP_SMTP_REQUIRE_TLS` para TLS implícito. Os casos 465 e 587 contra ele usam certificado gerado no teste e um
  validador injetável `internal`, visível só ao projeto de teste (`InternalsVisibleTo`). Se isso passar de uma
  hora de trabalho, a cobertura de 465 e 587 fica no teste de resolução do modo mais o smoke de produção do
  Felipe, e a decisão vai para a issue.
- **Fatias (commits verdes).** (1) porta, serviço, classificação e unitários; (2) fábrica única, `Program.cs`,
  canal, auth e appsettings; (3) Mailpit, integração e canário.

**Leitura mínima.** `EasyStock.Infra.Async/SmtpEmailService.cs`;
`EasyStock.Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs` (linhas 33 a 68 e 196 a 229);
`EasyStock.Infra.Async/EasyStock.Infra.Async.csproj`;
`EasyStock.Application/Ports/Output/IAsyncInfrastructure.cs` (linhas 50 a 70);
`EasyStock.Application/Ports/Output/Notifications/ICanalNotificacao.cs` (já com o `Desfecho` da N2);
`EasyStock.Infra.Notifications/Email/SmtpEmailCanal.cs`; `EasyStock.Worker/Program.cs` (linhas 60 a 90);
`EasyStock.Api/appsettings.json` (110 a 118); `docker-compose.local.yml` (56 a 100);
`EasyStock.Infra.Postgre.IntegrationTests/PostgreSqlDatabaseFixture.cs` e `HarnessCanaryTests.cs`;
`EasyStock.ArchitectureTests/RepoPaths.cs`; os testes de SMTP que a N2 deixa (abaixo).

**Testes Red.**
- **Migrar, sem duplicar.** Os testes de SMTP que a N2 deixa
  (`EasyStock.Api.UnitTests/Notifications/ClassificacaoDeFalhaTests.cs::Smtp_550_e_falha_permanente` e
  `::Smtp_421_e_transitoria_e_chama_o_servico_uma_vez`, e
  `EasyStock.Infra.Async.UnitTests/Email/SmtpEmailServiceTests.cs::Smtp_550_e_tentado_uma_vez`, com o SMTP falso
  em loopback) passam a exercitar `EnviarAsync` e o MailKit. Viram os testes abaixo, e o servidor falso da N2 é
  reaproveitado.
- `EasyStock.Infra.Async.UnitTests/Email/SmtpOpcoesTests.cs`
  - `::Porta465ResolveSslImplicito`, `::Porta587ResolveStartTls`, `::PortaPersonalizadaSemModoExigeStartTls`:
    não compilam hoje, porque `SmtpOpcoes` não existe; o comportamento atual é `EnableSsl` fixo
    (`SmtpEmailService.cs:27`).
  - `::ModoNenhumEmProductionRecusaSubir`: hoje nada valida a combinação (`Program.cs:72-79`).
  - `::SemFromEmailNaoInventaDominio`: hoje o padrão é `noreply@easystock.com` (`ServiceCollectionExtensions.cs:59`).
  - `::RemetenteSegurancaUsaCredencialPropriaECaiNoAvisos` e `::ChavesLegadasContinuamValendo`.
  - `::PortaInvalidaNomeiaAChave`: hoje `int.Parse` lança `FormatException` sem a chave (`ServiceCollectionExtensions.cs:56`).
- `EasyStock.Infra.Async.UnitTests/Email/ClassificadorFalhaSmtpTests.cs`
  - `::Codigos550A554SaoPermanentes`, `::Codigos421A452SaoTransitorios`,
    `::AutenticacaoRecusadaEPermanenteDeConfiguracao`, `::RedeETimeoutSaoTransitorios`: o classificador atual
    olha `SmtpException` do `System.Net.Mail`, que deixa de existir.
- `EasyStock.Infra.Async.UnitTests/Email/SmtpEmailServiceTests.cs` (estende o arquivo da N2)
  - `::ServidorMudoEstouraNoTetoConfigurado`: hoje o teto é o de 100 s do `SmtpClient` e não há token.
  - `::CancelarOTokenInterrompeOEnvio`: hoje não há como cancelar (`SmtpEmailService.cs:100`).
  - `::DezEnviosConcorrentesChegamTodos`: hoje o `SmtpClient` compartilhado recusa envio simultâneo.
  - `::Rcpt550GeraFalhaPermanenteComUmaUnicaConexao` e `::Resposta421GeraFalhaTransitoria`: as versões MailKit
    dos testes da N2.
- `EasyStock.Infra.Integrations.UnitTests/Notifications/SmtpEmailCanalTests.cs`
  - `::RepassaDesfechoEProviderDoServico` (inclui `Simulado` com provider `console`),
    `::SegurancaUsaRemetenteDeSeguranca`, `::RepassaOTokenDeCancelamento`: hoje o provider é fixo
    (`SmtpEmailCanal.cs:54-57`) e o token é descartado (`:39-46`).
- `EasyStock.Application.Tests/UseCases/EsqueciSenhaUseCaseTests.cs::EnviaPeloRemetenteDeSeguranca`, e o
  equivalente no teste de `CadastrarUsuario` e de `CriarTenantPorAdmin`: hoje o `SendAsync` não tem remetente
  (`EsqueciSenhaUseCase.cs:84`).
- `EasyStock.ArchitectureTests/EmailFabricaUnicaTests.cs::ApiEWorkerRegistramOEmailPelaMesmaExtensao`: hoje há
  duas fábricas (`ServiceCollectionExtensions.cs:54` e `Worker/Program.cs:72`); falha também se sobrar
  `new SmtpEmailService(` fora de teste. Detector novo: a prova red-bar vai na PR (ADR-0023, item 5), e ele entra
  como `Category=Architecture` porque a violação some na mesma PR.
- `EasyStock.Infra.Postgre.IntegrationTests/Email/MailpitSmtpIntegrationTests.cs`
  - `::HtmlChegaComRemetenteDeAvisos`, `::SegurancaChegaDeSegurancaComAutoSubmitted`, `::AnexoChegaIntacto`,
    `::CanalDoOutboxEntregaComCategoriaCerta` (monta `MensagemPronta` com `Categoria=Seguranca` e passa pelo
    `SmtpEmailCanal` real): hoje não há Mailpit nem MailKit.
- `EasyStock.Infra.Postgre.IntegrationTests/Email/MailpitCanaryTests.cs::Mailpit_sobe_de_verdade_quando_ci`:
  fica vermelho na CI se o container não subir, e pula visível fora dela (`[SkippableFact]` com `Skip.If`,
  ADR-0023). Envia uma mensagem e a lê de volta, para provar que não é só o container de pé.

**Aceite.**
- [ ] Dado o Mailpit no ar, quando o canal envia uma `MensagemPronta` de categoria `Seguranca`, então a mensagem
  chega com `From` de `seguranca@`, e a de categoria `Operacional` chega de `avisos@`, as duas em HTML íntegro e
  com `Auto-Submitted: auto-generated`.
- [ ] Dado um servidor que não responde e `TimeoutSegundos=2`, quando o envio roda, então volta falha
  transitória em até 4 s (hoje seriam 100 s).
- [ ] Dado o token cancelado no meio do envio, então sobe `OperationCanceledException` e nenhuma conexão fica
  aberta.
- [ ] Dados 10 envios simultâneos, então os 10 chegam.
- [ ] Dado um destinatário recusado com 550, então o desfecho é `FalhaPermanente` e há exatamente uma conexão no
  servidor, sem retentativa no serviço nem no canal.
- [ ] Dado porta 465, então TLS implícito; porta 587, `StartTls` obrigatório; `Modo=Nenhum` em Production não
  sobe e nomeia `Smtp:Modo`.
- [ ] Dada a mesma configuração, quando API e Worker sobem, então resolvem o mesmo provider e as mesmas opções
  (o teste de arquitetura garante um só lugar de construção).
- [ ] Dado `FromEmail` ausente, então o provider cai no console com aviso e nenhum `noreply@easystock.com`
  sobra no código.
- [ ] Dado o provider `console`, então o `EnviarAsync` devolve `Simulado` com provider `console`, e o diagnóstico
  ainda detecta "SMTP não configurado" pelo nome da classe.
- [ ] Dados reset de senha, confirmação de cadastro e conta criada pelo admin, então saem pelo remetente de
  segurança.
- [ ] Dada a CI sem Docker, então o canário fica vermelho (nunca verde-vazio).
- [ ] `dotnet build -c Release -warnaserror` sem aviso de vulnerabilidade dos pacotes novos.
- [ ] **Validação do Felipe (depende da Onda 0):** um disparo real chega de `seguranca@easystok.online` com
  `dkim=pass` no cabeçalho, e o `.env` com os nomes legados (`Smtp__Host`, `Smtp__Port`, `Smtp__Username`,
  `Smtp__Password`, `Smtp__FromEmail`) continua válido sem mudança.

**Fora.**
- Pool de conexões: o volume de plataforma é pequeno. Só vale pensar nisso acima de uns 5 envios por segundo.
- Assinatura DKIM no aplicativo: quem assina é o servidor do provedor (Onda 0).
- Webhook de bounce e lista de supressão por endereço.
- Cota por caixa (1.000 por dia): a N10 reduz o risco com a dedupe; o contador não entra aqui.
- Remover o SendGrid e o `SendTemplateAsync` (stub morto), e renomear `ConsoleEmailService`.
- A regra de `Simulado` no Dispatcher, o health de "stub em Production" e o purge de `Seguranca`: são da N2 e da
  N1. Aqui o serviço só devolve o desfecho certo.
- Mover o reset para o motor (N8) e remetente por loja: o `CanalEmail` do atendimento segue no remetente
  `Avisos` e na mesma cota, até existir remetente por loja. O risco de reputação (campanha por e-mail ao cliente
  na caixa de avisos de plataforma) fica registrado na issue.

**Depende de.** N2 (categoria `Seguranca`, desfecho tipado com `Simulado`, uma só camada de retentativa e o
servidor SMTP falso em loopback) e N1 (Worker como único host do motor). A Onda 0 (caixas, DNS, `.env`) só é
pré-requisito do smoke de produção; a CI não precisa dela.

**Tamanho.** G, fatiada em 3 commits verdes.

**Tier.** ALTO. Toca `EasyStock.Worker/Program.cs` (arquivo de entrada, R5 do `CLAUDE.md`), passa de 5 arquivos e
de 100 LoC e mexe em três use cases de autenticação (reset, confirmação de cadastro e conta criada), que a
ADR-0055 mantém em ALTO; a ADR-0057 (item 11) manda valer a regra mais restritiva. A PR fica aberta até a label
`aprovado`.

**Rollback.** `git revert` do squash. As chaves `Smtp__*` do `.env` da Onda 0 usam os nomes legados e valem nos
dois sentidos, então o rollback não mexe no `.env`. Sem migração e sem dado novo.

---

## Rollback do marco

M1 é uma única PR (N3). Reverter o squash devolve o `SmtpClient`, as duas fábricas e o padrão
`noreply@easystock.com`; o e-mail volta a depender dos `Smtp__*` legados no `.env`. Não há nada em banco nem em
fila: o que já está `Enviado` fica, e o que está `Pendente` segue para o serviço antigo. O Mailpit sai junto do
compose local e o canário sai com os testes.
