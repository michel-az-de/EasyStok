/* eslint-disable no-console */
// #2478: Pedidos no módulo Cadastros. Menu e permissão pela matriz da API, consulta da lista no formato do
// `GET api/pedidos`, situação no vocabulário da Cozinha, canal legível, saldo e linha do tempo do detalhe.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

// O domínio importa sem extensão (padrão do Vite); o Node puro precisa do `.js` (mesmo hook da prova-1447).
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { moduloPorId, telasDoMenu } = await import('../src/dominio/modulos.js')
const { GUIA_POR_MODULO } = await import('../src/dominio/guiaDoConsole.js')
const { moeda } = await import('../src/dominio/formato.js')
const {
  consultaDePedidos, linhaDoTempoDoPedido, numeroDoPedido, tituloDoPedido, ORDENS_PEDIDO, PERIODOS_PEDIDO, rotuloDaSituacao, rotuloDoCanal,
  rotuloDoMetodo, SITUACOES_PEDIDO, situacaoDoPagamento, situacaoPedeAcao,
} = await import('../src/dominio/pedidosCadastro.js')

// Menu: a tela fica em Cadastros, só no modo API e só para quem a matriz libera.
const cadastros = moduloPorId('cadastros')
const ids = (acoes, fonteApi = true) => telasDoMenu(cadastros, { fonteApi, acoes }).map(t => t.id)
assert.ok(ids({ verPedidos: true }).includes('pedidos'), 'Pedidos aparece para quem a API libera')
assert.equal(ids({ verPedidos: false }).includes('pedidos'), false, 'Visualizador não vê a tela que daria 403')
assert.equal(ids({}).includes('pedidos'), false, 'Sem a chave, a tela não abre')
assert.equal(ids(null, false).includes('pedidos'), false, 'Demonstração não tem pedidos da API')
assert.equal(cadastros.telas.find(t => t.id === 'pedidos').aba, 'cadastros-pedidos')
assert.ok(GUIA_POR_MODULO.cadastros.passos.some(p => /Pedidos/.test(p)), 'A Ajuda ensina a tela nova')
assert.match(readFileSync(new URL('../src/componentes/iconesDoConsole.js', import.meta.url), 'utf8'), /'cadastros\/pedidos'/)

// Consulta: os nomes do controller, página de 20, período pelo dia da loja e nada vazio na URL.
const meioDia = Date.parse('2026-11-13T15:00:00Z') // 12:00 em São Paulo
const madrugada = Date.parse('2026-11-14T02:30:00Z') // 23:30 do dia 13 em São Paulo
assert.deepEqual(consultaDePedidos({ pagina: 1 }, meioDia), { page: 1, pageSize: 20, sort: 'criadoem', order: 'desc' })
assert.deepEqual(consultaDePedidos({ busca: '  #12 ', pagina: 3, situacao: 'pronto', periodo: 'hoje', ordem: 'maior-valor' }, meioDia),
  { page: 3, pageSize: 20, search: '#12', status: 'pronto', desde: '2026-11-13', sort: 'total', order: 'desc' })
assert.equal(consultaDePedidos({ periodo: 'hoje' }, madrugada).desde, '2026-11-13', 'Hoje é o dia da loja, não o UTC')
assert.equal(consultaDePedidos({ periodo: '7d' }, meioDia).desde, '2026-11-07', '7 dias contam hoje')
assert.equal(consultaDePedidos({ periodo: '30d' }, meioDia).desde, '2026-10-15')
assert.equal(consultaDePedidos({ periodo: 'tudo' }, meioDia).desde, undefined)
assert.deepEqual(consultaDePedidos({ ordem: 'mais-antigos' }, meioDia).order, 'asc')
assert.equal(PERIODOS_PEDIDO[0].valor, 'tudo', 'Cadastro não esconde dado atrás de filtro')
assert.equal(ORDENS_PEDIDO[0].valor, 'mais-recentes')
assert.throws(() => consultaDePedidos({ ordem: 'qualquer' }, meioDia), /ordem/)

// Situação: os dez tokens do StatusPedidoMapper, com o nome da Cozinha; pílula só no que pede ação.
const tokens = ['rascunho', 'aguardando_pagamento', 'aguardando_aprovacao_baba', 'aprovado_baba', 'aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue', 'cancelado']
for (const t of tokens) assert.ok(SITUACOES_PEDIDO.some(s => s.valor.split(',').includes(t)), `Filtro sem a situação ${t}`)
assert.equal(new Set(SITUACOES_PEDIDO.map(s => s.rotulo)).size, SITUACOES_PEDIDO.length, 'Um rótulo por opção do filtro')
assert.equal(SITUACOES_PEDIDO.find(s => s.rotulo === 'Na fila').valor, 'aguardando,aprovado_baba', 'Na fila junta o pago e o aprovado, como a Cozinha')
assert.equal(consultaDePedidos({ situacao: 'aguardando,aprovado_baba' }, meioDia).status, 'aguardando,aprovado_baba')
assert.equal(rotuloDaSituacao('aguardando'), 'Na fila')
assert.equal(rotuloDaSituacao('aprovado_baba'), 'Na fila')
assert.equal(rotuloDaSituacao('preparando'), 'Em preparo')
assert.equal(rotuloDaSituacao('saiu_para_entrega'), 'Saiu para entrega')
assert.equal(rotuloDaSituacao('aguardando_pagamento'), 'Aguardando pagamento')
assert.equal(rotuloDaSituacao('status_novo'), 'Status novo', 'Token desconhecido vira texto legível, nunca some')
assert.equal(rotuloDaSituacao(null), 'Sem situação')
assert.deepEqual(tokens.filter(situacaoPedeAcao), ['aguardando_pagamento', 'aguardando_aprovacao_baba'])

// Canal: texto livre da API vira nome conhecido; o desconhecido aparece como veio, legível.
assert.equal(rotuloDoCanal('storefront'), 'Site')
assert.equal(rotuloDoCanal('whatsapp'), 'WhatsApp')
assert.equal(rotuloDoCanal('balcao'), 'Balcão')
assert.equal(rotuloDoCanal('comanda_cliente'), 'Link da comanda')
assert.equal(rotuloDoCanal('IFOOD'), 'Ifood')
assert.equal(rotuloDoCanal('web'), 'Painel')
assert.equal(rotuloDoCanal(''), null)

// Forma de pagamento: nome do caixa; o que o caixa não conhece aparece legível, nunca "Outro".
assert.equal(rotuloDoMetodo(' PIX '), 'Pix')
assert.equal(rotuloDoMetodo('credito'), 'Crédito')
assert.equal(rotuloDoMetodo('mercadopago'), 'Mercado Pago')
assert.equal(rotuloDoMetodo('vale_refeicao'), 'Vale refeicao')
assert.equal(rotuloDoMetodo(null), 'Forma não informada')

// Número: o do dia, no formato da Cozinha (`numeroDoDiaNaTela`), volta a 1 todo dia; vem com a data da loja.
assert.equal(numeroDoPedido({ numeroDoDia: 12, criadoEm: '2026-11-13T13:42:00Z' }), 'Nº 012 de 13/11')
assert.equal(numeroDoPedido({ numeroDoDia: null, criadoEm: '2026-11-14T02:30:00Z' }), 'Pedido de 13/11')
// Onda 3: o número é do dia de produção (encomenda feita dia 12 para o dia 15), não do dia em que foi feito.
assert.equal(numeroDoPedido({ numeroDoDia: 7, dataNumero: '2026-11-15', criadoEm: '2026-11-12T13:00:00Z' }), 'Nº 007 de 15/11')
assert.equal(numeroDoPedido({ numeroDoDia: null, dataNumero: null, criadoEm: '2026-11-12T13:00:00Z' }), 'Pedido de 12/11')
assert.equal(tituloDoPedido({ numeroDoDia: 12, criadoEm: '2026-11-13T13:42:00Z' }), 'Pedido Nº 012 de 13/11')
assert.equal(tituloDoPedido({ numeroDoDia: null, criadoEm: '2026-11-14T02:30:00Z' }), 'Pedido de 13/11', 'sem número não repete "Pedido"')
assert.equal(tituloDoPedido({}), 'Pedido')

// Saldo: pergunta da dona ao abrir; cancelado não cobra e total zero não inventa pendência.
assert.deepEqual(situacaoDoPagamento({ status: 'pronto', total: 75, totalPago: 75 }), { texto: 'Pago', falta: 0 })
// Onda 5: excedente e cancelado com valor recebido não somem atrás de "Pago" ou de nada.
assert.deepEqual(situacaoDoPagamento({ status: 'pronto', total: 75, totalPago: 80 }), { texto: `Recebido ${moeda(5)} a mais`, falta: 0, sobra: 5 })
assert.deepEqual(situacaoDoPagamento({ status: 'cancelado', total: 75, totalPago: 30 }), { texto: `Cancelado com ${moeda(30)} recebido`, falta: 0, sobra: 30 })
assert.deepEqual(situacaoDoPagamento({ status: 'aguardando_pagamento', total: 75, totalPago: 0 }), { texto: `Falta ${moeda(75)}`, falta: 75 })
assert.deepEqual(situacaoDoPagamento({ status: 'aguardando', total: 100.1, totalPago: 50.05 }), { texto: `Falta ${moeda(50.05)}`, falta: 50.05 })
assert.equal(situacaoDoPagamento({ status: 'cancelado', total: 75, totalPago: 0 }), null)
assert.equal(situacaoDoPagamento({ status: 'rascunho', total: 0, totalPago: 0 }), null)

// Linha do tempo: ordem dos fatos, situação com o nome da tela, quem fez e por onde.
const linha = linhaDoTempoDoPedido([
  { tipo: 'status_changed', statusAntigo: 'preparando', statusNovo: 'pronto', usuarioNome: 'Thati', origem: 'kds', ocorridoEm: '2026-11-13T14:10:00Z' },
  { tipo: 'criado', statusNovo: 'aguardando_pagamento', origem: 'whatsapp', ocorridoEm: '2026-11-13T13:42:00Z' },
  { tipo: 'pagamento', detalhes: 'Pix R$ 75,00', usuarioNome: null, origem: 'mercadopago', ocorridoEm: '2026-11-13T13:50:00Z' },
  { tipo: 'cancelado', statusAntigo: 'pronto', statusNovo: 'cancelado', detalhes: 'Cliente desistiu', usuarioNome: 'Ana', origem: 'console', ocorridoEm: '2026-11-13T15:00:00Z' },
  { tipo: 'evento_novo', origem: 'sistema', ocorridoEm: '2026-11-13T15:05:00Z' },
])
assert.deepEqual(linha.map(e => e.texto), [
  'Pedido criado: Aguardando pagamento', 'Pagamento registrado: Pix R$ 75,00', 'Em preparo → Pronto', 'Pedido cancelado: Cliente desistiu', 'Evento novo',
])
assert.equal(linha[2].quem, 'Thati · Cozinha')
assert.equal(linha[0].quem, 'WhatsApp')
assert.equal(linha[1].quem, 'Mercado Pago')
assert.deepEqual(linhaDoTempoDoPedido(null), [])

console.log('prova-2478-pedidos-cadastros: ok')
