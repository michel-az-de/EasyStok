# ADR-0046 — Shell modular: portal de entrada e modulo derivado da rota

- Status: Accepted
- Data: 2026-08-08
- Contexto: issue #1007 (shell modular), PR #1006
- Relacionado: ADR-0032 (menu lateral: IA + favoritos), ADR-0045 (sidebar superficie invertida)

## Contexto

O menu lateral do ADR-0032 tem 25 itens em 5 grupos accordion. Funciona, mas o usuario
carrega o sistema inteiro na tela o tempo todo, mesmo quando esta so conferindo o caixa.
A proposta do shell modular e simples: uma **home que apresenta os modulos** (o portal, em
`/launcher`) e, dentro de um modulo, um **menu que mostra so aquele grupo**.

Nada disso muda rota, tela interna ou controller. E arquitetura de navegacao, nao de
produto.

Uma primeira implementacao (commit `3aaf7fa6`) fez isso com **querystring**: o portal
mandava para `/estoque?modulo=producao`, o TagHelper lia `Request.Query["modulo"]` e
reanexava o parametro em cada href do menu. Tres problemas mataram a abordagem:

1. **Nao persiste.** A querystring so sobrevive enquanto o usuario clica no menu lateral.
   Qualquer `RedirectToAction` pos-POST, link dentro do conteudo, formulario ou paginacao
   devolve o menu inteiro no meio da tarefa.
2. **Mente quando o valor e invalido.** `?modulo=admin` (ou qualquer lixo) nao casava com
   grupo nenhum, entao o filtro era pulado em silencio — mas a sidebar exibia "Voltar ao
   portal" e a topbar exibia "Portal › Administracao" com os 25 itens do lado.
3. **URL malformada.** O card montava `href` concatenando `?` cru, e o modulo de producao
   (cujo destino ja tem query) virava `/estoque?status=vencido?modulo=producao` — quebrando
   ao mesmo tempo o filtro de vencidos e o modulo.

## Decisao

### D1. O modulo ativo e DERIVADO DA ROTA. Nao existe querystring `?modulo=`.

O `MenuViewModelBuilder` ja resolve o item ativo por segmentos de rota (ADR-0032), com
fallback para o `ActiveMenuItem` legado. **O grupo do item ativo e o modulo.**
`ModuloDefinition.ResolverPorRota(path, activeMenuItem)` reusa exatamente esse matching,
entao o modulo nunca discorda do item que o menu marca como ativo.

Consequencias:

- **Zero estado.** Nada de cookie, sessao ou parametro para propagar. Sobrevive a
  redirect, form, paginacao e link de conteudo — os tres problemas acima somem por
  construcao, nao por remendo.
- **Deep link coerente.** Quem abre `/contas-a-receber/123` de um e-mail cai no modulo
  Financeiro, com o menu certo. Nao ha URL "certa" e URL "errada" para a mesma tela.
- **Rota sem dono => menu inteiro (fail-open).** `/dashboard`, `/launcher`, `/faq` e
  qualquer rota que nao casa com item de grupo nao tem modulo. Nunca deixamos o usuario
  sem navegacao por um mapeamento faltando.
- **Nao da para "ver o menu inteiro" de dentro de um modulo.** E o ponto do shell, nao um
  efeito colateral. O escape esta a um clique: o Dashboard (D3) ou "Voltar ao portal".

### D2. O rodape vira o modulo virtual `admin`.

Dispositivos, Usuarios e Configuracoes nao formam grupo no `MenuDefinition`, mas sao um
modulo do ponto de vista do usuario. Quando a rota ativa cai no rodape, o menu esconde os
5 grupos e mantem o rodape — que, alias, permanece visivel em **todos** os modulos, porque
Configuracoes precisa estar sempre a mao.

### D3. O Dashboard permanece visivel em todos os modulos.

Ele e item fixo fora de grupo (ADR-0032) e continua sendo: e a ancora do sistema e o
caminho de 1 clique de volta ao menu inteiro. O `docs/transicao-shell-modular.md` sugeria
move-lo para dentro de Crescimento; nao fizemos — perder a ancora custa mais do que o
pouco de ruido que ela adiciona.

### D4. Favoritos ("Meu dia") resolvem ANTES do filtro de modulo.

A ordem do pipeline importa e agora esta explicita: o filtro da flag KDS **descarta** (o
item nao existe para aquele tenant), o filtro de modulo apenas **esconde** (apresentacao).
Favorito e lista de salto cross-modulo: de dentro do Financeiro o usuario continua vendo e
clicando no "Pedidos" que fixou. Clicar troca o modulo, porque a rota decide.

### D5. O portal e a home autenticada, renderizado em tela cheia.

`/launcher` substitui `/dashboard` como destino pos-login e como "inicio" em todos os
pontos do produto (landing autenticada, onboarding, 404, bottom nav mobile). O deep link
continua vencendo: `SafeRedirect` so cai no portal quando nao ha `returnUrl` valido.

O portal esconde a sidebar via `ViewBag.OcultarSidebar` no `_Layout` existente, em vez de
ganhar layout proprio: um segundo layout duplicaria o `<head>` inteiro (pipeline de CSS,
anti-flash de tema e de rail, fontes, es-fetch) e essa duplicacao ja driftou uma vez no
`Auth/SelecionarLoja.cshtml`, que ficou com Alpine por CDN enquanto o resto usa o vendored.

### D6. Rail permanente de 72px: DESCARTADO.

O plano original (`docs/mapeamento-componentes-shell.md`) pedia um rail fixo de 72px, com
a justificativa de que "`app-shell.css` ja tem `.side` com rail — e so ativar sempre". A
medicao refuta as duas partes:

- As classes `.app`/`.side`/`.side-item` daquele bloco sao **mortas**: nenhuma view as usa
  (so a galeria `Views/Dev/Components.cshtml`). O rail vivo e `html.es-rail`, a **64px**.
- O rail vivo ja existe e e uma **preferencia togglavel de dispositivo** (PATCH-1 do
  ADR-0032, persistida em `localStorage['es:rail']`, com anti-flash no `<head>`).
  Torna-lo permanente contradiz essa decisao e exigiria migrar quem ja tem a preferencia
  gravada.
- O bloqueio real nao e CSS: em rail, abrir um grupo hoje **desliga o rail**
  (`menu-sidebar.js`), saida que so existe porque o rail e opcional. Com rail permanente
  seria preciso um flyout — que o codigo evitou de proposito.

O rail togglavel convive com o shell modular sem ajuste nenhum: com o menu ja filtrado por
modulo, ele fica ainda mais util. Rail permanente, se um dia for desejado, e ADR proprio.

## Consequencias

- `ModuloDefinition` guarda **apenas** o mapeamento modulo <-> grupo; o `MenuDefinition`
  segue como fonte unica da estrutura (ADR-0032). Um teste de guarda amarra os dois, para
  que renomear a key de um grupo nao orfanize um modulo em silencio — o mesmo risco que o
  ADR-0032 ja registra para favoritos.
- Modulos de tenant (Comercial/CRM da FMA) sairam do codigo: eram mapeamentos para grupos
  inexistentes, selecionados por um `empresaId == "fma"` que comparava Guid stringificado
  com um literal e portanto nunca era verdadeiro. Gating de modulo por tenant depende de
  ler `TenantFeatureFlag` em runtime — tabela que existe mas nenhum codigo consome hoje —
  e fica para o epico da FMA.
- O atalho "Voltar ao portal" e o breadcrumb da topbar passam a aparecer exatamente quando
  o menu esta filtrado. Ambos vivem sobre a superficie invertida da sidebar/topbar e
  seguem o ADR-0045 (cor propria, contraste AA).
- O breadcrumb continua `hidden lg:flex`. No mobile o caminho de volta e o slot "Inicio"
  do bottom nav, que agora aponta para o portal.

## Alternativas consideradas

**Sessao/cookie guardando o modulo.** Sobrevive a redirect, mas cria estado invisivel: a
mesma URL renderiza menus diferentes para usuarios diferentes, o link compartilhado perde
o contexto e aparece a pergunta "quando limpar?". Derivar da rota entrega o mesmo beneficio
sem estado nenhum.

**Segmento de rota (`/m/{modulo}/...`).** Explicito e compartilhavel, mas quebraria a
promessa central do shell — que nenhuma rota muda — e obrigaria a reescrever todos os
`Url.Action`, links e testes do produto. Custo alto para informacao que a propria rota ja
carrega.

**Manter a querystring como override opcional de entrada.** Descartada porque nao ha caso
de uso: todo `HrefDefault` de modulo ja aterrissa numa rota do proprio grupo. Seria uma
segunda fonte de verdade, reabrindo a porta para "modulo diz A, menu mostra B".
