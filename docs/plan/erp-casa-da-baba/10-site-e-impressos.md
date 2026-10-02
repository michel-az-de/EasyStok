# 10 · Site casadababa.com e impressos

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) item 7 · Data: 2026-10-01
Base medida: EasyStok master `b713263a`; site `michel-az-de/casa-da-baba` `origin/main` `a28c5af` (lido por
`git archive`, sem tocar no clone local, que está 1 commit atrás e tem WIP de outra sessão em `AGENTS.md`/`CLAUDE.md`).
Plano irmão: [12-impressos.md](../atendimento-whatsapp/12-impressos.md) é dono de S49 a S53; aqui só se referencia.

## 0. Veredito

1. **O SITE ESTÁ FORA DO AR.** `casadababa.com` resolve para `92.113.33.60` (o mesmo IP de `api.easystok.online`),
   responde `503` com `Server: Caddy` e uma página de manutenção ("mudamos de servidor", botão para o WhatsApp).
   Medido em 01/10/2026 17:27 UTC. Nenhum script versionado publica o site na VPS.
2. **Não trocar a stack.** Vite 6 + Alpine 3 + CSS com tokens já está pronto para a marca: 1.953 usos de
   `var(--cdb-*)` contra 29 hex soltos no CSS. Vestir a roupa é trocar valores de token, fontes e logo, não reescrever.
3. **Impressos:** S49 e S52 mergeadas; S53 em PR verde aguardando `aprovado`; S50 e S51 sem issue e sem PR. O
   backlog de 7 documentos vira 7 fatias curtas, todas pelo ciclo mockup, aprovação, spec, código.

---

## Parte 1 · Site casadababa.com

### 1.1 Fatos medidos

| Tema | Medida | Fonte |
|---|---|---|
| Stack | Vite `^6.0.1`, Alpine `^3.14.9` + `@alpinejs/persist`, CSS puro, Vitest, Playwright + axe | `package.json:25-38` |
| Páginas (MPA) | 9 públicas: `index`, `cardapio`, `checkout`, `login`, `meus-pedidos`, `pedido-status`, `privacidade`, `entrega-zona-oeste`, `entrega-zona-sul` | raiz do repo |
| Tamanho | 280 arquivos versionados; 7.470 linhas JS em `src/`, 6.905 linhas CSS, 3.040 linhas HTML; 58 arquivos de teste | `git ls-tree origin/main` |
| "PWA" | **Não é PWA.** Sem manifest, sem service worker; `vite-plugin-pwa` não está no `package.json`. O README diz "PWA (Workbox)" | `vite.config.js:7-10`, `README.md:16` |
| API consumida | `api/storefront/{slug}/` `menu`, `frete`, `janelas`, `checkout`, `checkout/guest`, `auth/solicitar-otp`, `auth/validar-otp`, `pedidos`, `pedidos/{id}` | `src/api/*.js` (ex.: `checkout-guest.js:144`, `auth.js:142,213`) |
| Mesma origem | build com `VITE_API_BASE_URL` vazio: o site chama `/api/...` e o Caddy repassa à API | `Dockerfile:26,32`; `Caddyfile:51-60` |
| Chat do site (S36) e link do cardápio da conversa (S48) | backend mergeado (#1099, #1151); **o site não tem nenhum dos dois** (zero ocorrência de `api/public/chat` e de `cardapio-conversa`) | busca em `src/` e `*.html` |
| Grafia | guarda de build proíbe "Babá" acentuado | `scripts/check-brand.js:16-17` |

### 1.2 Onde está em produção (contradição 5 do estudo, resolvida)

| Época | Onde | Evidência |
|---|---|---|
| Plano inicial | Fly.io `gru` + Cloudflare | `README.md:21`; mas `fly.toml:1-9` é **TEMPLATE** com "TODO Felipe", nunca lançado (inferência: nenhum workflow de deploy em `.github/workflows/`, só `ci.yml`) |
| Até ago/2026 | VM Azure, container `casadababa-storefront` atrás do Caddy da stack | `docs/handoff/2026-06-24-finalizar-esteira-pedidos.md:34-36,111` (site); `docker-compose.azure.yml:209-217` e `Caddyfile:51-60` (EasyStok) |
| Hoje | **VPS da API, Caddy servindo página de manutenção 503** para `/` e para `/api/*` deste host | DNS `92.113.33.60`; `curl -I https://casadababa.com` |

Conclusão: o levantamento "Fly.io + Cloudflare" é plano que não aconteceu; o `Caddyfile` do repo é o da VM Azure.
**Lacuna declarada:** o `compose.yaml` e o Caddy da VPS (`/opt/stacks/easystok`) não estão versionados e não foram
lidos (leitura de produção por SSH não é do agente). `vps-deploy.sh` publica só `api worker web`
(`scripts/deploy/vps-deploy.sh:35`); o storefront ficou sem caminho de publicação na mudança de servidor.

Efeito colateral (inferência): o link do cardápio que a conversa envia (S48) aponta para o site
(`LinkCardapioConversaService.cs:31-33`), então hoje cai na página de manutenção.

### 1.3 Marca: site × design-system-v1

O prefixo é o mesmo (`--cdb-`), os valores não. Trocar o valor no `tokens.css` propaga para os 1.953 usos.

| Papel | Site hoje (`src/styles/tokens.css`) | DS v1 (`tokens.css:2-9`) | Ação |
|---|---|---|---|
| Ação principal | `--cdb-tomate #B84A2C` (102 usos) `:25` | Caramelo `#A25803` | tomate sai; botão Caramelo com texto Papel (5,29:1, `GUIA-DA-MARCA.md:46`) |
| Texto | `--cdb-marrom-terra #5C2E0D` `:28`, `--cdb-texto #3D1F08` `:34` | Cacau `#422814` | vira Cacau |
| Acento | `--cdb-dourado-trigo #D4A030` (18 usos) `:27` | Trigo `#E3A542` | vira Trigo, nunca em texto pequeno (`GUIA:44`) |
| Fundo | `--cdb-creme #FAF4E7` `:29`, `--cdb-creme-claro #FFFBF3` `:32` | Papel `#FEFEFE`, Creme `#FFF8E8` | fundo Papel, blocos Creme |
| Borda | `--cdb-borda #E5D4B0` `:41` | Linha `#E5D5BE` | vira Linha |
| Sucesso/verde | `--cdb-manjericao #6B7F4A` (43 usos) `:26` | **não existe** | vem da camada de tela do M0.1 (estados), não se inventa aqui |
| Título | Fraunces `:57` | Lora | troca, woff2 self-hosted |
| Texto/UI | Hanken Grotesk `:59` | Nunito Sans | troca, woff2 self-hosted |
| Logo | **ausente** (`public/img` tem só `wheat.svg`, `grao-papel.png`) | `assets/logo-oficial.png` 1536×1024 sobre `#FEFEFE` | entra no cabeçalho e no `og-image`; fundo escuro exige ativo próprio (`GUIA:26`) |
| `theme-color` | `#5C2E0D` nas 9 páginas (`index.html:12`) | | Cacau |

A página de manutenção servida hoje usa "Casa da Babá" acentuado e `#2c1605`, `#d4a030`, `#B84A2C`: fora da marca e
da regra do `check-brand.js`.

### 1.4 Checkout hoje (site `a28c5af` + API master)

```
carrinho (nome, telefone, CEP, número) ── cart-drawer.js:348
   │
   ├─ CEP atendido ou não calculado ──► /checkout.html (sem login, guest guardado) ── checkout.js:80-83
   │       janela + data ──► POST checkout/guest ──► linkPagamento (Checkout Pro) ──► Mercado Pago
   │       volta: pedido-status lê external_reference + token guardado ── pedido-status.js:151-152
   │
   ├─ CEP sem cobertura ──► POST checkout/guest SEM janela ── cart-drawer.js:357,375
   │       (ponte #1308: aguardando_aprovacao_baba, sem vaga, sem frete, sem cobrança)
   │       ──► sempre abre o WhatsApp com o resumo ── cart-drawer.js:408
   │
   └─ logado (OTP por WhatsApp) ──► POST checkout com janela ──► Mercado Pago
```

**PR #1308 e o que ela implica.** Mergeada 01/10 16:48 UTC. Aceita guest sem janela e data no "modo antigo"
(`IniciarCheckoutGuestUseCase.cs:35-38,95-96,141-174`); só um dos dois dá 422 (`:79-81`). O corpo da PR diz que a
ponte "sai quando a PR do site estiver publicada" e fixa a ordem **API primeiro, site depois**.

| Implicação | Fato |
|---|---|
| A ponte **não** some com o site #71 | o próprio #71 ainda chama `checkout/guest` sem janela para CEP fora da área (`cart-drawer.js:357-406`). Removê-la faz esse caminho cair em 422 e no toast "Não deu pra registrar agora" (o WhatsApp abre do mesmo jeito) |
| O modo sem janela não checa área | `CriarSemJanelaAsync` não chama a validação de CEP do núcleo: pedido fora da área entra na fila da dona |
| A ordem de publicação está correta e ainda não aconteceu | produção roda `buildSha 0a70eb56` (`/health/version`), anterior à #1308 (`98ed2cc1`). Com o site fora do ar, nada quebra hoje; ao religar o site, a API precisa estar em `≥ 98ed2cc1` |

### 1.5 Fatias do site

#### SI.1 · Site de volta no ar, com publicação versionada

**Problema.** O site está em manutenção desde a mudança de servidor e não há script que o publique na VPS.
**Abordagem.** Reaproveitar o `Dockerfile` do site (já pronto para mesma origem) e o padrão do `vps-deploy.sh`
(`git archive`, `flock`, tag `vps-<sha>`, `vps-prev-<ts>`, volta sozinho). Escopo do serviço conforme D-SI1.
**Escopo.** Script de publicação do storefront; bloco `casadababa.com` do Caddy da VPS versionado junto do script;
`version.txt` conferido no fim; README do site corrigido (deploy, "PWA", .NET 9, caminho `C:/easy`); `fly.toml` e
comentários "Fly" do `nginx.conf:1-5` removidos.
**Aceite.**
- [ ] `https://casadababa.com/` 200 e `/version.txt` igual ao SHA publicado.
- [ ] `/api/storefront/{slug}/menu` 200 pelo host do site (mesma origem).
- [ ] Guest com CEP atendido chega ao Checkout Pro do sandbox; guest fora da área cai no WhatsApp com número curto.
- [ ] Falha de saúde volta a imagem anterior sozinha.
**Depende de.** API publicada em `≥ 98ed2cc1` (#1308). **Quem roda.** O Felipe (publicação é dele).
**Rollback.** Religar a página de manutenção no Caddy. **Tamanho.** P. **Tier.** alto (produção).

#### SI.2 · Tokens e fontes da marca no site

**Problema.** Paleta "Butantã warmth" (tomate, manjericão) e Fraunces/Hanken divergem do DS v1 (§1.3).
**Abordagem.** Trocar valores dos primitivos em `src/styles/tokens.css` e mapear `semantic.css` para os estados
da camada de tela do M0.1 (mesma fonte do console). Lora e Nunito Sans em woff2 com subconjunto latino, a partir
de `design-system-v1/assets/fontes` (OFL, licença junto). Os 29 hex soltos do CSS e os 71 dos HTML viram token.
**Reaproveita.** Toda a estrutura de tokens, `fonts.css`, componentes, testes visuais (`tests/visual/tokens.html`).
**Aceite.**
- [ ] Nenhum hex da paleta antiga em `src/` e `*.html` (o `check:brand` passa a barrar `#B84A2C`, `#6B7F4A`, `#D4A030`, `#5C2E0D`, Fraunces, Hanken).
- [ ] Contraste ≥ 4,5:1 em todo texto (axe no e2e, sem regra desligada).
- [ ] `check:bundle` e orçamento do Lighthouse (`.lighthouserc.json`) sem piora.
**Depende de.** M0.1 (camada de tela). **Tamanho.** M. **Tier.** baixo.

#### SI.3 · Logo oficial e assinatura

**Problema.** O site não usa a logo confirmada em 30/09.
**Escopo.** Logo no cabeçalho, rodapé e `og-image.jpg`, sempre sobre Papel, com respiro (`GUIA:27`), em WebP/AVIF
gerados do PNG oficial sem redesenhar (`GUIA:24-25,31`). Favicon e versão para fundo escuro ficam como ativo
próprio a aprovar (D-SI3).
**Aceite.** [ ] Hash do PNG de origem igual ao do guia; [ ] nenhum lettering recriado em fonte; [ ] LCP sem piora.
**Tamanho.** P. **Tier.** baixo.

#### SI.4 · Site refeito com a roupa da Casa da Baba (mockup antes)

**Decisão do Felipe (01/10).** A manutenção é intencional e o site **volta já refeito**: o layout atual
foi reprovado ("tá feio"). Não há troca de token sobre o layout velho; o redesenho vem antes de religar.
**Problema.** Cards, sheet, carrinho e checkout foram desenhados para a paleta tomate e não seguem a marca.
**Abordagem.** Mockup em tamanho real (390 px e 1280 px) de todas as páginas, aprovação do Felipe, depois
código, página por página: cardápio e carrinho, checkout, status do pedido, home, login e meus pedidos,
landing pages e privacidade. Pode mudar layout, hierarquia e marcação; o ciclo é o mesmo dos impressos.
**Reaproveita.** Stores, `src/api`, validadores, os 58 arquivos de teste e os contratos da API; componentes
Alpine (`src/components/*`) só onde o novo desenho couber.
**Fora.** Contratos da API e analytics.
**Aceite.** [ ] Cada página igual ao mockup aprovado (teste visual); [ ] e2e do checkout guest e logado verdes;
[ ] nenhum hex solto fora dos tokens; [ ] grafia "Casa da Baba" (check-brand.js).
**Depende de.** SI.2, SI.3. **Tamanho.** G (fatiar por página). **Tier.** baixo.

#### SI.5 · Fim da ponte #1308 e destino do pedido fora da área

**Problema.** A ponte foi dita temporária, mas o site #71 depende dela para CEP fora da área (§1.4).
**Abordagem.** Conforme D-SI2. Na recomendada: o site deixa de registrar pedido fora da área (só WhatsApp com o
resumo), e a API volta a exigir janela e data no guest (`IniciarCheckoutGuestUseCase.cs:95-96` e `CriarSemJanelaAsync`
saem; 422 sem janela).
**Escopo.** Site: `cart-drawer.js:368-406`. API: use case, testes da #1308 invertidos, `changelog.d/`.
**Aceite.** [ ] Guest sem janela → 422; [ ] fora da área abre o WhatsApp sem POST; [ ] nenhum pedido novo em `aguardando_aprovacao_baba` vindo de `StorefrontGuest`.
**Depende de.** SI.1 no ar. **Ordem.** Site primeiro, API depois (inverso da #1308). **Tamanho.** P. **Tier.** baixo.

#### SI.6 · O site como canal de atendimento, integrado ao EasyStok

**Decisão do Felipe (01/10).** O site entra como **canal de atendimento** (D5: canal novo entra como canal,
não como tela). Cardápio e compra do site estão "mais ou menos": o fluxo fica, a cara muda no SI.4.
**Fato medido.** O backend de S36 (#1099, chat do site, `CanalConversa.ChatSite = 4` em
`Domain/Enums/Atendimento/CanalConversa.cs:12`) e S48 (#1151, cardápio da conversa) está mergeado e o site não
consome nenhum dos dois. O checkout do site não cria nem vincula `Conversa`: nenhuma referência a `Conversa`
nos use cases de checkout do storefront (`git grep`, 01/10). Hoje o pedido do site só aparece na lista de
pedidos e na cozinha, nunca no balcão.
**Escopo.**
1. Widget de chat com `fetch` + stream (não `EventSource`, o token vai no header, conforme a nota da S36).
2. Página do cardápio lê `?c=<token>`, envia por `POST api/storefront/cardapio-conversa/{token}/pedido` e trata 410.
3. **Pedido do checkout do site abre (ou reaproveita) a conversa `ChatSite` do cliente** e aparece no balcão
   com o pedido na Ficha, como os pedidos do WhatsApp; mensagens de status saem pelo canal de preferência do
   cliente (consentimento S38). Backend: vincular no `CheckoutCoreService` após criar o pedido, idempotente.
4. Cliente logado no site (OTP) e cliente do WhatsApp são o mesmo cadastro (identidade por canal, S34).
**Aceite.** [ ] Mensagem do site aparece no console com `Canal=ChatSite`; [ ] pedido feito no checkout do site
aparece no balcão vinculado ao cliente, sem duplicar conversa em novo pedido; [ ] token vencido mostra o pedido
de link novo; [ ] URL sem telefone nem nome.
**Depende de.** SI.1; visual depois de SI.2. **Tamanho.** M (fatiar: backend do vínculo, depois widget). **Tier.** baixo.

---

## Parte 2 · Impressos

### 2.1 Estado de S49 a S53 (medido com `gh pr list --state all`)

| Fatia | Estado | PR/Issue | Observação |
|---|---|---|---|
| S49 Motor + Pedido 3 papéis | ✅ mergeada | #1270 | `Api/Data/Templates/Impressao/pedido.*.sbn`, `impressos.css` |
| S50 "Pronto até" por loja | ⬜ sem PR, sem issue | | constante 30 min segue no use case |
| S51 Consumidores (navegador e 58 mm raster) | ⬜ sem PR, sem issue | | `escPosOrderLabel` texto segue no PWA (`pwa/index.html:18658`) |
| S52 Comanda | ✅ mergeada | #1282 | `comanda.*.sbn`, `comanda.css` |
| S53 Número do dia + conservação | 🟡 aberta | #1293 | `MERGEABLE`/`CLEAN`, labels `migrations`, tier alto: aguarda `aprovado` |

Issue-mãe #1267 está fechada; S50 e S51 ficaram sem rastreio.

### 2.2 Ordem recomendada (sem reescrever as specs)

```
S53 (#1293, aprovar) ──► S51 (consumidor) ──► S50 (pronto até) ──► SI.7 (tokens) ──► SI.8 a SI.14 (um por vez)
```

- **S51 antes de S50.** Sem S51 nenhum impresso novo chega ao papel da WTP05; a constante de 30 min de S49 atende
  até a S50. S50 é migração (tier alto) com ganho menor.
- **Risco em S51 (inferência):** a S51 põe o raster no PWA, que sai quando o M5 chegar à paridade (ADR-0056 item 7).
  O console no navegador não fala ESC/POS pelo Bluetooth do plugin nativo. Decidir onde mora o raster (D-SI4) antes
  de abrir a issue da S51.
- Abrir as issues de S50 e S51 antes de codar (P1 do CLAUDE.md).

### 2.3 Fatias do backlog e do alinhamento com a tela

Regras comuns (herdadas de `12-impressos.md:12-25`): térmica só preto e impressa como imagem; A4 com cor só em
linha, contorno e texto; CPF nunca; texto do cliente escapado; snapshot em `EasyStock.Api.UnitTests/Impressao/Snapshots/`;
endpoint com policy própria e 404 para outra empresa; **mockup aprovado e versionado em `impressos/` antes da spec**.
Cada fatia abaixo vira spec S nova no plano irmão quando o mockup for aprovado (`12-impressos.md:182`).

#### SI.7 · Uma fonte de tokens para tela, site e papel

**Problema.** Três cópias divergentes da marca: `impressos.css:3-4` (`--cacau`, `--caramelo`, `--linha` e um
`--mute #7a5f48` fora do DS), o `tokens.css` do site (§1.3) e a camada de tela do M0.1 (a criar).
**Abordagem.** Os primitivos do DS v1 (`--cdb-papel` a `--cdb-texto`) viram um arquivo único gerado de
`design-system-v1/tokens.json`; console, site e `impressos.css` importam; `--mute` vira token de estado aprovado ou sai.
Pré-visualização do impresso no console usa o mesmo tema.
**Aceite.** [ ] Snapshots de S49/S52/S53 sem mudança de pixel; [ ] um teste falha se algum dos três repetir hex da marca fora do arquivo gerado.
**Depende de.** M0.1. **Tamanho.** P. **Tier.** baixo.

| Fatia | Documento | Papéis | Reaproveita | Lacuna a resolver na spec | Depende de |
|---|---|---|---|---|---|
| SI.8 | Resumo do pedido (cliente) | 10×15, A4 | `PedidoImpressoDto` e modelos `pedido.*` (S49) | o que some do Pedido de expedição (nota interna, "pronto até") | S51 |
| SI.9 | Recibo de pagamento | 58 mm, A4 | `CobrancaPedido`, `PedidoPagamento` | recibo não é cupom fiscal (RN-28): texto obrigatório no rodapé | S51 |
| SI.10 | Recibo de envio | 10×15 | `ParadaViagem`, entregador (S44), CODE128 (S49) | dado do destinatário sem CPF; Lalamove (F17) muda o responsável | S51, F17 opcional |
| SI.11 | Etiqueta de agradecimento | 10×15 | `Storefront.InstagramUrl` (`Storefront.cs:116`); QRCoder já no `Infra.Async.csproj:19` | QR em SVG para snapshot determinístico; frase do agradecimento sem slogan inventado (`GUIA:104`) | S51, SI.3 (logo) |
| SI.12 | Caixa do dia | 58 mm, A4 | `GET api/caixa/dia`; substitui `printCashClosing` (`pwa/index.html:20489`) | contado e diferença e resumo por método só existem depois da F14 | F14 (#1244) |
| SI.13 | Lista de compras | 58 mm, A4 | substitui `shoppingListPrintHtml` (`pwa/index.html:20495`) | fonte da lista no backend (hoje é do PWA) | S51; M2 |
| SI.14 | Relatório do cliente | A4 multipágina | dossiê do cliente (tags, notas, pedidos) | quebra de página e cabeçalho repetido; LGPD: o que pode sair no papel | SI.7 |

Aceite mínimo de cada uma: [ ] igual ao mockup aprovado; [ ] térmica só preto; [ ] sem CPF; [ ] texto escapado;
[ ] outra empresa 404, modelo inválido 400. Tamanho P a M cada; tier baixo, salvo se precisar de campo novo
(migração, alto). SI.12 e SI.13 aposentam as funções equivalentes do PWA junto com o M5.

---

## 3. Ordem de execução deste doc

| Passo | O quê | Bloqueio |
|---|---|---|
| 1 | Publicar a API com #1308; preparar SI.1 (publicação versionada), sem religar ainda | D-SI1 |
| 2 | S53 com `aprovado`; abrir issues de S50 e S51 | D-SI4 |
| 3 | M0.1 (no 01-fundacao) → SI.7 → SI.2 → SI.3 | D-02 do README |
| 4 | SI.5 (fim da ponte), SI.6 (chat e link da conversa) | D-SI2 |
| 5 | SI.4 por página (mockups aprovados) e só então religar o site pelo SI.1; S51, S50; SI.8 a SI.14 um por vez | mockups aprovados |

## 4. Contradições encontradas

| # | Contradição | Lados | Fonte vigente |
|---|---|---|---|
| 1 | Onde o site roda | README e `fly.toml` (Fly + Cloudflare) × Caddyfile (VM Azure) × DNS (VPS em manutenção) | DNS e `curl` de 01/10: VPS, 503 |
| 2 | "Storefront em produção" | `00-estudo.md:79` × site em manutenção | `curl` |
| 3 | Ponte #1308 "temporária" | corpo da #1308 × site #71 ainda usa o modo sem janela fora da área | código do site `cart-drawer.js:357-406` |
| 4 | "PWA (Workbox)" | `README.md:16` × sem manifest nem service worker | `vite.config.js:7-10`, `package.json` |
| 5 | Mesmo prefixo, valores diferentes | `--cdb-creme #FAF4E7` (site) × `#FFF8E8` (DS) | DS v1 |
| 6 | Grafia na página de manutenção | "Casa da Babá" × regra "Baba" sem acento | `check-brand.js:16-17` |
| 7 | S50/S51 sem rastreio | `12-impressos.md` lista as duas × issue-mãe #1267 fechada | GitHub |

## 5. Decisões pendentes do Felipe

**D-SI1 · Como o site volta a ser publicado na VPS?**
- (a) **(Recomendado)** Script próprio no repo do site, copiando o padrão do `vps-deploy.sh` (trava, tag, volta sozinho), rodado por você; o EasyStok não conhece o repo do site.
- (b) Acrescentar `storefront` ao `SERVICES` do `vps-deploy.sh`, buscando a fonte do outro repo.
- (c) Publicar à parte como o console (build local + `scp` de arquivos estáticos para o Caddy servir).

**D-SI2 · Pedido do site com CEP fora da área**
- (a) **(Recomendado)** Não registra: só abre o WhatsApp com o resumo; a ponte #1308 sai inteira.
- (b) Registra como hoje (aprovação da dona) e a ponte vira regra permanente, com nome e teste próprios.
- (c) Bloqueia o envio no site com aviso de área não atendida, sem WhatsApp.

**D-SI3 · Ativos de marca que o DS v1 não tem (favicon, logo em fundo escuro, cor de sucesso)**
- (a) **(Recomendado)** Pedir os três como extensão versionada do DS (v1.1) antes de SI.3 e SI.2.
- (b) O agente propõe derivações no mockup do SI.4 e você aprova junto.
- (c) Ficar sem: favicon do trigo atual, nenhum fundo escuro, sucesso em Cacau com ícone.

**D-SI4 · Onde mora o raster do cupom de 58 mm (S51)**
- (a) **(Recomendado)** Na API: devolve PNG 1 bit de 384 px pronto; PWA hoje e console ou ponte de impressão amanhã só mandam os bytes.
- (b) No PWA, como a S51 escreveu, aceitando refazer quando o PWA sair.
- (c) Adiar a S51 até o M5 e imprimir o 58 mm só em texto até lá.

**D-SI5 · Escopo visual do site: DECIDIDO (01/10).** Redesenho completo por mockup (SI.4) antes de religar; a
manutenção atual é intencional. Não trocar token sobre o layout velho.
