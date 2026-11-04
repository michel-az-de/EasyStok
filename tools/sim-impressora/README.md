# Simulador de impressora térmica

Gate 4 da impressão v2 (#2197, spec `docs/plan/impressao-v2/README.md`). Roda a
PWA real em Node, captura os bytes que iriam para o Bluetooth e interpreta como
a impressora: TSPL na Oasis OIA-8381-B (cabeça de 104 mm, 8 pontos/mm) e
ESC/POS na térmica de 58 mm.

```
npm install
node matriz.js                                   # 36 casos contra a PWA deste checkout
node matriz.js --golden golden                   # idem, comparando com os goldens
node matriz.js --pwa <pwa do master> --saida saida/antes
node prancha.js                                  # saida/prancha-{52,80,10x15}.png
node regressao.js --antes <pwa do master>        # 58 mm ESC/POS e cupom, byte a byte
```

Uma PWA do master sai de `git archive origin/master EasyStock.Api/wwwroot/pwa`.

## O que a matriz reprova

3 tamanhos (52, 80, 10x15) x 2 orientações x 3 opções de código (QR, barra,
ambos) x 2 mídias (rolo contínuo, etiqueta recortada) = 36 casos.

| Código | Assert | Piso |
|---|---|---|
| P1 | fonte e ocupação | nome 10 pt, validade 9 pt, dado 7,5 pt; módulo do QR 3 pontos; largura usada 85% (e altura, no recortado) |
| P2 | papel | contínuo avança no máximo o conteúdo + 4 mm; recortado avança um passo (altura + gap) |
| P3 | leitura e orientação | QR lido por jsQR e ZXing; horizontal não sai igual à vertical |
| P4 | barra | Code128 lido; módulo 2 pontos, zona quieta 10 módulos, altura 8 mm |
| P5 | corte | nenhum ponto fora do papel físico |

A zona quieta dos códigos conta como conteúdo: a etiqueta seguinte não pode
comê-la.

## Arquivos

| Caminho | Papel |
|---|---|
| `casos.js` | a matriz e os dados do protótipo (mesmo QR e mesma chave curta) |
| `lib/ambiente.js` | carrega a PWA num `vm` com canvas real e as fontes de `fontes/` |
| `lib/tspl.js`, `lib/escpos.js` | interpretam os bytes e devolvem a tinta por ponto |
| `lib/leitura.js` | lê QR (jsQR e ZXing) e Code128 (ZXing) da tinta |
| `golden/` | PNG esperado de cada caso; até 0,5% de pixels diferentes |
| `referencia-prototipo/` | o protótipo aprovado, usado só na prancha |
| `saida/` | resultado da última rodada (ignorado pelo Git) |

Mudou o layout de propósito: `node matriz.js --golden golden --atualizar-golden`
e confira a prancha antes de commitar os goldens novos.
