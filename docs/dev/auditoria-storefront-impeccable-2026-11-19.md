# Auditoria técnica do storefront (Impeccable audit), 2026-11-19

Alvo: casadababa.com no ar (repo `casa-da-baba`, commit `00c8380`), só leitura.
Ferramentas: Impeccable 4.10.0 (`detect`, `shot`), axe-core via Playwright, Lighthouse mobile, medições por script no navegador.
Viewports: 390x844 (celular) e 1280x800 (desktop).

## Nota: 15/20, faixa BOA

| # | Dimensão | Nota | Achado principal |
|---|---|---|---|
| 1 | Acessibilidade | 3/4 | axe sem nenhuma falha séria nos 6 sites x 2 telas; texto de 11 px em excesso |
| 2 | Desempenho | 3/4 | Lighthouse 86; LCP de 3,7 s na home no 4G lento (meta 2,5 s) |
| 3 | Tema e tokens | 2/4 | 308 cores fixas fora dos tokens, 174 valores distintos |
| 4 | Responsivo | 4/4 | Nenhum alvo de toque abaixo de 44 px, nenhuma rolagem lateral |
| 5 | Anti-padrões | 3/4 | Detector no ar: 0 achados; só a curva elástica do "Adicionar" |

## Achados por prioridade

### P2: LCP da home em 3,7 s no celular
- Medido: Lighthouse mobile, FCP 1,5 s, LCP 3,7 s, TBT 210 ms, CLS 0, peso total 1.527 KiB.
- No cardápio, o LCP medido por PerformanceObserver (4G lento, CPU 4x) foi 3,5 s, na `IMG.dish__foto`.
- O hero carrega `hero-massas-1086.webp` (268 KiB) em telas com DPR 1,75 ou mais.
- Correção: gerar uma largura intermediária (900w) e AVIF para o hero; revisar a qualidade das fotos editoriais.
- Comando: `/impeccable optimize`

### P2: 308 cores fixas fora dos tokens
- `src/styles/direcao-b.css`: 116; `src/pages/comanda/comanda.css`: 50; `src/styles/cozinha-viva.css`: 39; `src/components/site-chat/site-chat.css`: 22.
- Existem 174 valores distintos, contra os tokens de `src/styles/tokens.css`. Cada tom solto é uma cor que pode divergir da marca na próxima mudança.
- Correção: levar os tons repetidos para tokens semânticos e trocar os literais.
- Comando: `/impeccable extract`

### P2: texto de 11 px no celular
- Cardápio: 58 textos em 11 px (ex.: botão de porção "600 g") e 45 em 12 px.
- Home: link do Instagram em 11 px; 404 e rodapé: título "À sua mesa" em `h2` de 11 px.
- Não reprova WCAG, mas pesa na leitura no celular, em especial nos botões de porção, que são escolha de compra.
- Comando: `/impeccable typeset`

### P3: barra superior fora de landmark
- axe `region` (moderado) em `.b-topbar` na home, cardápio e páginas de entrega.
- Correção: colocar a barra dentro do `header` ou dar a ela um landmark.

### P3: curva elástica no "Adicionar"
- `src/pages/cardapio/cardapio.css:1067`, token `--cdb-ease-elastica` com 2% de passagem do ponto final.
- É decisão documentada (M05). Manter e registrar a exceção, ou trocar por uma curva sem passagem.

### P3: pequenos ajustes
- Pontos do carrossel animam `width` (`hero-carousel.css:23`, `cozinha-viva.css:83`): troque por `transform: scaleX`.
- Fotos editoriais com `sizes="100vw"` aparecem menores que a tela: cerca de 20 KiB a mais por foto.

## O que está bem (manter)
- As 3 janelas (detalhe do prato, carrinho, atendimento): foco entra no "Fechar", Tab não escapa em 30 toques, Esc fecha e o foco volta ao botão de origem. axe sem falhas dentro delas.
- Carrossel do hero respeita `prefers-reduced-motion` e começa pausado em tela de toque, com botão de pausa.
- Lighthouse: acessibilidade 100, boas práticas 100, SEO 100.

## Lacunas
- Checkout com carrinho vazio redireciona para a home: o fluxo de pagamento com itens não foi auditado.
- Login e Meus pedidos: só o carregamento da página, sem fluxo autenticado.
- Lighthouse no cardápio falhou 2 vezes (`NO_NAVSTART`); o LCP de lá veio de medição própria.
- Os achados estáticos do detector (hierarquia plana e padding apertado) foram conferidos no ar e são falso positivo.

## Próximos passos sugeridos
1. `/impeccable optimize` na home: LCP e peso das fotos.
2. `/impeccable typeset` no cardápio: piso de 12 a 13 px para texto de compra.
3. `/impeccable extract`: tokens para as cores soltas, começando por `direcao-b.css`.
