// Congelamento da rodada de estabilização (#1889, F1 em #1891, decisão do Felipe de 20/10/2026).
//
// A versão operável cobre só a jornada "pedido a dinheiro": abrir loja e caixa, atender, montar
// o pedido, cobrar, Cozinha, entrega própria e fechar o caixa. O que está fora dela some da
// interface (não é botão desabilitado nem aviso "indisponível"); o código fica no lugar e volta
// por esta lista única. Vale nos dois modos (demonstração e API): congelar não depende de
// `fonteApi`.
//
// Quem consome: `telasDoMenu` e `modulosDoHall` (dominio/modulos.js), `rotaDaHash`
// (dominio/rota.js) e os pontos avulsos das features, pelo gate `congelado('<chave>')`.
//
// Para DESCONGELAR um item: tire-o da lista abaixo e confira se algum texto foi ajustado para não
// mentir enquanto o item estava fora (resumo do módulo em dominio/modulos.js, guia em
// dominio/guiaDoConsole.js). O financeiro gerencial voltou em 27/10/2026 (decisão do Felipe, #2095). O despacho
// da Lalamove voltou em 31/10/2026 (decisão do Felipe, #2162), agora pela API na corrida da viagem.
// `ferramentas/prova-congelados.mjs` confere que cada item da lista existe no catálogo e está
// fora da tela.

export const CONGELADOS = Object.freeze({
  // Módulo inteiro: sai do hall, do trilho e da busca; a rota `#/m/<id>` cai no hall.
  modulos: Object.freeze(['campanhas']),
  // Tela de um módulo que continua vivo (`modulo/tela`): sai do menu; a rota cai na primeira
  // tela viva do módulo.
  telas: Object.freeze([
    'cozinha/producao',
  ]),
  // Pontos avulsos das features (botão, link, chip), por chave.
  avulsos: Object.freeze([
    // Chip do canal E-mail no Balcão. `CANAIS` fica: `canalDaConversa` e a prova-1432 dependem.
    'canal-email',
  ]),
})

export const moduloCongelado = (id) => CONGELADOS.modulos.includes(id)

export const telaCongelada = (moduloId, telaId) => CONGELADOS.telas.includes(`${moduloId}/${telaId}`)

export const congelado = (chave) => CONGELADOS.avulsos.includes(chave)
