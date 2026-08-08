# ADR-0047 — Login multi-empresa em dois passos no BFF Web

- Status: Accepted
- Data: 2026-08-08
- Contexto: issue #1007 (shell modular), PR #1006
- Relacionado: ADR-0046 (shell modular), ADR-0038 (tenant owner flag)

## Contexto

O dominio suporta um usuario em varias empresas (`UsuarioEmpresa`, N:N com flag `Ativo`) e
a Api ja implementa o login em dois passos:

- `POST /api/auth/lista-empresas` — `[AllowAnonymous]`, rate-limited como o login, valida as
  credenciais **sem emitir token** e devolve as empresas do usuario.
- `POST /api/auth/login` — aceita `EmpresaId` opcional e valida o vinculo ativo antes de
  emitir o token (sem IDOR).
- `AutenticarUsuarioUseCase.ResolveEmpresaIdPadrao` devolve **null quando ha 2+ empresas
  ativas** — a Api nao escolhe pelo usuario.

O Web nunca consumiu esse caminho. Ele mandava so `{email, senha}` e, se o JWT voltasse sem
o claim `empresaId`, limpava a sessao e exibia *"Nao foi possivel identificar a empresa
associada a este usuario. Entre em contato com o suporte."*

O resultado e um **bug latente**: qualquer usuario com duas empresas ativas simplesmente
**nao consegue entrar** no sistema, com uma mensagem que sugere defeito de cadastro. Hoje
existe uma empresa real (Casa da Baba), entao o bug esta dormente — mas ele bloqueia o
segundo tenant, e o caminho para destrava-lo ja esta pronto do lado da Api.

## Decisao

### D1. O Web passa a implementar o login em dois passos, sem tocar a Api.

Passo 1: `POST auth/login` com `{email, senha}`, como hoje. Se o token vier **com**
`empresaId`, nada muda — o caminho de empresa unica segue identico. Se vier **sem**, o Web
chama `auth/lista-empresas`; com duas ou mais empresas, guarda a pendencia e leva ao
seletor. Com menos de duas, e anomalia de verdade (SuperAdmin sem empresa, vinculo
faltando) e a mensagem de suporte continua valendo.

Passo 2: o usuario escolhe a empresa e o Web chama `auth/login` de novo com
`{email, senha, empresaId}`. A Api revalida o vinculo; o Web checa antes se a empresa
estava na lista oferecida, para nao gastar a ida com um POST forjado.

### D2. A checagem do claim vem ANTES de gravar sessao e cookie.

O codigo anterior gravava tokens, montava claims, chamava `SignInAsync` e **so entao**
descobria que faltava `empresaId` — desfazendo tudo com `session.Clear()` +
`SignOutAsync()`. Autenticar para logo deslogar e um estado intermediario desnecessario
num caminho critico. O pipeline pos-token virou `ConcluirLoginAsync`, chamado apenas
quando ja existe `empresaId`, e compartilhado pelos dois caminhos.

### D3. A pendencia entre os passos vive na sessao server-side, CIFRADA e com prazo criptografico de 5 minutos.

O passo 2 exige as credenciais outra vez, entao a senha precisa sobreviver entre os dois
passos. Ela fica **so no servidor**; ao cliente vai apenas o id de sessao, em cookie
`HttpOnly` + `SameSite=Strict` + `Secure`.

> **Correcao de premissa (revisao do PR #1006).** A primeira versao deste ADR dizia que a
> pendencia ficava em `DistributedMemoryCache`, "a mesma superficie que ja guarda os tokens".
> **Isso e falso na configuracao implantada:** `WebHttpServicesExtensions` usa
> `AddStackExchangeRedisCache` sempre que `ConnectionStrings:Redis` existe — e o stack Azure
> define exatamente isso, para a sessao sobreviver a redeploys. O container `redis` sobe sem
> `requirepass`, na rede do compose. Somando o `Session:IdleTimeout` de **480 minutos**, a
> senha em claro ficaria legivel por qualquer processo daquela rede pelo resto do dia — e o
> TTL de 5 minutos, por ser avaliado apenas na leitura, nao apagava nada por abandono.

Por isso o payload e cifrado com **`ITimeLimitedDataProtector`** (Data Protection, chaves ja
persistidas em volume por `DataProtection:KeysPath`), com o prazo embutido no proprio texto
cifrado. Quatro garantias, cada uma coberta por teste:

1. **Nada de senha em claro no armazenamento.** O que vai para o Redis e texto cifrado; o
   teste assere que nem a senha nem o e-mail aparecem no valor gravado.
2. **Prazo criptografico, nao cooperativo.** Passados 5 minutos o payload deixa de ser
   decifravel, mesmo que a chave de sessao continue viva pelas 8 horas do `IdleTimeout` e
   ninguem venha ler. Abandonar a tela nao deixa credencial utilizavel para tras.
3. **Uso unico.** A pendencia e removida ANTES da chamada do passo 2 — com sucesso ou com
   falha. Nao ha caminho em que a senha sobreviva a uma tentativa.
4. **Desistir limpa.** Voltar para `GET /auth/login` descarta a pendencia.

Efeito colateral desejavel: um redeploy que troque as chaves de Data Protection invalida as
pendencias em voo — ninguem retoma um login pendente de outro processo.

### D4. Trocar de empresa DEPOIS de logado fica fora.

O `empresaId` e claim do JWT, nao estado de sessao — por isso a troca de **loja** funciona
sem re-login e a de **empresa** nao. Um seletor in-app exigiria um endpoint novo na Api
para reemitir o token (ex.: `POST /api/auth/trocar-empresa` a partir do refresh token).
Fica registrado como trabalho futuro; o picker no login ja resolve o bloqueio real.

## Alternativas consideradas

**TempData.** O provider default do MVC e cookie protegido por Data Protection. Ainda
assim seria a senha, cifrada, viajando ao browser e voltando — e sobrevivendo alem do
passo seguinte. Cifrar nao torna aceitavel expor a credencial ao cliente quando existe
alternativa que nao expoe.

**Campo hidden na tela de selecao.** Senha em texto claro no HTML. Descartado sem
discussao.

**Re-render do formulario de login com um select de empresa e um POST unico.** Elegante no
papel, mas browsers nao repopulam `<input type=password>` num re-render server-side: ou o
usuario digita a senha de novo (UX pior, sem ganho de seguranca) ou o servidor emite a
senha no HTML (pior que todas as opcoes anteriores).

## Consequencias

- **O passo 1 gasta um `POST /auth/login` cujo token e descartado.** Esse endpoint revoga as
  sessoes ativas, emite refresh token e grava auditoria — entao um login multi-empresa produz
  duas linhas de `login`, um refresh token orfao e uma auditoria espuria de "novo
  dispositivo" (a segunda chamada revoga a primeira). Corrigir de verdade exige mexer na Api
  (um caminho de validacao sem efeito colateral), o que ficou fora desta rodada; registrado
  como issue.
- O caminho de empresa unica esta coberto por teste de regressao, incluindo a preservacao
  do `returnUrl` (deep link continua vencendo o portal).
- A tela `Auth/SelecionarEmpresa` segue o molde do `SelecionarLoja` e nao usa Alpine: sao
  formularios, um por empresa, sem JS.
- Nota de rastreabilidade: o comentario do `EasyStock.Api/Controllers/AuthController` cita
  "ADR-0031" para o login 2-etapas, mas o ADR-0031 real e sobre cardapio produto-agnostico.
  A referencia estava quebrada; **este** ADR e o do login em dois passos.
