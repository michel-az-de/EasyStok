/* eslint-disable no-console */
// Prova da #2185: uma função só diz o que cada estado da conversa oferece.
//   1. matriz estado x ação: Assumir, Devolver, Concluir, Enviar cardápio e Passar conversa;
//   2. Assistente respondendo nunca oferece Devolver;
//   3. canal sem Assistente (chat do site, e-mail, Instagram) nunca oferece Devolver;
//   4. conversa de outro atendente oferece Assumir com confirmação e não oferece Devolver;
//   5. fila sem responsável oferece Assumir, mesmo depois de reabrir pedido entregue;
//   6. o balão do Assistente só propõe devolver onde a barra oferece, com o mesmo nome;
//   7. canal sem Assistente não diz que o Assistente "não soube responder".
//
//   node ferramentas/prova-2185-acoes-da-conversa.mjs

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
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true,
      source: readFileSync(new URL(url), 'utf8').replaceAll('import.meta.env', '({ VITE_FONTE_DADOS: "demonstracao" })') }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { acoesDaConversa } = await import('../src/dominio/acoesDaConversa.js')
const { PAUSA_POR_ASSUMIR, PAUSA_POR_OUTRO } = await import('../src/dominio/automatico.js')
const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { situacaoConversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { acoesPropostasVisiveis, descreverAcaoDoAssistente, ACOES_DO_ASSISTENTE } = await import('../src/dominio/acoesDoAssistente.js')
const { CANAIS } = await import('../src/infra/catalogo.js')

const canalDe = (nome) => CANAIS.find((c) => c.nome === nome)
const WHATSAPP = canalDe('WhatsApp')
const SITE = canalDe('Chat do site')
const EMAIL = canalDe('E-mail')
const INSTAGRAM = canalDe('Instagram')

const conversa = (extra = {}) => ({ id: 'c1', nome: 'Maria Souza', estado: 'Aberto', mensagens: [], ...extra })
const FILA = { motivo: 'Restrição alimentar', em: null, assumida: false }
const MOTIVO_DESLIGADO = (await import('../src/dominio/motivoDeEscalada.js')).AUTOMATICO_DESLIGADO

// Chave: [assumir, confirmar ao assumir, devolver, concluir, enviar cardápio, passar]
const MATRIZ = [
  ['Assistente respondendo', { conversa: conversa(), canal: WHATSAPP }, [1, 0, 0, 1, 1, 1]],
  ['você assumiu', { conversa: conversa(), canal: WHATSAPP, pausado: PAUSA_POR_ASSUMIR }, [0, 0, 1, 1, 1, 1]],
  ['você escreveu', { conversa: conversa(), canal: WHATSAPP, pausado: true }, [0, 0, 1, 1, 1, 1]],
  ['outro atendente', { conversa: conversa({ responsavel: 'Ana' }), canal: WHATSAPP, pausado: PAUSA_POR_OUTRO }, [1, 1, 0, 1, 1, 1]],
  ['passou para você', { conversa: conversa({ passagem: FILA }), canal: WHATSAPP, pausado: true }, [1, 0, 1, 1, 1, 1]],
  ['chat do site na fila', { conversa: conversa({ passagem: { ...FILA, origem: 'chat-site' } }), canal: SITE, pausado: true }, [1, 0, 0, 1, 1, 1]],
  ['chat do site assumido', { conversa: conversa(), canal: SITE, pausado: PAUSA_POR_ASSUMIR }, [0, 0, 0, 1, 1, 1]],
  ['e-mail na fila', { conversa: conversa({ passagem: FILA }), canal: EMAIL, pausado: true }, [1, 0, 0, 1, 1, 1]],
  ['Instagram assumido', { conversa: conversa(), canal: INSTAGRAM, pausado: PAUSA_POR_ASSUMIR }, [0, 0, 0, 1, 1, 1]],
  ['Assistente desligado na loja', { conversa: conversa({ passagem: { ...FILA, motivo: MOTIVO_DESLIGADO } }), canal: WHATSAPP, pausado: true }, [1, 0, 0, 1, 1, 1]],
  ['concluída', { conversa: conversa({ estado: 'Encerrado' }), canal: WHATSAPP }, [0, 0, 0, 0, 0, 0]],
  ['bloqueada', { conversa: conversa(), canal: WHATSAPP, pausado: true, bloqueada: true }, [0, 0, 0, 1, 0, 0]],
]

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('  ok  ', descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.error('  FALHA', descricao, '\n       ', erro.message)
  }
}

for (const [estado, entrada, esperado] of MATRIZ) {
  await confere(`matriz: ${estado}`, () => {
    const a = acoesDaConversa({ permitePassar: true, ...entrada })
    assert.deepEqual(
      [a.assumir, a.confirmarAoAssumir, a.devolver, a.concluir, a.enviarCardapio, a.passar].map(Number),
      esperado,
    )
  })
}

await confere('passar conversa depende da permissão, em qualquer estado aberto', () => {
  assert.equal(acoesDaConversa({ conversa: conversa(), canal: WHATSAPP, permitePassar: false }).passar, false)
})

await confere('fila sem responsável depois de reabrir pedido entregue oferece Assumir', () => {
  const reaberta = conversa({
    situacaoApi: 'Assumida', assumidaPorUsuarioId: null, pausaApi: true,
    passagem: { motivo: 'Conversa reaberta', origem: 'reabertura', assumida: false },
    pedido: { estado: 'entregue', status: 'entregue' },
    mensagens: [{ id: 'm1', dir: 'out', texto: 'Obrigada', em: '2026-11-01T10:00:00Z' }],
  })
  assert.equal(acoesDaConversa({ conversa: reaberta, canal: WHATSAPP, pausado: true }).assumir, true)
  assert.notEqual(resumoDoModo(reaberta, true).rotulo, 'Você respondendo')
})

await confere('conversa sem responsável na fila diz Aguardando equipe, e não Passou para você, no chat do site', () => {
  const c = conversa({ passagem: { ...FILA, origem: 'chat-site', motivo: 'Visitante do site aguardando atendimento da equipe.' } })
  assert.equal(resumoDoModo(c, true).rotulo, 'Aguardando equipe')
})

await confere('e-mail e Instagram na fila não dizem que o Assistente não soube responder', () => {
  for (const canal of ['Email', 'Instagram', 'Messenger', 'Sms']) {
    const { passagem } = situacaoConversaDaApi({ situacao: 'Assumida', canal, assumidaPorUsuarioId: null, motivoEscalada: null, naoLidas: 1 }, { id: 'u1', nome: 'Eu' })
    const origem = origemDaPassagem(passagem)
    assert.doesNotMatch(`${origem.curto} ${origem.longo} ${origem.motivo}`, /não soube|preferiu/i, canal)
    assert.equal(resumoDoModo(conversa({ passagem }), true).rotulo, 'Aguardando equipe', canal)
  }
})

await confere('WhatsApp escalado continua dizendo por que passou', () => {
  const { passagem } = situacaoConversaDaApi({ situacao: 'Assumida', canal: 'WhatsApp', assumidaPorUsuarioId: null, motivoEscalada: 'Restrição alimentar', naoLidas: 1 }, { id: 'u1', nome: 'Eu' })
  assert.equal(resumoDoModo(conversa({ passagem }), true).rotulo, 'Passou para você')
  assert.match(origemDaPassagem(passagem).curto, /Assistente/)
})

const propostas = [{ tipo: ACOES_DO_ASSISTENTE.RETOMAR_AUTOMATICO, chave: 'r' }, { tipo: ACOES_DO_ASSISTENTE.NOTA_INTERNA, chave: 'n', texto: 'x' }]
await confere('balão: Assistente respondendo não propõe devolver', () => {
  const a = acoesDaConversa({ conversa: conversa(), canal: WHATSAPP })
  assert.deepEqual(acoesPropostasVisiveis(propostas, a).map((p) => p.chave), ['n'])
})
await confere('balão: canal sem Assistente não propõe devolver', () => {
  const a = acoesDaConversa({ conversa: conversa(), canal: SITE, pausado: PAUSA_POR_ASSUMIR })
  assert.deepEqual(acoesPropostasVisiveis(propostas, a).map((p) => p.chave), ['n'])
})
await confere('balão: onde a barra oferece, propõe com o mesmo nome', () => {
  const a = acoesDaConversa({ conversa: conversa(), canal: WHATSAPP, pausado: PAUSA_POR_ASSUMIR })
  assert.deepEqual(acoesPropostasVisiveis(propostas, a).map((p) => p.chave), ['r', 'n'])
  assert.equal(descreverAcaoDoAssistente(propostas[0], 'Maria Souza').botao, 'Devolver ao Assistente')
})
await confere('balão: proposta já executada continua à vista depois que o estado muda', () => {
  const a = acoesDaConversa({ conversa: conversa(), canal: WHATSAPP })
  const feita = { ...propostas[0], estado: 'feita' }
  assert.deepEqual(acoesPropostasVisiveis([feita], a).map((p) => p.chave), ['r'])
})

console.log(`\n${passou} verificações ok, ${falhas.length} falha(s).`)
if (falhas.length) process.exit(1)
