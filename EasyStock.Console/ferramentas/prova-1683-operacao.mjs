/* eslint-disable no-console */
// Prova da issue #1683 (design sprint do Console, operação): palavras e decisões que a tela
// mostrava de um jeito e o sistema fazia de outro.
//
//   1  Cozinha: o pedido pago que espera o preparo está "Na fila". "Aguardando" fica com a
//      Ficha, que chama assim o pedido que ainda não foi pago;
//   2  Encerrar atendimento só cancela o pedido sem pagamento quando a caixa está marcada, e
//      cancelamento que falha vira erro (o modal não segue para o encerramento);
//   3  Fechar o caixa diz que o caixa não reabre e que a loja só abre com o caixa aberto;
//   4  o lançamento de entrada sugere "Venda no balcão";
//   5  o grupo que soma tudo o que não é dinheiro não se chama só "Pix e cartão".
//
// Exercita o domínio puro e as funções puras da aplicação. Não prova pixels nem o serviço remoto;
// o modal de verdade é exercitado em prova-1586-encerrar-ficha-ux.mjs.
//
//   node ferramentas/prova-1683-operacao.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  // A ação de encerrar puxa a infra: o JSON da massa entra como módulo, como na prova-1586.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const kds = await import('../src/dominio/kds.js')
const esteira = await import('../src/dominio/esteira.js')
const caixa = await import('../src/dominio/caixa.js')
const encerramento = await import('../src/aplicacao/api/encerramentoEMidia.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) } catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n  ' + erro.message) }
}

// --- 1. Cozinha -----------------------------------------------------------------------------

confere('1  cozinha: a coluna do pedido pago que espera o preparo é "Na fila"', () => {
  const coluna = kds.COLUNAS_KDS.find((c) => c.status === 'aguardando')
  assert.equal(coluna.rotulo, 'Na fila')
  assert.equal(kds.ETAPAS_KDS[0].rotulo, 'Na fila')
})
confere('1  nenhum rótulo da cozinha repete o "Aguardando" da Ficha (pedido não pago)', () => {
  assert.match(esteira.PASSOS.find((p) => p.id === 'aguardando').rotulo, /^Aguardando pagamento/)
  for (const etapa of kds.ETAPAS_KDS) assert.doesNotMatch(etapa.rotulo, /aguardando/i, etapa.status)
})
confere('1  o cartão escreve o rótulo da coluna, não o "Aguardando" que a API manda', () => {
  assert.equal(kds.rotuloDoStatusKds({ status: 'aguardando', statusRotulo: 'Aguardando' }), 'Na fila')
  assert.equal(kds.rotuloDoStatusKds({ status: 'preparando', statusRotulo: 'Em preparo' }), 'Em preparo')
  assert.equal(kds.rotuloDoStatusKds({ status: 'entregue', statusRotulo: 'Entregue' }), 'Entregue', 'fora das colunas vale a API')
  assert.match(readFileSync(new URL('../src/features/cozinha/CartaoKds.jsx', import.meta.url), 'utf8'), /rotuloDoStatusKds\(pedido\)/)
  assert.doesNotMatch(readFileSync(new URL('../src/features/hall/PulsoDoHall.jsx', import.meta.url), 'utf8'), /ou aguardando/)
})

// --- 2. Encerrar atendimento -----------------------------------------------------------------

const semPagamento = { pedidoId: 'p1', estado: 'aguardando', numero: 'P1ABCDEF' }

confere('2  encerrar: pedido sem pagamento oferece a caixa e, desmarcada, não cancela', () => {
  assert.equal(typeof encerramento.pedidoAoEncerrar, 'function', 'falta a decisão pura pedidoAoEncerrar')
  assert.deepEqual(encerramento.pedidoAoEncerrar(semPagamento, { fonteApi: true }), { ofereceCancelar: true, cancelar: false })
  assert.deepEqual(encerramento.pedidoAoEncerrar(semPagamento, { fonteApi: true, cancelarMarcado: false }), { ofereceCancelar: true, cancelar: false })
})
confere('2  encerrar: só cancela com a caixa marcada', () => {
  assert.deepEqual(encerramento.pedidoAoEncerrar(semPagamento, { fonteApi: true, cancelarMarcado: true }), { ofereceCancelar: true, cancelar: true })
  assert.deepEqual(encerramento.pedidoAoEncerrar({ ...semPagamento, pedidoId: undefined }, { fonteApi: false, cancelarMarcado: true }), { ofereceCancelar: true, cancelar: true })
})
confere('2  encerrar: pedido pago, sem pedido ou sem pedido no EasyStok não oferece nem cancela', () => {
  const nada = { ofereceCancelar: false, cancelar: false }
  assert.deepEqual(encerramento.pedidoAoEncerrar({ ...semPagamento, estado: 'pago' }, { fonteApi: true, cancelarMarcado: true }), nada)
  assert.deepEqual(encerramento.pedidoAoEncerrar(null, { fonteApi: true, cancelarMarcado: true }), nada)
  assert.deepEqual(encerramento.pedidoAoEncerrar({ ...semPagamento, pedidoId: undefined }, { fonteApi: true, cancelarMarcado: true }), nada)
})
confere('2  encerrar: o rótulo da caixa diz o número do pedido e que ele não foi pago', () => {
  assert.equal(encerramento.rotuloCancelarAoEncerrar('P1ABCDEF'), 'Cancelar também o pedido Nº P1ABCDEF, que ainda não foi pago')
})
confere('2  encerrar: cancelamento que falha vira erro e o atendimento não é encerrado', () => {
  assert.equal(encerramento.falhaAoCancelarNoEncerramento({ ok: true }, 'P1'), null)
  assert.equal(encerramento.falhaAoCancelarNoEncerramento(undefined, 'P1'), null, 'a demonstração é síncrona e não devolve nada')
  assert.equal(
    encerramento.falhaAoCancelarNoEncerramento({ erro: 'Sem conexão com o EasyStok.' }, 'P1'),
    'Não deu para cancelar o pedido Nº P1: Sem conexão com o EasyStok. O atendimento continua aberto. Tente de novo ou desmarque a caixa de cancelar.',
  )
  assert.match(encerramento.falhaAoCancelarNoEncerramento({}, 'P1'), /^Não deu para cancelar o pedido Nº P1\. O atendimento continua aberto\./)
})

confere('2  a caixa de cancelar nasce sempre desmarcada, com ou sem link (decisão de 22/10)', () => {
  assert.equal(encerramento.cancelarNasceMarcado, undefined, 'a regra que marcava a caixa sem link saiu')
  const modal = readFileSync(new URL('../src/features/encerramento/ModalEncerrar.jsx', import.meta.url), 'utf8')
  assert.match(modal, /const \[cancelarMarcado, setCancelarMarcado\] = useState\(false\)/)
  assert.match(modal, /setCancelarMarcado\(false\)/)
  const fonte = readFileSync(new URL('../src/aplicacao/api/encerramentoEMidia.js', import.meta.url), 'utf8')
  assert.match(fonte, /nasce desmarcada/)
  assert.doesNotMatch(fonte, /nasce marcada/, 'o comentário contradizia o código')
})

// --- 3. Fechar o caixa -------------------------------------------------------------------------

confere('3  fechar o caixa: a descrição diz que não reabre e que a loja depende do caixa aberto', () => {
  assert.equal(typeof caixa.AVISO_AO_FECHAR_CAIXA, 'string', 'falta AVISO_AO_FECHAR_CAIXA')
  assert.match(caixa.AVISO_AO_FECHAR_CAIXA, /não reabre/)
  assert.match(caixa.AVISO_AO_FECHAR_CAIXA, /Sem caixa aberto, a loja não abre/)
  assert.match(caixa.AVISO_AO_FECHAR_CAIXA, /estornados/, 'o aviso do estorno continua')
  assert.doesNotMatch(caixa.AVISO_AO_FECHAR_CAIXA, /—/)
})

// --- 4. Venda no balcão ----------------------------------------------------------------------

confere('4  lançamento de entrada sugere "Venda no balcão", sem perder "Suprimento"', () => {
  assert.deepEqual(caixa.CATEGORIAS_ENTRADA_SUGERIDAS, ['Venda no balcão', 'Suprimento'])
  assert.equal(caixa.categoriaAoTrocarTipo('Venda no balcão', caixa.TIPOS_MOVIMENTO.SAIDA), '')
  assert.equal(caixa.categoriaAoTrocarTipo('Venda no balcão', caixa.TIPOS_MOVIMENTO.ENTRADA), 'Venda no balcão')
})
confere('4  venda no balcão em dinheiro entra na gaveta e conta como entrada do dia', () => {
  const dia = {
    data: '2026-10-12', aberto: true, saldoInicial: 100, totalEntradasExtras: 30, totalSaidasExtras: 0, totalPagamentosPedidos: 0,
    movimentos: [{ tipo: 'entrada', categoria: 'Venda no balcão', meio: 'dinheiro', valor: 30 }], linhasExtras: [],
  }
  assert.equal(caixa.gavetaDoDia(dia).naGaveta, 130)
})

// --- 5. Rótulo do que fica fora da gaveta ----------------------------------------------------

confere('5  o grupo fora da gaveta soma transferência, outro e vale, e o rótulo diz isso', () => {
  const dia = {
    saldoInicial: 0,
    movimentos: [{ tipo: 'entrada', meio: 'transferencia', valor: 10 }, { tipo: 'entrada', meio: 'outro', valor: 5 }],
    linhasExtras: [{ meio: 'pix', valor: 20 }, { meio: 'outro', valor: 7 }],
  }
  assert.equal(caixa.gavetaDoDia(dia).pixECartao, 42, 'a conta não muda')
  assert.equal(caixa.ROTULO_FORA_DA_GAVETA, 'Fora da gaveta (Pix, cartão e outros)')
  assert.equal(caixa.ROTULO_FORA_DO_DINHEIRO, 'Pix, cartão e outros')
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} ok, 0 falha(s)`)
