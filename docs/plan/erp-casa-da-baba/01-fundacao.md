# M0 · Fundação: marca, shell, perfis × módulos, tenant fixo

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Plano: [README](README.md)
Base medida: master `b713263a` (worktree `erp-casa-da-baba-1316`), 01/10/2026. Leitura somente.

M0 destrava todos os módulos: sem tema, sem shell e sem permissão por módulo, nenhuma tela nova
tem onde morar nem quem a proteja. Cinco fatias:

| Fatia | Título | Toca | Tier | Depende de |
|---|---|---|---|---|
| M0.1 | Tema da marca no console | `EasyStock.Console/` | baixo | (nada) |
| M0.2 | Shell: login, sala de módulos, módulo com menu | `EasyStock.Console/` | baixo | M0.1, M0.3 |
| M0.3 | Perfis × módulos na API | Domain, Application, Api, migration | **alto** | (nada) |
| M0.4 | Tenant fixo e poda de código SaaS/FMA | Domain, Application, Api, Infra, Web | **alto** | M0.3 |
| M0.5 | Migration de limpeza das tabelas | Infra.Postgre (migration) | **alto** | M0.4 em produção |

**Ordem proposta:** M0.1 e M0.3 em paralelo (uma é console, a outra backend) → M0.2 → M0.4 → M0.5.
Diverge do README §3 ("tema → shell → perfis"): o shell precisa do endpoint de módulos da M0.3
para não decidir permissão no front (princípio 2 do README).

---

## M0.1 · Tema da marca no console

> Execução de 09/10/2026: tema global, tipografia, logo derivada, contraste automatizado e ajuste de largura implementados. Capturas e limites da homologação na [seção 13 do levantamento](11-levantamento-e-ondas-2026-10-09.md#13-continuação-autorizada-identidade-e-navegação-09102026). A M0.2 avançou com busca, login único e guarda compartilhado; M0.3/perfis continua pendente. Este registro não declara deploy.

**Fato medido.** O console define tudo em tokens (`src/estilos/tokens.css:1`, "nenhum componente
escreve cor... literal"), com `light-dark()` em 44 declarações e tema claro/escuro escolhido pelo
usuário (`tokens.css:18,226-227`; interruptor em `src/app/Moldura.jsx:135-160`). A identidade é o
tomate do protótipo (`--ragu #B0381C`, `--acao` em gradiente, `tokens.css:63-72`), corpo em SF/Inter
e marca em Fraunces (`tokens.css:202-206`), carregados do Google Fonts (`index.html:19`).
O design-system-v1 da marca (`...\marca\design-system-v1\`) tem 6 cores, 2 famílias (Lora,
Nunito Sans) e foi feito **para impressos** (`GUIA-DA-MARCA.md:3`, escopo cardápio, cartão e
etiqueta); o próprio `tokens.json` marca `"system_status": "proposta_visual_aguardando_avaliacao"`.
A logo só tem ativo para fundo claro `#FEFEFE`: "para fundo escuro ou transparente, será necessário
preparar um ativo próprio" (`GUIA-DA-MARCA.md`, §1). O PNG oficial tem 740 KB e o build é arquivo
único com tudo embutido (`vite.config.js`, `viteSingleFile`, `assetsInlineLimit: 100000000`).

**Dimensionamento (grep de `var(--token)` em `src/`, inclui as autorreferências do `tokens.css`).**

| Grupo | Tokens (usos) | Total | Arquivos |
|---|---|---|---|
| Marca/ação | `--ragu` 71, `--ragu-claro` 30, `--halo` 9, `--acao-gradiente` 3, `--acao-forte` 2, `--acao` 1, `--sobre-acao` 1, `--ragu-forte` 1, `--ragu-contraste` 0 | 118 | 34 |
| Texto | `--tinta-2` 184, `--tinta` 125, `--tinta-3` 118 | 427 | 45 |
| Superfície | `--papel` 83, `--elevado` 57, `--fundo` 15, `--semola` 11, `--semola-2` 10, `--vidro` 7, `--comanda` 4, `--chao-conversa` 2 | 189 | 28 |
| Linha | `--linha` 100, `--linha-forte` 48, `--linha-media` 18, `--preenchimento-forte` 24, `--preenchimento` 23 | 213 | 35 |
| Estado | `--parado` 43, `--atencao` 39, `--ok` 34, `--perigo` 25, `--ok-pastel` 13, `--perigo-fundo` 12, `--atencao-pastel` 12, `--parado-pastel` 6, `--aviso` 6, `--aviso-fundo` 5, `--sobre-parado` 4, `--ok-fundo` 3, `--entregue` 2, `--info` 2, `--info-fundo` 2 | 208 | 18 |
| Canal | `--c-whatsapp` 4, `--c-instagram` 4, `--c-site` 2 (sem Messenger, e-mail, SMS) | 10 | 2 |
| Tipografia | `--t-apoio` 303, `--t-corpo` 49, `--t-forte` 21, `--t-micro` 18, `--t-titulo` 14, `--t-11` 12, `--t-valor` 8, `--t-15` 8, outros degraus numéricos 21 | 454 | 43 |
| Fonte | `--f-dado` 68, `--f-corpo` 12, `--f-display` 8, `--f-marca` 3, `--f-papel` 3 | 94 | 24 |

Fora dos tokens: **28 hex literais** em 8 arquivos (15 em `features/ficha-cliente/cliente.module.css`,
5 em `ficha.module.css`) e 27 `rgb(a)(` literais em CSS. Espaço, raio e movimento não mudam.
Como os componentes chamam o token pelo papel (`--t-apoio`, `--tinta-2`), a troca é quase toda no
`tokens.css`; o risco está nos 28 literais e no texto que cresce dentro de colunas fixas
(`--largura-balcao: 360px`, `tokens.css:114`).

**Camada de tela proposta** (o que o DS não tem). Contraste calculado pela fórmula WCAG 2.2 sobre
Papel `#FEFEFE`, Creme `#FFF8E8` e Chão `#F6EEDD`; mínimo exigido 4,5:1 para texto e 3:1 para
borda de controle e foco.

| Token novo | Valor | Papel | Creme | Chão | Uso |
|---|---|---|---|---|---|
| `--fundo` | Chão `#F6EEDD` | | | | chão da tela |
| `--papel` | Papel `#FEFEFE` | | | | cartão, painel |
| `--elevado` | Creme `#FFF8E8` | | | | hover, faixa, bloco embutido |
| `--texto` | Cacau `#422814` | 13,47 | 12,84 | 11,77 | texto principal |
| `--texto-2` | `#5E4128` | 9,20 | 8,77 | 8,04 | apoio |
| `--texto-3` | `#735A45` | 6,35 | 6,05 | 5,55 | legenda, carimbo |
| `--marca` | Caramelo `#A25803` | 5,29 | 5,04 | 4,63 | título de seção, link, seleção |
| `--acao` / `--acao-forte` | `#A25803` / `#874902` | 5,29 / 6,98 (texto Papel sobre o botão) | | | botão primário, hover |
| `--acento` | Trigo `#E3A542` | 2,14 | | | **só decoração**; Cacau sobre Trigo 6,29 |
| `--linha` | Linha `#E5D5BE` | 1,43 | | | separador (não é borda de controle) |
| `--linha-forte` | `#8C7259` | 4,46 | 4,25 | 3,90 | borda de campo, foco (≥ 3:1) |
| `--foco` | anel 2 px `#A25803` | 5,29 | 5,04 | 4,63 | `:focus-visible` |
| `--sucesso` + pastel | `#2B6A45` / `#E3F1E7` | 6,41 | 6,11 | 5,60 | pastel 5,54 |
| `--alerta` + pastel | `#7A5A00` / `#FBF0CC` | 6,33 | 6,03 | 5,53 | pastel 5,60 |
| `--erro` + pastel | `#A8201A` / `#FBE5E2` | 7,21 | 6,87 | 6,30 | pastel 6,03 |
| `--info` + pastel | `#1F5A7A` / `#DFEDF5` | 7,43 | 7,08 | 6,49 | pastel 6,27 |

| Canal | Token | Valor | Papel | Chão |
|---|---|---|---|---|
| WhatsApp | `--c-whatsapp` | `#1A7A43` | 5,33 | 4,65 |
| Instagram | `--c-instagram` | `#C1306B` | 5,34 | 4,66 |
| Messenger | `--c-messenger` (novo) | `#1A5FD0` | 5,80 | 5,07 |
| Site | `--c-site` | `#7A4A1E` | 7,37 | 6,44 |
| E-mail | `--c-email` (novo) | `#5B4FA0` | 6,78 | 5,92 |
| SMS | `--c-sms` (novo) | `#4A5868` | 7,21 | 6,30 |

| Etapa da esteira (`src/dominio/esteira.js:10-37`) | Token | Valor | Papel |
|---|---|---|---|
| Aguardando pagamento | `--etapa-aguardando` = `--alerta` | `#7A5A00` | 6,33 |
| Pago | `--etapa-pago` = `--texto-2` | `#5E4128` | 9,20 |
| Em preparo | `--etapa-preparo` = `--sucesso` | `#2B6A45` | 6,41 |
| Embalado | `--etapa-embalado` | `#3E6B2B` | 6,23 |
| Em entrega | `--etapa-entrega` = `--info` | `#1F5A7A` | 7,43 |
| Entregue | `--etapa-entregue` | `#3D4F8F` | 7,67 |

Regras da camada: cor nunca é o único sinal (estado e etapa levam ícone e rótulo, porque Caramelo e
alerta ficam vizinhos na roda de cor); Trigo nunca em texto; a marca não significa estado (mantém a
regra do protótipo, `tokens.css:56-58`).

**Escala tipográfica grande** (pedido de fonte grande e alto contraste; base 16 px):

| Papel | Hoje | Proposto | Família |
|---|---|---|---|
| `--t-micro` | 13 | 14 | Nunito Sans |
| `--t-apoio` | 15 | 16 | Nunito Sans |
| `--t-corpo` | 16 | 17 | Nunito Sans |
| `--t-forte` | 17 | 19 | Nunito Sans 700 |
| `--t-titulo` | 20 | 22 | Lora 500 |
| `--t-valor` | 24 | 28 | Nunito Sans 700, algarismo tabular |
| `--t-display` | 32 | 36 | Lora 600 |
| `--t-11` (rótulo de etapa) | 11 | 13 | Nunito Sans 700 |

Fontes: `--f-corpo`/`--f-dado` → Nunito Sans; `--f-display` → Lora; `--f-marca` sai (a assinatura é
a logo; "Lora e Nunito Sans não substituem o lettering", `GUIA-DA-MARCA.md` §1); `--f-papel` (mono
da comanda) fica. `font-variant-numeric: tabular-nums` na Nunito Sans: suporte a `tnum` a conferir
no arquivo da fonte (inferência).

**Lacunas.**

| # | Lacuna | Onde | Correção |
|---|---|---|---|
| 1 | Logo sem ativo para tela (740 KB, embutido em base64 pelo singlefile) | `vite.config.js`, `GUIA` §1 | Derivado reduzido (PNG/WebP ~480 px de largura) gerado do canônico, com o SHA-256 do original citado no commit; a imagem não é redesenhada |
| 2 | Nome "Casa da Baba" escrito em Fraunces no topo e selo "cb" | `Moldura.jsx:249,309-310` | Logo no topo; texto só para leitor de tela |
| 3 | 28 hex literais fora dos tokens | 8 arquivos (lista acima) | Viram token; `npm run qualidade` ganha checagem de hex fora do `tokens.css` |
| 4 | Sem cor para Messenger, e-mail e SMS | `tokens.css:75-77` | Tokens da tabela de canais |
| 5 | Apelidos velhos (`--semola*`, `--aviso*`, `--perigo*`, `--ragu*`, degraus numéricos) | `tokens.css:42-43,100-106,147-149` | Renomear os usos para o nome novo e apagar o apelido no mesmo PR |

**Console.** `tokens.css` reescrito com os nomes da tabela; `base.css` usa `--f-corpo`; `index.html`
troca o link do Google Fonts para Lora 500/600 e Nunito Sans 400/700 (mesmo mecanismo de hoje,
`index.html:19`); logo em `src/assets/`.

**Decisão já tomada nesta spec.** Mapeamento por papel, não por cor: quem usava `--ragu` passa a
`--marca` ou `--acao` conforme o uso (texto ou fundo); os estados `ok/atencao/parado` passam a
`sucesso/alerta/erro`, e `parado` (rosa, cliente esperando) vira `erro`.

**Aceite.**
- [ ] `grep -rE '#[0-9A-Fa-f]{3,6}\b' src --include=*.css --include=*.jsx` fora do `tokens.css` devolve 0.
- [ ] `grep -rE 'ragu|semola|Fraunces|Inter' src index.html` devolve 0.
- [ ] Tabela de contraste deste doc conferida por script no repositório (`ferramentas/`), rodando no `npm run qualidade`, falhando abaixo de 4,5:1 (texto) ou 3:1 (borda e foco).
- [ ] Sem rolagem horizontal no balcão a 1280 × 800 e a 1024 × 768 (tablet), com a escala nova.
- [ ] Logo no topo e no login, derivada do canônico; bundle final menor que hoje + 150 KB.
- [ ] Captura antes/depois do balcão, da cozinha e da gestão anexada ao PR.

**Modo escuro (D-03, DECIDIDO 01/10: entra em tudo).** Tema claro e escuro desde a v1, pelo
`light-dark()` que o console já usa. Valores escuros: fundo `#1A120C`, texto `#F6EBDD` (15,71:1),
Caramelo claro `#F0A960` (9,29:1) para títulos e ação; estados e canais ganham par escuro com a mesma
regra de contraste. A logo, que só existe para fundo claro, fica dentro de uma **placa clara** (Papel
`#FEFEFE`, cantos retos) no topo e no login. Aceite extra: o script de contraste confere os dois temas,
e a captura antes/depois sai nos dois.

**Fora.** Ícones novos; mascote; impressos (S49–S53).

---

## M0.2 · Shell: login, sala de módulos, módulo com menu

**Fato medido.** O console não tem roteador nem biblioteca de rota (dependências: `react`,
`react-dom`, `@dnd-kit/*`, `package.json`). A tela sai de `rotaDaHash` (`src/dominio/rota.js:25-33`)
lida por `useHash` (`src/hooks/useHash.js:18-20`) em `App()` (`src/app/App.jsx:292-298`), com 4 rotas:
principal, `#/entregas`, `#/cozinha`, `#/cardapio-link/<id>`. As janelas abrem por `window.open` com
hash (`Moldura.jsx:214`, `GavetaEntregas.jsx:32`, `GavetaEntregasApi.jsx:22`, `Balao.jsx:51`). O
build é um arquivo HTML único (`vite.config.js`), servido pelo Caddy na mesma origem da API
(comentário em `vite.config.js`); o `Caddyfile` deste master ainda não tem host do console (F13, PR #1310).

Login: dois passos do ADR-0047 (`infra/api/autenticacao.js:5-12`: `lista-empresas` e depois
`login`); com uma empresa entra direto e recusa superadmin (`features/login/TelaLogin.jsx:30-38`);
título "EasyStok" e subtítulo "Console de atendimento" (`TelaLogin.jsx:46-47`), enquanto o topo diz
"Casa da Baba" (`Moldura.jsx:249`). A sessão fica no `sessionStorage`, sem refresh
(`infra/api/sessao.js:1-4`). A API já resolve a empresa sozinha quando o usuário tem uma só ativa
(`AutenticarUsuarioUseCase.cs:163-172`), então `POST api/auth/login` com `empresaId: null` basta.
`LoginUsuarioInfo` devolve `id, nome, email, nivel` e nada de módulo (`Api/Controllers/AuthController.cs:31`).

**Onde mora cada tela de hoje** (base para "outras telas deste módulo"):

| Módulo | Telas do console que já existem | Arquivos |
|---|---|---|
| M3 Atendimento | balcão, conversa, ficha, respostas, automáticas, lembretes, encerramento, notas, anexos, agente, assistente, cardápio para arrastar, cardápio por link, Gestão › Atendimento | `features/caixa-de-entrada`, `atendimento`, `ficha-cliente` (4.607 linhas), `respostas`, `automacoes`, `lembretes`, `encerramento`, `notas`, `anexos`, `agente`, `assistente`, `cardapio`, `cardapio-link`, `gestao/atendimento` |
| M4 Cozinha | tablet de parede, lote de papel | `features/cozinha`, `lote-papel` |
| M5 Caixa | Gestão › Caixa | `features/gestao/caixa` |
| M8 Entregas | janela de entregas, Gestão › Janelas | `features/entregas` (2.073 linhas), `gestao/janelas` |
| M2 Produção | Gestão › Produção e cardápio | `features/gestao/producao` |
| M6 Campanhas | Gestão › Fidelidade e cupons | `features/gestao/fidelidade` |
| M7 Configurações | Gestão › Entregas e integrações | `features/gestao/integracoes` |
| M1 Cardápio | nenhuma própria | (a aba de produção cobre parte) |

As abas da Gestão moram num modal só (`features/gestao/ModalGestao.jsx:20-30`). M0.2 **não move**
as abas: cada módulo leva a sua na própria fatia (Mx.1). O shell aponta para elas enquanto isso.

**Peças.**

| Peça | Backend | Onde |
|---|---|---|
| Login de um passo | `POST api/auth/login` com `empresaId: null` | `AuthController.cs:59`, `AutenticarUsuarioUseCase.cs:110` |
| Módulos liberados e porta de entrada | `GET api/auth/me/modulos` (M0.3) | novo |
| Sino | `GET api/notificacoes/badge`, `recentes`, `PATCH {id}/lida` | `NotificacaoController.cs:48,74,100` |
| Usuário (nome, sair) | claims do token; sair limpa sessão | `autenticacao.js:27-30` |

**Roteamento: hash, estendido.** Rotas `#/` (sala), `#/m/<modulo>` (início do módulo),
`#/m/<modulo>/<tela>`; as 4 rotas de hoje continuam válidas como apelido (`#/cozinha` =
`#/m/cozinha/fila`, `#/entregas` = `#/m/entregas/painel`). Motivos medidos: build de arquivo único
sem servidor de rota; 4 `window.open` e o link de cardápio já usam hash; History API exigiria
fallback no Caddy (host que ainda nem existe neste master) e reescrever esses 4 pontos, para ganho
só de URL bonita. `rotaDaHash` continua pura em `dominio/` e cresce com testes.

**Shell.**

```
#/ (sala)                               #/m/caixa/...
┌──────────────────────────────────┐    ┌──────┬────────────────────────────┐
│ logo  [ busca ]        sino  user│    │ menu │ tela                        │
│ ┌────┐ ┌────┐ ┌────┐ ┌────┐      │    │ lat. │                             │
│ │ M1 │ │ M2 │ │ M3 │ │ M4 │ ...  │    │      │ outras telas deste módulo → │
│ │Lib.│ │Sem │ │Lib.│ │Lib.│      │    └──────┴────────────────────────────┘
│ └────┘ └perm┘ └────┘ └────┘      │
└──────────────────────────────────┘
```

- Card com selo **Liberado** ou **Sem permissão** (vem da API, nunca do nível no front); card de
  módulo ainda sem tela no console mostra **Em breve** e não navega.
- Busca no topo filtra módulos e telas pelo nome (lista estática do shell; nada de busca de dados).
- Porta de entrada: depois do login, `portaDeEntrada` da M0.3 decide a rota (Atendimento → `#/m/atendimento`,
  Cozinha → `#/m/cozinha/fila`, Dona → `#/`; o entregador não loga, abre o link da viagem, D8-01).
- **Cockpit intacto (D5):** `#/m/atendimento` monta exatamente a `Composicao` de hoje
  (`App.jsx:46`) dentro do `AtendimentoProvider`; o menu lateral do M3 é recolhido por padrão e o
  topo ganha só o botão "módulos". As janelas de Cozinha e Entregas continuam sem
  `AtendimentoProvider` (`App.jsx:240-245`, comentário).
- Modo demonstração (sem `VITE_FONTE_DADOS`): todos os módulos liberados, sem login, como hoje.

**Lacunas.**

| # | Lacuna | Onde | Correção |
|---|---|---|---|
| 1 | Tablet da cozinha perde a sessão ao fechar a aba (`sessionStorage`, sem refresh) | `sessao.js:1-4` | D-05 DECIDIDO: sessão longa (refresh token e "sair" explícito) só no perfil Cozinha; os demais seguem no `sessionStorage` |
| 2 | Entregador não tem usuário: `Entregador` não referencia `Usuario` | `Domain/Entities/Atendimento/Entregador.cs:9-21` | Continua assim: D8-01 decidiu link por viagem, sem login (`09-m8-entregas.md`) |
| 3 | Login com "EasyStok / Console de atendimento" | `TelaLogin.jsx:46-47` | D-02 DECIDIDO: logo da Casa da Baba e "EasyStok" pequeno no login e no rodapé ("Casa da Baba · EasyStok") |
| 4 | Login com conta Google entrou no master depois desta spec (#1325, `features/login/BotaoGoogle.jsx`) | `BotaoGoogle.jsx:5-12` | O shell mantém os dois caminhos (e-mail e senha, Google); medir o fluxo antes de mexer na tela de login |
| 4 | `useSessaoApi` repetido em 4 componentes de rota | `App.jsx:258,273,280,287` | Um guarda de sessão só no shell |

**Aceite.**
- [ ] Login de um passo: o console não chama mais `lista-empresas` (`grep -r lista-empresas src` = 0).
- [ ] Usuário de cada perfil semeado (M0.3) cai na porta certa; teste de `rotaDaHash` e da porta por perfil em `dominio/`.
- [ ] Card bloqueado não navega e a rota digitada à mão de módulo sem permissão mostra "Sem permissão" (a API nega de qualquer forma).
- [ ] `#/cozinha`, `#/entregas` e `#/cardapio-link/<id>` continuam abrindo a mesma tela de hoje.
- [ ] Balcão, conversa e ficha sem regressão: o roteiro de homologação do F13 passa igual.
- [ ] Sino mostra o `badge` real e marca como lida.
- [ ] `npm run qualidade` verde; `ferramentas/verificar-camadas.mjs` sem exceção nova.

**Fora.** Mover as abas da Gestão para os módulos (cada Mx.1); vínculo do entregador; sessão longa do tablet.

---

## M0.3 · Perfis × módulos na API

**Fato medido.**

| Peça | Estado | Onde |
|---|---|---|
| `Perfil` (por empresa, com `Nivel`), `PerfilPermissao`, `UsuarioPerfil` (com `LojaId`) | existem | `Domain/Entities/Perfil.cs:3-15`, `PerfilPermissao.cs`, `UsuarioPerfil.cs` |
| Permissão gravada como **texto** | `HasConversion<string>()`, até 80 chars | `Infra.Postgre/Data/Configurations/PerfilPermissaoConfiguration.cs:9` |
| Permissões viram claim `permissao` no JWT | sim | `Api/Services/JwtTokenService.cs:34-35` |
| Login usa **um** perfil por empresa (o de menor `Nivel`) | sim | `AutenticarUsuarioUseCase.cs:124-138` |
| Regra efetiva: com permissão explícita valem só elas; sem nenhuma, fallback por nível | sim | `Domain/Services/PoliticaPermissao.cs:10-26` |
| Checagem fina no controller | `TemPermissao` | `CurrentUserAccessor.cs:48-55` (8 controllers usam) |
| Policies da API | só por nível: `SuperAdmin` 4, `Admin` 24, `Gerente` 26, `Operador` 94 usos de `[Authorize(Policy=...)]` | `ApiServiceCollectionExtensions.cs:113-121` |
| CRUD de perfil | não existe; só `PUT api/usuarios/{id}/perfis` | `UsuarioController.cs:107` |
| Perfis em produção | seeds de perfil só rodam em Development ou `SEED_DEMO_DATA=true` | `StartupMigrationsAndSeed.cs:225-229`; `CasaDaBabaSeed.cs:37-39` cria Admin, Gerente, Operador |

**Enum `Permissao` (29 membros, `Domain/Enums/Permissao.cs:3-34`), usos fora de migration:**

| Destino | Membros (usos) |
|---|---|
| **Remover** (18) | `GerenciarLojas` 0, `GerenciarFornecedores` 0, `GerarRelatorioVendas` 0, `AcessarInteligencia` 0, `VisualizarTickets` 2, `ResponderTickets` 2, `GerenciarTickets` 0, `ResponderTicketsInternos` 1, `EncaminharTicketNivel` 0, `RevelarPiiCliente` 0, `GerarBugFix` 0, `ConfigurarSla` 4, `VisualizarFaturas` 0, `EmitirFatura` 0, `GerenciarFaturas` 0, `CancelarFatura` 0, `GerenciarFaq` 0, `VisualizarMetricasHelpdesk` 0 |
| **Manter** (11) | `AtenderConversas` 17, `VisualizarContasAReceber` 10, `VisualizarContasAPagar` 9, `GerenciarUsuarios` 8, `VisualizarRelatorios` 7, `GerenciarEstoque` 6, `GerenciarProdutos` 5, `GerenciarContasAPagar` 2, `GerenciarContasAReceber` 2, `GerenciarCategoriasFinanceiras` 1, `GerenciarCentrosCusto` 1 |
| **Novos** (8) | `AcessarModuloCardapio`, `AcessarModuloProducao`, `AcessarModuloAtendimento`, `AcessarModuloCozinha`, `AcessarModuloCaixa`, `AcessarModuloCampanhas`, `AcessarModuloConfiguracoes`, `AcessarModuloEntregas` |

Os 9 usos das removidas estão em `PoliticaPermissao.cs:18-24`, `Infra.Async/Reporting/WorkerCurrentUserAccessor.cs:106`
e testes (`Domain.Tests/Services/PoliticaPermissaoTests.cs:38-41`). Helpdesk saiu na P03 (#1121) e
faturas na P02 (#1135), mas o enum ficou.

**Lacunas.**

| # | Lacuna | Risco | Correção |
|---|---|---|---|
| 1 | Linha de `PerfilPermissao` com nome removido quebra a leitura (conversão de texto para enum) | login falha para quem tem o perfil | Migration de dados: `DELETE FROM` perfis_permissoes `WHERE` permissão nas 18 removidas, no mesmo PR do enum |
| 2 | Perfil com permissão explícita perde o fallback inteiro (`PoliticaPermissao.cs:12-13`) | perfil "Cozinha" com só `AcessarModuloCozinha` perde `GerenciarEstoque` | Perfis novos levam a lista completa (módulos + finas); teste por perfil |
| 3 | Sem fallback de módulo por nível | usuários atuais (Admin/Gerente/Operador sem lista) ficam sem módulo | Fallback: Admin e SuperAdmin → 8 módulos; Gerente → todos menos Configurações; Operador → Atendimento, Cozinha, Caixa, Entregas; Visualizador → nenhum |
| 4 | Porta de entrada não tem onde morar | shell não sabe para onde ir | `Perfil.ModuloInicial` (texto curto, nulo = sala), migration aditiva |
| 5 | Perfis de produção não existem fora do dev | perfis novos não chegam à loja | Seed idempotente na inicialização, como `SuperAdminSeed`, criando os perfis da matriz na empresa fixa (sem apagar perfil existente) |

**Proposta de aplicação na API.**

- `Domain/Enums/Modulo.cs` (8 valores) e mapa `Modulo → Permissao` no domínio.
- Atributo `[Modulo(Modulo.Caixa)]` no controller, resolvido por `IAuthorizationRequirement` +
  handler que chama `ICurrentUserAccessor.TemPermissao`; soma-se à policy de nível existente (as duas
  precisam passar). A tela esconde, a API nega com 403.
- `GET api/auth/me/modulos` → `{ modulos: [{ id: "caixa", liberado: true }], portaDeEntrada: "atendimento" }`,
  calculado das claims (sem banco); `[Authorize]` sem policy de nível.
- Mapa controller → módulo (proposta; o PR fecha a lista):

| Módulo | Controllers |
|---|---|
| M1 Cardápio | `ProdutoController`, `CategoriaController`, `Storefront/TenantVitrineCardapioController`, `IaAutoPreenchimentoController` |
| M2 Produção | `ProducaoController`, `ProdutoComposicaoController`, `LotesController`, `ItemEstoqueController`, `MovimentacaoController`, `EstoqueDesacertosController`, `ListasComprasController`, `EtiquetaTemplatesController` |
| M3 Atendimento | `Atendimento*Controller` (12, menos Entregas), `ClientesController`, `ClientesCrmController`, `ClienteInteressesController`, `OcorrenciasController`, `PedidosController`, `PedidosCobrancaController`, `PedidoImpressoController` |
| M4 Cozinha | `KdsController` |
| M5 Caixa | `CaixaController`, `VendaController` |
| M6 Campanhas | `CampanhasController`, `CampanhasSugestoesController` |
| M7 Configurações | `UsuarioController`, `ConfiguracoesController`, `LojaController`, `NotificacoesConfiguracaoController`, `IntegracoesWhatsAppController`, `Storefront/StorefrontConfiguracaoController` |
| M8 Entregas | `AtendimentoEntregasController`, `Storefront/TenantVitrineEntregaController` |
| Transversal (sem módulo) | `AuthController`, `NotificacaoController`, `OperacaoEventosController`, `UploadsController`, `PreferenciaMenuController`, `PwaPushController`, `FeatureFlagsController`, `Diagnostico*`, `Webhook*`, `Storefront/*` públicos, `Internal/*`, `ImpressaoController` (bridge) |
| Bastidor do Web (nível + permissão fina, sem módulo) | `Financeiro*`, `ContasAPagar*`, `ContasAReceber*`, `CategoriasFinanceiras*`, `CentrosCusto*`, `FornecedorController`, `Analytics*`, `Reports*`, `Inteligencia*`, `EntityAuditController` |

**Perfis iniciais e matriz (D-01, DECIDIDO 01/10: três perfis).** ✔ = módulo liberado; Porta = rota depois do login.
Não há perfil Caixa na v1 (o Atendimento opera o caixa) nem perfil Entregador: o entregador entra por
link de viagem, sem login (D8-01).

| Perfil | Nível | M1 | M2 | M3 | M4 | M5 | M6 | M7 | M8 | Porta | Finas extras |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Dona | Admin | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | sala | todas as 11 |
| Atendimento | Operador | ✔ | | ✔ | ✔ | ✔ | | | ✔ | M3 balcão | `AtenderConversas`, `GerenciarProdutos` |
| Cozinha | Operador | | ✔ | | ✔ | | | | | M4 fila | `GerenciarEstoque` |

Fechar e estornar caixa continuam exigindo **Gerente** (`CaixaController.cs:61,111`, decisão do F14):
o Atendimento abre, lança e vê; fecha quem é Dona.

**Aceite.**
- [ ] Enum com 19 membros; `git grep -nE "Tickets|Sla|Fatura|Faq|Helpdesk" -- EasyStock.Domain/Enums/Permissao.cs` = 0.
- [ ] Migration de dados apaga as linhas das 18 permissões removidas; teste de integração: perfil com permissão antiga autentica depois da migration.
- [ ] Teste de domínio do fallback de módulo por nível (5 níveis × 8 módulos).
- [ ] Teste de arquitetura: todo controller da Api está no mapa de módulo, na lista transversal ou na lista do bastidor (controller novo sem classificação quebra o build).
- [ ] Teste de integração por módulo: Operador "Cozinha" recebe 403 em `GET api/caixa/dia` e 200 em `GET api/kds/...`.
- [ ] `GET api/auth/me/modulos` devolve a matriz de cada perfil semeado e a porta de entrada.
- [ ] Seed de perfis idempotente: rodar duas vezes não duplica perfil nem permissão.
- [ ] Usuários atuais sem permissão explícita continuam entrando no Web e no console como hoje.

**Fora.** CRUD de perfil e editor da matriz na tela (`08-m7-configuracoes.md`); permissão por loja (`UsuarioPerfil.LojaId` fica como está); união de vários perfis por usuário (um perfil por usuário, como o login já faz).

---

## M0.4 · Tenant fixo e poda de código SaaS/FMA

**Fato medido (call-sites fora de `Migrations/`, `git grep -lw`).**

| Peça | Arquivos (testes) | Refs | Quem depende de verdade |
|---|---|---|---|
| `Plano` | 26 (3) | 71 | limites de plano em `CriarLojaUseCase.cs:26-29`, `CriarUsuarioUseCase.cs:37`, `CadastrarProdutoUseCase.cs:59-62`; `IAdminTenantsQueries`; `PlanoValidacao`; seed `CasaDaBabaSeed.cs:22-25` |
| `AssinaturaEmpresa` | 14 (3) | 37 | os mesmos três use cases; `CriarTenantPorAdminUseCase` |
| `CobrancaAssinatura` | 8 (2) | 13 | só configuração e `FaturaId` (`CobrancaAssinatura.cs:28`) |
| `Cupom` (SaaS, sem `EmpresaId`) | 9 (3) | 20 | `AssinaturaEmpresa.AplicarCupom`, `CupomValidacao`; tabela `Cupons` |
| `Fatura`, `FaturaItem`, `FaturaEvento`, `FaturaPagamento`, `FaturaContador` | 26 próprios + 33 que citam o nome (parte só em comentário) | | `PaymentAttempt.FaturaId` obrigatório (`PaymentAttempt.cs:39`), `IPagamentoGateway` recebe `Fatura` (`IPagamentoGateway.cs:42`), `PagamentoOrchestrator`; `ContaReceber.FaturaId` sempre nulo (`ContaReceber.cs:44`) |
| `AdminTenantsController` | 2 | 3 | `api/admin/tenants` (SuperAdmin): criar (`:35`), listar (`:79`), detalhar (`:96`), **features** (`:114,126`) e **número da Meta** (`:157`, #1102) |
| `CriarTenantPorAdminUseCase` | 3 | 4 | só o controller acima |
| Flags `modulo.comercial`, `modulo.crm` | 5 | | `FeatureFlagsUseCases.cs:92-93,106-107`; nenhum consumidor fora de testes |
| Cliente PJ (`TipoPessoa`, migration `20260809132157`) | `TipoPessoa` 8 | | `Cliente.cs`, `CriarClienteUseCase`, `AtualizarClienteUseCase`, `ClienteResult`, `Web/Models/Api/Cliente.cs`; `InscricaoEstadual` (13) e `NomeFantasia` (14) também servem empresa e fornecedor |

**O que NÃO sai (medido).** `WebhookGatewayController` e `MercadoPagoWebhookProcessor` são o
webhook vivo do pedido (S32, `MercadoPagoWebhookProcessor.cs:10-21`); `EfiPixWebhookProcessor` baixa
parcela de conta a receber (`EfiPixWebhookProcessor.cs:19-22`); `TenantFeatureFlag` gateia o
atendimento (`ProcessarEventoWhatsAppUseCase.cs:85`); `lista-empresas` é usado pelo login do Web
(`EasyStock.Web/Controllers/AuthController.cs`); `CheckoutIdempotency.FaturaId` guarda na verdade o `pedido.Id` do checkout vivo do site (`IniciarCheckoutUseCase.cs:109-111`, sem FK), então só o nome é resíduo.

**Relação com a poda do plano irmão.** P02 (#1135) já tirou controllers, jobs e middleware do
billing, mas as entidades e repositórios acima ficaram; P06 (`07-poda.md:77-87`) previa tirar
`PaymentAttempt*`, roteador e adaptadores, e a migration única de tabelas. **M0.4 absorve a parte
SaaS da P06** (código) e M0.5 a parte de tabelas; a P06 fica com projetos, deploy, compras e rotulagem.

**Escopo.**

| Sai | Detalhe |
|---|---|
| Plano e assinatura | entidades, configurações, repositórios, `PlanoValidacao`, `CupomValidacao`, checagens de limite nos 3 use cases, helpers `UpsertPlanoAsync`/`UpsertAssinaturaAsync` e chamadas no seed |
| Fatura e cobrança | entidades, enums `*Fatura*`, `FaturaTemplate`, `FaturaNumeradorService`, `PaymentAttempt*`, `PagamentoOrchestrator`, `PagamentoGatewayRouter`, `IPagamentoGateway` e adaptadores de fatura (`EfiPixGatewayAdapter`, `ManualGatewayAdapter`, `StripeGatewayAdapter`, `MercadoPagoGatewayAdapter`, `MeasuredPagamentoGatewayDecorator`); `ContaReceber.Fatura` |
| Admin de tenant | `POST/GET api/admin/tenants` e `CriarTenantPorAdminUseCase`, `IAdminTenantsQueries` |
| B2B da FMA | flags `modulo.comercial` e `modulo.crm` do catálogo; Cliente PJ no código (`TipoPessoa` e os campos PJ de `Cliente`) |
| Seeds | `PastaBellaSpSeed` e `CantinaMauricioSeed` (placeholders vazios, `PastaBellaSpSeed.cs:15-19`), `MassasVenezaSeed`; `SEED_DEMO_VOLUME` perde sentido: dev semeia só a Casa da Baba |

| Fica | Motivo |
|---|---|
| `PATCH .../features/{feature}` e `PUT .../whatsapp` | único caminho de ligar canal e vincular o número da Meta; continuam em `AdminTenantsController` reduzido até o M7 levá-los para `api/configuracoes` com policy Admin |
| `EmpresaId`, filtro do EF, RLS | ADR-0056 item 2 |
| `Empresa`, `UsuarioEmpresa`, `lista-empresas` | login do Web; com uma empresa o passo de escolha não aparece |
| `Cupom` como nome | F15 já usa `CupomLoja` (`11-console-fechamento.md`, F15), então a poda não bloqueia F15; liberar o nome é bônus |

**Tenant fixo.** Nenhuma tela pergunta empresa (M0.2 já tira do console). A empresa fixa é a que
tem o número da Meta vinculado em produção; a API não ganha configuração nova de "empresa padrão"
porque `ResolveEmpresaIdPadrao` já resolve com uma empresa ativa por usuário (`AutenticarUsuarioUseCase.cs:163-172`).

**Lacunas.**

| # | Lacuna | Correção |
|---|---|---|
| 1 | Não se sabe quantas empresas e usuários existem no banco de produção (os seeds de outros tenants não rodam lá, `StartupMigrationsAndSeed.cs:225-229`) | Medir na VPS antes do PR: empresas, usuários por empresa, pedidos e conversas por empresa; anexar contagem (sem dado pessoal) à issue |
| 2 | O console foi homologado no tenant "Demonstração EasyStok" (`10-console.md:58`) | Confirmar qual empresa é a Casa da Baba de verdade (D-04) antes de qualquer limpeza |
| 3 | Usuário ligado a duas empresas recebe token sem empresa | Depois de D-04, cada usuário fica com uma empresa ativa |

**Aceite.**
- [ ] `git grep -lwE "Plano|AssinaturaEmpresa|CobrancaAssinatura|Fatura|PaymentAttempt|CriarTenantPorAdmin" -- '*.cs' ':!*/Migrations/*'` = 0, salvo o nome `FaturaId` do `CheckoutIdempotency` (renomear é opcional).
- [ ] `FeatureCatalogo.Conhecidas` sem `modulo.comercial`/`modulo.crm`; teste atualizado.
- [ ] Criar loja, criar usuário e cadastrar produto sem checar plano (testes de use case ajustados, nenhum removido sem substituto).
- [ ] Webhook do MP de pedido (S32) e webhook Efí de parcela passam nos testes de integração existentes.
- [ ] `PATCH features` e `PUT whatsapp` respondem como hoje (teste de controller).
- [ ] `gate.ps1` verde; `ProgramSize_Api` menor.

**Fora.** Derrubar tabelas e colunas (M0.5); mover features e WhatsApp para Configurações (M7); remover `EmpresaId`/RLS (ADR-0056 item 2).

---

## M0.5 · Migration de limpeza das tabelas

**Fato medido.** Tabelas: `planos`, `assinaturas_empresa`, `CobrancasAssinatura`, `Cupons`,
`faturas`, `fatura_itens`, `fatura_eventos`, `fatura_pagamentos`, `fatura_contador` (configurações
em `Infra.Postgre/Data/Configurations/*:7-16`), mais as de `PaymentAttempt*`; colunas
`ContaReceber.FaturaId` (FK `SetNull`, `ContaReceberConfiguration.cs:33`) e `TipoPessoa`,
`InscricaoEstadual`, `NomeFantasia` de clientes com o check `ck_clientes_tipo_pessoa`
(`Migrations/20260809132157_AddClientePessoaJuridica.cs:13-36`). A P06 já fixou a regra: a migration
de tabelas **só entra depois de uma release em produção sem o código** (`07-poda.md:86`).

**Escopo.** Uma migration `RemoverResiduoSaasEFma`, lista explícita no PR, `Down` recriando vazias.
Dados de outras empresas (D-04, DECIDIDO 01/10: **apagar na M0.5**): tudo que não for da Casa da Baba sai
nesta fatia, por script separado e revisado (nunca dentro da migration de esquema), rodado depois do
`pg_dump` do `vps-deploy.sh`. Antes de apagar, confirmar qual `Empresa` é a Casa da Baba real (o console foi
homologado em "Demonstração EasyStok") e anexar a contagem por empresa, antes e depois.

**Aceite.**
- [ ] Migration aplicada em banco restaurado de produção (cópia), com contagem de linhas antes/depois anexada.
- [ ] `Down` recria as tabelas vazias sem erro.
- [ ] Teste de isolamento RLS continua verde (nada de RLS mexido).
- [ ] Aplicação em produção só pelo script do Felipe (`scripts/deploy/vps-deploy.sh`), depois da release da M0.4.

**Fora.** Tabelas de helpdesk, fiscal e mobile (continuam na P06).

---

## Decisões do Felipe (respondidas em 01/10)

| # | Decisão |
|---|---|
| D-01 | Três perfis na v1: Dona, Atendimento, Cozinha (sem Caixa e sem Entregador) |
| D-02 | "Casa da Baba · EasyStok": logo da casa, nome do sistema pequeno no login e no rodapé |
| D-03 | Modo escuro entra em tudo; logo dentro de placa clara |
| D-04 | Dados de outras empresas são apagados na M0.5, com backup e contagem antes/depois |
| D-05 | Sessão longa só no perfil Cozinha |
