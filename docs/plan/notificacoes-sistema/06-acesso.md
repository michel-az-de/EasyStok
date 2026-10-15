# M5 · Acesso: credenciais, esqueci senha e primeiro acesso (N7, N8, N9)

Plano: [README](README.md) · Decisão: [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md) · Issue-mãe: #1344 · Data: 2026-10-01
Base medida: master `c2eb85ca`, .NET 10. O último commit (#1343) só deixa o superadmin escolher a empresa no login por senha e
não toca nada daqui. Regras comuns das specs: [README](README.md#regras-que-valem-para-todas-as-specs).

Legenda da coluna **Prova**: `caminho:linha` lido nesta data. **inferência** marca o que saiu do código sem ser reproduzido.

## Veredito

**HOJE A SENHA ERRADA NO PASSO 1 DO LOGIN NÃO CONTA, O TOKEN VALE ATÉ 8 HORAS DEPOIS DE A SENHA MUDAR E O E-MAIL DE REDEFINIÇÃO SAI
POR FORA DO MOTOR.** O primeiro acesso por convite, decidido em 01/10, não tem peça nenhuma no código: a dona define a senha de
cada funcionário. Este marco fecha os três buracos nesta ordem, e cada spec usa a anterior.

```
N7 revogar sessão ─► N8 esqueci a senha ─► N9 convite
  └─ RevogadorSessoes ──────┴────────────────┘         usado por N8 e N9
N6 WhatsApp de plataforma ─► N8 (código) e N9 (convite pelo WhatsApp)
reset_tokens com Finalidade: a N8 cria a base e a N9 acrescenta o valor Convite
```

**Contratos dos segredos** (valem para N8 e N9):

| Segredo | `Finalidade` | Formato | Validade | Tentativas | Canal | Spec |
|---|---|---|---|---|---|---|
| Link de redefinição | `Reset` | 32 bytes aleatórios em base64url | 30 min | 1 uso | e-mail | N8 |
| Código de redefinição | `ResetCodigo` | 6 dígitos | 10 min | 5 erros | WhatsApp, modelo de autenticação `codigo_redefinir_senha` | N8 |
| Convite | `Convite` | 32 bytes aleatórios em base64url | 72 h | 1 uso | e-mail e WhatsApp, 1 token por canal | N9 |

- O pedido de redefinição gera o link e, se a conta for elegível, o código. Um evento só, e o modo `todos` do motor (N5)
  entrega cada segredo pelo seu canal.
- Só o hash vai ao banco (SHA-256; no código, o hash de `{IdDoToken}:{código}`).
- Uso único por `UPDATE` condicional (linha afetada igual a 1), nunca ler e depois gravar.
- Abrir o link (GET) nunca consome.
- Conta que existe e conta que não existe recebem a mesma resposta.
- Nunca e-mail, telefone, token ou código em log.

---

## N7 · Contar toda falha e revogar a sessão na hora · issue aberta ao iniciar

**Problema.**

| # | Achado | Prova |
|---|---|---|
| 1 | O passo 1 do login confere a senha e **não conta falha**. O contador (5 falhas, bloqueio de 15 min) só existe no passo 2, e o console chama o passo 1 primeiro. Sobra o teto de 20 por minuto por IP, compartilhado com o login: até 28.800 tentativas por dia por IP sem bloqueio | `ListarEmpresasParaLoginUseCase.cs:38-43`; `AutenticarUsuarioUseCase.cs:62-73`; `Console/src/features/login/TelaLogin.jsx:34`; `ApiServiceCollectionExtensions.cs:201-213` |
| 2 | O JWT não é revogável: `AddJwtBearer` sem `Events`, e o token leva nível e permissões. Vale 480 min no `appsettings.json` e 60 em `docker-compose.yml`, `docker-compose.azure.yml` e `k8s/configmap.yaml` (480 no compose local). O compose da VPS não está no repositório: o valor efetivo lá não foi medido | `ApiServiceCollectionExtensions.cs:85-104`; `JwtTokenService.cs:23-43`; `appsettings.json:67`; `docker-compose.yml:34`; `docker-compose.azure.yml:95`; `k8s/configmap.yaml:12`; `docker-compose.local.yml:77` |
| 3 | Trocar a senha logado não revoga nada, nem o refresh. O reset revoga só o refresh, e em laço (N+1) | `AlterarSenhaUseCase.cs:31`; `AlterarSenhaUsuarioUseCase.cs:34`; `ResetarSenhaUseCase.cs:38-46` |
| 4 | Desativar tira só o vínculo com a empresa (`UsuarioEmpresa.Ativo`). O refresh só confere `Usuario.Ativo`, então o aparelho segue renovando | `DesativarUsuarioUseCase.cs:22`; `RefreshTokenUseCase.cs:29,49` |
| 5 | Trocar perfil ou e-mail não mexe em sessão, e as permissões viajam dentro do token | `AtribuirPerfilUsuarioUseCase.cs:43`; `AtualizarUsuarioUseCase.cs:33-40`; `JwtTokenService.cs:34-35` |
| 6 | O SSE sobrevive ao token: o laço só sai por cancelamento, e o `ClockSkew` padrão (5 min) dá mais tempo ao token | `TransmissaoSse.cs:21-34`; `OperacaoEventosController.cs:18-34`; `ApiServiceCollectionExtensions.cs:92-103` |
| 7 | O login revoga só o refresh dos outros aparelhos, e o JWT deles segue vivo. Por isso o carimbo novo **não pode** mudar no login: logar o tablet derrubaria o balcão | `AuthController.cs:132-135` (refresh de 30 dias em `:140`) |
| 8 | O console não renova o token: no vencimento vem 401 e volta ao login. Baixar o JWT para 60 min derrubaria a dona a cada hora | `Console/src/dominio/sessao.js:3-5`; `infra/api/sessao.js:2-3`; `aplicacao/useSessaoApi.js:6-15` |
| 9 | O cache da API é Redis só quando `ConnectionStrings:Redis` existe; senão é memória por processo | `ApiServiceCollectionExtensions.cs:407-422` |
| 10 | Os use cases de acesso quase não têm teste direto (`ResetarSenha`, `AlterarSenha`, `AlterarSenhaUsuario`, `Desativar`, `AtribuirPerfil`, `CriarUsuario`) e vários testes constroem as classes à mão: o construtor muda e eles quebram (R8) | busca em `EasyStock.*Tests*`; `Api.UnitTests/Controllers/AuthControllerTests.cs:37,55-82`; `UsuarioControllerTests.cs:45-50` |

**Abordagem.**

```
Requisição ─► JwtBearer (assinatura, exp) ─► OnTokenValidated ─► cache 60 s (sessao:v1:{usuario})
                                                └ miss: banco ─► { Ativo, SessoesValidasDesde }
              iat < SessoesValidasDesde, ou conta inativa ─► 401
Revogam: reset · troca de senha · desativação · troca de perfil · troca de e-mail ou telefone   (nunca o login)
```

1. **O passo 1 conta falha.** `Usuario.RegistrarFalhaDeSenha()` leva para o domínio a regra que hoje vive em
   `AutenticarUsuarioUseCase.cs:64-69` (incrementa e bloqueia por 15 min na 5ª), e os dois use cases a chamam.
   `ListarEmpresasParaLoginUseCase` ganha `IUnitOfWork` e grava o contador. Senha certa no passo 1 **não** zera nada: quem zera é
   o login completo, como hoje. A N4 já faz a senha errada da troca de contato contar como falha de login, com as chamadas de
   hoje (`IncrementarTentativasFalha` e `BloquearPorTentativas`); a N7 as troca por `RegistrarFalhaDeSenha()`.
2. **Carimbo de sessão.** `Usuario.SessoesValidasDesde` (`DateTime?`, nulo = nunca revogou; migração aditiva) e
   `Usuario.RevogarSessoes(agora)`, que trunca ao segundo e nunca recua. Serviço `RevogadorSessoes` (Application): grava o
   carimbo, revoga os refresh tokens ativos num `UPDATE` (o `RevogarSessoesAtivasAsync` que já existe) e apaga a chave do cache,
   tudo na transação de quem chama.
3. **Quem revoga.** `ResetarSenhaUseCase` (que troca o laço N+1), `AlterarSenhaUseCase`, `AlterarSenhaUsuarioUseCase`,
   `DesativarUsuarioUseCase`, `AtribuirPerfilUsuarioUseCase` e a troca de contato da N4: o **e-mail** quando ele de fato muda,
   isto é, na confirmação do endereço novo (`ConfirmEmailUseCase`, também no fluxo do Admin), nunca no pedido; o **telefone** em
   `PUT api/auth/me/telefone`. **Nunca o login.** Desativar tira o vínculo de uma empresa, mas revoga o usuário todo: quem está
   em duas empresas entra de novo e fica só com a que continua ativa.
4. **Checagem no token.** `ValidadorSessaoUsuario` (`Api/Authentication`) no `OnTokenValidated`: lê `sub` e `iat` e consulta
   `ICacheService` (chave `sessao:v1:{usuarioId:N}`, TTL de 60 s, valor `{ Ativo, ValidasDesde }`). No miss lê a projeção leve
   `IUsuarioRepository.ObterSessaoAsync`: a tabela `usuarios` não tem `EmpresaId`, então não há bypass de RLS nem entrada nova
   na allowlist. O token falha se o usuário não existe ou está inativo, se falta `sub` ou `iat`, ou se `iat < ValidasDesde`.
   Comparação em segundos inteiros: `iat >= carimbo` vale, então o token emitido no mesmo segundo do reset passa (janela de 1 s,
   aceita e documentada). A chave `Auth:SessoesRevogaveis` (padrão `true`) desliga a checagem sem migração. No mesmo processo o
   efeito é imediato; entre réplicas, imediato com Redis e em até 60 s sem Redis.
5. **SSE.** `TransmissaoSse.TransmitirAsync` recebe o `exp` do token e o verificador: a cada heartbeat (25 s) confere o carimbo,
   e encerra ao passar de `exp` (sem tolerância) ou quando o carimbo revoga. O cliente reconecta e recebe 401, que o console já
   trata como "volta ao login". A F08 item 7 (`11-console-fechamento.md:116`) pede o mesmo "fechar no `exp`": quem entrar primeiro
   entrega, e o outro rebaseia.
6. **JWT padrão de 60 min** (`appsettings.json:67`, de 480 para 60), **só com o console renovando o token**: hoje ele não renova,
   e 60 min derrubaria a dona a cada hora. Sem a renovação este item não entra no PR e vira nota na issue; os itens 1 a 5 já
   fecham o risco, porque revogam na hora.
7. **R8.** Os construtores mudam. Atualizar no mesmo commit `Api.UnitTests/Controllers/AuthControllerTests.cs:37,55-82`,
   `Api.UnitTests/Controllers/UsuarioControllerTests.cs:45-50` e
   `Application.Tests/UseCases/ListarEmpresasParaLoginUseCaseTests.cs:25`.

**Leitura mínima.** `Application/UseCases/AutenticarUsuario/ListarEmpresasParaLoginUseCase.cs` e `AutenticarUsuarioUseCase.cs`;
`Domain/Entities/Usuario.cs`; `Api/Configuration/ApiServiceCollectionExtensions.cs:60-124,407-425`;
`Api/Services/JwtTokenService.cs`; `Application/UseCases/{ResetarSenha,AlterarSenha,AlterarSenhaUsuario,DesativarUsuario,AtribuirPerfilUsuario,AtualizarUsuario,RefreshToken}`;
`Api/Services/Operacao/TransmissaoSse.cs` e `Api/Controllers/OperacaoEventosController.cs`;
`Infra.Postgre/Repositories/UsuarioRepository.cs` e `RefreshTokenRepository.cs:47`;
`Application/Ports/Output/IAsyncInfrastructure.cs` (`ICacheService`); `Console/src/dominio/sessao.js` e `infra/api/sessao.js`.
Moldes de teste: `Application.Tests/Services/Notifications/PollingOutboxSignalerTests.cs` (`FakeTimeProvider`),
`Infra.Postgre.IntegrationTests/Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs`.

**Testes Red.**

| Projeto/arquivo::Método | Falha hoje porque |
|---|---|
| `Domain.Tests/Entities/UsuarioTests.cs::RevogarSessoesTruncaAoSegundoENuncaRecua`, `::RegistrarFalhaDeSenhaBloqueiaNaQuintaPor15Minutos` | os métodos não existem; a regra está dentro do use case (`AutenticarUsuarioUseCase.cs:64-69`) |
| `Application.Tests/UseCases/ListarEmpresasParaLoginUseCaseTests.cs::SenhaErradaIncrementaAsTentativas`, `::QuintaFalhaBloqueiaAConta`, `::ContaBloqueadaRecusaSemVerificarASenha`, `::SenhaCertaNoPasso1NaoZeraOContador`; `AutenticarUsuarioUseCaseTests.cs::FalhasDosDoisPassosSomam` | o passo 1 não grava nada (`ListarEmpresasParaLoginUseCase.cs:41-43`) |
| `Application.Tests/UseCases/RevogacaoDeSessaoTests.cs::ResetDeSenhaRevogaAsSessoes`, `::TrocaDeSenhaRevoga` (`AlterarSenha` e `AlterarSenhaUsuario`), `::DesativarRevoga`, `::TrocaDePerfilRevoga`, `::ConfirmarNovoEmailRevoga`, `::PedidoDeTrocaDeEmailNaoRevoga`, `::TrocaDeTelefoneRevoga`, `::LoginNaoRevoga` | nenhum desses use cases chama revogação |
| `Application.Tests/Services/Auth/RevogadorSessoesTests.cs::GravaCarimboRevogaRefreshEApagaOCache` | o serviço não existe |
| `Api.UnitTests/Authentication/ValidadorSessaoUsuarioTests.cs::TokenAnteriorAoCorteFalha`, `::TokenNoMesmoSegundoDoCorteVale`, `::UsuarioInativoOuInexistenteFalha`, `::TokenSemIatOuSemSubFalha`, `::HitDeCacheNaoLeOBanco`, `::ApagarAChaveFazOProximoPedidoLerOBanco`, `::CacheExpiraEmSessentaSegundos` (`FakeTimeProvider`), `::ChaveDesligadaNaoValida` | não há `OnTokenValidated` (`ApiServiceCollectionExtensions.cs:85-104`) |
| `Api.UnitTests/Services/Operacao/TransmissaoSseTests.cs::FechaNoExp`, `::FechaQuandoOCarimboRevoga`, `::HeartbeatContinuaSemRevogacao` | o laço só sai por cancelamento (`TransmissaoSse.cs:21-34`) |
| `Infra.Postgre.IntegrationTests/Auth/SessoesValidasDesdeIntegrationTests.cs::LeituraLeveDevolveCarimboEAtivo`, `::RevogarSessoesAtivasRevogaSoDoUsuario`, `::MigracaoAdicionaColunaNula` (`[SkippableFact]`) | a coluna não existe |
| `Api.UnitTests/Startup/JwtExpiracaoPadraoTests.cs::AppsettingsTemSessentaMinutos` (só no commit do item 6) | o padrão é 480 (`appsettings.json:67`) |

**Aceite.**
- [ ] Dado 5 senhas erradas no passo 1 (`lista-empresas`), quando a 6ª tem a senha certa, então o passo 1 e o login recusam por
  15 min.
- [ ] Dado token emitido antes de reset, troca de senha, desativação, troca de perfil, confirmação de e-mail novo ou troca de
  telefone, quando chama `GET api/auth/me`, então 401. O emitido depois vale, e o refresh anterior também falha. O mero pedido
  de troca de e-mail não revoga nada.
- [ ] Dado login em outro aparelho, então o token do primeiro continua valendo (comportamento de hoje preservado).
- [ ] Dado SSE aberto, quando o token vence ou o carimbo revoga, então o stream fecha em até um heartbeat (25 s).
- [ ] Dada a leitura do carimbo, então ela não usa bypass de RLS e a allowlist do arch-test não cresce.
- [ ] Dada a chave `Auth:SessoesRevogaveis=false`, então a checagem some sem migração.
- [ ] Dado o console ainda sem renovar o token, então `Jwt:ExpirationMinutes` continua 480 e o item 6 fica anotado na issue.
- [ ] (Felipe) medir `Jwt__ExpirationMinutes` na VPS e registrar na issue.

**Fora.**
- A rota de permissões do perfil e a M7.3 do ERP: quando as permissões de um perfil mudarem, a M0.3 chama o `RevogadorSessoes`
  para os usuários dele, porque as permissões viajam no token.
- A mensagem "Conta bloqueada temporariamente." do login, que revela que a conta existe.
- Rotação e teto de refresh tokens; limpeza de `RefreshToken` e `EmailConfirmationToken` (`DeleteExpiredAsync` também sem
  chamador).
- O token efêmero de SSE do PWA mobile (`OperationController.cs:456-483`), que é de outro tipo e preso ao aparelho. 2FA.

**Depende de.** N4 (a troca de e-mail em duas etapas e a de telefone, que são os dois ganchos de contato). Nenhuma outra: N8 e
N9 usam o `RevogadorSessoes`, por isso a N7 vai antes. Sobreposição com a F08 item 7 no SSE.

**Tamanho.** M, em 3 commits: (1) contagem no passo 1; (2) carimbo, serviço e ganchos; (3) validador, SSE e, se o console já
renovar o token, o JWT de 60 min.

**Tier.** ALTO. ADR-0055, item 2: JWT e autenticação, e migração EF. R5 do `CLAUDE.md`: mais de 5 arquivos e mais de 100 linhas,
tocando a configuração de autenticação da API. A PR espera a label `aprovado`.

**Rollback.** `Auth:SessoesRevogaveis=false` desliga a checagem sem deploy. Depois, revert do squash. O `Down` da migração remove a
coluna; sem `Down` ela fica nula, sem dano, porque carimbo nulo vale como "nunca revogou".

---

## N8 · Esqueci a senha pelo motor: link seguro, código no WhatsApp e uso único de verdade · issue aberta ao iniciar

**Problema.**

| # | Achado | Prova |
|---|---|---|
| 1 | O e-mail sai direto por `IEmailService`, dentro do request, com assunto e texto fixos no código: fora do catálogo, do remetente `seguranca@`, da quarentena e do vigia do motor | `EsqueciSenhaUseCase.cs:12,59-91` (texto em `:75-82`) |
| 2 | O token é um `Guid` de 1 h, e cada pedido cria outro **sem invalidar os anteriores** | `EsqueciSenhaUseCase.cs:37,39,40-46` |
| 3 | Uso único sem `UPDATE` condicional: lê, confere `EstaValido()` e só depois marca. Dois POST simultâneos com o mesmo token passam os dois | `ResetarSenhaUseCase.cs:16-17,35`; `ResetToken.cs:33-41`; `ResetTokenRepository.cs:11-15` |
| 4 | O e-mail da conta vai para o log em 4 pontos (#301) | `EsqueciSenhaUseCase.cs:16,31,85,89` |
| 5 | O link aponta para o Web (`/auth/redefinir-senha?token=`), com o token na query string, e o access log do Caddy do Web grava a URI inteira por até 30 dias. O console não tem tela de redefinição (0 ocorrências em `Console/src`) | `EsqueciSenhaUseCase.cs:66-72`; `Web/Controllers/AuthController.cs:310-316`; `Caddyfile:16-23` |
| 6 | Sem limite por conta nem espaçamento: só o balde `auth` por IP, 20 por minuto, dividido com o login | `AuthController.cs:229-234`; `ApiServiceCollectionExtensions.cs:201-213` |
| 7 | Sem IP nem agente na auditoria do pedido: `ResetToken.Criar(..., null, null)` e `AuditLog.Criar(..., null, null)` | `EsqueciSenhaUseCase.cs:40-46,48-55` |
| 8 | O corpo é igual, mas o tempo não: o ramo "conta existe" faz insert, commit e SMTP no request, e o "não existe" volta na hora (canal lateral de enumeração, **inferência** a partir do código) | `EsqueciSenhaUseCase.cs:28-34,36-91` |
| 9 | `DeleteExpiredAsync` não tem chamador e carrega tudo em memória: token expirado e usado fica para sempre | `IResetTokenRepository.cs:10`; `ResetTokenRepository.cs:39-45` |
| 10 | O reset não avisa ninguém, e revoga refresh em laço sem tocar o JWT (N7) | `ResetarSenhaUseCase.cs:16-65` |
| 11 | Pelo WhatsApp só serve **código**: a Meta proíbe URL no template de autenticação e proíbe OTP em template de utilidade (ADR-0057, item 8) | **doc Meta** (autenticação; veja a [N6](05-whatsapp-plataforma.md)) |
| 12 | O reset não tem teste de unidade, o teste do esqueci senha exige o envio direto por `IEmailService` (`EsqueciSenhaUseCaseTests.cs`) e o `AuthControllerTests` constrói os use cases à mão (R8) | busca em `EasyStock.*Tests*`; `Api.UnitTests/Controllers/AuthControllerTests.cs:55-71` |

**Abordagem.**

```
POST forgot-password {email} ─► limites (IP 5/15 min · conta 3/h, 6/dia, 60 s) ─► 202 sempre
   invalida anteriores ─► link (sempre) + código (se elegível) ─► 1 evento ResetSenha (mesma transação, ADR-0030)
   Worker, modo todos ─► e-mail seguranca@ com o link (30 min)  +  WhatsApp de plataforma com o código (10 min)
POST reset-password / reset-password-code ─► UPDATE condicional (uso único) ─► senha ─► revoga sessões (N7) ─► avisa os canais
```

1. **O pedido não faz rede.** `POST api/auth/forgot-password { email }` devolve **sempre 202 com o mesmo corpo**, exista a
   conta ou não (a única exceção é o 429 do limite por IP, que não depende da conta). Dentro do request só há banco: valida o
   formato, aplica os limites, invalida os segredos anteriores, grava o link e, se a conta for elegível, o código, e enfileira
   **um** evento `ResetSenha` no motor (`INotificadorService.EnfileirarEventoAsync`, mesma transação, ADR-0030), categoria
   `Seguranca` (N2). O modo `todos` da rotina (N5, catálogo da N13) entrega o link por e-mail e o código por WhatsApp, cada um
   com o seu template. Sem I/O externa, o tempo do SMTP deixa de separar os dois ramos; a diferença que sobra (um INSERT) é
   aceita e documentada. O `baseUrl` do corpo é ignorado: a base do link vem da configuração. O pedido é anônimo, então o
   evento nasce na empresa do usuário quando ele tem uma só empresa ativa e na empresa padrão (ADR-0057, item 7; resolvida pelo `IEmpresaPadraoResolver` da N13) nos demais casos (superadmin, duas empresas, sem vínculo), e `ITenantContextAccessor.SetCurrentTenant`
   fixa essa empresa antes de gravar, como no webhook do atendimento: sem isso a RLS barra o INSERT do evento (a mesma classe de
   erro 42501 do diagnóstico).
2. **Link do e-mail (`Reset`).** 32 bytes aleatórios em base64url (o padrão de `JwtTokenService.GerarRefreshToken`), 30 min, só o
   hash no banco, texto do catálogo (N13) e remetente `seguranca@` (N3). Base configurável em `Auth:LinkRedefinirSenha`: hoje a
   origem confiável do Web mais `/auth/redefinir-senha?token={0}`; quando o console tiver a tela, `{origem}/#/redefinir?t={0}`,
   com o token no fragmento, que não chega ao servidor nem ao log do proxy. A allowlist de origens (`LinkBaseUrlResolver`, #765)
   continua valendo. A página do Web passa a responder `Referrer-Policy: no-referrer`.
3. **Código por WhatsApp (`ResetCodigo`).** Gerado junto com o link só para conta elegível: usuário ativo, **não
   superadmin**, `TelefoneVerificadoEm` preenchido (pela verificação administrativa da N4 ou pelo aceite do convite da N9),
   opt-in de WhatsApp (N4 e N6) e plataforma ligada. Não elegível: o payload leva
   `canais: ["Email"]` e só o link sai, com a mesma resposta 202. São 6 dígitos (`RandomNumberGenerator.GetInt32`), 10 min, 5
   tentativas, um código ativo por usuário, hash de `{IdDoToken}:{código}`. Vai no modelo `codigo_redefinir_senha` (autenticação,
   com copy code: o código no corpo e no botão) e **nunca em link**. Confirmação: `POST api/auth/reset-password-code { email,
   codigo, novaSenha }`, que recusa conta de superadmin com a mesma mensagem genérica. Usar um dos dois segredos mata o outro.
4. **Uso único de verdade.** `IResetTokenRepository.ConsumirAsync(id, agora)` é um
   `UPDATE ... SET "Usado" = true WHERE "Id" = @id AND "Usado" = false AND "ExpiraEm" > @agora`, e só a linha afetada igual a 1
   autoriza trocar a senha. Para o código, `RegistrarTentativaAsync` é um `UPDATE ... SET "Tentativas" = "Tentativas" + 1 WHERE ...
   AND "Tentativas" < 5` com o novo valor devolvido. A comparação do código é em tempo constante.
5. **Invalida os anteriores.** Cada pedido marca como usados os segredos abertos do usuário (`Reset` e `ResetCodigo`) antes de
   criar o novo, e um reset concluído invalida todos.
6. **Limites próprios.** Por IP: 5 pedidos em 15 min, contados por `ICacheService` (`IncrementAsync` e `SetExpiryAsync`, chave com
   o hash do IP), 429 além disso, valendo também para `reset-password` e `reset-password-code`. O balde `auth` de 20 por minuto
   continua como teto grosso. Por conta: 3 por hora, 6 por dia e 60 s entre pedidos, contados nas linhas de `reset_tokens`
   (`CriadoEm`), sem tabela nova. Excedeu por conta: responde 202 sem enviar e audita `forgot-password-limitado`.
7. **Efeitos do reset.** Troca a senha pela política existente (`ResetarSenhaCommandValidator`), zera o contador de falhas, chama o
   `RevogadorSessoes` (N7), invalida os demais segredos e **avisa por todos os canais verificados** (evento `SenhaAlterada`,
   categoria `Seguranca`).
8. **Auditoria e log.** `ResetToken.IpCriacao`, `UserAgent` e o `AuditLog` passam a gravar IP e agente (o controller monta o
   comando, nunca o corpo da requisição). Nenhum log leva e-mail ou telefone: só `UsuarioId` e o domínio do e-mail (há `MaskEmail`
   pronto). Fecha a #301.
9. **Limpeza agendada.** `LimpezaTokensAcessoService` (hosted service do Worker, a cada 6 h) apaga em lote (`ExecuteDeleteAsync`)
   os `reset_tokens` expirados há mais de 24 h. O `DeleteExpiredAsync` sem chamador sai.
10. **Dados.** Migração aditiva em `reset_tokens`: `Finalidade varchar(20) NOT NULL DEFAULT 'Reset'`, `Tentativas int NOT NULL
    DEFAULT 0`, `Canal varchar(20) NULL` e índice parcial `(UsuarioId, Finalidade) WHERE "Usado" = false`. As linhas existentes
    viram `Reset` e continuam valendo. Medir antes do Red: a N4 pode já ter criado `Finalidade` ou `Tentativas` para a
    verificação do telefone, e então elas são reaproveitadas.
11. **Contrato com o catálogo (N13) e o motor (N5).** Tipo `ResetSenha` (já existe, valor 4; rotina `reset_senha_global`,
    `Seguranca`, Email e WhatsApp em modo `todos`), payload `usuarioId`, `nome`, `email`, `link_redefinicao`, `codigo`, `expira_em_minutos`
    e a chave de controle `canais` (lista que restringe os canais da rotina naquele evento, ao lado de `enviarApos` e
    `chaveIdempotencia`, `NotificadorService.cs:22-31`; entregue pela N5). A quarentena de
    `ResetSenha` é de 30 min (N1), e a categoria `Seguranca` apaga corpo, payload e metadados ao terminar (N2). Tipo novo
    `SenhaAlterada`, para o aviso do item 7: valor da faixa livre da S0 (80 ou o próximo), grupo Segurança da quarentena (30
    min), rotina global em modo `todos`, template de e-mail e o modelo da Meta `senha_alterada` (utilidade): "EasyStok informa: a
    senha da sua conta foi alterada em {{1}}. Se não foi você, fale agora com a responsável da sua empresa." (parâmetro `data`).
    Entra pelo mesmo seed versionado da N13.
12. **R8.** Atualizar `Api.UnitTests/Controllers/AuthControllerTests.cs:55-71` e `Application.Tests/UseCases/EsqueciSenhaUseCaseTests.cs`.

**Leitura mínima.** `Application/UseCases/EsqueciSenha/` e `ResetarSenha/`; `Domain/Entities/ResetToken.cs`;
`Infra.Postgre/Repositories/ResetTokenRepository.cs` e `Data/Configurations/ResetTokenConfiguration.cs`;
`Api/Controllers/AuthController.cs:126-241`; `Web/Controllers/AuthController.cs:274-335`;
`Application/UseCases/Common/LinkBaseUrlResolver.cs`; `Application/Services/Notifications/NotificadorService.cs:54-66,117-291`;
`Api/Services/JwtTokenService.cs:54-59`; `Application/Ports/Output/IAsyncInfrastructure.cs`;
`Api.UnitTests/Notifications/LogsDeEnvioSemDadoPessoalTests.cs` (`LoggerQueGuarda`); `Caddyfile:10-24`; a
[N6](05-whatsapp-plataforma.md) (template de autenticação) e a N7, acima neste arquivo.

**Testes Red.**

| Projeto/arquivo::Método | Falha hoje porque |
|---|---|
| `Application.Tests/UseCases/EsqueciSenhaUseCaseTests.cs` (reescrito): `::EnfileiraNoMotorENaoEnviaPeloUseCase`, `::ContaExistenteEInexistenteTemMesmaRespostaESemRede`, `::InvalidaOsSegredosAnteriores`, `::LinkTemTrintaMinutosETrintaEDoisBytes`, `::BaseDoLinkVemDaConfiguracaoNuncaDoCorpo`, `::TerceiroPedidoDaHoraPassaEOQuartoNao`, `::SessentaSegundosEntrePedidos`, `::LimiteDeContaResponde202SemEnviar`, `::GravaIpEAgenteNaAuditoria`, `::NaoLogaEmailNemTelefone` (`LoggerQueGuarda`), `::EnfileiraUmEventoResetSenhaComLinkECodigo`, `::ContaElegivelGeraCodigoDeSeisDigitos`, `::SuperadminSoRecebeOLinkPorEmail`, `::SemTelefoneVerificadoSoRecebeOLink`, `::SemOptInSoRecebeOLink` | o use case envia por `IEmailService` dentro do request (`EsqueciSenhaUseCase.cs:59-91`), usa `Guid` (`:37`), 1 h (`:39`) e não invalida nada |
| `Application.Tests/UseCases/ResetarSenhaUseCaseTests.cs` (novo): `::UsoUnicoUmSoPassa` (o fake devolve 0 linhas ao segundo), `::TokenDeOutraFinalidadeNaoServe`, `::RevogaSessoesPeloRevogador`, `::AvisaOsCanaisVerificados`, `::SenhaForaDaPoliticaRecusa` | lê, confere e depois marca (`ResetarSenhaUseCase.cs:16-35`) |
| `Application.Tests/UseCases/ResetarSenhaPorCodigoUseCaseTests.cs`: `::CodigoCertoTrocaASenha`, `::CincoErradosMatamOToken`, `::SextaTentativaComCodigoCertoFalha`, `::SuperadminNaoRedefinePorCodigo`, `::CodigoDeOutraContaNaoServe` | o use case não existe |
| `Application.Tests/Services/Auth/LimitePedidosAcessoTests.cs::SextoPedidoDoMesmoIpEm15MinutosEhRecusado`, `::JanelaReiniciaDepoisDe15Minutos` (`FakeTimeProvider`) | só existe o balde `auth` do ASP.NET, por IP e sem janela de 15 min |
| `Application.Tests/Services/Auth/LimpezaTokensAcessoTests.cs::ApagaEmLoteSoOQueExpirouHaMaisDe24h` | `DeleteExpiredAsync` não tem chamador |
| `Application.Tests/Services/Notifications/NotificadorServiceCanalTests.cs::PayloadCanaisRestringeARotina` (só se a N5 não entregou) | a rotina usa a lista de canais dela, sem restrição por evento |
| `Api.UnitTests/Notifications/NotificacoesGlobaisSeedTests.cs::SenhaAlteradaTemRotinaETemplatePorCanal`, `::ResetSenhaEmModoTodos` | o tipo `SenhaAlterada` não existe e a rotina do reset não está em modo `todos` (N13) |
| `Infra.Postgre.IntegrationTests/Auth/EsqueciSenhaTenantTests.cs::PedidoAnonimoGravaOEventoNoTenantDoUsuario`, `::SuperadminGravaNaEmpresaPadrao` (`[SkippableFact]`, papel `rls_test_client`, sem JWT; molde `TenantContextAccessorIntegrationTests.cs`) | sem tenant fixado a policy `tenant_isolation` recusa o INSERT do evento (42501) |
| `Infra.Postgre.IntegrationTests/Auth/ResetTokenConcorrenciaTests.cs` (`[SkippableFact]`, PG real): `::DoisUsosSimultaneosUmSoVence`, `::DezCodigosErradosEmParaleloContamNoMaximoCinco`, `::LimpezaApagaExpiradosEUsadosAntigos`, `::MigracaoPreservaTokensExistentesComoReset` | ler e depois gravar deixa os dois passarem (`ResetTokenRepository.cs:11-15`), e não há contador |
| `Api.UnitTests/Controllers/AuthControllerTests.cs::ForgotPasswordDevolve202ComMesmoCorpo`, `::ForgotPasswordIgnoraBaseUrlDoCorpo`, `::ResetPasswordCodeMapeiaLimiteParaQuatrocentosEVinteENove` | hoje devolve 200 com dados e aceita `baseUrl` (`AuthController.cs:229-234`) |
| `Web.UnitTests/Controllers/AuthRedefinirSenhaTests.cs::RedefinirSenhaEnviaReferrerPolicyNoReferrer`, `::TelaDoCodigoPostaNaApi` | a página não manda o cabeçalho (`Web/Controllers/AuthController.cs:310-316`) e não há tela de código |

**Aceite.**
- [ ] Dado conta existente e conta inexistente, quando pedem a redefinição, então o status (202) e o corpo são iguais e nenhuma
  chamada de rede acontece dentro do request.
- [ ] Dado o pedido anônimo (sem JWT) contra o banco com o papel `rls_test_client`, então o evento é gravado, na empresa do
  usuário ou na empresa padrão, sem erro 42501.
- [ ] Dados dois pedidos seguidos, então só o último segredo vale e o do primeiro nunca mais redefine.
- [ ] Dado o mesmo token em 2 POST simultâneos, então 1 sucesso e 1 falha (Postgres real).
- [ ] Dada conta com telefone verificado e opt-in, então a mesma solicitação gera um evento só, e o motor entrega o **link** por
  e-mail e o **código** pelo 2º número (modelo de autenticação), nunca link no WhatsApp. Para superadmin, ou sem telefone
  verificado, ou sem opt-in, chega só o link por e-mail.
- [ ] Dados 5 códigos errados, então o token morre e o 6º, mesmo certo, falha.
- [ ] Dado reset concluído, então a senha troca, os JWT e refresh anteriores caem (N7), os canais verificados recebem "senha
  alterada" e o `AuditLog` guarda IP e agente.
- [ ] Dado o 4º pedido na mesma hora para a mesma conta, então 202 sem envio e auditoria `forgot-password-limitado`; dado o 6º
  pedido do mesmo IP em 15 min, então 429.
- [ ] Dado o link do e-mail, então ele vem de `Auth:LinkRedefinirSenha`, vale 30 min e leva 32 bytes; o `baseUrl` do corpo é
  ignorado.
- [ ] Dado qualquer log do fluxo, então nenhum contém e-mail, telefone, token ou código (fecha a #301).
- [ ] Dado `reset_tokens` expirados há mais de 24 h, então a limpeza agendada os apaga.
- [ ] (Felipe) reset real por e-mail com `dkim=pass` no cabeçalho e por WhatsApp com o botão "Copiar código".

**Fora.**
- A tela do console (M0 e M7 do ERP; hoje só o Web), a troca de e-mail ou telefone pelo usuário (N4) e o botão "Não pedi este
  código" (beta na Meta).
- A verificação do telefone por código do próprio usuário: a N4 a deixou para a N6, e o segredo de uso único com tentativas que
  nasce aqui a tornaria barata, mas hoje a equipe se verifica pelo endpoint administrativo da N4 e o convidado no aceite da N9.
  Vira a spec seguinte (ver a [N6](05-whatsapp-plataforma.md)).
- Filtro de query string no Caddy: enquanto o link for do Web, o token fica no access log (`Caddyfile:16-23`). O proxy da VPS não
  foi medido; o fragmento do console resolve de vez.
- Limpeza de `RefreshToken` e `EmailConfirmationToken`. Mudar a mensagem de bloqueio do login.
- Risco aceito: quem conhece o e-mail da vítima pode gastar as 3 tentativas por hora dela; a vítima ainda recebe o que já foi
  enviado e pode tentar de novo na hora seguinte.

**Depende de.**
- N2 (`Seguranca` apaga corpo e payload; `Indeterminado`), N3 (remetente `seguranca@`), N1 (quarentena de 30 min para
  `ResetSenha` e para o `SenhaAlterada` novo), N4 (telefone verificado e opt-in), N5 e N13 (modo `todos`, rotina e template por
  canal, `ResetSenha` com `codigo_redefinir_senha`), N6 (envio do código e opt-in no WhatsApp de plataforma) e N7
  (`RevogadorSessoes`).

**Tamanho.** G, em 4 commits: (1) segredo com `Finalidade`, `UPDATE` condicional, limites e limpeza; (2) pedido pelo motor com o
link por e-mail; (3) código por WhatsApp e a tela no Web; (4) aviso, auditoria e log (fecha a #301).

**Tier.** ALTO. ADR-0055, item 2: autenticação (redefinição de senha, limites, tokens), migração EF e endpoint público novo
(`reset-password-code`). R5 do `CLAUDE.md`: mais de 5 arquivos e mais de 100 linhas. A PR espera a label `aprovado`.

**Rollback.** Revert do squash devolve o envio direto por `IEmailService`. A migração é aditiva: o `Down` remove as colunas, e
sem `Down` elas ficam ociosas (as linhas antigas valem como `Reset`). Segredos de código ainda abertos deixam de servir, porque
o código antigo só entende token por link. Desligar só o código, sem deploy: `Notifications__WhatsApp__Plataforma__Provider=stub`
faz o WhatsApp virar `Simulado` e o link por e-mail segue sozinho.

---

## N9 · Primeiro acesso por convite com link · issue aberta ao iniciar

Decisão do Felipe em 01/10/2026: **DM7-3 = opção (c), convite com link** (a dona nunca define a senha de ninguém).

**Problema.**

| # | Achado | Prova |
|---|---|---|
| 1 | A dona define a senha de cada funcionário: o formulário "Convidar" do Web exige `Senha` e a API cria o usuário com ela | `Web/Controllers/UsuariosController.cs:43-65`; `CriarUsuarioUseCase.cs:7,41-42`; `UsuarioController.cs:47-56` |
| 2 | Não existe convite nem token com finalidade: `ResetToken` só serve a reset, sem `Finalidade`, `Tentativas` nem `Canal` | `ResetToken.cs:3-41`; busca por "convite" no código de produto, sem resultado |
| 3 | `Usuario.SenhaHash` é obrigatório. A LGPD já usa um hash inutilizável (`$2a$10$INVALIDATED_...`), e o hasher trata `SaltParseException` como senha errada. Nenhum teste prova isso com o hash real: `Infra.Async.UnitTests` não tem teste do hasher | `Usuario.cs:10,93`; `BCryptPasswordHasher.cs:19-33` |
| 4 | O e-mail é único no sistema todo: o Admin ouve "Email ja cadastrado" para e-mail de qualquer empresa | `CriarUsuarioUseCase.cs:31-33` |
| 5 | Editar o e-mail pelo Admin não invalida nada nem verifica o canal novo | `AtualizarUsuarioUseCase.cs:33-40` |
| 6 | O login Google entra por e-mail (ou alias do Gmail) e não muda `EmailConfirmado`. O login por senha só avisa em log quando o e-mail não foi confirmado | `IdentificarUsuarioGoogleUseCase.cs:17-26`; `AutenticarUsuarioUseCase.cs:44-47,92-94` |
| 7 | "Esqueci a senha" de quem ainda não definiu senha geraria um token de reset: um caminho paralelo ao aceite do convite, sem verificar o canal (**inferência** a partir do código) | `EsqueciSenhaUseCase.cs:28-46` |
| 8 | O plano do ERP promete "senha inicial" na M7.2 e dá o convite por e-mail como "fora" | `08-m7-configuracoes.md:129-131,225` (antes da edição desta entrega, que mexe nessas linhas); `README.md:133` do mesmo plano |

**Abordagem.**

```
Dona cria o usuário (sem senha) ─► Usuario com hash inutilizável + 1 token Convite por canal (72 h)
Pessoa abre o link (GET não consome) ─► POST aceitar-convite { token, novaSenha }
   UPDATE condicional ─► senha ─► verifica o canal do token (e-mail: EmailConfirmado · WhatsApp: TelefoneVerificadoEm)
   ─► revoga os outros convites ─► volta ao login
Google com e-mail verificado ─► consome o convite e confirma o e-mail
```

1. **Dados** (migração aditiva). `reset_tokens` (com `Finalidade`, `Tentativas` e `Canal`, da N8) ganha o valor `Convite`;
   `usuarios.ConviteAceitoEm timestamptz NULL` e `ConviteAceitoVia varchar(40) NULL`. Convite pendente é `SenhaHash` com o
   marcador `$2a$10$CONVIDADO_`: `Usuario.CriarConvidado(nome, email)` nasce `Ativo`, com `EmailConfirmado = false` e esse hash
   (o login por senha falha; o teste com BCrypt real prova que o hasher devolve `false` sem lançar, e se lançar o marcador
   muda). `ConviteAceitoVia` guarda só texto já mascarado: `+55•••1234`
   (código do país e 4 últimos dígitos), `e-mail` ou `Google`; o telefone inteiro nunca vai para essa coluna.
2. **Criar sem senha.** `POST api/usuarios` (Admin): `Senha` passa a ser opcional (`string?`). Sem `Senha` cria o convidado, o
   vínculo e o perfil, e emite o convite. `Telefone` e `AtestaOptInWhatsApp` (a dona atesta que a pessoa aceitou receber por
   WhatsApp) são opcionais: sem o atestado só sai e-mail, e com ele grava o `ConsentimentoNotificacao` da pessoa, com a dona
   como autora. Com `Senha` (legado) nada muda, marcado como obsoleto no Swagger até a M7.2 trocar o formulário. **Superadmin
   nunca nasce por convite.**
3. **Um token por canal, um evento só.** E-mail: link com o token (32 bytes em base64url, 72 h). WhatsApp, se houver telefone
   e atestado: outro token, no botão URL do modelo descrito abaixo. Cada token guarda o `Canal`. Sai **um** evento
   `ConviteAcesso` (catálogo da N13: `Seguranca`, Email e WhatsApp em modo `todos`; payload `usuarioId`, `nome`, `email`, `empresa`,
   `link_convite`, `expira_em_dias`), mais a chave `token_convite_whatsapp` quando há atestado. Sem atestado o payload leva
   `canais: ["Email"]` (a chave de controle da N8) e nenhum token de WhatsApp é criado. Base do link em `Auth:LinkConvite`: hoje
   `/auth/convite?token={0}` no Web; no console, `#/convite?t={0}`, no fragmento. A quarentena é a dos "demais" da N1 (24 h):
   vencido o evento, a dona reenvia.
   - **O WhatsApp do convite leva o link.** A N13 já pede à Meta o modelo `convite_acesso_link` (utilidade, com botão URL):
     "Olá, {{1}}! Você foi convidado para o EasyStok da empresa {{2}}. Use o botão abaixo para criar sua senha." Esta spec
     liga o parâmetro do botão ao `token_convite_whatsapp` (`botaoUrl0` nos `Metadados`, enviado pela N6). O prefixo da URL do
     botão é fixo no modelo aprovado: confirmar na homologação se o `{{1}}` aceita `#`, senão usar o caminho `/convite/{{1}}`.
4. **Abrir não consome.** O GET só mostra o formulário, que lê o token do fragmento ou da query e não chama a API. Quem consome é
   o POST.
5. **Aceitar.** `POST api/auth/aceitar-convite { token, novaSenha }` (anônimo, com o limite por IP da N8): `ConsumirAsync`
   condicional (só a linha afetada igual a 1 passa), troca o marcador pelo hash da senha (política de senha existente), grava
   `ConviteAceitoEm` e `ConviteAceitoVia` e **verifica o canal do token**: o do e-mail marca `EmailConfirmado`, e o do WhatsApp
   chama `MarcarTelefoneVerificado` (N4) e grava os opt-ins `Seguranca` e `Operacional` com data e IP, como a verificação
   administrativa da N4. Todos os outros convites do usuário são revogados na mesma transação.
   Devolve 200 e a pessoa volta ao login, sem emitir token por este endpoint. Convite revogado, usado, vencido ou de superadmin:
   a mesma mensagem, "Convite inválido ou expirado".
6. **Editar o contato revoga.** `AtualizarUsuarioUseCase` (e-mail) e a troca de telefone (N4), para usuário com convite pendente:
   revoga os convites abertos e emite novo para o contato novo. Aqui a troca de e-mail do Admin é **imediata** e dispensa o
   `EmailPendente` da N4: a confirmação em duas etapas protege conta que já teve acesso, e o convidado nunca teve. Para quem já
   aceitou vale a regra da N4 e o convite não entra.
   - **Exceção à regra de WhatsApp da N4.** O `IResolvedorAudiencia` só entrega WhatsApp a telefone verificado e com opt-in, e no
     convite o telefone ainda não foi verificado (o aceite é que o verifica) e o opt-in vem do atestado da dona. Esta spec
     acrescenta ao resolvedor a audiência `convidado` (`ParametrosJson.audiencia` da rotina `convite_acesso_global`): o e-mail do
     convidado sempre, e o WhatsApp só com o atestado gravado em `ConsentimentoNotificacao(WhatsApp, Seguranca, OptIn)`, com a
     dona em `AtualizadoPor`, e sem exigir `TelefoneVerificadoEm`. A relaxação vale só para `ConviteAcesso` e só até o aceite.
7. **Reenviar.** `POST api/usuarios/{id}/convite` (Admin, 3 por hora por usuário, contados nas linhas) revoga os abertos e emite
   novos.
8. **Esqueci a senha de quem está pendente** reemite o convite e nunca gera token de reset: o reset só vale para quem já aceitou.
9. **Google.** `IdentificarUsuarioGoogleUseCase`: identidade com e-mail verificado de usuário pendente consome o convite (revoga
   os demais), marca `EmailConfirmado` e grava `ConviteAceitoVia = "Google"`. A senha segue inutilizável até a pessoa definir uma
   por "esqueci a senha", que depois do aceite passa a valer.
10. **A dona vê.** `ListarUsuariosUseCase` devolve `convite { estado: pendente, aceito ou nenhum; aceitoEm; via }`, para a tela
    mostrar "pendente" ou "aceito via +55•••1234".
11. **Web.** `Convidar` deixa de pedir senha (`UsuariosController.cs:47`) e ganha a tela `/auth/convite`. A tela do console é da
    M7.2 do ERP.
12. **Documento do ERP.** `docs/plan/erp-casa-da-baba/08-m7-configuracoes.md` (DM7-3 e M7.2) **precisa ser atualizado**, e a
    edição pontual foi feita junto com a escrita desta spec, em 01/10: a DM7-3 passou a decidida pela opção (c) e a M7.2 não
    promete mais "senha inicial". Falta o resumo da DM7-3 em `docs/plan/erp-casa-da-baba/README.md:133` ("dona cria com senha
    inicial e troca obrigatória"), que a PR desta spec ajusta.
13. **R8.** `CriarUsuarioCommand` muda de contrato e os construtores dos use cases mudam: atualizar no mesmo commit
    `Api.UnitTests/Controllers/UsuarioControllerTests.cs:45-50`, `AuthControllerTests.cs:55-82` e os testes de
    `IdentificarUsuarioGoogleUseCase` e `AtualizarUsuarioUseCase`.

**Leitura mínima.** `Domain/Entities/Usuario.cs`, `ResetToken.cs` e `UsuarioEmpresa.cs`;
`Application/UseCases/{CriarUsuario,AtualizarUsuario,ListarUsuarios}/`;
`Application/UseCases/AutenticarUsuario/IdentificarUsuarioGoogleUseCase.cs` e `AutenticarUsuarioUseCase.cs`;
`Api/Controllers/UsuarioController.cs`; `Web/Controllers/UsuariosController.cs:41-66` e `AuthController.cs`;
`Infra.Async/BCryptPasswordHasher.cs`; `Api/Controllers/ConsentimentosController.cs`;
`docs/plan/erp-casa-da-baba/08-m7-configuracoes.md` (fatos 6 e 7, M7.2, DM7-3); a N8, acima neste arquivo.

**Testes Red.**

| Projeto/arquivo::Método | Falha hoje porque |
|---|---|
| `Domain.Tests/Entities/UsuarioTests.cs::CriarConvidadoNasceComHashInutilizavelESemEmailConfirmado`, `::ConvitePendenteDependeDoMarcador`, `::AceitarConviteTrocaOHashEGravaViaMascarada`; `ResetTokenTests.cs::FinalidadePadraoEhReset`, `::ConviteNaoServeComoReset` | os métodos e o campo não existem (`Usuario.cs:28`; `ResetToken.cs`) |
| `Infra.Async.UnitTests/Security/BCryptPasswordHasherTests.cs::HashDoConvidadoNuncaConfere` (BCrypt real) | a pasta e o teste não existem; protege contra o marcador lançar exceção |
| `Application.Tests/UseCases/CriarUsuarioUseCaseTests.cs` (novo): `::SemSenhaCriaConvidadoEEmiteConviteDeEmail`, `::ComTelefoneEAtestadoLevaOTokenDeWhatsAppNoPayload`, `::SemAtestadoRestringeOsCanaisAoEmailESemTokenDeWhatsApp`, `::ConviteExpiraEmSetentaEDuasHoras`, `::ConvidarSuperAdminRecusa`, `::ComSenhaSegueComoHoje` | `Senha` é obrigatória e não há convite (`CriarUsuarioUseCase.cs:7,41-42`) |
| `Application.Tests/UseCases/AceitarConviteUseCaseTests.cs::DefineSenhaEVerificaOCanalDoToken` (Theory: e-mail e WhatsApp), `::UsoUnicoUmSoPassa`, `::AceitarPorWhatsAppGravaOsOptInsDaVerificacao`, `::AceitarRevogaOsOutrosConvites`, `::TokenDeResetNaoAceitaConvite`, `::ConviteVencidoRecusaComMensagemGenerica`, `::SuperadminRecusa`, `::SenhaForaDaPoliticaRecusa` | o use case não existe |
| `Application.Tests/UseCases/AtualizarUsuarioUseCaseTests.cs::EditarEmailDeConvidadoEhImediatoERevogaEReemite`, `::EditarEmailDeQuemJaAceitouNaoMexeNoConvite` | o e-mail muda sem tocar em convite (`AtualizarUsuarioUseCase.cs:33-40`) |
| `Application.Tests/Services/Notifications/ResolvedorAudienciaTests.cs::ConvidadoComAtestadoRecebePorWhatsAppSemTelefoneVerificado`, `::ConvidadoSemAtestadoSoRecebePorEmail`, `::AudienciaUsuarioContinuaExigindoTelefoneVerificado` | a N4 só entrega WhatsApp a telefone verificado, então o convite nunca sairia pelo WhatsApp |
| `Application.Tests/UseCases/IdentificarUsuarioGoogleUseCaseTests.cs::GoogleDeConvidadoConsomeOConviteEConfirmaOEmail`, `::GoogleDeQuemNaoEConvidadoNaoMudaNada` | o Google não olha convite (`IdentificarUsuarioGoogleUseCase.cs:17-26`) |
| `Application.Tests/UseCases/EsqueciSenhaUseCaseTests.cs::PendenteReemiteOConviteENuncaGeraReset` | gera token de reset para qualquer usuário ativo (`EsqueciSenhaUseCase.cs:28-46`) |
| `Application.Tests/UseCases/ReenviarConviteUseCaseTests.cs::RevogaOsAbertosEEmiteNovos`, `::TerceiroDaHoraPassaEQuartoNao` | o use case não existe |
| `Api.UnitTests/Notifications/NotificacoesGlobaisSeedTests.cs::ConviteAcessoWhatsAppEmV2LevaBotaoUrl`, `::SeedTrocaOV1PeloV2SemTocarEmTemplateEditadoPorPessoa` | o catálogo da N13 tem o WhatsApp do convite em v1, sem link nem botão |
| `Application.Tests/UseCases/ListarUsuariosUseCaseTests.cs::MostraPendenteOuAceitoComTelefoneMascarado` | a listagem não devolve convite |
| `Infra.Postgre.IntegrationTests/Auth/ConviteIntegrationTests.cs` (`[SkippableFact]`, PG real): `::DoisAceitesSimultaneosUmSoVence`, `::AceitarFixaEmailConfirmadoOuTelefoneVerificado`, `::MigracaoMarcaTokensExistentesComoReset` | as colunas e o `UPDATE` condicional não existem |
| `Web.UnitTests/Controllers/UsuariosConvidarTests.cs::ConvidarNaoPedeSenha`; `AuthConviteTests.cs::AbrirOLinkNaoChamaAApi`, `::PostDoAceiteChamaAApiComTokenESenha` | o formulário exige senha (`UsuariosController.cs:47`) e não há tela de convite |

**Aceite.**
- [ ] Dado Admin que cria usuário sem senha, então ele nasce `Ativo` com perfil e vínculo, o login com qualquer senha falha (em
  `login` e em `lista-empresas`) e um convite por e-mail é enfileirado.
- [ ] Dado telefone com o atestado "a pessoa aceitou receber por WhatsApp", então o mesmo evento entrega também o convite pelo
  2º número, no modelo `convite_acesso_link` com botão de link. Sem o atestado o payload restringe a `Email` e só sai e-mail.
- [ ] Dado abrir o link (GET, inclusive por scanner de e-mail), então nada é consumido e o mesmo link ainda funciona no POST.
- [ ] Dado POST com token e senha válidos, então a senha é definida e o canal do token é verificado (`EmailConfirmado` para
  e-mail, `TelefoneVerificadoEm` para WhatsApp), e os demais convites do usuário morrem. Com 2 POST simultâneos, 1 sucesso.
- [ ] Dado convite revogado, usado ou vencido, então a mesma mensagem, "Convite inválido ou expirado".
- [ ] Dado Admin que edita e-mail ou telefone de convidado pendente, então os convites antigos morrem e um novo sai para o
  contato novo. A edição de quem já aceitou segue a N4 e não mexe em convite.
- [ ] Dado convidado que entra pelo Google com e-mail verificado, então o convite é consumido, `EmailConfirmado` fica verdadeiro
  e a via é "Google".
- [ ] Dado pendente que pede "esqueci a senha", então recebe convite novo e nenhum token de reset.
- [ ] Dado superadmin, então ele nunca nasce por convite nem aceita convite.
- [ ] Dada a lista de usuários, então a dona vê "pendente" ou "aceito via +55•••1234" (máscara feita no servidor), "e-mail" ou
  "Google".
- [ ] Dado o texto do plano do ERP, então `08-m7-configuracoes.md` marca a DM7-3 como decidida em 2026-10-01 pela opção (c) e a
  M7.2 não promete "senha inicial" (já editado com a escrita desta spec, conferir na PR), e o resumo em `README.md:133` do ERP
  é ajustado na PR da N9.
- [ ] Dado o token de convite, então tem 32 bytes aleatórios, só o hash vai ao banco e vale 72 h, um por canal.
- [ ] (Felipe) aprovar o modelo `convite_acesso_link` no WhatsApp Manager; convite real por e-mail e por WhatsApp, e confirmar se
  o botão URL aceita o token com `#`.

**Fora.**
- A tela do console (M7.2 do ERP), login automático depois do aceite (a pessoa volta ao login), convite por SMS, reenvio
  automático e validade configurável por empresa. 2FA.
- Convidar quem já existe em outra empresa: o e-mail é único no sistema (`CriarUsuarioUseCase.cs:31-33`) e continua respondendo
  "Email ja cadastrado".
- Remover o campo `Senha` de `POST api/usuarios`: sai na M7.2, quando o console substituir o formulário do Web.

**Depende de.**
- N4 (`Usuario.Telefone`, `TelefoneVerificadoEm`, `MarcarTelefoneVerificado`, o `IResolvedorAudiencia` que esta spec estende e a
  troca de e-mail em duas etapas, da qual o convidado pendente é exceção), N6 (o envio por WhatsApp), N7
  (`RevogadorSessoes`), N8 (`ResetToken` com `Finalidade`, `ConsumirAsync`, limites, limpeza),
  N5 e N13 (modo `todos`, a chave `canais` do payload, o tipo `ConviteAcesso`, o seed versionado e o modelo `convite_acesso_link` já pedido pela N13)
  e N3 (e-mail real).

**Tamanho.** G, em 3 commits: (1) domínio, migração e criação sem senha; (2) aceite, reenvio, edição de contato e Google;
(3) listagem, telas do Web e o ajuste do documento do ERP.

**Tier.** ALTO. ADR-0055, item 2: autenticação e autorização (criação de usuário, aceite anônimo de convite), e migração EF.
R5 do `CLAUDE.md`: mais de 5 arquivos e mais de 100 linhas. A PR espera a label `aprovado`.

**Rollback.** Antes do revert, `DELETE FROM reset_tokens WHERE "Finalidade" = 'Convite'`, senão o código antigo trataria o convite
como token de reset. Depois, revert do squash. A migração é aditiva: o `Down` remove as colunas de `usuarios` e o valor novo
deixa de existir; sem `Down` ficam ociosas. Quem já aceitou continua entrando normalmente, e quem estava pendente precisa de
convite novo ou de "esqueci a senha". `POST api/usuarios` com `Senha` continua funcionando em qualquer ponto.

---

## Rollback do marco

Desfazer na ordem inversa: N9, N8, N7. Antes de cada revert, aplicar o passo "antes" daquela spec (apagar os convites abertos; nenhum
passo antes na N8 e na N7). O que não pode ser desfeito sem custo: a **revogação de sessão já gravada** (um usuário revogado precisa
entrar de novo) e os `Seguranca` já apagados pelo motor (N2). Para o dia a dia há duas chaves sem deploy: `Auth:SessoesRevogaveis=false`
(N7) e `Notifications__WhatsApp__Plataforma__Provider=stub` (N6, que faz o código de redefinição e o convite por WhatsApp caírem para o
e-mail). O e-mail de redefinição e o convite por e-mail não têm chave própria: voltam com o revert da N8 e da N9.
