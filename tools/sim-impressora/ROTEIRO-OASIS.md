# Roteiro de teste na Oasis OIA-8381-B com rolo contínuo

Para o Felipe, depois do OTA da impressão v2 (#2202). Leva uns 15 minutos. Tenha à
mão: rolo contínuo de 80 mm na Oasis, a MTP-II com bobina de 58 mm, uma régua e o
celular com o app atualizado.

O simulador já mediu o tamanho esperado de cada etiqueta. O teste confirma que a
impressora real faz o mesmo.

## 1. Configurar (Ajustes, Impressoras)

| Passo | Faça | Esperado |
|---|---|---|
| 1.1 | Abra Ajustes | As duas impressoras que já estavam pareadas aparecem na lista, sem refazer nada. A Oasis mostra "confirme o papel". |
| 1.2 | Toque na Oasis. Passo 2: **Etiqueta de produção**. Passo 3: **Rolo contínuo de 80 mm** | Salva sem erro. Some o "confirme o papel". |
| 1.3 | Toque na MTP-II. Passo 2: **Cupom e etiqueta de pedido**. Passo 3: **Bobina de 58 mm** | A lista mostra a Oasis com Etiqueta e a MTP-II com Cupom. |
| 1.4 | Toque **Testar** na Oasis | Sai uma etiqueta de uns 31 mm e a impressora para logo depois dela, sem avançar papel em branco. |

## 2. Etiqueta de produção no rolo de 80 mm

Imprima a etiqueta de um lote qualquer (Etiquetas, produção) e meça com a régua.

| Caso | Orientação | Códigos (Ajustes, leitura) | Comprimento esperado |
|---|---|---|---|
| 2.1 | Vertical | Só QR | 47 mm (até 51 mm) |
| 2.2 | Horizontal | Só QR | 47 mm (até 51 mm) |
| 2.3 | Vertical, 2 cópias | Só QR | 2 etiquetas de 47 mm, sem pedaço em branco entre elas |

Em cada uma confira:

- [ ] O QR lê na câmera do app (Etiquetas, ler) e abre o lote certo.
- [ ] Nome do produto, VAL, FAB, LOTE e PESO inteiros, nada cortado na borda.
- [ ] O corte na serrilha cai depois do QR, não em cima dele.
- [ ] A impressora não procura recorte (não avança 25 cm de papel).

A barra (Code128) ainda não sai: ela espera o número curto do lote, que vem do
servidor (decisão D4). Escolher "QR e barra" hoje imprime só o QR.

| Caso | Faça | Esperado |
|---|---|---|
| 2.4 | Em Ajustes, leitura: **Só barras**. Imprima | Sai com o QR. Aviso "Enviado. Sem código de barras: o lote ainda não tem número curto. Saiu o QR no lugar." |
| 2.5 | Toque em imprimir duas vezes seguidas | Sai uma etiqueta só. O segundo toque mostra "Impressão em andamento: toque de novo quando terminar". |

## 3. Duas impressoras

| Passo | Faça | Esperado |
|---|---|---|
| 3.1 | Imprima a etiqueta de um pedido | Sai na MTP-II, igual a antes da atualização. |
| 3.2 | Imprima uma etiqueta de produção e, sem esperar, a de um pedido | Aparece "Na fila: imprime em seguida na MTP-II". As duas saem, uma depois da outra. |
| 3.3 | Desligue a Oasis e imprima uma etiqueta de produção | Aviso com o erro do Bluetooth (não "Sem conexão BT") e o botão de reimprimir. O app não trava: em seguida o cupom de um pedido sai na MTP-II. |

## 4. Papel que não cabe

| Passo | Faça | Esperado |
|---|---|---|
| 4.1 | Na Oasis, passo 3: **Etiqueta 40 x 25 mm**. Imprima a etiqueta de produção | Não imprime. Aparece "Escolha um papel maior em Ajustes." seguido do motivo. |
| 4.2 | Volte para **Rolo contínuo de 80 mm** | Imprime normal de novo. |

Só com etiqueta 50 x 30 mm recortada carregada (no rolo contínuo a Oasis procuraria o
recorte): vertical, só QR e um produto de nome curto sai deitada, com o aviso
"Enviado. Saiu deitada: em pé não cabe."

## O que me mandar

Uma foto das etiquetas de 2.1, 2.2 e 2.3 ao lado da régua, e qualquer item que não
bateu com a coluna Esperado (com o texto exato do aviso, se houve). Se tudo bateu,
basta "roteiro ok".
