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
// Para DESCONGELAR um item: tire-o da lista abaixo e desfaça os dois textos que foram ajustados
// para não mentir enquanto o item está fora: o `resumo` do Financeiro em dominio/modulos.js e o
// `faz` do Financeiro em dominio/guiaDoConsole.js (ambos dizem só "Caixa do dia").
// `ferramentas/prova-congelados.mjs` confere que cada item da lista existe no catálogo e está
// fora da tela.

export const CONGELADOS = Object.freeze({
  // Módulo inteiro: sai do hall, do trilho e da busca; a rota `#/m/<id>` cai no hall.
  modulos: Object.freeze(['campanhas']),
  // Tela de um módulo que continua vivo (`modulo/tela`): sai do menu; a rota cai na primeira
  // tela viva do módulo.
  telas: Object.freeze([
    'financeiro/visao-geral',
    'financeiro/pagar',
    'financeiro/receber',
    'financeiro/extrato',
    'financeiro/relatorios',
    'financeiro/categorias',
    'financeiro/centros-custo',
    'cozinha/producao',
  ]),
  // Pontos avulsos das features (botão, link, chip), por chave.
  avulsos: Object.freeze([
    // Cotação, chamado e link externo da Lalamove no despacho. O cartão em Integrações fica:
    // a chave segue cotando o frete do pedido (#1885).
    'lalamove',
    // Chip do canal E-mail no Balcão. `CANAIS` fica: `canalDaConversa` e a prova-1432 dependem.
    'canal-email',
  ]),
})

export const moduloCongelado = (id) => CONGELADOS.modulos.includes(id)

export const telaCongelada = (moduloId, telaId) => CONGELADOS.telas.includes(`${moduloId}/${telaId}`)

export const congelado = (chave) => CONGELADOS.avulsos.includes(chave)
