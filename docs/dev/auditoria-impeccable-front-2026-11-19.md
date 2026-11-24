# Auditoria Impeccable do front da Casa da Baba (2026-11-19)

Ferramenta: Impeccable 4.10.0 (`detect`, `shot`), Lighthouse 12.2.1 mobile, `npm run contraste` do Console, varredura estática.
Sem login no Console (só a tela de entrada foi vista ao vivo); o resto do Console foi auditado no código.

## Nota: 15/20 (Bom)

| # | Dimensão | Nota | Achado principal |
|---|---|---|---|
| 1 | Acessibilidade | 3 | Loja: Lighthouse 100 (home e cardápio). Console: 264 pares de contraste aprovados nos 2 temas, 64 regras `focus-visible`, 29 arquivos com `prefers-reduced-motion`. Falha: busca da Caixa de entrada sem foco visível. PWA: 186 textos de 9 a 11 px |
| 2 | Performance | 2 | Lighthouse não mediu (erro NO_NAVSTART, 3 tentativas). Home da loja pesa 1.527 KiB; PWA é um `index.html` de 1,06 MB e 24.674 linhas (377 KB comprimido) |
| 3 | Tema | 4 | Console: 0 cor fixa fora de `tokens.css`, tema claro e escuro |
| 4 | Responsivo | 3 | Loja e login do Console bons em 390 px. Alvos de toque abaixo de 44 px em 2 pontos do Console |
| 5 | Anti-padrões | 3 | Console e loja: 0 achados do detector. PWA e Web legado: 52 achados (borda lateral, brilho escuro, easing com quique) |

## Achados

| Prioridade | Onde | O quê | Norma | Correção |
|---|---|---|---|---|
| P1 | `EasyStock.Console/src/features/caixa-de-entrada/caixa.module.css:112` | `.entradaBusca:focus { outline: none }` sem substituto: quem navega por teclado não vê o foco na busca | WCAG 2.4.7 | `.campoBusca:focus-within { outline: var(--b-3) solid var(--marca); outline-offset: 2px }` |
| P2 | `EasyStock.Api/wwwroot/pwa/index.html` | 186 ocorrências de `font-size` 9, 10 e 11 px; ilegível no celular do balcão | WCAG 1.4.4 (boa prática) | Piso de 12 px, ideal 13 px via token |
| P2 | `EasyStock.Api/wwwroot/pwa/index.html` | Arquivo único de 1,06 MB (HTML, CSS e JS juntos): todo carregamento baixa tudo, cache invalida inteiro a cada mudança | Performance | Separar CSS e JS em arquivos com hash; fora do escopo de correção rápida |
| P2 | `casadababa.com` | Home com 1.527 KiB transferidos (carrossel de fotos no topo) | Performance | Conferir formato e tamanho das fotos do carrossel; carregar só a primeira de imediato |
| P3 | `ficha-cliente/CorrecaoVinculoCliente.module.css:16` | Caixa de seleção de 20 px | WCAG 2.5.8 (24 px) | Área clicável de 24 px ou rótulo clicável |
| P3 | `atendimento/bolhaAvaliacao.module.css:10` | Botão de 32 px | WCAG 2.5.8 passa; 44 px recomendado | Avaliar se é tocado no celular |
| P3 | PWA e `EasyStock.Web/wwwroot/css` | Borda lateral em cartão arredondado (11), brilho sem deslocamento (12), easing com quique (8), texto em degradê (2) | Anti-padrão visual | Só se o Web legado continuar no ar |

## Refutado no caminho

- Detector apontou `user-scalable=no` na PWA: é comentário dizendo que foi removido. A viewport atual permite zoom.
- Detector apontou `<img>` sem `src` na PWA: são imagens preenchidas por JavaScript (logo e foto de produto), com `alt`.
- 543 "cores fixas" no Console: são números de issue nos comentários (`#2232`). Cor fixa real fora dos tokens: 0.

## O que está bom

- Console tem sistema de design de verdade: tokens, 2 temas, prova de contraste no `npm run qualidade`, `--toque` como altura padrão.
- Loja casadababa.com: hierarquia clara, CTA principal forte, contraste e SEO 100 no Lighthouse.

## Lacunas

- Performance real (LCP, CLS) não medida: Lighthouse falhou no trace. Repetir em outra máquina ou pelo PageSpeed.
- Telas internas do Console não vistas ao vivo (exigem login).
- Fonte da loja casadababa.com não está neste repositório; auditada só ao vivo.
- `EasyStock.Web` não aparece no `docker-compose.yml`; tratado como legado.

## Issues abertas

| Prioridade | Achado | Issue |
|---|---|---|
| P1 | Foco na busca da Caixa de entrada | #2884 (corrigida, PR #2888, no ar) |
| P2 | Texto de 9 a 11 px na PWA | #2891 (corrigida, PR #2899, no ar) |
| P2 | `index.html` único de 1 MB na PWA | #2892 (tier alto, precisa de spec) |
| P2 | Peso da home da loja | casa-da-baba#155 |
| P3 | Alvos de toque pequenos no Console | #2893 (corrigida, PR #2897, no ar) |
| P3 | Anti-padrões visuais na PWA e no Web legado | #2894 (corrigida, PR #2913, no ar; detector 54 para 1) |
