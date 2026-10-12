# M2 · Catálogo: funciona mesmo sem produtor (N13, N5)

Marco M2 do plano de notificações de plataforma · issue #1344 · [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md) ·
linhas medidas em `c2eb85ca` (.NET 10) em 2026-10-01; as specs anteriores da ordem movem código, então a PR confere
pelo nome do método. Convenções do executor:
[README do plano](README.md#regras-que-valem-para-todas-as-specs) e
[README do atendimento](../atendimento-whatsapp/README.md#convenções-do-executor-vinculantes-resumo-do-claudemd-v40).

Legenda da coluna **Prova**: **código** = lido na linha citada; **medido** = contagem ou execução de script
descartável sobre o arquivo; **doc Meta** = documentação da Meta, a reconferir na PR; **inferência** = deduzido, não
reproduzido.

**Leitura do tier.** A ADR-0057 (`docs/adr/0057-notificacoes-de-plataforma.md:68-69`) manda valer a regra mais
restritiva entre a ADR-0055 e a R5 do `CLAUDE.md` (`CLAUDE.md:69-71`: mais de 100 linhas ou de 5 arquivos, migração ou
arquivo de entrada). Pela ADR-0055 sozinha (`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:18-25`), as duas
specs desta página seriam baixo, porque não tocam migração, RLS, autenticação nem policy; pela R5, as duas passam de 5
arquivos e de 100 linhas, então ambas são ALTO e esperam a label `aprovado`. **Corrigido:** o plano marca N13 e N5
como baixo.

## Ordem

```
N3 ─► N13 (dados: tipos, rotinas, templates e disparo de teste) ─► N5 (motor: resolve e entrega o que a N13 cadastrou)
```

A N13 prova sozinha a perna de e-mail do canal primário, que é o caminho que o motor já faz. As pernas
secundárias (segundo canal, fallback, WhatsApp) fecham na N5, na N4 e na N6. O teste de completude da N13 olha
dados (tipo e canal), não o resolvedor, então ela não depende da N5.

---

## N13 · Catálogo versionado e disparo de teste por tipo · issue aberta ao iniciar

**Problema.** O motor aceita eventos de qualquer tipo, mas só tem rotina e template para uma parte deles, o
seed não atualiza o que já foi semeado e não há como provar o caminho sem um produtor.

| # | Achado | Prova |
|---|---|---|
| 1 | Dos cinco tipos pedidos só `ResetSenha` existe. `ConviteAcesso`, `IncidenteSistema`, `PrazoEstourado` e `ResumoDiario` não existem; o último valor em uso é `LembreteVencido = 47` e `AvaliacaoSolicitada = 42` está fora de ordem. O enum vai para o banco como texto (até 40 caracteres), então um nome repetido ou renomeado é que quebra, não o número. A S0 reserva a faixa 48 a 63 para esta spec | **código** `TipoEventoNotificacao.cs:8,72,76`; `EventoNotificacaoConfiguration.cs:12`; S0 em [01-verdade-do-motor.md](01-verdade-do-motor.md) |
| 2 | O seed só insere o que falta, por `Codigo`. Editar assunto, corpo, canais ou categoria de algo já semeado não chega a banco nenhum | **código** `NotificacoesGlobaisSeed.cs:55-66` (templates), `:81-92` (rotinas) |
| 3 | Template criado ou atualizado pela API nasce `Ativo=false` e `Aprovado=false`; `Aprovar` só aprova; `Atualizar` desativa a versão vigente sem ativar a nova. O único `Ativar()` do repositório é o do seed. Logo o catálogo só pode entrar pelo seed | **código** `TemplateNotificacaoCommands.cs:30-35,64-71,97`; `NotificacoesGlobaisSeed.cs:67-68`; `TemplateNotificacao.cs:57-58,75-76` |
| 4 | Nenhum endpoint publica evento: `PublicarEventoNotificacaoUseCase` está registrado e nenhum controller o chama. Sem produtor não há como provar o caminho | **medido** `grep` por `PublicarEventoNotificacaoUseCase` em `EasyStock.Api`: nenhum uso fora do registro de DI e do próprio arquivo |
| 5 | 14 dos 54 pares (rotina, canal) do seed não têm template pelo código da rotina. Dez têm um irmão do mesmo tipo e canal com outro código (ex.: `ticket_criado_global` aponta `ticket_criado_inapp_v1`, e o e-mail é `ticket_criado_email_v1`). Quatro não têm template nenhum: `TicketRespondidoCliente`/Email, `PagamentoConfirmado`/Email, `AlertaEstoqueCritico`/InApp e `ProdutoVencendo`/InApp | **medido** varredura do seed (60 templates, 40 rotinas, 14 multicanais, nenhum par tipo e canal repetido); **código** `NotificacoesGlobaisSeed.cs:268-274,659-661,638-656,710-712,735-737` |
| 6 | Esses quatro tipos não têm produtor vivo (helpdesk removido em #1121, pagamento e estoque crítico sem emissor, produtos vencendo fora do escopo por decisão do Felipe), então escrever template para eles seria trabalho sem uso | **medido** `grep` por `TipoEventoNotificacao.<tipo>` fora do seed e dos testes: só `ColetorProdutosVencendo.cs:79` |
| 7 | O teste do seed só confere que o código da rotina existe, não que cada canal da rotina tem template. E o `EmailTemplateRenderSmokeTests` só pega erro de sintaxe: variável ausente vira vazio no Scriban, então ele não prova payload por tipo | **código** `NotificacoesGlobaisSeedTests.cs::TodaRotinaApontaParaTemplateExistente`; `EmailTemplateRenderSmokeTests.cs::Template_de_email_renderiza_sem_erro_de_sintaxe` |
| 8 | E-mail HTML não pode morar no `.cs` do seed (regra de arquitetura). Mora em `Data/Templates/Email/*.html`, embutido por wildcard no csproj, então arquivo novo não exige editar o csproj da API | **código** `EmailTemplateDiscipline.cs:28-53`; `EasyStock.Api.csproj:76-77`; `EmailTemplateLoader.cs` |
| 9 | O catálogo de variáveis da migração cobre poucos tipos e usa nomes que os templates não usam (`usuario.nome`, `urlConfirmacao` contra `nome`, `link_confirmacao`), então não serve de fonte para o exemplo do teste | **código** `Migrations/20260506221516_AddNotificationsCore.cs:495-537`; `confirmacao_email_email_v1.html` |
| 10 | O template de reset existe no catálogo, mas o fluxo real monta texto puro e envia direto, sem passar por ele (a N8 muda isso). Hoje ele envia a qualquer conta `Ativa`, confirmada ou não | **código** `EsqueciSenhaUseCase.cs:28-29,59-84`; `reset_senha_email_v1.html` |
| 11 | O molde de endpoint de superadmin do repositório exige motivo com no mínimo 10 caracteres e grava auditoria | **código** `AdminTenantsController.cs:10-60`; `RequestGuards.cs:20` |
| 12 | Evento de plataforma precisa de uma empresa (`EventoNotificacao.EmpresaId` é obrigatório). A ADR-0057 (item 7) manda usar a empresa padrão, a mesma de `Auth:Google:EmpresaPadrao` (corrige o plano, que falava numa empresa "Plataforma" semeada). Hoje só o `AuthController` sabe resolvê-la, num helper privado do controller que não serve ao Worker | **código** `EventoNotificacao.cs:9`; `AuthController.cs:109-124`; `GoogleIdTokenValidator.cs:17,29`; `docs/adr/0057-notificacoes-de-plataforma.md:56-57` |

**Abordagem.**
- **Tipos.** Quatro membros novos em `TipoEventoNotificacao` (`ConviteAcesso`, `IncidenteSistema`,
  `PrazoEstourado`, `ResumoDiario`), na faixa 48 a 63 que a S0 reservou para esta spec. `ResetSenha` (4) já existe.
  A N4 (`ContatoAlterado`) e a N8 (`SenhaAlterada`) acrescentam os tipos delas, na faixa livre a partir de 80, pelo
  mesmo seed versionado.
- **Catálogo.** Cada tipo ganha rotina global e um template por canal da rotina:

| Tipo | Rotina global | Categoria | Canais e modo | Audiência (N4) | Template de e-mail | Template de WhatsApp · modelo da Meta |
|---|---|---|---|---|---|---|
| `ResetSenha` (existe) | `reset_senha_global` (existe; passa a `Seguranca`) | `Seguranca` | Email, WhatsApp · `todos` | `usuario` | `reset_senha_email_v1` (existe, sem mudança) | `reset_senha_whatsapp_v1` · `codigo_redefinir_senha` |
| `ConviteAcesso` | `convite_acesso_global` | `Seguranca` | Email, WhatsApp · `todos` | sem `audiencia` até a N9 (o destino vem do payload); a N9 grava `convidado` | `convite_acesso_email_v1` | `convite_acesso_whatsapp_v1` · `convite_acesso_link` (botão URL; a N9 liga o token) |
| `IncidenteSistema` | `incidente_sistema_global` | `Operacional` | Email, WhatsApp · `todos` | `superadmins` | `incidente_sistema_email_v1` | `incidente_sistema_whatsapp_v1` · `incidente_sistema` |
| `PrazoEstourado` | `prazo_estourado_global`, janela de 07:00 a 22:00 | `Operacional` | Email, WhatsApp · `todos` | `gestores` | `prazo_estourado_email_v1` | `prazo_estourado_whatsapp_v1` · `prazo_estourado` |
| `ResumoDiario` | `resumo_diario_global`, **inativa** (molde; liga por empresa na N12) | `Operacional` | Email, WhatsApp · `todos` | `admins` | `resumo_diario_email_v1` | `resumo_diario_whatsapp_v1` · `resumo_diario` |

  A rotina guarda o modo e a audiência em `ParametrosJson` (`{"modoCanais":"todos","audiencia":"admins"}`); a
  leitura é da N5 (modo) e da N4 (audiência), e até lá as chaves são inertes. O nome `modoCanais` evita confundir com
  a chave `canais` do payload, que a N5 entrega. O domínio ganha `RotinaNotificacao.DefinirJanela(inicio, fim)` para o
  catálogo declarar a janela de `prazo_estourado_global`.
- **Contrato de payload.** Chaves fechadas por tipo. Os produtores entregam os valores já formatados em pt-BR; o
  template não formata moeda nem data. Valem para todos a chave de controle `canais` (lista que restringe os canais da
  rotina naquele evento, entregue pela N5) e a chave `usuarioId`, que o motor já lê para consentimento
  (`NotificadorService.cs:70-81`) e que a audiência `usuario` da N4 usa.

| Tipo | Chaves | Observação |
|---|---|---|
| `ResetSenha` | `nome`, `email`, `usuarioId`, `link_redefinicao`, `codigo`, `expira_em_minutos` | link só no e-mail e código só no WhatsApp (N8) |
| `ConviteAcesso` | `nome`, `email`, `usuarioId`, `empresa`, `link_convite`, `expira_em_dias` | a N9 acrescenta `token_convite_whatsapp` e sobe o WhatsApp para v2, com o link no botão |
| `IncidenteSistema` | `componente`, `estado_texto`, `gravidade`, `desde`, `duracao` | só valores de listas fechadas, sem texto livre (N10) |
| `PrazoEstourado` | `tipo_legivel`, `referencia`, `prazo_texto`, `atraso_texto` | sem nome nem telefone de cliente (N11) |
| `ResumoDiario` | `data`, `entregues`, `faturamento`, `ticket_medio`, `pendentes`, `valor_pendentes`, `caixa_texto`, `pix_texto` | strings formatadas (N12) |

- **Exemplos.** `ExemplosDeEvento` (Application/Services/Notifications) é um dicionário tipo para payload de
  exemplo, fonte única do disparo de teste e do teste de renderização. Link de exemplo termina em `#teste` e
  nunca leva token real.
- **Seed versionado.** Cada entrada do catálogo declara `versao` (padrão 1). O seed lê os templates globais por
  `Codigo` e faz:
  1. ausente: insere aprovado e ativo, como hoje;
  2. presente com última linha do sistema (`AtualizadoPor = "system"`) e `Versao` menor que a do catálogo:
     desativa a vigente e insere uma linha nova (a `Versao` do catálogo, aprovada e ativa). O histórico fica, e
     `GetAtivoAsync` já devolve a maior versão ativa (`TemplateNotificacaoRepository.cs:20-24`);
  3. presente e editada por pessoa (`AtualizadoPor` diferente de `system`): não mexe e loga aviso com o código
     (a customização vence o catálogo);
  4. rotinas não têm coluna de versão, e esta spec não cria migração: o seed compara canais, template, categoria,
     janela e `ParametrosJson` com o catálogo e atualiza no lugar quando a rotina é do sistema
     (`AtualizadaPor = "system"`). Rotina mexida por pessoa, inclusive desativada, fica como está, com aviso.
     Rotina nova nasce `Ativa` conforme o catálogo (`resumo_diario_global` nasce inativa);
  5. idempotente: a segunda execução não escreve nada. Roda onde já roda, no startup da API, sob o advisory lock
     e o bypass de RLS (`StartupMigrationsAndSeed.cs:256-260`).
- **Quatro canais sem template saem das rotinas.** `ticket_respondido_cliente_global` perde `Email`,
  `pagamento_confirmado_global` perde `Email`, `alerta_estoque_critico_global` e `produto_vencendo_global`
  perdem `InApp`. Quem religar o produtor escreve o template e devolve o canal; a regra de completude exige.
- **Templates.** HTML em `EasyStock.Api/Data/Templates/Email/{codigo}.html` (nome do arquivo igual ao código),
  com a moldura dos 22 existentes (`reset_senha_email_v1.html`), corpo em 16 px e contraste AA, e o código entra na
  lista do `EmailTemplateRenderSmokeTests` (R8). Assuntos sem travessão, com `:` e `·` (ex.:
  `EasyStok: {{ componente }} {{ estado_texto }}`). Texto de WhatsApp no `.cs`, com `MetadadosJson` no padrão do S13
  (`{"template":"incidente_sistema","idioma":"pt_BR","param1":"{{ componente }}",...}`). O template de autenticação do
  reset leva o código no corpo e no botão de copiar, como a N6 exige:
  `{"template":"codigo_redefinir_senha","idioma":"pt_BR","param1":"{{ codigo }}","botaoUrl0":"{{ codigo }}"}`. A chave
  `botaoUrl0` fica inerte até a N6 passar a lê-la.
- **Quarentena dos tipos novos** (N1, `PoliticaValidadeNotificacao`, sobrescrevível em
  `Notifications:Quarentena:Prazos:<Tipo>`, em minutos): `IncidenteSistema` 2 h, `PrazoEstourado` 6 h,
  `ResumoDiario` 4 h; `ConviteAcesso` fica no grupo "demais" de 24 h, como a N9 espera. O teste da N1 que exige prazo
  para todo tipo passa a cobrir os novos. O prazo do outbox conta da `ProximaTentativaEm`, então o aviso adiado pela
  janela não vence durante a noite.
- **Modelos da Meta, para o Felipe aprovar no WhatsApp Manager.** Nomes e textos propostos (sem variável no
  começo nem no fim, como a Meta exige), com o prazo de envio (`message_send_ttl_seconds`) alinhado à quarentena:

| Modelo | Categoria | Texto | Parâmetros | TTL (s) |
|---|---|---|---|---|
| `incidente_sistema` | UTILITY | EasyStok informa: o componente {{1}} está {{2}} desde {{3}}. Veja o resumo no e-mail enviado agora. | `componente`, `estado_texto`, `desde` | 7200 |
| `prazo_estourado` | UTILITY | Atenção na loja: {{1}} há {{2}} (referência {{3}}). Abra o EasyStok para resolver. | `tipo_legivel`, `atraso_texto`, `referencia` | 21600 |
| `resumo_diario` | UTILITY | Resumo de {{1}} no EasyStok: {{2}} pedidos entregues, faturamento de {{3}} e caixa {{4}}. O resumo completo está no seu e-mail. | `data`, `entregues`, `faturamento`, `caixa_texto` | 14400 |
| `convite_acesso_link` | UTILITY, com botão URL | Olá, {{1}}! Você foi convidado para o EasyStok da empresa {{2}}. Use o botão abaixo para criar sua senha. | `nome`, `empresa`; botão `botaoUrl0` com o token do convite (a N9 gera; o disparo de teste usa um token fictício) | 43200 |
| `codigo_redefinir_senha` | AUTHENTICATION, com botão de copiar código | texto padrão da Meta; {{1}} é o código | `codigo` no corpo e em `botaoUrl0` (até 15 caracteres) | 600 |

  **doc Meta:** o prazo de envio vale de 30 s a 12 h em utilidade e de 30 s a 15 min em autenticação, então os 24 h
  da quarentena do convite não cabem e o teto de 12 h vale. O modelo `senha_alterada` é pedido pela N8. O convite já nasce como
  `convite_acesso_link`, para não aprovar dois modelos na Meta. A verificação do telefone (N4) é administrativa e não usa modelo.
- **Empresa dos eventos de plataforma.** Pela ADR-0057 (item 7), evento sem tenant próprio usa a empresa padrão
  (`Auth:Google:EmpresaPadrao`, CNPJ ou nome exato; tenant único da ADR-0056). Esta spec extrai o helper do
  `AuthController.cs:109-124` para `IEmpresaPadraoResolver` (Application; lê a chave da configuração do host, a mesma
  na API e no Worker, e o `IEmpresaRepository`, que o login anônimo já chama sem tenant; resolve uma vez por processo
  e não guarda falha). O controller passa a usá-lo (R8: `AuthControllerTests`), e a N4, a N8, a N10 e o disparo de
  teste o reaproveitam. Sem a chave, ou sem casamento único, quem precisa da empresa não publica e loga aviso nomeando
  `Auth:Google:EmpresaPadrao`.
- **Disparo de teste por tipo.** `AdminNotificacoesController` (`api/admin/notificacoes`,
  `[Authorize(Policy = "SuperAdmin")]`), controller fino que delega a `DispararTesteNotificacaoUseCase`:
  - `POST api/admin/notificacoes/disparo-teste` com `{ "tipo": "IncidenteSistema", "motivo": "..." }`. Motivo
    validado por `RequestGuards.TryValidarMotivo`, auditoria por `AdminAuditService` sem endereço. Tipo fora do
    catálogo responde 400 com a lista dos válidos;
  - **o destinatário é sempre o contato do próprio superadmin que chamou**, lido de `Usuario` (e-mail
    confirmado), nunca do corpo: o payload leva o `email` e o `usuarioId` dele. O WhatsApp entra quando a N4 trouxer
    telefone verificado e a N6 o canal de plataforma;
  - publica o exemplo do tipo com `teste: true` pelo caminho real (`EnfileirarEventoAsync`), com a rotina, os
    canais e o remetente do catálogo. A empresa é a do token do superadmin (o console exige empresa no token,
    #1326) ou, sem ela, a empresa padrão, com o tenant ligado no escopo, como em
    `NotificarAtrasoPedidoUseCase.cs:29`. Responde 202 com o `eventoId`;
  - `teste: true` faz o motor prefixar o assunto com `[TESTE]` (três linhas em `NotificadorService`; a N5 leva o
    prefixo junto para o construtor);
  - limite de 10 disparos por hora por superadmin num `LimitadorDisparoTeste` em memória (uma instância de API
    hoje; é teto de segurança, não cota contábil);
  - `GET api/admin/notificacoes/disparo-teste/{eventoId}` devolve, por mensagem do outbox, canal, status,
    provider, tentativas e erro curto, para o smoke de produção sem SQL. Resolve a empresa como o `POST`, e a
    categoria `Seguranca` não guarda corpo depois de terminar (N2), então a resposta nunca traz texto.
- **Fatias (commits verdes).** (1) tipos, catálogo, templates, quarentena e testes de completude; (2) seed
  versionado; (3) resolvedor da empresa padrão, endpoint de teste, limitador e prefixo `[TESTE]`.

**Leitura mínima.** `EasyStock.Api/Data/NotificacoesGlobaisSeed.cs` (estrutura de 24 a 100, e as entradas dos
tipos tocados); `EasyStock.Domain/Enums/Notifications/TipoEventoNotificacao.cs`;
`EasyStock.Domain/Entities/Notifications/TemplateNotificacao.cs` e `RotinaNotificacao.cs`;
`EasyStock.Api/Data/EmailTemplateLoader.cs`; `EasyStock.Api/Data/Templates/Email/reset_senha_email_v1.html`;
`EasyStock.Api/Controllers/AdminTenantsController.cs` (1 a 60) e `EasyStock.Api/Http/RequestGuards.cs`;
`EasyStock.Api/Controllers/AuthController.cs` (90 a 125) e
`EasyStock.Infra.Integrations/Auth/GoogleIdTokenValidator.cs` (10 a 30);
`EasyStock.ArchitectureTests/EmailTemplateDiscipline.cs`; `EasyStock.Api/Startup/StartupMigrationsAndSeed.cs`
(250 a 262); `EasyStock.Api.UnitTests/Notifications/NotificacoesGlobaisSeedTests.cs` e
`EmailTemplateRenderSmokeTests.cs`;
`EasyStock.Application/Services/Notifications/NotificadorService.cs` (38 a 81 e 202 a 220); a
`PoliticaValidadeNotificacao` que a N1 entrega; S0 e N1 em [01-verdade-do-motor.md](01-verdade-do-motor.md).

**Testes Red.**
- `EasyStock.Api.UnitTests/Notifications/NotificacoesGlobaisSeedTests.cs` (estende o existente)
  - `::TodoCanalDeCadaRotinaTemTemplateDoMesmoTipo`: hoje 14 pares falham, 4 sem template algum (medido); o
    teste de hoje só confere o código da rotina.
  - `::CincoTiposTemRotinaComCanaisCategoriaEAudienciaDoCatalogo`: hoje quatro tipos não existem.
  - `::TemplatesDosCincoTiposRenderizamComOExemplo`: renderiza assunto, corpo e metadados de cada template com o
    payload de exemplo e confere que nenhuma variável do template (`{{ nome }}`, extraída por regex) ficou sem
    valor. Complementa o smoke de sintaxe, que não pega variável ausente.
  - `::ResumoDiarioEntraInativoNoCatalogo`, `::CanaisSemTemplateSaemDasRotinas` e
    `::TemplateDeAutenticacaoLevaOCodigoNoCorpoENoBotao`.
- `EasyStock.Application.Tests/Services/Notifications/ExemplosDeEventoTests.cs` (novo)
  - `::TodoTipoDoCatalogoTemExemplo` e `::ExemploNaoTemChaveDeClienteNemSegredoReal`.
- `EasyStock.Application.Tests/Services/Notifications/PoliticaValidadeNotificacaoTests.cs` (estende o da N1)
  - `::TiposDoCatalogoTemPrazoProprio`: sem a entrada, os tipos novos cairiam no padrão de 24 h.
- `EasyStock.Application.Tests/Services/EmpresaPadraoResolverTests.cs` (novo)
  - `::AchaPorCnpj`, `::AchaPorNomeExatoQuandoUnico`, `::NomeAmbiguoNaoResolve`, `::SemChaveNaoResolve`: hoje a
    regra é um helper privado do controller (`AuthController.cs:109-124`).
- `EasyStock.Application.Tests/UseCases/Notifications/DispararTesteNotificacaoUseCaseTests.cs` (novo)
  - `::UsaOEmailDoSuperadminQueChamou`, `::PublicaOExemploDoTipoComTesteVerdadeiro`,
    `::RecusaTipoForaDoCatalogo`, `::UsaAEmpresaDoTokenOuAPadrao` e
    `::SemEmpresaResolvidaNaoPublicaENomeiaAChave`: hoje não há o use case.
- `EasyStock.Application.Tests/Services/Notifications/LimitadorDisparoTesteTests.cs` (novo)
  - `::DecimoPrimeiroNaHoraEhRecusado` e `::UmaHoraDepoisLibera` (relógio falso).
- `EasyStock.Application.Tests/Services/Notifications/NotificadorServiceTesteTests.cs` (novo)
  - `::AssuntoDeEventoDeTesteGanhaPrefixo`: hoje o assunto sai como renderizado
    (`NotificadorService.cs:208`).
- `EasyStock.Api.UnitTests/Controllers/AdminNotificacoesControllerTests.cs` (novo)
  - `::AdminComumRecebe403`, `::SemMotivoRecebe400`, `::TipoDesconhecidoRecebe400ComALista`,
    `::DestinatarioNuncaVemDoCorpo` (campo extra `destinatario` no corpo é ignorado): hoje o controller não existe.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/NotificacoesGlobaisSeedIntegrationTests.cs` (novo)
  - `::SeedEmBaseNovaERodarDeNovoNaoMudaNada`,
    `::SeedSubstituiTemplateDoSistemaQuandoOCatalogoSobeDeVersao` (linha nova ativa, anterior inativa,
    `GetAtivoAsync` devolve a nova), `::SeedNaoSobrescreveTemplateEditadoPorPessoa`,
    `::SeedAtualizaRotinaDoSistemaENaoReativaRotinaDesligadaPorPessoa`: hoje só insere por `Codigo`
    (`NotificacoesGlobaisSeed.cs:66,92`).
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/DisparoTestePorTipoIntegrationTests.cs` (novo)
  - `::CadaTipoChegaNoMailpitComORemetenteDoCatalogo`: teoria sobre os cinco tipos, perna de e-mail. Roda o
    Avaliador e o Dispatcher como `rls_test_client`, pelos mesmos pontos de entrada que o Worker usa depois da
    N1. `ResetSenha` e `ConviteAcesso` chegam de `seguranca@`, os demais de `avisos@`, com assunto `[TESTE] ...`.
    Reaproveita o fixture e o canário do Mailpit da N3.

**Aceite.**
- [ ] Dado o catálogo, quando o teste do seed roda, então todo canal de toda rotina global tem template ativo e
  aprovado do mesmo tipo e canal (zero pares faltando; eram 14).
- [ ] Dada uma base com o seed antigo, quando o seed novo roda, então os quatro tipos novos e seus templates
  aparecem, a rotina do reset passa a `Seguranca` com e-mail e WhatsApp, os quatro canais sem template saem das
  rotinas, e rodar de novo não escreve nada.
- [ ] Dado um template do sistema com versão menor que a do catálogo, então entra uma linha nova ativa e a
  anterior fica inativa, com o histórico preservado.
- [ ] Dado um template ou uma rotina mexida por pessoa, então o seed não sobrescreve e loga aviso com o código.
- [ ] Dado `resumo_diario_global`, então nasce inativa; as demais rotinas novas nascem ativas.
- [ ] Dado um superadmin, quando dispara o teste de cada tipo, então responde 202 e a mensagem chega ao e-mail
  dele, de `seguranca@` (`ResetSenha`, `ConviteAcesso`) ou de `avisos@` (os demais), com assunto `[TESTE] ...`.
- [ ] Dado um superadmin sem empresa no token e `Auth:Google:EmpresaPadrao` configurada, então o teste usa a
  empresa padrão; sem a chave, nada é publicado e o erro nomeia a chave.
- [ ] Dado um Admin comum, então 403; sem motivo, 400; tipo fora do catálogo, 400 com a lista; o 11º disparo na
  hora, 429; um campo `destinatario` no corpo não muda o destino.
- [ ] Dado o `GET` do disparo, então mostra canal, status, provider e erro curto de cada mensagem, sem corpo.
- [ ] A regra `EmailTemplateDiscipline` segue verde: o seed continua sem tag HTML e os quatro e-mails novos são
  arquivos embutidos.
- [ ] **Validação do Felipe:** os cinco modelos da tabela aprovados no WhatsApp Manager (`codigo_redefinir_senha`,
  `convite_acesso_link`, `incidente_sistema`, `prazo_estourado`, `resumo_diario`), com o prazo de envio da última
  coluna, antes da N6.

**Fora.**
- Ativar template criado pela API: `Aprovar` não ativa e `Atualizar` desativa a versão vigente sem ativar a
  nova (achado 3 confirmado). O catálogo entra pelo seed, e o uso de override por empresa não foi medido: a S0
  pode contar `notif_templates` com `EmpresaId` não nulo. Vira issue própria.
- Editar template global pelo superadmin (não existe endpoint).
- Envio real pelo WhatsApp e o remetente de plataforma por tipo, que classifica os tipos novos (N6); audiência (N4)
  e a perna multicanal (N5): até lá o teste vai ao e-mail do próprio superadmin.
- Templates para os quatro canais retirados (achados 5 e 6).
- O tipo `RotinaAgendada` que o plano citava: o resumo é `ResumoDiario`, e o motor genérico de agenda (N12) emite
  o tipo que a rotina declarar.
- O catálogo de variáveis da migração (`Migrations/20260506221516_AddNotificationsCore.cs:495-537`):
  desatualizado, sem uso novo.
- Os avisos de plataforma ficam no tenant padrão e aparecem na listagem de envios da dona
  (`NotificacoesConfiguracaoController.cs:202`, `GET api/notificacoes/envios`). Aceitável com tenant único
  (ADR-0056); revisar se entrar um segundo tenant.

**Depende de.** N3 (e-mail real e Mailpit), N2 (categoria `Seguranca`) e N1 (loops lendo, a quarentena por tipo e o
catálogo global legível). A perna WhatsApp do teste fecha com a N6, a multicanal com a N5, e a audiência com a N4.

**Tamanho.** G, fatiada em 3 commits verdes.

**Tier.** ALTO. A ADR-0055 (item 1, `docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:18-20`) sozinha daria
baixo: spec de plano aprovado, sem migração, RLS, autenticação nem policy. A R5 (`CLAUDE.md:69-71`) manda ALTO: mais de
5 arquivos e de 100 linhas (quatro tipos, quatro e-mails, seed, endpoint, resolvedor e testes), e a ADR-0057 (item 11)
manda valer a regra mais restritiva. **Corrigido:** o plano marca N13 como baixo. O endpoint novo usa a policy
`SuperAdmin` que já existe, sem criar nem alterar policy, e só entrega ao e-mail de quem chamou. Atenção: o seed muda
rotinas de produção no startup, por isso a PR traz o SQL de volta.

**Rollback.** `git revert` do squash. O enum guarda o nome como texto: reverter o código com linhas desses tipos
no banco quebra a leitura delas. Por isso o dado vem antes do código: desativar as quatro rotinas novas
(`UPDATE notif_rotinas SET "Ativa" = false WHERE "Codigo" IN (...)`), esperar o motor drenar ou fechar os eventos
dos tipos novos e só então reverter. O seed antigo só insere por `Codigo` e não restaura, então a PR traz também o
SQL que devolve categoria e canais de `reset_senha_global` e os canais das quatro rotinas que perderam canal. As
versões novas de template ficam como histórico.

---

## N5 · Motor: modelo por tipo e canal, rotina da empresa, pausa por empresa, multicanal e janela · issue aberta ao iniciar

**Problema.** O motor foi escrito para um canal só por evento e para o caminho feliz. Os achados abaixo são os
que impedem a N13 de chegar ao destinatário e a N4, N8, N9, N10, N11 e N12 de funcionar.

| # | Achado | Prova |
|---|---|---|
| 1 | O fallback nunca acha o template: o Dispatcher procura o código da rotina no canal seguinte, e o código do seed embute o canal. São os 14 pares da N13 (10 resolvíveis por tipo e canal, 4 sem template) | **código** `NotificacoesDispatcherOrchestrator.cs:216-217`; **medido** varredura do seed |
| 2 | "Código lógico mais canal" não cabe sem migração: para templates de empresa o índice único `(Codigo, EmpresaId, Versao)` impede dois canais com o mesmo código (o `NULL` dos globais o deixa passar, mas o seed e o `GetAtivoAsync` assumem um canal por código). A saída sem migração é resolver por `(TipoEvento, Canal)`, e o índice `(TipoEvento, Canal, Ativo)` já existe (corrige o plano) | **código** `TemplateNotificacaoConfiguration.cs:30-31`; `TemplateNotificacaoRepository.cs:13-25` |
| 3 | O primeiro canal permitido pode não ser o primeiro da lista (bloqueio, canal inativo). O template é buscado pelo código da rotina nesse canal e o evento inteiro vira `Falhado` | **código** `NotificadorService.cs:186-198` |
| 4 | O fallback copia o destinatário do canal anterior (um e-mail usado como número de WhatsApp) e descarta os metadados do template (nome do modelo da Meta e parâmetros) | **código** `NotificacoesDispatcherOrchestrator.cs:240-250` |
| 5 | O `ParsePayload` do fallback lê tudo com `GetString()`: o primeiro valor que não é texto lança, o `catch` engole e as chaves seguintes somem. O caminho principal trata número e booleano | **código** `NotificacoesDispatcherOrchestrator.cs:259-270` contra `NotificadorService.cs:336-362` |
| 6 | Rotina da empresa contra global: `ListarAtivasAsync` não ordena e o `FirstOrDefault` pega a primeira que casar. A mesma escolha está duplicada no Dispatcher | **código** `RotinaNotificacaoRepository.cs:17-23`; `NotificadorService.cs:137-138`; `NotificacoesDispatcherOrchestrator.cs:212-213` |
| 7 | A pausa da empresa não pausa nada: o endpoint cria o bloqueio com `EmpresaId`, e o motor só honra bloqueio global (`EmpresaId == null`). O comentário do `ResolvedorCanal` diz que o caller valida a empresa, e o caller não valida. A N1 acrescenta a checagem no envio e herdaria o mesmo critério | **código** `NotificacoesConfiguracaoController.cs:188-192`; `NotificadorService.cs:129`; `Notifications/ResolvedorCanal.cs:47-56`; N1 em [01-verdade-do-motor.md](01-verdade-do-motor.md) (abordagem, kill switch no envio) |
| 8 | Multicanal só existe como fallback depois de `Falhado`. Nada garante dois canais ao mesmo tempo (incidente; reset com link no e-mail e código no WhatsApp) | **código** `NotificadorService.cs:186-187`; `NotificacoesDispatcherOrchestrator.cs:176-177` |
| 9 | `EnfileirarEventoAsync` não aceita `CorrelationId`, então o produtor não dá chave determinística ao evento | **código** `NotificadorService.cs:54-66`; `EventoNotificacao.cs:34`; `INotificadorService.cs` |
| 10 | A janela da rotina (`JanelaInicio`, `JanelaFim`, `RespeitarFusoLoja`), a do canal e o limite diário existem só como coluna: nenhum código as lê. A decisão de 08/08 ("a janela é o controle de quando") não tem implementação (corrige o plano) | **medido** `grep` por `JanelaInicio`, `JanelaFim`, `RespeitarFusoLoja`, `JanelaPermitidaInicio` e `LimiteDiarioPorUsuario` fora de entidade, configuração EF e migrações: zero leitura (as outras ocorrências do prefixo são campos de outros módulos, `JanelaInicioUtc` no caixa e `JanelaInicioEm` no health de pagamentos); **código** `RotinaNotificacao.cs:19-21`; `ConfiguracaoCanal.cs:12-14` |
| 11 | O `ResolvedorCanal` acrescenta InApp sozinho para `Operacional`, mesmo sem template do tipo para esse canal. (A categoria `Seguranca` sem opt-out já é da N2, que a trata como `Transacional` em `Notifications/ResolvedorCanal.cs:72`) | **código** `Notifications/ResolvedorCanal.cs:36-42,66-85`; N2 em [01-verdade-do-motor.md](01-verdade-do-motor.md) |
| 12 | Rotina criada pela API nasce sem canais: o comando e o endpoint não têm lista de canais, e `DefinirFallback` só é chamado pelo seed. O motor trata rotina sem canal como "nada a fazer" e marca o evento `Processado` em silêncio | **código** `RotinaNotificacaoCommands.cs:9-18`; `NotificacoesConfiguracaoController.cs:236-240`; `NotificadorService.cs:150-156`; **medido** `grep` de `DefinirFallback` fora de testes: só `NotificacoesGlobaisSeed.cs` e a definição na entidade |
| 13 | A N8 precisa restringir os canais da rotina por evento (reset de conta sem telefone verificado só por e-mail), mas o payload só tem duas chaves de controle (`enviarApos` e `chaveIdempotencia`) | **código** `NotificadorService.cs:22-31`; N8 em [06-acesso.md](06-acesso.md) |

**Abordagem.**
- **Rotina determinística.** `ListarAtivasAsync` ordena (empresa antes de global, depois `CriadaEm`; depois da N1 a
  consulta já traz as duas) e um `SeletorRotina` (Application) escolhe a da empresa e cai na global; rotina da
  empresa sem canais não esconde a global. O Avaliador e o fallback do Dispatcher usam o mesmo seletor.
- **Template por código e depois por tipo e canal.** `ITemplateRepository.GetAtivoPorTipoAsync(tipo, canal,
  empresaId)`: empresa antes de global, maior `Versao` ativa e aprovada, com a mesma leitura que a N1 definiu para o
  catálogo global (`IgnoreQueryFilters()` e predicado `EmpresaId == empresa ou nulo`, sem bypass direto, amparada
  pela policy só de SELECT dos globais). Sem migração.
- **`ConstrutorMensagemOutbox`** (Application): o único lugar que, dados evento, rotina, canal e destinatário,
  resolve o template, renderiza assunto, corpo e metadados (com o prefixo `[TESTE]` da N13), escolhe o contato do
  canal, calcula a chave de idempotência e a janela. `NotificadorService` e o fallback do Dispatcher passam a
  chamá-lo. Somem o `ParsePayload`, a cópia do destinatário e a perda dos metadados. O construtor recebe uma lista
  de destinatários: a N5 entrega uma lista de um (o do payload, `ResolverDestinatario`), e a N4 a preenche. Se o
  construtor do `NotificadorService` mudar, os três testes que o instanciam mudam no mesmo commit (R8:
  `NotificadorServiceMetadadosTests`, `NotificadorServicePushTests`, `NotificarClienteStatusPedidoHandlerTests`).
- **Modo da rotina** em `ParametrosJson.modoCanais`: `fallback` (padrão; sem a chave tudo como hoje) ou `todos`
  (uma mensagem por canal permitido, cada uma com template, contato e metadados próprios). Canal sem template ou
  sem contato é pulado com aviso, e o evento só falha quando nenhum canal sobra. O InApp que o `ResolvedorCanal`
  acrescenta para `Operacional` só entra quando existe template do tipo.
- **Chave de controle `canais` no payload**, ao lado de `enviarApos` e `chaveIdempotencia`: lista que restringe os
  canais da rotina naquele evento. Canal fora da lista da rotina é ignorado. A N8 a usa para mandar só o link
  quando a conta não tem telefone verificado, e a N9 para mandar só e-mail quando não há atestado de WhatsApp.
- **Pausa da empresa.** `ResolvedorCanal` e `NotificadorService` honram bloqueio com o `EmpresaId` do evento, por
  canal ou geral. A pausa da empresa nunca bloqueia `Seguranca`, e a mesma regra vale na checagem de kill switch
  que a N1 põe no envio do Dispatcher; a pausa global do superadmin bloqueia tudo, como hoje. O `Seguranca` sem
  opt-out é da N2, e o opt-in do WhatsApp de plataforma é da N4 e da N6.
- **Janela.** `JanelaDeEnvio.ProximaAbertura(inicio, fim, agoraUtc)`, puro, sobre `HorarioBrasil`: intervalo
  `[inicio, fim)`, vira a meia-noite quando `inicio > fim`, sem efeito quando alguma ponta é nula ou iguais.
  Fora da janela o outbox ganha `AgendarPara(abertura)`, o mesmo mecanismo do `enviarApos`
  (`OutboxMensagemNotificacao.cs:100-104`), e o Dispatcher já filtra `ProximaTentativaEm <= agora`
  (`OutboxNotificacaoRepository.cs:19`): adia, nunca suprime. A quarentena da N1 conta da `ProximaTentativaEm`,
  então o adiado não expira durante a noite. `Seguranca` ignora a janela, e o `enviarApos` do payload vale se for
  mais tarde. `RespeitarFusoLoja`, a janela do canal e o limite diário seguem só como coluna.
- **Chave de correlação.** `EnfileirarEventoAsync(tipo, empresaId, payloadJson, refEntidadeId, ct, correlationId = null)`:
  o parâmetro novo vai depois do `ct`, para os cinco call-sites posicionais seguirem compilando.
- **Rotina da empresa com canais.** `CriarRotinaCommand` e o endpoint aceitam `canais` (ordem de preferência) e
  chamam `DefinirFallback`. Sem isso a N12 não consegue ligar o resumo por empresa.
- **Fatias (commits verdes).** (1) seleção e ordem da rotina; (2) `ConstrutorMensagemOutbox` e template por tipo
  e canal; (3) modo `todos`, chave `canais` e pausa da empresa; (4) janela, `correlationId` e canais na API.

**Leitura mínima.** `EasyStock.Application/Services/Notifications/NotificadorService.cs` (inteiro);
`ResolvedorCanal.cs`; `EasyStock.Infra.Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs`
(101 a 270, já com a estrutura da N1); `EasyStock.Infra.Postgre/Repositories/Notifications/RotinaNotificacaoRepository.cs`
e `TemplateNotificacaoRepository.cs`; `EasyStock.Application/Ports/Output/Notifications/INotificadorService.cs`;
`EasyStock.Domain/Entities/Notifications/OutboxMensagemNotificacao.cs`, `RotinaNotificacao.cs` e
`ConfiguracaoCanal.cs`; `EasyStock.Api/Controllers/NotificacoesConfiguracaoController.cs` (150 a 200 e 236 a 240);
`EasyStock.Application/UseCases/Notifications/RotinaNotificacaoCommands.cs`;
`EasyStock.Application/Common/HorarioBrasil.cs` (`InstanteUtc`, `Hoje`, `ConverterParaBrasilia`); N1 e N2 em
[01-verdade-do-motor.md](01-verdade-do-motor.md).

**Testes Red.**
- `EasyStock.Application.Tests/Services/Notifications/SeletorRotinaTests.cs` (novo)
  - `::RotinaDaEmpresaVenceAGlobalNaOrdemQueVier`: hoje o `FirstOrDefault` sobre a lista sem ordem escolhe a
    primeira que casar (`NotificadorService.cs:137-138`).
  - `::RotinaDaEmpresaSemCanaisNaoEscondeAGlobal`.
- `EasyStock.Application.Tests/Services/Notifications/ConstrutorMensagemOutboxTests.cs` (novo, sem banco)
  - `::PayloadComNumeroEBooleanoPreservaTodasAsVariaveis`: hoje o `ParsePayload` aborta no primeiro valor que não
    é texto (`NotificacoesDispatcherOrchestrator.cs:259-270`).
  - `::UsaOContatoDoCanalDeDestinoENuncaOAnterior`: hoje copia `Destinatario` (`:245`).
  - `::PreservaOsMetadadosDoTemplate`: hoje perde (`:240-250`).
  - `::AchaTemplatePorTipoECanalQuandoOCodigoNaoTemOCanal`: hoje só por código (`:216-217`).
  - `::CanalSemTemplateOuSemContatoEPulado`, `::ChaveDeNegocioEntraNaIdempotencia` e
    `::EventoDeTesteGanhaOPrefixoNoAssunto` (o teste da N13 segue verde depois da mudança de lugar).
- `EasyStock.Application.Tests/Services/Notifications/NotificadorServiceCanalTests.cs` (novo)
  - `::PrimarioBloqueadoUsaOProximoCanalComTemplateProprio`: hoje o evento vira `Falhado`
    (`NotificadorService.cs:186-198`).
  - `::ModoTodosGeraUmaMensagemPorCanalPermitido`: hoje só uma, o resto fica de fallback (`:186-187`).
  - `::PayloadCanaisRestringeARotina`: hoje a rotina usa a lista dela, sem restrição por evento (`:22-31`).
  - `::EventoSoFalhaQuandoNenhumCanalTemTemplate` e `::SemChaveDeModoSegueOFallback` (retrocompatibilidade).
- `EasyStock.Application.Tests/Services/Notifications/ResolvedorCanalTests.cs` (estende o existente)
  - `::PausaDaEmpresaSuprimeOCanalDaEmpresaENaoDasOutras` (`Notifications/ResolvedorCanal.cs:47-56`);
    `::PausaGeralDaEmpresaSuprimeOEventoMasNaoSeguranca` (`NotificadorService.cs:129`);
    `::InAppAcrescentadoSoComTemplate` (`:36-42`).
- `EasyStock.Application.Tests/Services/Notifications/JanelaDeEnvioTests.cs` (novo)
  - `::ForaDaJanelaAdiaParaAAbertura`, `::DentroDaJanelaNaoAdia`, `::JanelaQueViraAMeiaNoite`,
    `::SemJanelaNaoAdia`, `::SegurancaIgnoraAJanela`, `::UsaOFusoDeBrasilia`: hoje nenhum código lê a janela.
- `EasyStock.Application.Tests/Services/Notifications/NotificadorServiceJanelaTests.cs` (novo)
  - `::EventoForaDaJanelaGravaProximaTentativaEmNaAbertura` e `::EnviarAposDoPayloadVenceSeForMaisTarde`.
- `EasyStock.Application.Tests/Services/Notifications/NotificadorServiceCorrelacaoTests.cs` (novo)
  - `::EnfileirarEventoRepassaCorrelationId`: hoje o parâmetro não existe (`NotificadorService.cs:54-66`).
- `EasyStock.Application.Tests/UseCases/Notifications/CriarRotinaUseCaseTests.cs` (novo)
  - `::AceitaCanaisEOrdem`: hoje a rotina da empresa nasce sem canais (`RotinaNotificacaoCommands.cs:9-18`;
    `RotinaNotificacao.cs:14`).
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/TemplatePorTipoECanalIntegrationTests.cs` (novo)
  - `::AchaGlobalComPapelNobypassrlsPreferindoEmpresaEMaiorVersao`: hoje não existe a consulta por tipo e canal.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/RotinaNotificacaoRepositoryIntegrationTests.cs` (novo)
  - `::ListarAtivasOrdenaEmpresaAntesDeGlobalComTenantLigado`.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/PausaDaEmpresaDispatcherTests.cs` (novo)
  - `::PausaDaEmpresaSuprimeOperacionalENaoSeguranca`: o critério da N1 no envio suprimiria também `Seguranca`.

**Aceite.**
- [ ] Dada uma rotina `["InApp","Email"]` com o InApp bloqueado ou inativo, quando o evento chega, então sai pelo
  e-mail com o template de e-mail (hoje o evento vira `Falhado`).
- [ ] Dada uma rotina em modo `todos` com e-mail e WhatsApp, então saem duas mensagens, cada uma com seu template,
  seu contato e seus metadados (o modelo da Meta); sem o modo, o comportamento é o de hoje.
- [ ] Dado um payload com `canais: ["Email"]` numa rotina `todos`, então só o e-mail sai.
- [ ] Dada a falha do primeiro canal em modo `fallback`, então a mensagem do canal seguinte usa o contato do
  próprio canal e preserva metadados e todas as variáveis do payload, número e booleano incluídos.
- [ ] Dadas uma rotina da empresa e uma global ativas, então vale a da empresa, e a da empresa sem canais não
  esconde a global.
- [ ] Dada a pausa da empresa, por canal ou geral, então o canal ou o evento é suprimido para aquela empresa e só
  para ela, na avaliação e no envio; `Seguranca` passa; a pausa global do superadmin segue calando tudo.
- [ ] Dada uma rotina com janela de 07:00 a 22:00 e um evento às 23:30, então o outbox nasce com
  `ProximaTentativaEm` às 07:00 do dia seguinte e não sai antes; `Seguranca` ignora a janela.
- [ ] Dado `EnfileirarEventoAsync` com `correlationId`, então o evento o grava.
- [ ] Dado `POST api/notificacoes/rotinas` com `canais`, então a rotina da empresa nasce com esses canais.
- [ ] **Antes de habilitar,** a PR cola a contagem de pausas de empresa ativas hoje
  (`SELECT count(*) FROM notif_bloqueios WHERE "RemovidoEm" IS NULL AND "EmpresaId" IS NOT NULL`), porque elas
  passam a valer.
- [ ] Os testes existentes do motor seguem verdes sem alteração, exceto os que fixavam o comportamento errado
  descrito no Problema.

**Fora.**
- Avisos ao cliente final (S13, campanhas): as rotinas deles não têm `ParametrosJson.modoCanais`, seguem em
  `fallback` e não mudam. A pausa da empresa passa a valer para elas, por desenho.
- Fuso por loja (`RespeitarFusoLoja`), janela do canal (`JanelaPermitida*`) e `LimiteDiarioPorUsuario`: seguem só
  como coluna. Decidir se ligam ou saem é issue própria.
- Quarentena e TTL, isolamento do evento veneno, bypass e o kill switch no envio (N1); `Simulado`, retentativa,
  classificação de falha e `Seguranca` sem opt-out (N2); destinatário por audiência e chave por destinatário
  (N4); opt-in e remetente do WhatsApp de plataforma (N4 e N6).
- Ativar template criado pela API (N13, Fora).

**Depende de.** N1 (a policy só de SELECT para globais, para a rotina e o template globais aparecerem dentro do
escopo do tenant; o escopo por item; o kill switch no envio que a pausa da empresa também governa), N2 (categoria
`Seguranca`) e N13 (dados completos por tipo e canal). A lista de destinatários começa com um (o do payload); a
N4 a preenche.

**Tamanho.** G, fatiada em 4 commits verdes.

**Tier.** ALTO. A ADR-0055 (item 1, `docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:18-20`) sozinha daria
baixo: spec de plano aprovado, sem migração, RLS, autenticação, policy nem arquivo de entrada. A R5 (`CLAUDE.md:69-71`)
manda ALTO: mais de 5 arquivos e de 100 linhas, no centro do motor, e a ADR-0057 (item 11) manda valer a regra mais
restritiva. **Corrigido:** o plano marca N5 como baixo. O modo `todos` é opt-in por rotina; para os avisos ao cliente,
o que muda é só a pausa da empresa passar a valer e a janela, que nenhuma rotina semeada configura hoje. A PR espera a
label `aprovado`.

**Rollback.** `git revert` do squash. `ParametrosJson.modoCanais`, a chave `canais`, `correlationId` e `canais` na
API são aditivos e sem migração: sem o modo, o comportamento volta ao anterior. Nada a desfazer em banco.

---

## Rollback do marco

N13 e N5 são PRs separadas e independem na reversão, com uma regra: o dado vem antes do código. Reverter a N5
devolve fallback quebrado, rotina sem ordem e pausa que não pausa; os dados da N13 continuam e ficam inertes.
Reverter a N13 pede primeiro desativar as quatro rotinas novas e drenar ou fechar os eventos dos tipos novos
(o enum é guardado como texto, e um nome que some do código com linha no banco quebra a leitura dela), e a PR
traz o SQL que devolve o que o seed alterou. Sem migração em nenhuma das duas. Interruptor sem deploy para as
rotinas novas: `Ativa = false`.
