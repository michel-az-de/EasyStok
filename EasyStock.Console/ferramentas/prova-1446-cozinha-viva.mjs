// Prova da issue #1446: a Cozinha do modo API alcança a do protótipo. Cobre as
// partes puras de `dominio/kds.js`:
//   - o cartão do KDS vira o MESMO canhoto de 80 mm do protótipo
//     (`documentoDoCanhoto`), agrupado por linha, com molho e observação, sem
//     preço e com a observação do pedido;
//   - a transição entre duas leituras da fila (quem entrou, quem mudou de
//     coluna, quem saiu), que é o que a tela anima;
//   - a regra do soltar no arrasto (só o próximo passo aceita);
//   - o filtro por linha, a urgência e a barra de tempo do cartão e a fila de
//     impressão da cozinha.
//
// Roda com: node ferramentas/prova-1446-cozinha-viva.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const kds = await import('../src/dominio/kds.js')
const { documentoDoCanhoto } = await import('../src/dominio/impressao.js')
const {
  canhotoDoKds, filtrarPorLinha, transicaoDaFila, respostaAoSoltarKds, urgenciaDoPedido,
  ETAPAS_KDS, gruposDoKds, impressoesDaCozinha, tempoAteInicio, avisoDeInicio,
} = kds

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const LINHAS = {
  servir: { rotulo: 'Para servir' },
  casa: { rotulo: 'Preparar em casa' },
}

const pedido = (extra = {}) => ({
  id: 'p1',
  numeroCurto: 'A1B2C3D4',
  clienteNome: 'Ana Souza',
  clienteApt: 'Apto 12',
  janela: { label: '11h-12h', data: '2026-10-07', inicio: '11:00:00', fim: '12:00:00' },
  status: 'aguardando',
  statusRotulo: 'Aguardando',
  linhas: ['paraServir', 'prepararEmCasa'],
  itens: [
    { nome: 'Nhoque', variacao: '500 g', qtd: 2, observacao: 'sem cebola', linha: 'paraServir', molho: 'Sugo' },
    { nome: 'Lasanha', variacao: null, qtd: 1, observacao: null, linha: 'prepararEmCasa', molho: null },
    { nome: 'Pão', variacao: null, qtd: 1, observacao: null, linha: null, molho: null },
  ],
  inicioPrevistoEm: '2026-10-07T13:30:00Z',
  atrasado: false,
  pagoEm: '2026-10-07T13:00:00Z',
  criadoEm: '2026-10-07T12:55:00Z',
  observacoes: 'Interfone quebrado, ligar ao chegar',
  endereco: 'Rua das Flores, 10',
  ...extra,
})

const textos = (documento) => documento.paginas[0].desenhos.filter((d) => d.tipo === 'texto').map((d) => d.texto)

// --- Canhoto ------------------------------------------------------------------
confere('canhoto: cartão do KDS vira o canhoto de 80 mm do protótipo', () => {
  const doc = documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS))
  const t = textos(doc)
  assert.equal(doc.nomeArquivo, 'canhoto-A1B2C3D4.pdf')
  assert.ok(t.includes('CANHOTO DE PEDIDO, NÃO É CUPOM FISCAL'))
  assert.ok(t.includes('Nº A1B2C3D4'))
  assert.ok(t.some((x) => x.startsWith('Ana Souza · Apto 12') && x.includes('11h-12h')), t.join('|'))
  assert.ok(t.includes('Rua das Flores, 10'))
})

confere('canhoto: itens agrupados por linha, servir antes de casa, porção e molho com observação', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  const iServir = t.indexOf('PARA SERVIR')
  const iCasa = t.indexOf('PREPARAR EM CASA')
  assert.ok(iServir >= 0 && iCasa > iServir, t.join('|'))
  assert.ok(t.indexOf('Nhoque') > iServir && t.indexOf('Nhoque') < iCasa)
  assert.ok(t.includes('500 g'))
  assert.ok(t.includes('Molho Sugo · sem cebola'), t.join('|'))
  assert.ok(t.indexOf('Lasanha') > iCasa)
})

confere('canhoto: item sem linha não some do papel (cai em Para servir)', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  const iCasa = t.indexOf('PREPARAR EM CASA')
  const iPao = t.indexOf('Pão')
  assert.ok(iPao >= 0 && iPao < iCasa, t.join('|'))
})

confere('canhoto: observação do pedido sai no papel; sem preço', () => {
  const t = textos(documentoDoCanhoto(canhotoDoKds(pedido(), LINHAS)))
  assert.ok(t.some((x) => x.includes('Interfone quebrado')), t.join('|'))
  assert.ok(!t.some((x) => /R\$/.test(x)))
})

confere('canhoto: sem janela, sem endereço e sem observação não quebra', () => {
  const doc = documentoDoCanhoto(canhotoDoKds(pedido({ janela: null, endereco: null, observacoes: null, clienteApt: null }), LINHAS))
  const t = textos(doc)
  assert.ok(t.some((x) => x.startsWith('Ana Souza · Janela não escolhida')), t.join('|'))
})

confere('comanda em tela: mesmos grupos do papel', () => {
  const grupos = gruposDoKds(pedido(), LINHAS)
  assert.deepEqual(grupos.map((g) => g.chave), ['servir', 'casa'])
  assert.deepEqual(grupos[0].itens.map((l) => l.produto.nome), ['Nhoque', 'Pão'])
  assert.equal(grupos[0].itens[0].obs, 'Molho Sugo · sem cebola')
})

// --- Filtro por linha -----------------------------------------------------------
confere('filtro: por linha do console, aceita o token da API', () => {
  const soServir = pedido({ id: 'a', itens: [{ nome: 'X', qtd: 1, linha: 'paraServir' }] })
  const soCasa = pedido({ id: 'b', itens: [{ nome: 'Y', qtd: 1, linha: 'preparar_em_casa' }] })
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], null).map((p) => p.id), ['a', 'b'])
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], 'servir').map((p) => p.id), ['a'])
  assert.deepEqual(filtrarPorLinha([soServir, soCasa], 'casa').map((p) => p.id), ['b'])
})

// --- Transição da fila (o que a tela anima) -----------------------------------------
confere('transição: primeira leitura não anima nada', () => {
  const t = transicaoDaFila(null, [pedido()])
  assert.deepEqual(t, { novos: [], mudaram: [], sairam: [] })
})

confere('transição: entrou, mudou de coluna e saiu', () => {
  const antes = [pedido({ id: 'a' }), pedido({ id: 'b' }), pedido({ id: 'c', status: 'saiu_para_entrega' })]
  const depois = [pedido({ id: 'a' }), pedido({ id: 'b', status: 'preparando' }), pedido({ id: 'd' })]
  const t = transicaoDaFila(antes, depois)
  assert.deepEqual(t.novos, ['d'])
  assert.deepEqual(t.mudaram, ['b'])
  assert.deepEqual(t.sairam.map((p) => p.id), ['c'])
})

// --- Arrastar e soltar -----------------------------------------------------------
confere('soltar: só a coluna do próximo passo aceita', () => {
  assert.deepEqual(respostaAoSoltarKds('aguardando', 'preparando'), { aceita: true, motivo: null })
  assert.deepEqual(respostaAoSoltarKds('aguardando', 'aguardando'), { aceita: false, motivo: null })
  const pulo = respostaAoSoltarKds('aguardando', 'pronto')
  assert.equal(pulo.aceita, false)
  assert.match(pulo.motivo, /Um passo por vez.*Em preparo/)
  const volta = respostaAoSoltarKds('pronto', 'preparando')
  assert.equal(volta.aceita, false)
  assert.match(volta.motivo, /Saiu para entrega/)
  assert.equal(respostaAoSoltarKds('entregue', 'aguardando').aceita, false)
})

// --- Urgência ------------------------------------------------------------------
confere('urgência: atrasado, agora, logo ou nada; só enquanto aguarda', () => {
  const agora = Date.parse('2026-10-07T13:20:00Z')
  assert.equal(urgenciaDoPedido(pedido({ atrasado: true }), agora), 'atrasado')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T13:20:00Z' }), agora), 'agora')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T13:30:00Z' }), agora), 'logo')
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: '2026-10-07T14:30:00Z' }), agora), null)
  assert.equal(urgenciaDoPedido(pedido({ inicioPrevistoEm: null }), agora), null)
  assert.equal(urgenciaDoPedido(pedido({ status: 'preparando', inicioPrevistoEm: '2026-10-07T13:00:00Z' }), agora), null)
})

// --- Etapas e fila de impressão --------------------------------------------------------
confere('etapas: a trilha da comanda vai de Na fila a Entregue', () => {
  assert.deepEqual(ETAPAS_KDS.map((e) => e.status), ['aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue'])
})

confere('fila de impressão da cozinha: só pedido que está na fila do KDS, mais antigo primeiro', () => {
  const fila = [
    { id: 'i2', pedidoId: 'p2', status: 'pendente', criadaEm: '2026-10-07T13:05:00Z' },
    { id: 'i9', pedidoId: 'fora', status: 'pendente', criadaEm: '2026-10-07T12:00:00Z' },
    { id: 'i1', pedidoId: 'p1', status: 'pendente', criadaEm: '2026-10-07T13:01:00Z' },
    { id: 'i3', pedidoId: 'p3', status: 'falhou', criadaEm: '2026-10-07T13:00:00Z' },
  ]
  const naCozinha = [pedido({ id: 'p1' }), pedido({ id: 'p2' }), pedido({ id: 'p3' })]
  assert.deepEqual(impressoesDaCozinha(fila, naCozinha).map((i) => i.id), ['i1', 'i2'])
  assert.deepEqual(impressoesDaCozinha(fila, null), [])
})

confere('tempo até o início: fração que falta entre o pagamento e o início previsto', () => {
  const p = pedido({ pagoEm: '2026-10-07T13:00:00Z', inicioPrevistoEm: '2026-10-07T13:40:00Z' })
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T13:00:00Z')), 1)
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T13:30:00Z')), 0.25)
  assert.equal(tempoAteInicio(p, Date.parse('2026-10-07T14:00:00Z')), 0)
  assert.equal(tempoAteInicio(pedido({ inicioPrevistoEm: null }), Date.parse('2026-10-07T13:00:00Z')), null)
  assert.equal(tempoAteInicio(pedido({ status: 'preparando' }), Date.parse('2026-10-07T13:00:00Z')), null)
})

confere('aviso de início: "Começar em" só enquanto aguarda; atraso de outro dia vale em qualquer coluna', () => {
  const agora = Date.parse('2026-10-07T13:00:00Z')
  assert.match(avisoDeInicio(pedido({ status: 'aguardando' }), agora).texto, /Começar em 30 min/)
  assert.equal(avisoDeInicio(pedido({ status: 'preparando' }), agora), null)
  assert.equal(avisoDeInicio(pedido({ status: 'pronto' }), agora), null)
  assert.equal(avisoDeInicio(pedido({ status: 'preparando', inicioPrevistoEm: null, atrasado: true }), agora).atrasado, true)
})

confere('#1474: início distante mostra a hora, não centenas de minutos', () => {
  const agora = Date.parse('2026-10-08T21:50:00-03:00')
  assert.match(avisoDeInicio({ inicioPrevistoEm: '2026-10-09T09:30:00-03:00', atrasado: false, status: 'aguardando' }, agora).texto,
    /^Começar amanhã às 09:30$/)
  assert.match(avisoDeInicio({ inicioPrevistoEm: '2026-10-08T23:30:00-03:00', atrasado: false, status: 'aguardando' }, agora).texto,
    /^Começar às 23:30$/)
  assert.match(avisoDeInicio({ inicioPrevistoEm: '2026-10-08T22:30:00-03:00', atrasado: false, status: 'aguardando' }, agora).texto,
    /^Começar em 40 min$/)
  assert.match(avisoDeInicio({ inicioPrevistoEm: '2026-10-11T09:30:00-03:00', atrasado: false, status: 'aguardando' }, agora).texto,
    /^Começar em 11\/10 às 09:30$/)
})

// #1899 (#1883, parte do console): um nome só para o pedido. Com numeroComanda, "Comanda N" e o curto
// vira referência; sem ele, o curto sozinho (sem inventar número). O KDS e as Entregas usam esta função.
const { nomeDoPedido } = await import('../src/dominio/pedido.js')
// J2 (#1990, #1641-1 parte console): o curto do id não aparece mais como "ref" na tela, só no título
// (suporte); o número do dia, quando a API manda, vai ao lado da comanda.
confere('#1899/#1641-1: nome do pedido é "Comanda N" com o número do dia; o curto só no título', () => {
  assert.deepEqual(nomeDoPedido({ numeroComanda: 12, numeroCurto: '76A32A4D' }),
    { principal: 'Comanda 12', referencia: null, frase: 'Comanda 12', titulo: 'Comanda 12 · ref 76A32A4D' })
  assert.deepEqual(nomeDoPedido({ numeroComanda: 12, numeroCurto: '76A32A4D', numeroDoDia: 42 }),
    { principal: 'Comanda 12', referencia: 'nº 042', frase: 'Comanda 12', titulo: 'Comanda 12 · nº 042 · ref 76A32A4D' })
  assert.deepEqual(nomeDoPedido({ numeroCurto: '76A32A4D', numeroDoDia: 7 }),
    { principal: 'Nº 007', referencia: null, frase: 'Pedido nº 007', titulo: 'Nº 007 · ref 76A32A4D' })
  assert.equal(nomeDoPedido({ numeroComanda: 12, numeroDoDia: null }).referencia, null, 'campo ausente ou nulo não inventa número')
  assert.deepEqual(nomeDoPedido({ numeroCurto: '76A32A4D' }), { principal: '76A32A4D', referencia: null, frase: 'Pedido 76A32A4D', titulo: '76A32A4D' })
  assert.deepEqual(nomeDoPedido({ numeroComanda: null, numeroCurto: null }), { principal: '', referencia: null, frase: 'Pedido', titulo: '' })
  assert.equal(nomeDoPedido(undefined).principal, '')
  assert.equal(nomeDoPedido({ numeroComanda: 0, numeroCurto: 'A1B2' }).principal, 'A1B2', 'zero não é comanda')
})
// --- Frente C da #1889: #1686 itens 5 e 8 ------------------------------------------------------
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')
const esteira = await import('../src/dominio/esteira.js')
const { rotuloDoEstado } = await import('../src/dominio/pedido.js')
const envio = await import('../src/aplicacao/envioComDesfazer.js')

confere('#1686-8 a Ficha fala a língua da Cozinha: "Pronto" e "Saiu para entrega", nos rótulos e nos toques', () => {
  const coluna = (status) => kds.COLUNAS_KDS.find((c) => c.status === status)
  assert.equal(esteira.passoPorId('embalado').rotulo, coluna('pronto').rotulo)
  assert.equal(esteira.passoPorId('entrega').rotulo, coluna('saiu_para_entrega').rotulo)
  assert.equal(esteira.passoPorId('preparo').rotulo, coluna('preparando').rotulo)
  assert.equal(rotuloDoEstado('pronto'), 'Pronto')
  assert.equal(rotuloDoEstado('saiu para entrega'), 'Saiu para entrega')
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.match(barra, /pago: \{ rotulo: 'Começar preparo'/)
  assert.match(barra, /preparo: \{ rotulo: 'Marcar pronto'/)
  assert.match(barra, /embalado: \{ rotulo: 'Saiu para entrega'/)
  for (const [status, toque] of [['aguardando', 'Começar preparo'], ['preparando', 'Marcar pronto'], ['pronto', 'Saiu para entrega']]) {
    assert.equal(coluna(status).toque, toque, status)
  }
  assert.doesNotMatch(fonte('dominio/respostas.js'), /"Embalado"|"Em entrega"/)
})

// O envio é assíncrono: esta conferência espera as promessas antes de contar.
const confereEsperando = async (descricao, fn) => { await fn(); passou += 1; console.log('ok  ' + descricao) }
await confereEsperando('#1686-5 envio com desfazer: agenda, desfaz antes de sair e descarrega ao sair da tela', async () => {
  let agora = 0
  const timers = new Map()
  let seq = 0
  const relogio = { agendar: (fn, ms) => { seq += 1; timers.set(seq, { fn, em: agora + ms }); return seq }, cancelar: (t) => timers.delete(t), agora: () => agora }
  const andar = (ms) => { agora += ms; for (const [t, { fn, em }] of [...timers]) if (em <= agora) { timers.delete(t); fn() } }
  const enviados = []
  const mudancas = []
  const e = envio.criarEnvioComDesfazer({ prazoMs: 5000, relogio, aoMudar: (l) => mudancas.push(l.map((x) => x.chave)) })
  const a = e.agendar('a', () => { enviados.push('a'); return 'ok-a' }, { status: 'preparando' })
  assert.equal(e.agendar('a', () => enviados.push('dobrado')), a, 'o segundo toque recebe a mesma promessa')
  const b = e.agendar('b', () => { enviados.push('b'); return 'ok-b' })
  assert.deepEqual(e.pendentes().map((x) => [x.chave, x.ate]), [['a', 5000], ['b', 5000]])
  andar(4999)
  assert.equal(e.desfazer('a'), true)
  assert.deepEqual(await a, { desfeito: true })
  andar(1)
  assert.equal(await b, 'ok-b')
  assert.deepEqual(enviados, ['b'])
  const c = e.agendar('c', () => { enviados.push('c'); return 'ok-c' })
  e.descarregar()
  assert.equal(await c, 'ok-c')
  assert.deepEqual(enviados, ['b', 'c'])
  assert.equal(e.desfazer('c'), false, 'o que já saiu não se desfaz')
  assert.deepEqual(mudancas.at(-1), [])
  assert.equal(envio.segundosAte(5000, 1001), 4)
})

confere('#1686-5 o cartão, a comanda e as Entregas mostram "Desfazer" no lugar do toque enquanto esperam', () => {
  assert.equal(kds.rotuloDoDestinoKds('preparando'), 'Em preparo')
  assert.equal(kds.rotuloDoDestinoKds('entregue'), 'Entregue')
  const cartao = fonte('features/cozinha/CartaoKds.jsx')
  assert.match(cartao, /\{proximo && adiado && \(\s+<DesfazerComPrazo/)
  assert.match(cartao, /\{proximo && !adiado && \(/)
  assert.match(cartao, /disabled: !proximo \|\| movendo \|\| Boolean\(adiado\)/, 'não arrasta o cartão que espera')
  assert.match(fonte('features/cozinha/ComandaKds.jsx'), /\{proximo && adiado && \(\s+<DesfazerComPrazo/)
  const tela = fonte('features/cozinha/TelaCozinhaApi.jsx')
  assert.match(tela, /adiado=\{adiados\.get\(p\.id\) \?\? null\} aoDesfazer=\{desfazerAvanco\}/)
  const entregas = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(entregas, /acoes\.adiadoAte\(chaveDaEntrega\(viagem\.id, parada\.pedidoId\)\) \? \(\s+<DesfazerComPrazo/)
  assert.match(entregas, /\{montando && acoes\.adiadoAte\(chaveDaSaida\(viagem\.id\)\) && \(\s+<DesfazerComPrazo/)
  assert.match(fonte('componentes/DesfazerComPrazo.jsx'), /Desfazer · \{restam\}/)
})

// --- Frente J2 (#1990): entregas no modo API --------------------------------------------------
confere('J2-8 Cozinha e Entregas não mostram mais "ref" com o id técnico', () => {
  for (const arquivo of ['features/cozinha/CartaoKds.jsx', 'features/entregas/ConteudoEntregasApi.jsx',
    'features/entregas/AgendaEncomendasApi.jsx', 'features/entregas/EntregasDoDia.jsx']) {
    assert.doesNotMatch(fonte(arquivo), />ref \{/, arquivo)
  }
})

const confereAsync = async (descricao, fn) => { await fn(); passou += 1; console.log('ok  ' + descricao) }
const recebimento = await import('../src/aplicacao/recebimentoNaEntrega.js')
const entregasApi = await import('../src/dominio/entregasApi.js')

await confereAsync('J2-2 "Recebi" lança o pagamento e só então marca entregue; sem método não sai nada', async () => {
  const chamadas = []
  const portas = {
    registrarPagamento: async (...a) => { chamadas.push(['pagar', ...a]) },
    marcarEntregue: async (...a) => { chamadas.push(['entregue', ...a]) },
  }
  const pedido = { viagemId: 'v', pedidoId: 'p', valor: 57, metodo: 'dinheiro', chave: 'k' }
  await recebimento.receberEEntregar(portas, pedido)
  assert.deepEqual(chamadas, [['pagar', 'p', 57, 'dinheiro', 'k'], ['entregue', 'v', 'p', true]])
  chamadas.length = 0
  await assert.rejects(recebimento.receberEEntregar({ ...portas, registrarPagamento: async () => { throw new Error('recusado') } }, pedido), /recusado/)
  assert.deepEqual(chamadas, [], 'pagamento recusado não marca entregue')
  await assert.rejects(recebimento.receberEEntregar(portas, { ...pedido, metodo: '' }), /como recebeu/)
  assert.deepEqual(chamadas, [])
  // Pagamento entrou e a entrega falhou: o toque seguinte (mesmo minutos depois) só marca a entrega.
  const recebidos = new Set()
  let entregaCai = true
  const instavel = {
    ...portas, recebidos,
    marcarEntregue: async (...a) => { if (entregaCai) throw new Error('sem conexão.'); chamadas.push(['entregue', ...a]) },
  }
  await assert.rejects(recebimento.receberEEntregar(instavel, pedido), /O pagamento entrou, mas a entrega não foi marcada/)
  entregaCai = false
  await recebimento.receberEEntregar(instavel, pedido)
  assert.deepEqual(chamadas, [['pagar', 'p', 57, 'dinheiro', 'k'], ['entregue', 'v', 'p', true]], 'um pagamento só')
  assert.equal(recebidos.size, 0)
  chamadas.length = 0
  assert.deepEqual(recebimento.METODOS_NA_PORTA.map((m) => m.valor), ['', 'dinheiro', 'debito', 'credito', 'pix'])
  assert.equal(recebimento.metodoSugeridoNaPorta('Pagamento na entrega: dinheiro, troco para R$ 100,00.'), 'dinheiro')
  assert.equal(recebimento.metodoSugeridoNaPorta(null), '')
  const tela = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(tela, /acoes\.receberEEntregar\(viagem\.id, parada\.pedidoId, confirmando\.valor, metodoNaPorta\)/)
  assert.match(tela, /Recebi \{moeda\(confirmando\.valor\)\}/)
  assert.match(fonte('aplicacao/useEntregasApi.js'),
    /receberEEntregar: \(id, pedidoId, valor, metodo\) => envio\.agendar\(chaveDaEntrega\(id, pedidoId\)/)
})

await confereAsync('J2-3 sem entregador cadastrado, a viagem sai com "Entrega própria (sem cadastro)"', async () => {
  const { ENTREGA_PROPRIA, entregadorDaEntregaPropria, opcoesDeEntregador, motivoDeSaida } = entregasApi
  assert.deepEqual(opcoesDeEntregador([]).map((o) => o.valor), ['', ENTREGA_PROPRIA])
  assert.equal(opcoesDeEntregador([]).at(-1).rotulo, 'Entrega própria (sem cadastro)')
  const cadastrados = [{ id: 'e1', nome: 'Entrega Própria' }, { id: 'e2', nome: 'João' }]
  assert.deepEqual(opcoesDeEntregador(cadastrados).map((o) => o.valor), ['', 'e1', 'e2'], 'a já cadastrada não repete')
  assert.equal(entregadorDaEntregaPropria(cadastrados)?.id, 'e1')
  assert.match(motivoDeSaida({ entregadorId: null, paradas: [{}] }), /Entrega própria/)
  const feitas = []
  let todos = []
  const portas = {
    listarComInativos: async () => todos,
    reativarEntregador: async (id) => { feitas.push(['reativar', id]) },
    criarEntregador: async (corpo) => { feitas.push(['criar', corpo]); return { id: 'novo' } },
    definirEntregador: async (viagemId, entregadorId) => { feitas.push(['definir', viagemId, entregadorId]) },
  }
  await recebimento.usarEntregaPropria(portas, { viagemId: 'v', entregadores: [] })
  assert.deepEqual(feitas, [
    ['criar', { nome: 'Entrega própria', tipo: 'Proprio', empresa: 'Propria', telefone: null, veiculo: null, placa: null }],
    ['definir', 'v', 'novo'],
  ])
  feitas.length = 0
  await recebimento.usarEntregaPropria(portas, { viagemId: 'v', entregadores: cadastrados })
  assert.deepEqual(feitas, [['definir', 'v', 'e1']], 'não cadastra de novo')
  feitas.length = 0
  todos = [{ id: 'e9', nome: 'Entrega própria', ativo: false }]
  await recebimento.usarEntregaPropria(portas, { viagemId: 'v', entregadores: [] })
  assert.deepEqual(feitas, [['reativar', 'e9'], ['definir', 'v', 'e9']], 'a desativada volta, sem cadastro duplicado')
  const tela = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(tela, /opcoesDeEntregador\(entregadores\)/)
  assert.match(tela, /e\.target\.value === ENTREGA_PROPRIA \? acoes\.usarEntregaPropria\(viagem\.id\)/)
  assert.match(fonte('aplicacao/useEntregasApi.js'), /usarEntregaPropria: \(id\) =>/)
})

confere('J2-1 o entregador vê "Dinheiro na entrega" e o troco na parada', () => {
  const tela = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(tela, /const \{ identificacao, aReceber, observacoes, troco \} = paradaDaViagem\(/)
  assert.match(tela, /aReceber > 0 && troco && <p className=\{css\.apoio\}>\{troco\}<\/p>/)
})

// --- Frente K (#2007): Entregas fala do pedido como a Cozinha ---------------------------------
const roteiroMod = await import('../src/dominio/roteiroDoDia.js')
const semNbsp = (s) => String(s ?? '').replace(/ /g, ' ')
const OBS_TROCO = 'Pagamento na entrega: dinheiro, troco para R$ 100,00.'

confere('K-1 o troco do dinheiro na entrega chega ao roteiro, à parada fora da lista e ao diálogo da porta', () => {
  const r = roteiroMod.roteiroDoDia({
    data: '2026-10-24', viagens: [], entregadores: [], janelas: [], bloqueios: [],
    pedidos: [{ id: 'p1', numeroCurto: '57A22217', numeroComanda: 12, status: 'saiu_para_entrega', aReceber: 57,
      observacoes: OBS_TROCO, janela: null, agendadoParaEm: '2026-10-24T12:00:00Z', criadoEm: '2026-10-23T12:00:00Z' }],
  })
  assert.equal(semNbsp(roteiroMod.situacaoDoPagamento(r.grupos[0].pedidos[0])),
    'Receber R$ 57,00 na entrega · Dinheiro na entrega · troco para R$ 100,00 · levar R$ 43,00')
  // O pedido de outro dia não está na lista que a tela leu: a parada traz as observações da API.
  const parada = { pedidoId: 'p9', numeroPedido: '57A22217', numeroDoDia: 4, observacoes: OBS_TROCO }
  const fora = roteiroMod.paradaDaViagem(parada, undefined)
  assert.equal(semNbsp(roteiroMod.trocoDaPorta(fora.observacoes, 57)), 'Dinheiro na entrega · troco para R$ 100,00 · levar R$ 43,00')
  const dentro = roteiroMod.paradaDaViagem(parada, { numeroComanda: 12, numeroCurto: '57A22217', aReceber: 57, observacoes: OBS_TROCO })
  assert.equal(dentro.aReceber, 57)
  assert.match(semNbsp(dentro.troco), /troco para R\$ 100,00 · levar R\$ 43,00/)
  const tela = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(tela, /paradaDaViagem\(parada, pedidoPorId\?\.get\(parada\.pedidoId\)\)/)
  assert.match(tela, /trocoDaPorta\(/, 'o diálogo da porta mostra o troco')
  assert.match(tela, /doDia\?\.pedidos/, 'a parada acha o pedido também na lista do dia escolhido')
})

confere('K-3 a parada diz "Comanda N" ou o nº do dia, como o roteiro, e não o código curto', () => {
  const parada = { pedidoId: 'p9', numeroPedido: '57A22217', numeroDoDia: 4 }
  assert.equal(roteiroMod.paradaDaViagem(parada, { numeroComanda: 12, numeroCurto: '57A22217', numeroDoDia: 4 }).identificacao.principal, 'Comanda 12')
  assert.equal(roteiroMod.paradaDaViagem(parada, undefined).identificacao.principal, 'Nº 004')
  const tela = fonte('features/entregas/ConteudoEntregasApi.jsx')
  assert.doesNotMatch(tela, /\{parada\.ordem\}\. \{parada\.numeroPedido\}/)
  assert.doesNotMatch(tela, /Confirmar a entrega do pedido \$\{parada\.numeroPedido\}/)
})

confere('K-11a "Entrega própria" não repete a empresa entre parênteses', () => {
  assert.equal(roteiroMod.textoDeQuemLeva({ nome: 'Entrega própria', empresa: 'Própria' }), 'Entrega própria')
  assert.equal(roteiroMod.textoDeQuemLeva({ nome: 'João', empresa: 'Própria' }), 'João (Própria)')
  assert.equal(roteiroMod.textoDeQuemLeva({ nome: 'Carlos', empresa: 'Lalamove' }), 'Carlos (Lalamove)')
})

console.log(`prova #1446 (cozinha viva no modo API): ${passou} conferências ok`)
