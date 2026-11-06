# Spec: impressão v3 (o que aparece na tela é o que sai no papel)

- Data: 05/11/2026. Base: `master` em `2775cdc98`, depois da v2 (#2197, F0 a F4).
- Issue: #2247. Página visual com diagnóstico e protótipo das telas: https://claude.ai/artifact/PvJehrCRYZJfHu7HbWMe3f
- Estado: APROVADA para o G1 (Felipe, 05/11, decisões D1 a D3). G2, G3 e G4 recebem o detalhe em PR própria antes de começar.

## 1. Problema (medido no código, sem prova em aparelho)

| Queixa | Causa | Evidência |
|---|---|---|
| Tela ≠ papel | Na produção do app, a prévia usa `etiquetaTermica` (384 px, "Fabricação/Validade") e a Oasis imprime outro desenho (`cdbEtiquetaCanvas`, "FAB/VAL"). "Salvar PNG" sai da prévia | `pwa/index.html:20939-20973` vs `:20065-20137` |
| Tela ≠ papel | Prévia usa o código local (`getProdScanMode`, padrão QR e barras); o papel usa a regra do servidor (vazia = só QR) | `pwa/index.html:14636`; `pwa/impressoras.js:244-256` |
| Tela ≠ papel | 6 famílias de fonte para a mesma etiqueta; a prévia mede com uma e o papel com outra | `pwa/etiqueta/render.js:7-11`; `pwa/etiqueta-resolver.js:16` |
| Tela ≠ papel | Console: prévia e papel são duas requisições; a prévia não confere a largura | `Console/src/componentes/ImprimirPedido.jsx:96-119`; `htmlNoNavegador.js:66-72` |
| Torto | BITMAP sempre em x=0 numa cabeça de 104 mm; sem ajuste fino; `offsetMm` existe no modelo e não tem tela; o teste de alinhamento só imprime | `pwa/etiqueta-canvas.js:197`; `pwa/impressora-tspl.js:61-126` |
| Torto | Console: etiqueta 80×40 fixa por `window.print()` da página; escala e margem no diálogo do Windows | `etiqueta.module.css:3`; `AbaProducaoDoDiaApi.jsx:162`; `ImprimirPedido.jsx:222` |
| Torto | `@page{size:80mm auto}`, que é inválido, cai no A4 | `Api/Data/Templates/Impressao/canhoto.css:5`; `ImpressoHtml.cs:45` |
| Errado | 8 tabelas de papel, sem equivalência; largura útil 48/72 mm no app e 46/70 mm no CSS | `pwa/impressao-papeis.js:26`; `bilhete.css:14` |
| Errado | Regra da loja (F4) sem tela que grave; o Console não lê | `LojaImpressaoController.cs:15`; `Console/src/infra/preferenciasImpressao.js:9` |
| Confuso | "Impressora" em Configurações e "Perfil de impressão" no modal; "modelo" com 4 sentidos; a via da cozinha com 3 nomes; duas portas de ajuste no app | `AbaImpressao.jsx`; `pwa/index.html:8286, 8383` |

O simulador (`tools/sim-impressora`) não pegaria nenhuma das três queixas: compara bytes com bytes, nunca com a prévia, e supõe o papel em x=0 (`matriz.js:30-40`).

## 2. Abordagem

Quatro regras, válidas em web, app e servidor:

1. **Um desenho só.** Cada documento é desenhado uma vez, no tamanho final. A prévia mostra esse desenho e a impressora recebe esse desenho.
2. **Um catálogo de papéis.** Uma tabela com tamanho, área útil e posição na cabeça. Todo o resto lê dela.
3. **Uma configuração.** A regra da loja (servidor) diz o que sai e em qual papel. O aparelho guarda só qual impressora está ligada e o ajuste fino dela.
4. **Uma palavra por coisa.** Glossário da seção 4, com teste que reprova termo proibido.

```
Documento -> regra da loja (servidor) -> motor (resolvedor + canvas, 8 pontos/mm, fonte embutida)
  -> prévia: o mesmo canvas, em tamanho real
  -> térmica: o mesmo canvas em BITMAP/GS v 0, centralizado na cabeça + ajuste fino
  -> folha: o mesmo canvas em página do tamanho exato do papel, escala 100%
```

O motor é o par `pwa/etiqueta-resolver.js` + `pwa/etiqueta-canvas.js` da v2 (F2). Os dois rodam em navegador e Node (IIFE em `globalThis`). O Console passa a importar esses arquivos pelo Vite, sem cópia, em vez de manter um desenho próprio.

## 3. Decisões (Felipe, 05/11)

| # | Decisão |
|---|---|
| D1 | No computador, a térmica recebe pela impressão por um **agente local instalado no PC** (G4). Até lá, o Console usa a folha com página do tamanho exato. |
| D2 | O termo único é **Via da cozinha**. "Comanda" fica com o Atendimento. |
| D3 | A ordem começa pelo **G1**. |

## 4. Glossário

| Use | Significa | Não use |
|---|---|---|
| Impressora | O aparelho físico, com nome dado pela loja | perfil, perfil de impressão, destino |
| Papel | O que está carregado: Bobina 58 mm, Bobina 80 mm, Etiqueta L × A mm, Etiqueta 10 × 15 cm, Folha A4, Folha A5 | mídia, modelo, formato, cupom |
| Documento | Etiqueta de produção, Etiqueta de entrega, Pedido, Via da cozinha, Resumo do dia | template, layout, modelo |
| Prévia | O desenho exato que vai sair, em tamanho real | visualizar, preview |
| Teste | 1 unidade com moldura e régua | calibrar, teste de alinhamento, diagnóstico |
| Ajuste fino | Deslocamento em mm, por impressora, neste aparelho | offset, shift, referência |

Vale para textos de tela. Identificadores de código seguem ADR-0011 e ADR-0018 e mudam só quando a tela muda junto.

## 5. Pacotes

| Pacote | Entrega | Tier |
|---|---|---|
| G1 | Etiqueta de produção reta e igual à tela, no app e no Console (detalhe na seção 6) | baixo, com esta spec |
| G2 | Configurações › Impressoras no Console e no app, com 4 passos (impressora, papel, prévia, teste e ajuste fino). A regra da loja é gravada pela tela e lida pelos dois. Folha Imprimir única. Glossário aplicado e testado | baixo, se não houver migração |
| G3 | Pedido, Via da cozinha, entrega e resumo passam pelo motor, completando a F5 da v2. O recibo do app ganha acento e prévia, e a bobina de 58 mm entra no motor. Aposenta `CanhotoHtml`, `canhoto.css`, `CanhotoTexto`, os PDFs próprios do Console e as cópias de `etiqueta/` | baixo; alto se tocar `Program.cs` |
| G4 | Agente local no PC que recebe os bytes do motor e fala com USB, rede e Bluetooth. Usa o caminho `X-Impressao-Api-Key` que já existe | alto (novo executável e auth) |

## 6. G1 em detalhe

Problema: na etiqueta de produção, que é o que mais se imprime, a prévia não é o papel e a posição não tem ajuste.

| # | Mudança | Onde |
|---|---|---|
| 1 | `cdbEtiquetaCanvas.previa(dados, papel, regra)` devolve o mesmo canvas 1 bit que `tspl()` e `escpos()` usam. A prévia, "Salvar PNG" e "Enviar" da produção passam a usar essa função | `pwa/etiqueta-canvas.js`; `pwa/index.html` (prévia de produção, miniatura da aba Loja) |
| 2 | Uma função `regraEfetiva()` (servidor, com reserva local) alimenta a prévia e o papel. `getProdScanMode` deixa de decidir sozinho | `pwa/impressoras.js`; `pwa/index.html:14636` |
| 3 | O BITMAP vai para `x = (cabeça − papel)/2 + ajuste.x`. O vertical usa `SHIFT` com `ajuste.y`. A posição do papel na cabeça (centro ou borda) vira dado do catálogo, com padrão centro | `pwa/impressora-tspl.js`; `pwa/etiqueta-canvas.js:197` |
| 4 | Ajuste fino na tela da impressora: setas de 0,5 mm, faixa de ±10 mm, salvo em `offsetMm` do `cdb-impressoras.v2` e mostrado no cartão. O "Teste" imprime moldura e régua com o ajuste aplicado | `pwa/index.html` (cartão da impressora) |
| 5 | Fonte única: Nunito Sans (woff2, já usada no Console) embutida para o motor, que espera a carga antes de medir e desenhar. Sem a fonte, recusa com motivo | `pwa/etiqueta-resolver.js`; `pwa/etiqueta-canvas.js` |
| 6 | Catálogo único: `pwa/impressao-papeis.js` é a fonte. `docs/contracts/papeis.v1.json` é gerado dele, e um teste C# confere `RegraImpressaoCatalogo` e as larguras úteis do `bilhete.css` contra o contrato | `pwa/impressao-papeis.js`; contrato; teste |
| 7 | Console: `EtiquetaDeProducao.jsx` desenha pelo motor (import do Vite com `server.fs.allow`). A prévia mostra o PNG em tamanho real. A impressão abre a página com `@page` do papel e a imagem em mm. "Estoque do dia" ganha a mesma prévia | `EasyStock.Console/src/features/gestao/producao/**`; `vite.config.js` |

Aceite do G1:
- [ ] Simulador "prévia = bytes": para os 36 casos da matriz, a imagem dos bytes TSPL e a da prévia diferem em até 0,5% dos pontos.
- [ ] Simulador "posição na cabeça": o papel é posicionado na cabeça de 104 mm pelo catálogo, a moldura do teste fica centrada com tolerância de ±0,5 mm, e o ajuste fino desloca exatamente o valor salvo.
- [ ] O Console imprime a etiqueta de produção numa página do tamanho do papel, com 1 página por etiqueta, conferido em PDF gerado pelo Chrome.
- [ ] Contrato de papéis igual em app, servidor e CSS.
- [ ] A regressão de 58 mm ESC/POS e dos cupons continua byte a byte (`tools/sim-impressora/regressao.js`).
- [ ] Prancha ANTES e DEPOIS no PR e prova do Felipe na Oasis: 3 etiquetas por papel, com foto ao lado da prévia.

Rollback: o G1 sai por OTA do app e deploy do Console. A volta é o revert do PR, sem migração e sem dado novo no servidor. O `offsetMm` salvo é ignorado pela versão anterior.

## 7. Gate e banca

Vale o gate de simulação e a banca da v2 (`docs/plan/impressao-v2/README.md`, seções 4 e 5), com três acréscimos:
- **Prévia = bytes**: compara a imagem da prévia com a dos bytes enviados.
- **Posição na cabeça**: o simulador considera onde o papel fica na cabeça e o ajuste fino.
- **Glossário** (a partir do G2): varre os textos das telas e reprova termo proibido.

## 8. Fora do escopo

- O editor de template do Web legado (B9 de 03/11).
- O RLS de `etiqueta_templates` (B12).
- O WebView do Atendimento no APK (B2), que entra no G4 ou em issue própria.

Há WIP não commitado em `C:\rep\.worktrees\EasyStok\impressao-perfis-1756` (#1756, 13/10) nos mesmos arquivos de impressão do Console e do servidor. Ele não é usado nem descartado. O dono decide o destino antes do G3.
