# Spec: impressão v2 (templates por tamanho, várias impressoras, A4)

- Data: 03/11/2026.
- Base: `master` em `42b462f`.
- Issue: #2197. Fontes: as auditorias 1 (mídia e TSPL), 2 (templates) e 3 (várias impressoras), resumidas aqui.
- Estado: APROVADA. As decisões D1 a D4 estão na seção 7.

## 1. Problema (fatos medidos)

| # | Defeito | Causa | Prova |
|---|---|---|---|
| P1 | Etiqueta pequena na Oasis | O desenho é fixo em 384 px, em pé, feito para a bobina de 58 mm. O `paraEtiqueta` só reduz a escala, então na 80x40 o desenho ocupa 39% da largura. | `impressora-tspl.js:98-103`, auditoria 1 seção 1 |
| P2 | Uns 25 cm de papel gastos | O app manda `GAP 2 mm` fixo e nunca calibra. Num rolo contínuo, a impressora avança até o limite de busca. | auditoria 1 seção 2 |
| P3 | Nenhum template tem layout por tamanho | Saem quebrados 23 de 28 combinações (template × mídia) | auditoria 2 seção 4 |
| P4 | A barra nunca sai na térmica | O código de 30 caracteres precisa de 730 pontos e só existem 360, então ela cai no QR sem avisar | auditoria 2, T15 |
| P5 | A configuração não chega | Os selects não mostram o perfil salvo, mudar um select sozinho não faz nada, e o padrão 80x40 é aplicado em silêncio | auditoria 1, C1 a C5 |
| P6 | Uma impressora sobrescreve a outra | Os selects são globais e só existe uma impressora ativa (`cdb-printer`) | auditoria 3, B1 a B6 |
| P7 | A4 impossível | `@page 58mm` fixo, e o `window.print` não é confiável no APK | `index.html:5109`, `:21262` |
| P8 | Web e app divergem | Há dois desenhos de etiqueta: o `LayoutJson` com `render.js`, e o `etiqueta-termica.js` | auditoria 2 seção 3 |

O ano 2025 na foto não é bug. O papel traz 2026, mas o dígito sai com 2 mm de altura e borrou na foto.

## 2. Abordagem

```
Documento (produção, pedido, teste)
  -> regra da loja: papel, template, orientação, códigos, cópias    [igual em PWA, APK e Web]
  -> resolvedor: gera o LayoutJson v1 no tamanho final, sem escalar  [pisos de legibilidade]
  -> destino deste aparelho para aquele papel                         [MAC, linguagem, calibração]
       tspl-bt   -> canvas de 8 pontos/mm, girado 90° se preciso -> BITMAP
       escpos-bt -> canvas -> GS v 0
       sistema   -> render.js no DOM -> @page do papel -> imprimir ou PDF (A4 em grade)
```

Princípios:
1. **Desenhar no tamanho final, nunca escalar.** Se nem os campos obrigatórios cabem, o sistema recusa e diz o motivo.
2. **Um contrato só**, o `LayoutJson` v1, que já existe no servidor com CRUD, validador e editor.
3. **A regra da loja e o destino físico ficam separados.** A regra diz "como sai" e vai para o servidor. O destino diz "onde sai" e fica no aparelho.
4. **Códigos são opção do documento**, não do template. O resolvedor reorganiza o layout quando um código está desligado.
5. **QR** leva o código completo. **Code128 C** leva uma chave numérica curta. EAN-13 não serve: exige GTIN e não carrega o lote.

Pisos, que a banca valida:
- Nome com 10 pt, dados com 7,5 pt e validade com 9 pt.
- Módulo do QR com pelo menos 3 pontos inteiros e zona quieta de 4 módulos.
- Módulo da barra com 2 pontos, zona quieta de 10 módulos e altura mínima de 8 mm.

Protótipo: a matriz de 52 mm, 80 mm e 10x15, nas duas orientações e com QR, barras ou os dois, tem **18 de 18 códigos lidos** por jsQR e ZXing. A auditoria 2 também tem 3 folhas A4.

## 3. Fases (cada uma é uma PR coesa)

| Fase | Entrega | Resolve | Tier | Sai por |
|---|---|---|---|---|
| F0 | A configuração chega: o select mostra o perfil salvo, presets como dado único (52, 80, 10x15 e os atuais), **contínuo como padrão** (`GAP 0,0` e altura igual à do conteúdo), recortado com calibração opcional (`GAPDETECT`, `SET TEAR ON`), e teste de alinhamento (moldura, cruz e régua de 10 mm) | P2, P5 | baixo (fix) | OTA |
| F1 | Várias impressoras no aparelho: modelo v2 `cdb-impressoras.v2` com mapa papel -> destino, migração local idempotente e Ajustes em 3 passos | P6 | baixo com esta spec aprovada | OTA |
| F2 | Resolvedor e backend canvas para a etiqueta de produção na térmica: 52, 80 e 10x15, vertical e horizontal, QR, barras ou os dois, com testes golden da matriz | P1, P3, P4 | baixo com esta spec aprovada | OTA |
| F3 | A4: `POST api/etiquetas/pdf` gera em QuestPDF a grade de etiquetas por folha (por exemplo Pimaco A4256, ou "quantas cabem"), na vertical ou na horizontal, a partir do LayoutJson. O APK e o Web abrem o mesmo PDF. | P7 | baixo se não mexer em `Program.cs` nem em csproj; se mexer, alto | deploy |
| F4 | Regra da loja no servidor: `ConfiguracaoLoja.ImpressaoJson` em jsonb, `GET` e `PUT api/lojas/{id}/impressao` com 409, número curto do lote (D4), e Console e Web lendo a mesma regra | P8 | **ALTO**: migração, label `aprovado` e GO para o `database update` | deploy |
| F5 | O pedido e o teste viram receitas, e o `EtiquetaDeProducao.jsx` do Console usa o mesmo render | P8 | baixo | deploy |

Ordem: F0, F1 e F2 primeiro, porque destravam a Oasis hoje sem servidor. Até a F4 chegar, a regra da loja vive como cache local. F3 e F4 podem correr em paralelo com F2.

## 4. Gate de simulação e prancha comparativa (OBRIGATÓRIO em toda PR)

Pedido do Felipe: "revisar com simulação e comparar prints + banca revisora".

1. **Simulador de impressora** (`tools/sim-impressora/`, só Node):
   - Lê os bytes reais que o app manda, em TSPL (`SIZE`, `GAP`, `DIRECTION`, `REFERENCE`, `BITMAP`, `BOX`, `BAR`, `TEXT`, `PRINT`) e em ESC/POS (`GS v 0`).
   - Rasteriza esses bytes em PNG a 8 pontos/mm, com a largura física da cabeça: 104 mm na Oasis e 48 mm na de 58 mm.
   - Informa o **papel consumido**: altura do `SIZE` mais o gap, ou o avanço até o próximo gap no recortado.
2. **Matriz**: 3 tamanhos × 2 orientações × 3 opções de código × 2 mídias (contínua e recortada), mais o A4 em PDF. São 36 casos térmicos e 2 folhas.
3. **Asserções automáticas** em cada caso:
   - QR e barra decodificados por jsQR e ZXing, com o conteúdo certo.
   - Ocupação de 85% ou mais da área útil.
   - Pisos de fonte respeitados.
   - Papel consumido no máximo igual à altura do conteúdo mais 4 mm no contínuo.
   - Nada cortado fora da cabeça.
4. **Golden**: o PNG de cada caso fica versionado. Diferença acima de 0,5% dos pixels reprova, até alguém aprovar o novo golden.
5. **Prancha comparativa** (HTML, depois PNG): para cada caso, ANTES (master) ao lado de DEPOIS (PR) ao lado do protótipo da auditoria 2, com o papel consumido escrito embaixo. Ela vai no PR e para o Felipe antes do merge.
6. A prova no aparelho vem depois: a foto da Oasis entra na prancha, ao lado da simulação.

## 5. Banca revisora (em toda PR, depois do gate 4)

| Revisor | Pergunta |
|---|---|
| Prancha | Cada caso da prancha bate com o protótipo e o papel consumido está certo? O revisor olha as imagens, não o código. |
| Correção | Bytes TSPL e ESC/POS, presets, rotação e migração idempotente |
| Legibilidade | Os pisos são respeitados e o QR e a barra são decodificados no golden |
| UX | A tela de Ajustes dá para usar sem jargão em 3 passos, com recusa explicada |
| Regressão | O ESC/POS de 58 mm (WTP05, MTP-II) e os cupons continuam iguais |
| Segurança e dados | Só na F4: migração, RLS por empresa e loja, e 409 de concorrência |

Os achados críticos são corrigidos antes do merge. Depois vem a prova no aparelho, feita pelo Felipe: em cada rolo, calibrar, alinhar e imprimir 3 etiquetas, com avanço de A + gap (±1 mm) e nenhum branco extra.

## 6. Aceite

- [ ] Etiqueta de produção em 52, 80 e 10x15, nas duas orientações e nas 3 opções de código, com ocupação de 85% ou mais e códigos lidos no golden.
- [ ] Gate 4 verde: 36 casos simulados, golden aprovado e prancha anexada à PR.
- [ ] Na Oasis, com rolo contínuo, 3 etiquetas seguidas sem avanço extra (no máximo o conteúdo mais 4 mm).
- [ ] Duas impressoras ativas ao mesmo tempo, por exemplo a MTP-II para cupom e a Oasis para etiqueta, sem trocar à mão.
- [ ] A4 vertical e horizontal em grade pelo navegador.
- [ ] A mesma regra da loja dá a mesma etiqueta no PWA, no APK e na Web (F4).

## 7. Rollback

- F0 a F3 e F5: OTA da sequência anterior (`ota-deploy` preserva as versões) ou revert da PR.
- F4: a coluna é anulável e ignorada pelo código anterior.

## 8. Decisões do Felipe (03/11/2026)

| # | Pergunta | Decisão | Efeito |
|---|---|---|---|
| D1 | Tipo das etiquetas de 52 e 80 mm | **ROLO CONTÍNUO NOS DOIS** | Contínuo passa a ser o padrão: `GAP 0 mm,0 mm` e altura do `SIZE` igual à do conteúdo, que o resolvedor calcula. A mídia recortada (com `GAPDETECT`) fica como opção do destino. |
| D2 | Onde fica a regra | **SERVIDOR, POR LOJA** | Entra na F4: `ConfiguracaoLoja.ImpressaoJson`. Tier alto, depende da label `aprovado`. |
| D3 | A4 no APK | **PDF GERADO NO SERVIDOR** (QuestPDF) | A F3 vira o endpoint `POST api/etiquetas/pdf`, que monta a grade A4 a partir do mesmo LayoutJson. O Android abre o PDF para imprimir, sem código nativo novo. |
| D4 | O que vai na barra | **NÚMERO CURTO DO LOTE** | Sequencial por loja, com 8 a 10 dígitos, em Code128 C. Precisa de coluna ou sequência no servidor, que entra na F4. Até lá, a barra fica desligada no padrão e o QR continua. |

Pendente de prova no aparelho: o comprimento real que a Oasis avança no contínuo depois da F0.
