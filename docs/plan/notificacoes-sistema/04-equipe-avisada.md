# M3 · Equipe avisada (N4, N10, N11, N12)

Marco M3 do plano de notificações de plataforma · issue #1344 · [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md) ·
medido no worktree `c2eb85ca` (.NET 10) em 2026-10-01. Convenções do executor:
[README do plano](README.md#regras-que-valem-para-todas-as-specs) e
[README do atendimento](../atendimento-whatsapp/README.md#convenções-do-executor-vinculantes-resumo-do-claudemd-v40).

Legenda da coluna **Prova**: **código** = lido na linha citada; **medido** = contagem ou execução de script
descartável; **inferência** = deduzido, não reproduzido.

**Leitura do tier.** A ADR-0057 (`docs/adr/0057-notificacoes-de-plataforma.md:68-69`) manda valer a regra mais
restritiva entre a ADR-0055 e a R5 do `CLAUDE.md` (`CLAUDE.md:69-71`: mais de 100 linhas ou de 5 arquivos, migração
ou arquivo de entrada). Pela ADR-0055 sozinha, a N10 e a N11 seriam baixo, porque não tocam migração, RLS,
autenticação nem policy; pela R5, as duas passam de 5 arquivos e de 100 linhas, então as quatro specs desta página
são ALTO e esperam a label `aprovado`. **Corrigido:** o plano marca a N10 e a N11 como baixo.

**Decisões do Felipe (01/10) que valem aqui.** Prazos (N11): cliente sem resposta, pedido atrasado, impressão
travada e caixa esquecido. Rotinas (N12): resumo diário à dona, mais o motor genérico de agendamento; contas a
vencer e produtos vencendo ficam fora agora, e o produtor duplicado de contas vencendo continua desligado. E a
decisão de 08/08, que continua valendo: o `CronExpression` da rotina não será ligado, a janela de horário é o
controle de "quando" e adiar por janela é gravar `ProximaTentativaEm` no outbox.

## Ordem

```
N4 (telefone, audiência, chave por destinatário) ─► N10 ∥ N11 ∥ N12
```

A N4 prepara quem recebe. A N10, a N11 e a N12 só produzem eventos dos tipos que a N13 catalogou
(`IncidenteSistema`, `PrazoEstourado`, `ResumoDiario`). Eventos sem tenant próprio (incidente, aviso de contato do
superadmin) nascem na empresa padrão, pelo `IEmpresaPadraoResolver` da N13. Conflito de merge esperado entre as
três: o registro de coletores em `EasyStock.Infra.Postgre/DependencyInjection/ServiceCollectionExtensions.Notifications.cs`.

---

## N4 · Contato verificado, audiência de usuários internos e troca de contato endurecida · issue aberta ao iniciar

**Problema.** O motor só sabe mandar para o que vier escrito no payload, o usuário interno não tem telefone, e
trocar o e-mail da conta é o caminho mais curto para tomar a conta de alguém.

| # | Achado | Prova |
|---|---|---|
| 1 | `Usuario` não tem telefone nem verificação de telefone (`EmailConfirmado` é só um booleano), e o export de dados pessoais também não os conhece | **código** `Usuario.cs:3-26`; `ExportarMeusDadosUseCase.cs:9` |
| 2 | O destinatário vem só do payload (`email`, `emailDestino`, `usuarioEmail`; `telefone`, `whatsapp`, `celular`). O `usuarioId` do payload (a variável `usuarioDestinoId`) só serve para ler consentimentos e para o destino de Push e InApp, nunca para buscar e-mail ou telefone | **código** `NotificadorService.cs:70-81,158-160,222-231,364-392` |
| 3 | A chave de idempotência por negócio é só `(chave, canal)`, sem destinatário: com vários destinatários o segundo cai no `ExisteAsync` e no índice único como se fosse duplicata do primeiro | **código** `OutboxMensagemNotificacao.cs:145-149`; `NotificadorService.cs:235-247`; `OutboxMensagemNotificacaoConfiguration.cs:36` |
| 4 | Para o WhatsApp da equipe não existe regra de opt-in: `Operacional` é permitido por padrão sem registro algum | **código** `Notifications/ResolvedorCanal.cs:66-85` |
| 5 | `PATCH api/auth/me` troca o e-mail sem pedir a senha, sem validar o formato, sem reverificar e sem avisar o endereço antigo | **código** `AtualizarUsuarioAtualUseCase.cs:25-39`; `AuthController.cs:255-257` |
| 6 | O Admin troca o e-mail de usuário vinculado a outra empresa e de SuperAdmin: o guarda só confere o vínculo com a empresa corrente. Quem tem qualquer `UsuarioPerfil` com `Perfil.Nivel = SuperAdmin` já entra como superadmin | **código** `AtualizarUsuarioUseCase.cs:27-41`; `UsuarioController.cs:63-69`; `AutenticarUsuarioUseCase.cs:103-123` |
| 7 | Zerar `EmailConfirmado` na troca, como o plano descreve, trancaria o usuário para fora: o login recusa e-mail não confirmado, e um erro de digitação do endereço novo não tem volta pela própria conta (corrige o plano: a troca vira duas etapas) | **código** `AutenticarUsuarioUseCase.cs:44`; `ConfirmEmailUseCase.cs:28-29` |
| 8 | Senha atual errada hoje devolve 401 (`CredenciaisInvalidasException`), que o console trata como sessão vencida, e não conta como falha de login | **código** `AlterarSenhaUseCase.cs:26-28`; `GlobalExceptionHandler.cs:116,123` |
| 9 | A anonimização e o export não conhecem telefone nem opt-in, e os consentimentos guardam o IP de origem | **código** `Usuario.cs:83-99`; `AnonimizarMeusDadosUseCase.cs:45-49`; `ConsentimentoNotificacao.cs` (`IpOrigem`) |
| 10 | A regra de E.164 existe duas vezes e nenhuma é um VO estrito: `Telefone` aceita de 7 a 15 dígitos (não é E.164), e `NormalizadorTelefone` é a regra BR, mas mora na Application, com 7 call-sites e testes (corrige o plano: não criar uma terceira regra) | **código** `Telefone.cs:14,21-37`; `NormalizadorTelefone.cs:15,34-69`; **medido** `grep` de `NormalizarE164Br`: 7 usos |
| 11 | SuperAdmin é global: `Perfil.EmpresaId` nulo, `UsuarioPerfil.EmpresaId = Guid.Empty` e nenhum `UsuarioEmpresa`. A audiência "superadmins" não sai de uma consulta por empresa, e criar um `Perfil` SuperAdmin atado à empresa padrão já daria poder global no login | **código** `SuperAdminSeed.cs:100,148`; `AutenticarUsuarioUseCase.cs:103-123` |

**Abordagem.**
- **VO `TelefoneE164`** (Domain/ValueObjects). A regra E.164 BR sai do `NormalizadorTelefone` para o VO, e o
  `NormalizarE164Br` passa a delegar (mesma assinatura: os 7 call-sites e `NormalizadorTelefoneTests` seguem sem
  mudança). O `Telefone` frouxo não muda.
- **`Usuario`.** Três campos novos: `Telefone` (`TelefoneE164?`, `varchar(16)`), `TelefoneVerificadoEm` e
  `EmailPendente` (`varchar(255)`). Métodos de domínio: `DefinirTelefone` (zera a verificação),
  `MarcarTelefoneVerificado`, `SolicitarTrocaDeEmail` e `ConfirmarNovoEmail`. `Anonimizar()` limpa os três.
  Migração aditiva `AddContatoUsuario` (três colunas nulas em `usuarios`; a tabela não tem `EmpresaId`, então não
  entra RLS nova).
- **Audiência.** `IResolvedorAudiencia` (Application). A rotina declara em `ParametrosJson.audiencia` um destes
  valores: `usuario`, `admins`, `gestores` ou `superadmins`. Sem a chave, o comportamento de hoje: destinatário
  pelas chaves do payload, que seguem valendo para o cliente final e para quem entrega o contato (confirmação de
  e-mail, aviso ao endereço antigo). Um `usuarioId` explícito no payload vence a audiência da rotina (usado pelo
  disparo de teste, pelo reset e pelo atendente do `cliente_sem_resposta`); na audiência `usuario` ele é a própria
  definição do destinatário, e sem ele a audiência cai nas chaves do payload. Regras:
  - e-mail: usuário `Ativo`. Nas audiências coletivas (`admins`, `gestores`, `superadmins`) também `EmailConfirmado`,
    para aviso operacional não ir a endereço digitado errado; para o usuário explícito do payload basta `Ativo`,
    porque o produtor o escolheu e hoje o esqueci senha já envia a qualquer conta ativa
    (`EsqueciSenhaUseCase.cs:28-29`);
  - WhatsApp: telefone verificado **e** opt-in explícito em `ConsentimentoNotificacao` (WhatsApp, categoria da
    rotina). Sem registro, não envia. A regra permissiva de hoje fica para os outros canais e para o cliente
    final, que não passam por aqui. **Guarda até a N6:** o resolvedor só devolve WhatsApp quando o WhatsApp de
    plataforma está configurado (`Notifications:WhatsApp:Plataforma:PhoneNumberId`, chave da N6). Sem ele, o canal da
    loja nunca carrega aviso de plataforma, nem se alguém verificar um telefone antes da hora;
  - `PreferenciaNotificacaoUsuario.Habilitada = false` pula a pessoa naquela rotina, exceto `Seguranca`;
  - `admins` e `gestores` (Admin, e Admin mais Gerente): usuários ativos da empresa do evento
    (`UsuarioEmpresa` e `UsuarioPerfil.Perfil.Nivel`), sempre dentro do tenant do evento;
  - `superadmins`: só vale em rotina **global**. Numa rotina da empresa o resolvedor ignora a chave (com aviso) e o
    `POST api/notificacoes/rotinas` recusa com 400, porque o Admin de uma empresa também grava `ParametrosJson`
    (`NotificacoesConfiguracaoController.cs:236-240`) e não pode apontar a rotina dele para os superadmins. A
    consulta é própria, só leitura, devolvendo Id, nome e contato (`SuperAdminSeed.cs:84-148` é o precedente), e
    roda num escopo de DI próprio e curto com `IRowLevelSecurityBypass.Begin()` aberto antes da primeira conexão (o
    interceptor só lê a flag na abertura; a prova está na N1), nunca dentro do escopo do evento. A classe entra na
    `Allowlist` de `RlsBypassAllowlistTests` com a issue #1344 e o motivo "cross-tenant por natureza", e chamar
    `UseRowLevelSecurityBypass()` direto é proibido no módulo (N1). **Não** se cria `Perfil` SuperAdmin atado à
    empresa padrão.
- **Chave por destinatário.** `ComputarIdempotencyKey(chave, canal, destinatarioChave)`. Sem destinatário (cliente
  final, S13) a chave de hoje não muda, então nada que já está no outbox deixa de deduplicar. Cada mensagem leva o
  `UsuarioDestinoId` do destinatário resolvido (a coluna já existe), que a guarda de opt-in do WhatsApp de plataforma
  exige (N6).
- **Opt-in e verificação do telefone.** Ao verificar o telefone, grava os consentimentos explícitos (`Seguranca` e
  `Operacional`, com data e IP). `Seguranca` não se revoga sem remover o telefone. Nesta spec a verificação é
  administrativa: `POST api/admin/usuarios/{id}/telefone/verificar` (`SuperAdmin`, motivo de 10 caracteres,
  auditoria), que serve à equipe pequena (a dona e o Felipe). O consentimento nasce com o superadmin em
  `AtualizadoPor` e o IP da chamada dele em `IpOrigem`, porque a pessoa não deu o aceite no aparelho dela, e ela o
  revoga em Preferências. A verificação por código no WhatsApp fica para depois da N6 e da N8: o código precisa do
  canal de plataforma e do segredo de uso único com tentativas. O próprio usuário define o telefone em
  `PUT api/auth/me/telefone` com a senha atual.
- **Troca de e-mail em duas etapas.**

```
PATCH api/auth/me { email, senhaAtual }
  senha ausente ou errada ─► 403, e a senha errada conta como falha de login
  senha certa ─► Usuario.EmailPendente = novo (Email e EmailConfirmado ficam como estão)
        ├─► ConfirmacaoEmail ─► endereço NOVO (link pela allowlist de origens, token de 24 h)
        └─► ContatoAlterado  ─► endereço ANTIGO (novo endereço mascarado)
clique no link ─► ConfirmEmail: Email = EmailPendente, EmailPendente = nulo
```

  Um novo pedido apaga os tokens de confirmação anteriores do usuário (o link só vale para o pendente mais
  recente). O formato é validado (`EmailValidator`) e a unicidade é checada no pedido e de novo na confirmação.
  A senha errada é 403 e não 401, para o console não entender sessão vencida. O endpoint ganha
  `[EnableRateLimiting("auth")]`, porque vira oráculo de senha. O evento nasce na empresa do token; sem empresa
  (SuperAdmin), na empresa padrão (`IEmpresaPadraoResolver`, N13).
- **Telefone.** Mesmo contrato: exige a senha, zera a verificação e avisa o e-mail atual (e, com N6 e telefone
  antigo verificado, o WhatsApp antigo).
- **Admin.** `PUT api/usuarios/{id}` só troca o e-mail de usuário **exclusivo** da empresa (um único
  `UsuarioEmpresa`) e **nunca** de quem tem perfil SuperAdmin; nos demais casos 403 com a mensagem "peça ao
  próprio usuário ou ao suporte". Mesmo fluxo de duas etapas e mesmo aviso. Alterar só o nome segue permitido. O
  SuperAdmin segue cross-tenant, pelo mesmo fluxo e com auditoria.
- **LGPD.** `AnonimizarMeusDadosUseCase` revoga os opt-ins (`OptIn = false`, `MotivoOptOut = "anonimizacao"`),
  zera `IpOrigem` e apaga as preferências. O export traz telefone, verificação, consentimentos e preferências.
- **Tipo novo `ContatoAlterado`** (categoria `Seguranca`, só e-mail): rotina `contato_alterado_global` e
  `contato_alterado_email_v1.html`, pelo seed versionado da N13, com valor da faixa livre da S0 (80 em diante; o
  banco guarda o nome, então o número não importa) e quarentena do grupo Segurança (30 min, N1). A rotina
  `confirmacao_email_global` passa a `Seguranca` pelo mesmo caminho.
- **Interruptor.** `Notifications:Audiencia:Habilitada` (padrão `true`); `false` volta ao destinatário só pelo
  payload, sem deploy.
- **Fatias (commits verdes).** (1) domínio e migração: VO, `Usuario`, anonimização e export; (2) audiência e
  chave por destinatário no motor; (3) endurecimento da troca de contato.

**Leitura mínima.** `EasyStock.Domain/Entities/Usuario.cs`;
`EasyStock.Application/UseCases/AtualizarUsuarioAtual/AtualizarUsuarioAtualUseCase.cs`;
`.../AtualizarUsuario/AtualizarUsuarioUseCase.cs`; `.../AlterarSenha/AlterarSenhaUseCase.cs`;
`.../ConfirmEmail/ConfirmEmailUseCase.cs`; `.../AutenticarUsuario/AutenticarUsuarioUseCase.cs` (40 a 50 e 100
a 125); `.../AnonimizarMeusDados/` e `.../ExportarMeusDados/`;
`EasyStock.Api/Controllers/AuthController.cs` (250 a 290) e `UsuarioController.cs` (60 a 75);
`EasyStock.Application/Services/Atendimento/NormalizadorTelefone.cs` e `EasyStock.Domain/ValueObjects/Telefone.cs`;
`EasyStock.Domain/Entities/Notifications/ConsentimentoNotificacao.cs` e `PreferenciaNotificacaoUsuario.cs`;
`EasyStock.Application/Services/Notifications/NotificadorService.cs` (150 a 232 e 364 a 392) e
`ResolvedorCanal.cs`; `OutboxMensagemNotificacao.cs` (140 a 150); `EasyStock.Api/Data/SuperAdminSeed.cs` (80 a
150); `EasyStock.Infra.Postgre/Data/Configurations/UsuarioConfiguration.cs`; a migração mais recente e
`EasyStock.ArchitectureTests/MigrationDesignerHygieneTests.cs`;
`EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs` e
`EasyStock.Application/Ports/Output/Security/IRowLevelSecurityBypass.cs`.

**Testes Red.**
- `EasyStock.Domain.Tests/ValueObjects/TelefoneE164Tests.cs`
  - `::AceitaFormatosComunsENormalizaParaE164` (as mesmas entradas do `NormalizadorTelefoneTests`) e
    `::RecusaNumeroForaDoPadraoBrasileiro`: hoje não existe VO estrito, e o `Telefone` aceita de 7 a 15 dígitos
    (`Telefone.cs:14`).
- `EasyStock.Domain.Tests/Entities/UsuarioContatoTests.cs`
  - `::TrocarTelefoneZeraAVerificacao`, `::VerificarTelefoneGravaOInstante`,
    `::SolicitarTrocaDeEmailNaoAlteraOEmailNemAConfirmacao`, `::ConfirmarNovoEmailTrocaEZeraAPendencia`.
  - `::AnonimizarLimpaTelefoneVerificacaoEEmailPendente`: hoje `Anonimizar()` não conhece nada disso
    (`Usuario.cs:83-99`).
- `EasyStock.Application.Tests/UseCases/AtualizarUsuarioAtualUseCaseTests.cs`
  - `::TrocaDeEmailSemSenhaAtualRecusaComForbidden`: hoje troca (`AtualizarUsuarioAtualUseCase.cs:38-39`).
  - `::SenhaAtualErradaRecusaComForbiddenEContaFalhaDeLogin`: hoje devolve 401 e não conta
    (`AlterarSenhaUseCase.cs:26-28`).
  - `::ComSenhaGravaEmailPendenteSemTrocarOEmail` e
    `::EnfileiraConfirmacaoNoEnderecoNovoEAvisoMascaradoNoAntigo`.
  - `::EmailJaUsadoPorOutroUsuarioRecusa` e `::EmailInvalidoRecusa` (hoje sem validação de formato).
  - `::NovoPedidoInvalidaOsTokensAnteriores` e `::TelefoneNovoExigeSenhaEZeraAVerificacao`.
- `EasyStock.Application.Tests/UseCases/AtualizarUsuarioUseCaseTests.cs`
  - `::AdminNaoTrocaEmailDeUsuarioDeDuasEmpresas`, `::AdminNaoTrocaEmailDeSuperAdmin`,
    `::AdminTrocaEmailDeUsuarioExclusivoPeloFluxoPendente`, `::AdminAindaAlteraSoONomeDeUsuarioDeDuasEmpresas`:
    hoje o guarda só confere o vínculo com a empresa corrente (`AtualizarUsuarioUseCase.cs:27-31`).
- `EasyStock.Application.Tests/UseCases/ConfirmEmailUseCaseTests.cs`
  - `::ConfirmarComEmailPendenteTrocaOEnderecoEMantemOLogin`: hoje só marca `EmailConfirmado`
    (`ConfirmEmailUseCase.cs:28`).
- `EasyStock.Application.Tests/Services/Notifications/ResolvedorAudienciaTests.cs` (com repositórios falsos)
  - `::UsuarioComEmailConfirmadoRecebePorEmail`, `::EmailNaoConfirmadoNaoRecebeEmAudienciaColetiva`,
    `::UsuarioExplicitoComEmailNaoConfirmadoRecebe`, `::TelefoneNaoVerificadoNaoRecebePorWhatsApp`,
    `::SemOptInExplicitoNaoRecebePorWhatsApp`, `::SemWhatsAppDePlataformaConfiguradoNaoEntregaWhatsApp`,
    `::PreferenciaDesligadaPulaAPessoa`, `::PreferenciaNaoDesligaSeguranca`,
    `::GestoresSaoAdminEGerenteDaEmpresaDoEvento`, `::UsuarioDoPayloadVenceAAudienciaDaRotina`,
    `::SuperadminsEmRotinaDaEmpresaEhIgnorado`, `::AudienciaUsuarioSemUsuarioIdCaiNasChavesDoPayload`,
    `::SemAudienciaMantemAsChavesDoPayload`: hoje o destinatário só sai do payload (`NotificadorService.cs:364-392`).
- `EasyStock.Application.Tests/UseCases/Notifications/CriarRotinaUseCaseTests.cs` (estende o da N5)
  - `::AudienciaSuperadminsEmRotinaDaEmpresaEhRecusada`: hoje a API grava o `ParametrosJson` sem olhar
    (`NotificacoesConfiguracaoController.cs:236-240`).
- `EasyStock.Application.Tests/Services/Notifications/IdempotenciaPorDestinatarioTests.cs`
  - `::ChaveDeNegocioMudaComODestinatario` e `::ChaveSemDestinatarioNaoMudaParaOCliente`: hoje a chave ignora o
    destinatário (`OutboxMensagemNotificacao.cs:145-149`).
- `EasyStock.Application.Tests/UseCases/ExportarMeusDadosUseCaseTests.cs`
  - `::ExportaTelefoneVerificacaoEConsentimentos`; e em `AnonimizarMeusDadosUseCaseTests.cs`
    `::RevogaOptInZeraIpEApagaPreferencias`: hoje ficam fora do export e da anonimização
    (`ExportarMeusDadosUseCase.cs:9`; `AnonimizarMeusDadosUseCase.cs:45-49`).
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/AudienciaIntegrationTests.cs`
  - `::SuperAdminsSaoLidosComNobypassrlsPelaPortaDeBypass` (escopo de DI próprio, porta ligada antes da primeira
    conexão), `::AdminsDeOutraEmpresaNaoVazam`, `::DoisAdminsComAMesmaChaveDeNegocioGeramDuasMensagens`: hoje o
    segundo seria barrado pelo índice único e por `ExisteAsync`.
- `EasyStock.Infra.Postgre.IntegrationTests/Migrations/ContatoUsuarioMigrationTests.cs`
  - `::MigracaoPreservaUsuariosExistentes` e `::DownRemoveSoAsColunasNovas`.

**Aceite.**
- [ ] Dado `PATCH api/auth/me` com e-mail novo e sem `senhaAtual`, então 403 e o e-mail não muda. Dada a senha
  errada, então 403 e conta como falha de login (cinco erros bloqueiam por 15 minutos, a regra do login).
- [ ] Dada a senha correta, então `Email` fica igual e `EmailPendente` recebe o novo; a confirmação sai para o
  endereço novo e o aviso, com o endereço novo mascarado, para o antigo; o login segue possível; só depois do
  clique o `Email` troca; um novo pedido invalida o link anterior.
- [ ] Dado um Admin da empresa A editando o e-mail de um usuário também vinculado à empresa B, então 403; o mesmo
  vale para usuário SuperAdmin; usuário exclusivo passa pelo fluxo de duas etapas com aviso ao antigo; alterar só
  o nome segue permitido.
- [ ] Dado um telefone novo, então `TelefoneVerificadoEm` fica nulo e o WhatsApp não recebe até a verificação.
- [ ] Dado um evento para a empresa X com audiência `gestores`, então cada gestor elegível recebe uma mensagem
  por canal elegível, e ninguém de outra empresa recebe.
- [ ] Dados dois destinatários e a mesma chave de negócio, então saem duas mensagens (uma por pessoa), e a chave
  do cliente final não mudou.
- [ ] Dado o WhatsApp, então telefone verificado sem opt-in explícito não recebe e com opt-in recebe. Dado o
  e-mail, então em audiência coletiva o endereço não confirmado não recebe, e o usuário explícito do payload recebe
  com a conta ativa, confirmada ou não.
- [ ] Dado o WhatsApp de plataforma sem `Notifications:WhatsApp:Plataforma:PhoneNumberId`, então a audiência não
  entrega WhatsApp, mesmo com telefone verificado e opt-in.
- [ ] Dada a audiência de superadmins, então ela vem da consulta própria, pela porta de bypass e só em rotina
  global, e nenhum `Perfil` SuperAdmin atado à empresa padrão foi criado. Dada uma rotina da empresa com
  `audiencia: superadmins`, então o `POST` responde 400 e, se a linha já existir, o resolvedor a ignora com aviso.
- [ ] Dada a anonimização, então telefone, verificação e pendência saem, os opt-ins ficam revogados sem IP, e o
  export traz telefone, verificação, consentimentos e preferências.
- [ ] Dada a migração, então os usuários existentes ficam intactos e o `Down` remove só as três colunas.
- [ ] Dado `Notifications:Audiencia:Habilitada=false`, então o motor volta ao destinatário só pelo payload.
- [ ] Dado `PATCH api/auth/me`, então o limite `auth` (20 por minuto por IP) vale para o endpoint.
- [ ] **Validação do Felipe:** o telefone da dona e o dele ficam verificados por
  `POST api/admin/usuarios/{id}/telefone/verificar` (o WhatsApp só sai quando a N6 estiver configurada), e uma
  troca de e-mail de teste mostra o aviso no endereço antigo.

**Fora.**
- Verificação do telefone por código no WhatsApp e aviso ao WhatsApp antigo: dependem do canal de plataforma (N6)
  e, o código, do segredo de uso único da N8. A N6 e a N8 já os registram como depois.
- Revogar sessões na troca de contato, `SessoesValidasDesde` e contar falha em `lista-empresas` (N7).
- Reset e convite pelo motor (N8, N9).
- Login Google: `IdentificarUsuarioGoogleUseCase.cs:20-21` continua casando por e-mail. Com a senha e a caixa nova
  confirmada, o atacante precisaria das duas coisas; fica registrado, sem mudança aqui.
- Telefone internacional, unicidade de telefone, telefone de cliente final e tela do console.
- Troca de telefone feita pelo Admin: só o próprio usuário (com senha) e a verificação administrativa.

**Depende de.** N13 (tipos, rotinas, a mecânica do seed versionado e o `IEmpresaPadraoResolver`), N5 (construtor
com lista de destinatários e modo `todos`), N2 (`Seguranca`) e N1 (loops lendo, escopo por item, porta de bypass e
allowlist). O WhatsApp da audiência só liga com a N6.

**Tamanho.** G, fatiada em 3 commits verdes.

**Tier.** ALTO. Migração EF e autenticação (ADR-0055, item 2,
`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:22-23`): o fluxo de troca de e-mail e a regra de quem o
Admin pode editar. A PR fica aberta até a label `aprovado`.

**Rollback.** O `Down` da migração remove as três colunas, e só perde telefones e pendências, que ainda não têm
uso em produção. `Notifications:Audiencia:Habilitada=false` desliga a audiência sem deploy. `git revert` devolve a
troca de e-mail antiga **com a brecha**: registrar isso na issue do revert. `ContatoAlterado` e a rotina ficam
inertes.

---

## N10 · Problemas no sistema avisam o superadmin · issue aberta ao iniciar

**Problema.** Hoje o sistema pode cair, ou passar a errar, sem que ninguém seja avisado: o erro vira linha de
log e a "saúde" vira gráfico.

```
EndpointHealthMonitor (Worker, 60 s) ──┐
HealthSnapshotService (API, 60 s) ─────┼─► AvaliadorIncidente ─► IPublicadorIncidenteSistema ─► IncidenteSistema
ColetorPicoDeErros5xx (Worker, 5 min) ─┘                                      (empresa padrão)       └─► superadmins: e-mail e WhatsApp
vigia de integração da F16 ───────────────────────────────────────► mesma porta, quando existir
```

| # | Achado | Prova |
|---|---|---|
| 1 | O `GlobalExceptionHandler` só grava `SystemErrorLog` (sem esperar) e incrementa uma métrica: nenhum aviso | **código** `GlobalExceptionHandler.cs:45-56,266-296` |
| 2 | O `HealthSnapshotService` mede banco, Redis e erros a cada 60 s e guarda em memória e no Redis, mas não alerta. E `ErrorCount > 0` já vira `degraded`: qualquer linha de erro em 65 s, o que alertaria o tempo todo | **código** `HealthSnapshotService.cs:126,137,168,172-175` |
| 3 | O `EndpointHealthMonitorService` já tem a máquina de estado (3 falhas, cooldown de 24 h, persistida em `endpoint_health_state`), mas só alerta por POST em `/api/ci/tickets`, rota apagada em `6b0a6f98`, e só se `Ci:AutoTicketKey` existir. Também guarda `ex.Message` (até 256 caracteres) e o manda no corpo | **código** `EndpointHealthMonitorService.cs:93,136,158,178-215`; `6b0a6f98` removeu `Controllers/Ci/AutoTicketController.cs` |
| 4 | Não há aviso de "resolvido": a recuperação vira só uma linha de log | **código** `EndpointHealthMonitorService.cs:139-148` |
| 5 | `DiagnosticoEmailReportJob` está desligado em todo ambiente e manda relatório diário, não alerta | **código** `BackgroundJobOptions.cs:30`; `EasyStock.Api/appsettings.json:131`; `EasyStock.Api/appsettings.Production.json:48` |
| 6 | A dedupe de 15 min sem tabela nova existe no outbox: a chave por negócio vira `IdempotencyKey` com índice único e checagem `ExisteAsync`. Só vale por canal, não por destinatário (a N4 corrige) | **código** `NotificadorService.cs:235-247`; `OutboxMensagemNotificacao.cs:145-149`; `OutboxMensagemNotificacaoConfiguration.cs:36` |
| 7 | `system_error_logs` não tem `EmpresaId` (fora da RLS) e tem índice em `CriadoEm` e em `(Source, Level)`: serve de fonte do pico de 5xx para o Worker, sem bypass. Mas `Message` e `Details` guardam exceção e pilha | **código** `SystemErrorLog.cs`; `SystemErrorLogConfiguration.cs:19-20` |
| 8 | O vigia de integração (token da Meta) é da F16 e ainda não existe no código: a N10 não pode duplicá-lo nem esperar por ele | **código** F16 em `docs/plan/atendimento-whatsapp/11-console-fechamento.md` (vigia a cada 15 min, lembrete da dona e faixa no console); **medido** nenhum job de integração em `EasyStock.Api/BackgroundServices` |

**Abordagem.**
- **`IPublicadorIncidenteSistema`** (Application): `PublicarAsync(ComponenteIncidente, EstadoIncidente,
  SeveridadeIncidente, desdeUtc, ct)`, tudo enum fechado. **Não existe parâmetro de texto livre**, então
  mensagem de exceção, query string e dado de cliente não têm por onde entrar. Monta o payload (`componente`,
  `estado_texto`, `gravidade`, `desde`, `duracao`) com textos fixos em pt-BR, resolve a empresa padrão
  (`IEmpresaPadraoResolver`, N13), liga o tenant dela no escopo e enfileira `IncidenteSistema` com
  `chaveIdempotencia = incidente:{componente}:{estado}:{janela}`, onde `janela` é
  `unixSegundos / (JanelaDedupeMinutos * 60)`. Sem empresa padrão resolvida não publica e loga aviso nomeando
  `Auth:Google:EmpresaPadrao`. O Worker precisa dessa chave no `.env` dele (hoje só a API a usa, #1326).
  - Dedupe de 15 min sem tabela nova: a chave vira o `IdempotencyKey` do outbox. Limite honesto: a janela é fixa,
    não deslizante, então uma falha que atravessa a virada pode gerar dois avisos em menos de 15 min. A chave
    inclui o destinatário (N4); sem isso o segundo superadmin seria tratado como duplicata do primeiro.
  - `Notifications:Incidentes:JanelaDedupeMinutos` (15) e interruptor `Notifications:Incidentes:Habilitado`.
- **`AvaliadorIncidente`** (Application, puro): máquina de estado sobre o `endpoint_health_state` que já existe
  (`ConsecutiveFailures`, `LastFailureAt`, `LastAlertedAt`; mesma tabela, sem coluna nova). `Avaliar(estado,
  saudavel, agora, limiares)` devolve `Nada`, `Abrir`, `Reavisar` ou `Resolver`. Abre na N-ésima falha seguida
  (padrão 3, como hoje), re-emite enquanto estiver aberto (a dedupe de 15 min vira o lembrete de "ainda fora") e
  resolve depois de 2 avaliações boas seguidas, com `duracao`. `LastFailureMessage` passa a guardar só um código
  fechado (`HTTP_503`, `TIMEOUT`, `CONEXAO_RECUSADA`, `OUTRO`), nunca `ex.Message`.
- **Fontes iniciais.**
  - **Endpoint** (Worker): `EndpointHealthMonitorService` troca `TryOpenTicketAsync` (o POST morto e o
    `Ci:AutoTicketKey`) pelo publicador (`ComponenteIncidente.Api`), publica "resolvido" na recuperação e perde o
    cooldown de 24 h (a janela de 15 min cuida do ritmo). Saem `AutoTicketResponse`, o POST e o `Sha256Hex`. O
    comentário de `Worker/Program.cs:107-109`, que ainda fala da rota apagada e do cooldown, é corrigido junto: a
    PR já é ALTO, então mexer no arquivo de entrada não muda o tier.
  - **Snapshot de saúde** (API): `HealthSnapshotService` avalia só transições para e de `critical` (banco: 2
    snapshots ruins seguidos abrem, 3 bons resolvem) e `degraded` por Redis (`RedisStatus = "falha"`).
    `degraded` por `ErrorCount > 0` (`:174-175`) **não alerta**: o pico de 5xx cobre isso, com limiar.
  - **Pico de 5xx** (Worker): `ColetorPicoDeErros5xx : IColetorEventoNotificacao` conta `system_error_logs` com
    `Source = 'api_backend'` e `Level = 'error'` na janela (padrão 5 min) e abre acima de `Limite` (padrão 20).
    Só lê o `COUNT`, nunca `Message` nem `Details`. Estado no `endpoint_health_state` com o nome `sistema/5xx`.
  - **Banco fora do ar** não gera evento (não há onde gravar). Quem cobre é o vigia externo da N1.
- **Vigia da F16.** A N10 não cria vigia de token da Meta nem de integração. Entrega a porta, com
  `ComponenteIncidente.Integracao`, para o vigia da F16 chamar quando o teste de uma integração falhar (a F16 hoje
  só prevê lembrete da dona e faixa no console). Sem vigia ainda, não há o que ligar: a ligação entra na PR da F16.
- **Destino.** Rotina `incidente_sistema_global` (N13): audiência `superadmins` (N4), e-mail e WhatsApp, modo
  `todos`, sem janela (incidente ignora horário). TTL curto na quarentena da N1 (2 h, definido na N13; depois disso
  vale o aviso de resolvido).
- **Volume.** No pior caso, quatro avisos por hora por assinatura e por destinatário (96 por dia) contra a cota
  de 1.000 por dia por caixa. A janela configurável é o botão.
- **Fatias (commits verdes).** (1) publicador, avaliador e testes puros; (2) o endpoint do Worker e o POST morto
  removido; (3) snapshot da API e coletor de 5xx.

**Leitura mínima.** `EasyStock.Worker/BackgroundServices/EndpointHealthMonitorService.cs`;
`EasyStock.Worker/Program.cs` (100 a 112); `EasyStock.Domain/Entities/EndpointHealthState.cs`; `EasyStock.Api/BackgroundServices/HealthSnapshotService.cs`;
`EasyStock.Api/Observability/GlobalExceptionHandler.cs` (40 a 60 e 260 a 300);
`EasyStock.Domain/Entities/SystemErrorLog.cs`; `OutboxMensagemNotificacao.cs` (140 a 150);
`NotificadorService.cs` (232 a 250); `docs/plan/atendimento-whatsapp/11-console-fechamento.md` (procure F16);
`EasyStock.Infra.Postgre/Notifications/Collectors/ColetorProdutosVencendo.cs` (molde de coletor);
`EasyStock.Api/Configuration/BackgroundJobOptions.cs`.

**Testes Red.**
- `EasyStock.Application.Tests/Services/Notifications/AvaliadorIncidenteTests.cs`
  - `::TerceiraFalhaAbre`, `::FalhaIsoladaNaoAbre`, `::AbertoReavisa`, `::DuasBoasResolvem`,
    `::RecuperacaoSemIncidenteAbertoNaoAvisa`: hoje a lógica mora num `BackgroundService` sem teste e sem
    "resolvido" (`EndpointHealthMonitorService.cs:139-168`).
- `EasyStock.Application.Tests/Services/Notifications/PublicadorIncidenteSistemaTests.cs`
  - `::PayloadSoTemChavesFechadas`, `::ChaveDeDedupeMudaNaVirada15Min` (relógio falso),
    `::InterruptorDesligadoNaoPublica`, `::SemEmpresaPadraoNaoPublicaENomeiaAChave` e
    `::NaoExisteParametroDeTextoLivre` (reflexão: nenhum parâmetro `string` no método público).
- `EasyStock.Application.Tests/Services/Notifications/IncidentesNaoVazamDadoTests.cs`
  - `::AlertaNaoCarregaExcecaoNemQueryStringNemDadoDeCliente`: alimenta o estado com `ex.Message` contendo um
    e-mail, uma URL com `?token=` e um nome de cliente; o payload serializado, o assunto e o corpo renderizados
    não contêm nenhum deles. Hoje o monitor guarda e envia `ex.Message` (`EndpointHealthMonitorService.cs:136,190-191`).
- `EasyStock.Application.Tests/Services/Notifications/TransicaoSaudeSnapshotTests.cs`
  - `::CriticalAbreEOkResolve`, `::DegradadoPorErrosNaoAlerta`, `::DegradadoPorRedisAlerta`,
    `::BlipDeUmSnapshotNaoAbre`: hoje o snapshot não avalia transição (`HealthSnapshotService.cs:106-176`).
- `EasyStock.ArchitectureTests/RotaMortaCiTicketsTests.cs::NenhumCodigoPostaParaApiCiTickets`
  - varre os `.cs` dos projetos core pelo literal `"/api/ci/tickets"` entre aspas; comentário não conta. Hoje há
    uma ocorrência (`EndpointHealthMonitorService.cs:195`). Detector novo: a prova red-bar vai na PR
    (ADR-0023, item 5), e ele entra como `Category=Architecture` porque a violação some na mesma PR.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/IncidenteSistemaIntegrationTests.cs`
  - `::DuasFalhasNaMesmaJanelaGeramUmaMensagemPorDestinatarioECanal`, `::JanelaSeguinteGeraNovoAviso`,
    `::ResolvidoSaiDepoisDoAberto`, `::ChegaNoMailpitComRemetenteDeAvisos`: hoje o tipo não existe.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/ColetorPicoDeErros5xxIntegrationTests.cs`
  - `::AbaixoDoLimiteNaoAbre`, `::AcimaDoLimiteAbreUmaVez` e `::NaoLeMensagemNemDetalhes` (papel
    `rls_test_client`; `system_error_logs` fica fora da RLS).

**Aceite.**
- [ ] Dadas duas falhas do mesmo componente dentro de 15 min, então sai um e-mail por superadmin (e um WhatsApp
  quando a N6 existir); na janela seguinte, novo aviso.
- [ ] Dada a recuperação, então sai um aviso "normalizado" com a duração; sem incidente aberto, a recuperação não
  avisa.
- [ ] Dada a API fora do ar por 3 verificações seguidas do Worker, então o aviso sai em até 5 min, e nenhuma
  chamada a `/api/ci/tickets` acontece.
- [ ] Dado `degraded` só por linhas de erro, então nenhum aviso; `critical` do banco e `degraded` do Redis avisam.
- [ ] Dado um pico de 5xx acima do limite na janela, então um aviso por janela de dedupe; abaixo do limite, nada.
- [ ] Dado qualquer aviso, então não há mensagem de exceção, query string, e-mail nem dado de cliente (teste com
  sentinelas), e o método público não tem parâmetro de texto.
- [ ] Dado `Notifications:Incidentes:Habilitado=false`, então nada é publicado. Dado o Worker sem
  `Auth__Google__EmpresaPadrao` no `.env`, então nada é publicado e o log nomeia a chave.
- [ ] O vigia da F16 não foi criado, e a porta está pronta para ele.
- [ ] **Validação do Felipe:** conferir `Auth__Google__EmpresaPadrao` no `.env` do Worker e, em homologação, parar
  o container da API por 5 min: chega o e-mail "com problema" e depois o de "normalizado".

**Fora.**
- Vigia de integração e de token da Meta (F16) e WhatsApp real (N6).
- Banco fora do ar: o vigia externo da N1 cobre.
- Tela de incidentes, alerta por tenant, tabela de incidentes e contador de cota por caixa.
- `DiagnosticoEmailReportJob`: continua desligado.

**Depende de.** N13 (tipo, rotina, templates e `IEmpresaPadraoResolver`), N5 (modo `todos`), N4 (audiência e chave
por destinatário), N3 e N1 (Worker como host único e escopo por item). O WhatsApp depende da N6.

**Tamanho.** M, fatiada em 3 commits verdes.

**Tier.** ALTO. A ADR-0055 (item 1, `docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:18-20`) sozinha daria
baixo: spec de plano aprovado, sem migração (reaproveita `endpoint_health_state`), RLS, autenticação, policy nem
arquivo de entrada, porque os serviços novos entram pelas extensões de DI que já existem, não pelo `Program.cs`. A R5
(`CLAUDE.md:69-71`) manda ALTO: publicador, avaliador, coletor, dois serviços alterados, DI e testes passam de 5
arquivos e de 100 linhas, e a ADR-0057 (item 11) manda valer a regra mais restritiva. **Corrigido:** o plano marca a
N10 como baixo. A PR espera a label `aprovado`.

**Rollback.** `Notifications:Incidentes:Habilitado=false` desliga sem deploy; `git revert` do squash devolve o
POST morto (que já não fazia nada). Sem migração: `LastFailureMessage` passa a guardar um código curto, que cabe
na coluna.

---

## N11 · Prazo estourado chega à dona e à equipe · issue aberta ao iniciar

**Problema.** Os quatro prazos que o Felipe escolheu já são medidos em algum lugar, mas nenhum chega ao motor:
cada um termina em tela, em Web Push ou no sino, e nunca em e-mail ou WhatsApp.

| # | Achado | Prova |
|---|---|---|
| 1 | Nenhum dos quatro prazos chega ao motor por e-mail ou WhatsApp: pedido atrasado e impressão travada só publicam SSE, o lembrete só vai por Web Push e o caixa só vai ao sino in-app | **código** `NotificarAtrasoPedidoUseCase.cs:35`; `AlertarImpressoesAtrasadasUseCase.cs:26-27`; `AvaliarLembretesUseCase.cs:99`; `CaixaEsquecidoJob.cs:127-129` |
| 2 | Os limites são constantes: 10 min sem resposta, 15 min sem baixa, 3 min de impressão, rodada de 2 min | **código** `AvaliarLembretesUseCase.cs:36-37`; `AlertarImpressoesAtrasadasUseCase.cs:17`; `ImpressaoPendenteAlertaJob.cs:15` |
| 3 | O console usa dois números: 10 min no lembrete (igual ao backend) e 5 min no selo "Precisa de você". Não é divergência simples, são dois sinais (corrige o plano) | **código** `lembrete.js:15`; `automatico.js:201,237` |
| 4 | `AlertarImpressoesAtrasadasUseCase` repete a cada rodada enquanto a impressão estiver pendente e não grava marca: ligar ao motor sem dedupe mandaria um e-mail a cada 2 min | **código** `AlertarImpressoesAtrasadasUseCase.cs:6-11,24-28`; `ImpressaoPendenteAlertaJob.cs:15` |
| 5 | O use case das impressões lê em bypass curto e não grava. Gravar o evento de outra empresa exige um escopo por impressão com o tenant ligado (padrão do `PedidoAtrasoJob`), senão a policy recusa (42501) | **código** `ImpressaoPendenteRepository.cs` (`ListarAtrasadasAsync`); `PedidoAtrasoJob.cs:64-66`; `Migrations/20260511120000_AddRowLevelSecurity.cs:89-98` |
| 6 | O pedido atrasado já tem marca e transação (`AtrasoNotificadoEm`, `SELECT FOR UPDATE`, commit): é o ponto certo para enfileirar o evento de forma atômica (ADR-0030) | **código** `NotificarAtrasoPedidoUseCase.cs:41-52` |
| 7 | O lembrete "cliente sem resposta" nasce vencido e é avisado na mesma rodada: o gancho é o `AvisarVencidosAsync`, no mesmo commit do carimbo. O host já liga o bypass e segura o advisory lock | **código** `AvaliarLembretesUseCase.cs:60-71,88-103`; `AvaliadorLembretesBackgroundService.cs:44-57` |
| 8 | O `CaixaEsquecidoJob` roda uma vez por dia às 10:00 UTC (07:00 BRT), faz tudo dentro de um `ProcessarAsync` privado e carimba depois de publicar. Não dá para testar o aviso sem extrair o miolo | **código** `CaixaEsquecidoJob.cs:33,82-148` |
| 9 | A janela de horário que "respeitando janela" pressupõe não existe no motor (N5, achado 10). O `CorrelationId` é `varchar(64)`, então a chave determinística precisa caber: o coletor de produtos estoura com 65 caracteres | **código** `EventoNotificacaoConfiguration.cs:19`; **medido** 65 caracteres em `ColetorProdutosVencendo.cs:50,62` |

**Abordagem.**
- **Evento `PrazoEstourado`** (tipo novo da N13). Payload sem dado de cliente: `tipo_legivel`, `referencia`
  (código curto de 8 caracteres do pedido, da conversa, da impressão ou da abertura de caixa), `prazo_texto` e
  `atraso_texto`. `chaveIdempotencia = prazo:{tipo}:{referenciaId}` e `CorrelationId = prazo:{tipo}:{referenciaId:N}`
  (no máximo 59 caracteres; o `tipo` é um de `cliente_sem_resposta`, `pedido_atrasado`, `impressao_travada`,
  `caixa_esquecido`).
- **Adaptadores.** Cada um enfileira com `EnfileirarEventoAsync`, na unidade de trabalho do próprio fato
  (ADR-0030):
  1. `AvaliarLembretesUseCase.AvisarVencidosAsync`: para lembretes automáticos `ClienteSemResposta`, ao lado do
     `LembreteVencido` (Web Push). O `ParaUsuarioId` (atendente) entra como `usuarioId` no payload e vira o
     destinatário; sem atendente, a audiência `gestores`. `PagamentoSemBaixa` e lembrete manual ficam só no Push,
     porque estão fora de Q2.
  2. `NotificarAtrasoPedidoUseCase.MarcarNoLockAsync`: enfileira antes do `CommitAsync()` de `:48`, então evento e
     marca saem no mesmo commit. O use case passa a receber `INotificadorService` (R8: `PedidoAtrasoJobTests`).
  3. Impressão travada: o job passa a receber a lista de atrasadas do use case (hoje devolve só a contagem, R8
     nos dois call-sites) e, por impressão, abre um escopo próprio e chama `NotificarImpressaoTravadaUseCase`,
     que liga o tenant, pré-checa o `CorrelationId` determinístico em
     `IEventoNotificacaoRepository.ExisteCorrelacaoAsync(empresaId, correlationId)` (sem migração; a N12 acrescenta
     o índice único como segunda trava) e enfileira. O SSE segue repetindo a cada rodada, o e-mail não.
  4. `CaixaEsquecidoJob`: o miolo de `ProcessarAsync` é extraído para `AvisarCaixasEsquecidasUseCase`
     (Application, sem mudar comportamento, no primeiro commit) e então acrescenta o `PrazoEstourado
     {caixa_esquecido}` ao lado do `CaixaAbertoEsquecido`, que segue alimentando o sino. A marca
     `NotificadoEsquecidoEm` continua única, e o `CorrelationId` determinístico impede aviso duplo se o processo
     cair entre publicar e carimbar.
- **Limites em configuração.** `Notifications:Prazos` (`IOptions<PrazosOptions>`): `ClienteSemRespostaMin` (10) e
  `ImpressaoPendenteMin` (3). As constantes públicas deixam de ser a fonte. Pedido atrasado e caixa esquecido não
  ganham limite numérico (usam o início previsto e o dia anterior).
- **Destino e janela.** Rotina `prazo_estourado_global` (N13): audiência `gestores` (Admin e Gerente, N4), e-mail
  e WhatsApp, janela de 07:00 a 22:00 de Brasília. A janela é aplicada pela N5: fora dela o aviso é adiado,
  nunca suprimido, e a quarentena da N1 descarta o que já não vale (6 h, definida na N13, contada da abertura da
  janela).
- **Console.** O selo de 5 min é um sinal visual, diferente do alerta externo, que usa o limite do backend. As
  opções são (A) manter os dois, (B) selo em 10 min ou (C) alerta em 5 min; o padrão desta spec é **A**, e a
  decisão é do Felipe.
- **Interruptor.** `Notifications:Prazos:Habilitado` (padrão `true`); `false` pula o enfileiramento nos quatro
  adaptadores, sem deploy.
- **Fatias (commits verdes).** (1) o evento, a configuração e o cliente sem resposta; (2) pedido atrasado;
  (3) impressão travada; (4) extração do `AvisarCaixasEsquecidasUseCase` e o caixa esquecido.

**Leitura mínima.** `EasyStock.Application/UseCases/Atendimento/Lembretes/AvaliarLembretesUseCase.cs`;
`EasyStock.Api/BackgroundServices/AvaliadorLembretesBackgroundService.cs`;
`EasyStock.Api/BackgroundServices/PedidoAtrasoJob.cs` e
`EasyStock.Application/UseCases/Operacao/Atraso/NotificarAtrasoPedidoUseCase.cs`;
`EasyStock.Api/BackgroundServices/ImpressaoPendenteAlertaJob.cs`,
`EasyStock.Application/UseCases/Operacao/Impressao/AlertarImpressoesAtrasadasUseCase.cs` e
`EasyStock.Infra.Postgre/Repositories/Operacao/ImpressaoPendenteRepository.cs`;
`EasyStock.Api/BackgroundServices/CaixaEsquecidoJob.cs`;
`EasyStock.Console/src/dominio/automatico.js` (190 a 240) e `lembrete.js` (10 a 20);
`EasyStock.Application/Ports/Output/Notifications/INotificadorService.cs`;
`EasyStock.Infra.Postgre.IntegrationTests/Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs` (molde RLS).

**Testes Red.**
- `EasyStock.Application.Tests/UseCases/Atendimento/Lembretes/AvaliarLembretesPrazoEstouradoTests.cs`
  - `::ClienteSemRespostaVencidoEnfileiraPrazoEstourado`, `::PagamentoSemBaixaNaoEnfileiraPrazoEstourado`,
    `::LimiteVemDaConfiguracao`, `::AtendenteAssumidoViraODestinatarioDoAviso`: hoje só sai o `LembreteVencido`
    (`AvaliarLembretesUseCase.cs:99`), e o tipo não existe.
- `EasyStock.Application.Tests/UseCases/Operacao/NotificarAtrasoPedidoPrazoEstouradoTests.cs`
  - `::EventoSaiNoMesmoCommitDaMarca` (a ordem importa: `EnfileirarEventoAsync` antes do `CommitAsync`) e
    `::PedidoJaNotificadoNaoEnfileira`.
- `EasyStock.Application.Tests/UseCases/Operacao/NotificarImpressaoTravadaUseCaseTests.cs`
  - `::ImpressaoTravadaGeraUmEventoMesmoComVariasRodadas`, `::LimiteVemDaConfiguracao`,
    `::LigaOTenantDaImpressaoAntesDeGravar`: hoje o use case não grava (`AlertarImpressoesAtrasadasUseCase.cs:24-28`).
- `EasyStock.Application.Tests/UseCases/Caixa/AvisarCaixasEsquecidasUseCaseTests.cs`
  - `::SemMudarComportamentoPublicaCaixaAbertoEsquecido` (caracterização do miolo extraído) e
    `::TambemEnfileiraPrazoEstouradoComChaveDeterministica`.
- `EasyStock.Application.Tests/Services/Notifications/PrazoEstouradoCorrelacaoTests.cs`
  - `::CorrelationIdCabeEmSessentaEQuatroCaracteres`, para os quatro tipos com `Guid` completo (o coletor de
    produtos falha este mesmo teste hoje).
- `EasyStock.Infra.Postgre.IntegrationTests/Operacao/PrazoEstouradoPedidoAtrasoIntegrationTests.cs`
  - `::MarcaEEventoSaemNoMesmoCommitSobRls` (papel `rls_test_client`, tenant do pedido ligado) e
    `::EventoDeImpressaoGravaSobRlsComTenantDaImpressao`: hoje não há evento.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/PrazoEstouradoChegaNoMailpitIntegrationTests.cs`
  - `::PedidoAtrasadoChegaPorEmailComRemetenteDeAvisos`.

**Aceite.**
- [ ] Dado um lembrete "cliente sem resposta" vencido, quando o avaliador roda, então sai um `PrazoEstourado`, e
  rodar três vezes não gera o segundo.
- [ ] Dado um pedido com o início previsto vencido, então marca e evento saem no mesmo commit; se o commit falha,
  não saem nem a marca nem o evento.
- [ ] Dada uma impressão pendente por 6 rodadas, então um evento (o SSE segue repetindo).
- [ ] Dado um caixa aberto de ontem, então o sino segue como hoje e sai também um `PrazoEstourado`; o dedupe
  `NotificadoEsquecidoEm` segue em um aviso por sessão.
- [ ] Dado o limite configurado em 5 min, então o aviso usa 5; sem configuração, 10 e 3.
- [ ] Dado qualquer payload do tipo, então não há chave de cliente (nome, telefone, e-mail) e o `CorrelationId`
  tem no máximo 64 caracteres.
- [ ] Dado um evento às 23:30, então o aviso é adiado para 07:00 (janela, N5) e não sai fora dela.
- [ ] Dado `Notifications:Prazos:Habilitado=false`, então nenhum adaptador enfileira.
- [ ] O SSE e o Web Push seguem iguais: os testes existentes dos quatro fluxos ficam verdes.

**Fora.**
- Pagamento sem baixa e lembrete manual por e-mail e WhatsApp, e qualquer prazo novo.
- Remover as constantes públicas dos use cases: ficam como padrão da opção.
- Mudar a hora do `CaixaEsquecidoJob` ou dar advisory lock ao `PedidoAtrasoJob`.
- O selo de 5 min do console.

**Depende de.** N13 (tipo, rotina e template), N5 (janela, `correlationId` e modo `todos`), N4 (audiência
`gestores`), N3 e N1.

**Tamanho.** M, fatiada por adaptador em 4 commits verdes.

**Tier.** ALTO. A ADR-0055 (item 1, `docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:18-20`) sozinha daria
baixo: spec de plano aprovado, sem migração, RLS, autenticação, policy nem arquivo de entrada. A R5
(`CLAUDE.md:69-71`) manda ALTO: quatro adaptadores, dois use cases novos, a extração do `CaixaEsquecidoJob`, a
configuração e os testes passam de 5 arquivos e de 100 linhas, e a ADR-0057 (item 11) manda valer a regra mais
restritiva. **Corrigido:** o plano marca a N11 como baixo. O que muda em produção é aditivo (um evento a mais por
fato, atrás de interruptor); o SSE e o Push não mudam. A PR espera a label `aprovado`.

**Rollback.** `Notifications:Prazos:Habilitado=false` desliga sem deploy; `git revert` do squash devolve os
quatro fluxos como estavam. Sem migração.

---

## N12 · Rotinas agendadas e resumo diário · issue aberta ao iniciar

**Problema.** Não existe agendamento no motor, e o jeito como o repositório agenda hoje (esperar até uma hora UTC
dentro do processo) perde o dia quando o processo reinicia na hora errada.

```
rotina da empresa (ParametrosJson.agenda.horario) ─► ColetorRotinasAgendadas (a cada 5 min, no loop do motor)
   devida e sem evento do dia? ─► construtor do tipo ─► EnfileirarEventoAsync(correlationId = agenda:{rotinaId}:{dia})
   índice único (EmpresaId, CorrelationId) = "última execução persistida" e trava contra dois hosts
```

| # | Achado | Prova |
|---|---|---|
| 1 | O ramo cron do Avaliador é stub: `DeveriasExecutar` calcula, e o laço só loga "matched", sem criar evento nem guardar a última execução. A janela de avaliação é deslizante e sem estado | **código** `NotificacoesAvaliadorOrchestrator.cs:55-68`; `RotinaScheduler.cs:26-40` |
| 2 | A API aceita criar rotina `Cron` que nunca dispara (o campo mente), e o pacote `Cronos` só existe para isso | **código** `NotificacoesConfiguracaoController.cs:153-158`; `RotinaNotificacao.cs:38-39`; `EasyStock.Application.csproj:22`; **medido** `Cronos` só aparece em `RotinaScheduler.cs` |
| 3 | A decisão de 08/08 vale: o cron não será ligado, a janela é o controle e adiar é `ProximaTentativaEm` no outbox. A agenda desta spec é horário diário local, sem cron | memória do projeto (`notificacoes-ficam-no-easystok`); `OutboxNotificacaoRepository.cs:19` |
| 4 | O padrão de job diário do repositório é `Task.Delay` até uma hora UTC fixa, em memória: reiniciar o processo na hora perde o dia, não há catch-up e a hora é UTC | **código** `CaixaEsquecidoJob.cs:33-52`; `ContaFinanceiraVencimentoJob.cs:40-43,107` |
| 5 | O coletor já roda a cada 5 min no loop do motor, e o contrato `IColetorEventoNotificacao` é `ColetarAsync(ct)`: serve de entrada, desde que o "já rodou hoje?" seja persistido | **código** `NotificationsHostingOptions.cs:44`; `ColetorLoopHostedService.cs`; `IColetorEventoNotificacao.cs` |
| 6 | `notif_eventos` não tem índice único por `CorrelationId`: só `(Status, OcorridoEm)` e `(EmpresaId, Tipo)`. A dedupe do coletor atual é "ler e depois inserir", com corrida entre dois hosts | **código** `EventoNotificacaoConfiguration.cs:22-23`; `ColetorProdutosVencendo.cs:53-64` |
| 7 | O `ColetorProdutosVencendo`, único coletor registrado, usa data UTC, grava `CorrelationId` de 65 caracteres numa coluna de 64 (nunca gravou) e tem payload que não casa com o template (só `quantidade` coincide) | **código** `ColetorProdutosVencendo.cs:31,45,50,62,66-76`; `Migrations/20260506221516_AddNotificationsCore.cs:112`; `produto_vencendo_email_v1.html`; **medido** 65 caracteres por script |
| 8 | O resumo do dia já existe: `GetResumoDiaAsync` devolve entregues, faturamento, ticket médio, pendentes, caixa, saldo e Pix na janela de Brasília, com cache de 30 s | **código** `AnalyticsRepository.ResumoDia.cs:13-147`; `AnalyticsRepository.cs:54`; `IAnalyticsRepository.cs:168` |
| 9 | Fora de um escopo com tenant ligado, as consultas do resumo voltam vazias sem erro (filtro do EF e RLS), e o resumo viraria "tudo zero" | **inferência** do padrão de `NotificarAtrasoPedidoUseCase.cs:29` e do bypass em `CaixaEsquecidoCrossTenantRlsTests`; o teste de integração prova |
| 10 | `ContaFinanceiraVencimentoJob` (flag padrão `true`) publica `ContaPagarVencendo` e cia. sem rotina semeada e sem destinatário no payload, e carimba `NotificadaD3Em`: o aviso some e a marca impede o reaviso | **código** `BackgroundJobOptions.cs:36`; `ContaFinanceiraVencimentoJob.cs:133-134,151-152,169-170,185-186,205-206,225-226`; **medido** o seed não tem rotina para os tipos 32 a 36 |
| 11 | A rotina da empresa criada pela API nasce sem canais (N5, achado 12), então "ligar o resumo para a Casa da Baba" depende da N5 | **código** `RotinaNotificacaoCommands.cs:9-18` |

**Abordagem.**
- **Contrato de agenda, sem cron.** `RotinaNotificacao.ParametrosJson.agenda = { "horario": "HH:mm" }`, em hora
  de parede de Brasília. Ao criar ou atualizar rotina, o horário é validado (`HH:mm`) e `TriggerTipo = Cron`
  passa a responder 400 ("use `agenda.horario`"). O ramo cron do Avaliador (`:55-68`) sai, com o
  `RotinaScheduler`, o parâmetro do construtor (`NotificacoesAvaliadorOrchestrator.cs:18`), os testes dele e o
  pacote `Cronos`: código morto que mente, removido com os testes (ADR-0023, item 6). A coluna `CronExpression` e
  o valor `Cron` do enum ficam (linhas antigas continuam legíveis).
- **`AgendaDiariaLocal`** (Application, puro, sobre `HorarioBrasil`): `Devida(horario, agoraUtc)`,
  `DiaLocal(agoraUtc)` e `Chave(rotinaId, diaLocal)`. A rotina é devida quando a hora de parede de Brasília já
  passou do horário no dia local de hoje. **Catch-up só dentro do mesmo dia local:** se o Worker ficou fora a
  noite toda, o dia é pulado (um resumo de ontem na manhã seguinte engana). A virada do dia UTC (21:00 BRT) não
  adianta nem atrasa, porque a conta usa `HorarioBrasil.Hoje()` e `InstanteUtc`, nunca `UtcNow.Date`.
- **`ColetorRotinasAgendadas : IColetorEventoNotificacao`** (Infra.Postgre/Notifications/Collectors, registrado ao
  lado do atual com `TryAddEnumerable`, como a N1 deixa o registro). A cada rodada lista as rotinas **da empresa**
  ativas com `agenda` (uma rotina global com `agenda` é ignorada, com aviso) e, para cada uma devida e ainda sem
  evento do dia:
  - a listagem é a única leitura entre tenants: roda no escopo da rodada, com `IRowLevelSecurityBypass.Begin()` em
    transação curta, como a N1 faz com o coletor atual, e o arquivo do coletor entra na `Allowlist` de
    `RlsBypassAllowlistTests` com a issue #1344 e o motivo "cross-tenant por natureza". Depois disso, cada rotina
    abre um escopo de DI próprio com o tenant da empresa ligado antes da primeira conexão;
  - monta o payload com o construtor do tipo (`IConstrutorPayloadAgendado`, registrado por
    `TipoEventoNotificacao`). **O motor é genérico:** o resumo é o primeiro construtor, e o segundo tipo
    agendado só acrescenta um;
  - enfileira com `EnfileirarEventoAsync(..., correlationId: "agenda:{rotinaId:N}:{yyyyMMdd}")` (48 caracteres) e
    commita. Violação do índice único (23505) é lida como "outro host já fez" e ignorada.
  - "Última execução persistida" é o próprio evento do dia: pré-checagem barata por
    `(EmpresaId, CorrelationId)` antes de montar o payload (a parte cara) e índice único como trava final. Sem
    tabela nova.
- **Índice único** `(EmpresaId, CorrelationId)` em `notif_eventos`, migração aditiva
  `AddIndiceUnicoCorrelacaoEvento`. Antes de aplicar, a PR roda e cola a contagem de duplicatas
  (`SELECT "EmpresaId", "CorrelationId", count(*) FROM notif_eventos GROUP BY 1, 2 HAVING count(*) > 1`); como o
  `CorrelationId` padrão é um `Guid` aleatório, o esperado é zero.
- **Resumo diário** (`ResumoDiario`, tipo e templates da N13). `ConstrutorPayloadResumoDiario` chama
  `IAnalyticsRepository.GetResumoDiaAsync(empresaId, null)` e formata em pt-BR: data, entregues, faturamento,
  ticket médio, pendentes e valor, caixa (aberto com saldo, fechado ou sem caixa) e Pix. Roda com o tenant da
  empresa ligado, senão o filtro devolve zero sem erro. **Opt-in por empresa:** `resumo_diario_global` entra
  inativa (molde), e o resumo só roda para a empresa que tiver a própria rotina ativa (a Casa da Baba), com
  `agenda.horario` padrão `20:00`, audiência `admins` e os canais da N5. Isso evita mandar e-mail aos
  administradores de todo tenant de demonstração. Para ligar: `POST api/notificacoes/rotinas` (policy `Admin`,
  com `canais`, da N5) e `PATCH .../ativar`. Um resumo que não saiu em 4 h (quarentena da N13) expira em vez de
  chegar fora de hora.
- **Produtos vencendo continua fora** (Q3), agora explícito: `ColetorProdutosVencendo` só é registrado com
  `Notifications:Coletores:ProdutosVencendo:Habilitado=true` (padrão `false`). Sem isso, assim que a N1 destravar a
  leitura, o coletor passaria a falhar a cada 5 min (o `CorrelationId` de 65 caracteres) e a poluir o log. Os três
  defeitos (UTC, 65 caracteres e payload que não casa com o template) ficam na issue para quem ligar.
- **Contas vencendo continua desligado** (Q3): `BackgroundJobs:EnableContaFinanceiraNotificacoes` (padrão `false`)
  faz o `ContaFinanceiraVencimentoJob` pular as coortes D-3 e D-1 e, na coorte de vencidas, só a publicação e o
  carimbo, mantendo a marcação da parcela vencida (regra de negócio) e sem gastar o carimbo, para o aviso não se
  perder quando ligarem (a N1 já faz o carimbo depender de a publicação dar certo; a flag pula a publicação, então
  nada carimba). O job ganha um ponto de entrada `internal` e o `EasyStock.Api.csproj` um
  `InternalsVisibleTo` para o projeto de integração (a spec já é ALTO).
- **Fatias (commits verdes).** (1) `AgendaDiariaLocal` e o construtor do resumo, com testes puros; (2) índice
  único, coletor genérico e integração; (3) remoção do cron e validação de agenda; (4) flags de produtos e de
  contas.

**Leitura mínima.** `EasyStock.Application/Services/Notifications/Orchestrators/NotificacoesAvaliadorOrchestrator.cs`
e `INotificacoesAvaliadorOrchestrator.cs`; `EasyStock.Application/Services/Notifications/RotinaScheduler.cs`;
`EasyStock.Infra.Postgre/Notifications/Collectors/ColetorProdutosVencendo.cs`;
`EasyStock.Application/DependencyInjection/ServiceCollectionExtensions.Notifications.cs` (linha 16) e
`EasyStock.Infra.Postgre/DependencyInjection/ServiceCollectionExtensions.Notifications.cs` (linha 39);
`EasyStock.Application/Services/Notifications/NotificationsHostingOptions.cs`;
`EasyStock.Infra.Notifications/Hosting/ColetorLoopHostedService.cs`;
`EasyStock.Domain/Entities/Notifications/EventoNotificacao.cs` e
`EasyStock.Infra.Postgre/Data/Configurations/Notifications/EventoNotificacaoConfiguration.cs`;
`EasyStock.Application/Common/HorarioBrasil.cs`; `EasyStock.Infra.Postgre/Repositories/AnalyticsRepository.ResumoDia.cs`;
`EasyStock.Api/BackgroundServices/ContaFinanceiraVencimentoJob.cs` (100 a 235) e
`EasyStock.Api/Configuration/BackgroundJobOptions.cs`;
`EasyStock.Application/UseCases/Notifications/RotinaNotificacaoCommands.cs`;
`EasyStock.Api/Controllers/NotificacoesConfiguracaoController.cs` (150 a 170);
`EasyStock.Application/EasyStock.Application.csproj`; a migração mais recente e
`EasyStock.ArchitectureTests/MigrationDesignerHygieneTests.cs`;
`EasyStock.Infra.Postgre.IntegrationTests/Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs` (molde).

**Testes Red.**
- `EasyStock.Application.Tests/Services/Notifications/AgendaDiariaLocalTests.cs`
  - `::DevidaDepoisDoHorarioNoDiaLocal`, `::NaoDevidaAntesDoHorario`,
    `::ViradaDoDiaUtcNaoAdiantaNemAtrasa` (23:30Z de 01/10 é 20:30 de Brasília do dia 01/10),
    `::PerdeODiaQuandoPassouDaMeiaNoite`, `::ChaveUsaADataLocalECabeEmSessentaEQuatroCaracteres`,
    `::HorarioInvalidoEhRecusado`: hoje o padrão do repositório é `Task.Delay` até hora UTC
    (`CaixaEsquecidoJob.cs:33-52`).
- `EasyStock.Application.Tests/Services/Notifications/ConstrutorPayloadResumoDiarioTests.cs`
  - `::FormataValoresEmPtBr`, `::CaixaAbertoFechadoESemCaixaViramTextoCerto`, `::UsaOResumoDoDiaDeBrasilia`
    (com `IAnalyticsRepository` falso).
- `EasyStock.Application.Tests/UseCases/Notifications/RotinaAgendadaValidacaoTests.cs`
  - `::HorarioForaDoFormatoRecusa` e `::CronNaoEhCriavel`: hoje a API cria e nunca dispara
    (`RotinaNotificacao.cs:38-39`).
- `EasyStock.Application.Tests/Services/Notifications/Orchestrators/NotificacoesAvaliadorOrchestratorTests.cs`
  - `::NaoTemMaisRamoCron`: hoje o construtor recebe o `RotinaScheduler` (`NotificacoesAvaliadorOrchestrator.cs:18`).
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/ColetorRotinasAgendadasIntegrationTests.cs`
  - `::TresRodadasNoMesmoDiaGeramUmEvento`, `::DoisHostsSimultaneosGeramUmEvento` (dois coletores em
    `Task.WhenAll`), `::NovoDiaGeraNovoEvento` (relógio falso), `::RotinaGlobalInativaNaoRoda`,
    `::RotinaDeOutraEmpresaNaoVaza` (papel `rls_test_client`) e `::ResumoVemDaEmpresaCerta` (duas empresas com
    pedidos diferentes; o DI é montado como o do Worker, com `AddEasyStockPostgreInfrastructure` e
    `AddEasyStockApplication`). Hoje não há coletor nem índice.
- `EasyStock.Infra.Postgre.IntegrationTests/Notifications/IndiceUnicoCorrelacaoMigrationTests.cs`
  - `::MesmaCorrelacaoNaMesmaEmpresaViolaOIndice`, `::MesmaCorrelacaoEmEmpresasDiferentesPassa` e
    `::MigracaoPreservaEventosExistentes`.
- `EasyStock.Infra.Postgre.IntegrationTests/Workflows/ContaFinanceiraNotificacoesDesligadasIntegrationTests.cs`
  - `::PadraoMarcaParcelaVencidaENaoPublicaNemCarimba`: hoje publica e carimba
    (`ContaFinanceiraVencimentoJob.cs:133-134,205-206`).
- `EasyStock.Api.UnitTests/BackgroundServices/BackgroundJobRegistrationTests.cs`
  - `::ColetorDeProdutosVencendoNaoNasceRegistrado`: hoje é registrado sempre
    (`ServiceCollectionExtensions.Notifications.cs:39`).

**Aceite.**
- [ ] Dada uma empresa com rotina `ResumoDiario` ativa e `agenda.horario = 20:00`, quando o relógio de Brasília
  passa das 20:00, então sai um evento no dia; rodar o coletor três vezes, ou dois hosts juntos, não gera o
  segundo; no dia seguinte sai outro.
- [ ] Dado o Worker parado até 00:10 do dia seguinte, então o dia é pulado (sem resumo de ontem). Dado ele de
  volta às 23:50, então o resumo sai no mesmo dia.
- [ ] Dado 21:30 de Brasília (00:30 UTC do dia seguinte), então a data do resumo e da chave é a do dia local.
- [ ] Dada a rotina global inativa, então nenhuma empresa recebe; a empresa A não gera nem enxerga o resumo da B.
- [ ] Dado o resumo, então traz entregues, faturamento, ticket médio, pendentes, caixa e Pix do dia local da
  empresa, e nenhum dado de outra.
- [ ] Dado `POST api/notificacoes/rotinas` com `TriggerTipo = Cron` ou horário fora do formato, então 400.
- [ ] Dado `Notifications:Coletores:ProdutosVencendo:Habilitado` ausente, então o coletor não é registrado e não
  há erro de 65 caracteres no log a cada 5 min.
- [ ] Dado `EnableContaFinanceiraNotificacoes=false`, então a parcela vencida é marcada e nenhum evento sai nem
  carimbo grava.
- [ ] Dada a migração, então os eventos antigos ficam intactos e o `Down` remove só o índice.
- [ ] A PR traz a contagem de duplicatas de `(EmpresaId, CorrelationId)` colada, e o pacote `Cronos` saiu.
- [ ] **Validação do Felipe:** ligar o resumo da Casa da Baba pelo `POST` de rotinas e receber o e-mail das 20:00
  de Brasília, e o WhatsApp quando a N6 existir.

**Fora.**
- Contas a pagar e a receber vencendo e produtos vencendo: decisão do Felipe (Q3). O que sobra para quem ligar
  está na issue (os três defeitos do coletor e o payload do job de contas).
- Fuso por loja, mais de um horário por dia, agenda semanal ou mensal, e tela do console para o horário.
- Um segundo tipo agendado: o motor está pronto, e cada tipo novo só acrescenta um construtor.
- Retenção e limpeza de `notif_eventos`.

**Depende de.** N13 (tipo `ResumoDiario`, rotina molde e templates), N5 (`correlationId`, canais na API e modo
`todos`), N4 (audiência `admins`), N1 (varredura entre tenants pela porta de bypass, escopo por item e registro
idempotente do coletor) e N3. O WhatsApp do resumo depende da N6.

**Tamanho.** G, fatiada em 4 commits verdes.

**Tier.** ALTO. Migração EF (índice único em `notif_eventos`), pela ADR-0055 (item 2,
`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:21-22`) e pela R5 (`CLAUDE.md:69-71`). O plano já marcava
ALTO se houvesse índice. A PR fica aberta até a label `aprovado`.

**Rollback.** O `Down` remove o índice, sem perda de dados. `Ativa = false` na rotina do resumo da empresa
desliga sem deploy. `BackgroundJobs:EnableContaFinanceiraNotificacoes=true` e
`Notifications:Coletores:ProdutosVencendo:Habilitado=true` religam os produtores antigos. `git revert` devolve o
ramo cron de mentira e o `Cronos`.

---

## Rollback do marco

M3 tem quatro PRs independentes na reversão, com ordem sugerida N12, N11, N10 e por último N4, porque as três
primeiras consomem a audiência e a chave por destinatário da N4. A N10 e a N11 não têm migração. As duas
migrações aditivas são `AddContatoUsuario`, na N4 (`Down` remove três colunas), e `AddIndiceUnicoCorrelacaoEvento`,
na N12 (`Down` remove o índice). Interruptores sem deploy: `Notifications:Audiencia:Habilitada`,
`Notifications:Prazos:Habilitado`, `Notifications:Incidentes:Habilitado` e `Ativa = false` nas rotinas novas.
Reverter a N4 reabre a troca de e-mail sem senha: registrar na issue. O enum guarda o nome como texto: antes de
reverter o código que remove um tipo, desativar a rotina e fechar os eventos pendentes dele, como na N13.
