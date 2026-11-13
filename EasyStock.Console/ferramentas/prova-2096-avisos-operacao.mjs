/* eslint-disable no-console */
// Prova da issue #2096 (parte do console): regras puras da aba Avisos, da confirmação da
// encomenda pelo cliente e da impressão automática do canhoto. Importa as MESMAS funções
// que as telas usam:
//
//   node ferramentas/prova-2096-avisos-operacao.mjs

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const {
  AVISOS, GRUPOS_DE_AVISOS, avisoLigado, avisosMudaram, corpoDosAvisos, ligarAviso, mudarPrazo, normalizarAvisos,
  textoDoSla, validarAvisos,
} = await import('../src/dominio/avisosDaOperacao.js')
const { confirmacaoDoCliente, roteiroDoDia } = await import('../src/dominio/roteiroDoDia.js')
const {
  atalhoDoChrome, contagemDoDia, pendentesParaImprimir, somarImpressa, textoDaContagem,
} = await import('../src/dominio/impressaoAutomatica.js')
const { normalizarPreferenciasImpressao, trocarCanhotoAutomatico, trocarPapelPadrao } = await import('../src/dominio/impressos.js')
const { lerContagemAutomatica, gravarContagemAutomatica, CHAVE_CONTAGEM_AUTOMATICA } = await import('../src/infra/preferenciasImpressao.js')
const { moduloPorId, telasDoMenu } = await import('../src/dominio/modulos.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const DA_API = {
  clienteSemResposta: true, pedidoAtrasado: true, impressaoTravada: false, caixaEsquecido: true,
  prontoSemEntregadorMinutos: 0, encomendaProximaHoras: 2, encomendaSemConfirmacaoHoras: 24,
  estoqueVencendoDias: 2, estoqueParadoDias: 0, reclamacaoCliente: true, suspeitaAtendimento: false,
  slaRespostaMinutos: 7,
}

confere('aba Avisos: cinco grupos na ordem pedida, onze avisos, só no modo API', () => {
  assert.deepEqual(GRUPOS_DE_AVISOS.map((g) => g.titulo), ['Atendimento', 'Pedidos e cozinha', 'Encomendas', 'Estoque', 'IA'])
  assert.equal(AVISOS.length, 11)
  const atendimento = moduloPorId('atendimento')
  assert.ok(telasDoMenu(atendimento, { fonteApi: true }).some((t) => t.id === 'avisos' && t.aba === 'avisos'))
  assert.ok(!telasDoMenu(atendimento, { fonteApi: false }).some((t) => t.id === 'avisos'))
})

confere('normaliza o que a API devolve e o SLA vira texto', () => {
  const a = normalizarAvisos(DA_API)
  assert.equal(a.slaRespostaMinutos, 7)
  assert.equal(a.prontoSemEntregadorMinutos, 0)
  assert.equal(normalizarAvisos({ impressaoTravada: 'sim', estoqueParadoDias: -3 }).impressaoTravada, false)
  assert.equal(normalizarAvisos({ estoqueParadoDias: -3 }).estoqueParadoDias, 0)
  assert.match(textoDoSla(7), /depois de 7 minutos sem resposta; ajuste na aba Atendimento/)
})

confere('desligar grava 0 e religar sugere o padrão de cada prazo', () => {
  let a = normalizarAvisos(DA_API)
  assert.equal(avisoLigado(a, 'prontoSemEntregadorMinutos'), false)
  const padroes = {
    prontoSemEntregadorMinutos: 15, encomendaProximaHoras: 2, encomendaSemConfirmacaoHoras: 24, estoqueVencendoDias: 2, estoqueParadoDias: 10,
  }
  for (const [campo, padrao] of Object.entries(padroes)) {
    a = ligarAviso(ligarAviso(a, campo, false), campo, true)
    assert.equal(a[campo], padrao, campo)
  }
  a = ligarAviso(a, 'estoqueParadoDias', false)
  assert.equal(a.estoqueParadoDias, 0)
  assert.equal(corpoDosAvisos(a).estoqueParadoDias, 0)
  assert.equal(ligarAviso(a, 'impressaoTravada', true).impressaoTravada, true)
})

confere('faixa de cada prazo: fora dela não salva e diz a faixa', () => {
  const base = normalizarAvisos(DA_API)
  const casos = [
    ['prontoSemEntregadorMinutos', 4, 5, 240, 241],
    ['encomendaProximaHoras', 0.5, 1, 48, 49],
    ['encomendaSemConfirmacaoHoras', '', 1, 72, 73],
    ['estoqueVencendoDias', 'abc', 1, 30, 31],
    ['estoqueParadoDias', 2, 3, 120, 121],
  ]
  for (const [campo, invalido, min, max, acima] of casos) {
    for (const valor of [invalido, acima]) {
      const erros = validarAvisos(mudarPrazo(base, campo, String(valor)))
      assert.ok(erros[campo], `${campo}=${valor} deveria barrar`)
      assert.match(erros[campo], new RegExp(`De ${min} a ${max} `))
    }
    for (const valor of [min, max]) assert.deepEqual(validarAvisos(mudarPrazo(base, campo, String(valor))), {}, `${campo}=${valor}`)
  }
  // Campo vazio continua ligado (não some da tela) e barra o salvar.
  assert.equal(avisoLigado(mudarPrazo(base, 'encomendaProximaHoras', ''), 'encomendaProximaHoras'), true)
})

confere('corpo do PUT: sem o SLA, prazos em número, mudança detectada', () => {
  const base = normalizarAvisos(DA_API)
  const editado = mudarPrazo(base, 'encomendaProximaHoras', '6')
  const corpo = corpoDosAvisos(editado)
  assert.equal('slaRespostaMinutos' in corpo, false)
  assert.equal(corpo.encomendaProximaHoras, 6)
  assert.deepEqual(Object.keys(corpo).sort(), AVISOS.map((a) => a.campo).sort())
  assert.equal(avisosMudaram(editado, base), true)
  assert.equal(avisosMudaram(mudarPrazo(base, 'encomendaProximaHoras', '2'), base), false)
})

const AGORA = Date.parse('2026-10-27T15:00:00-03:00')
confere('encomenda: botão enquanto vai acontecer, selo quando o cliente confirmou', () => {
  const futura = { status: 'aprovado_baba', agendadoParaEm: '2026-10-28T13:00:00Z', confirmadoPeloClienteEm: null }
  assert.equal(confirmacaoDoCliente(futura, AGORA), 'pendente')
  assert.equal(confirmacaoDoCliente({ ...futura, confirmadoPeloClienteEm: '2026-10-27T17:00:00Z' }, AGORA), 'confirmada')
  assert.equal(confirmacaoDoCliente({ ...futura, agendadoParaEm: '2026-10-27T17:00:00Z' }, AGORA), null, 'já passou')
  assert.equal(confirmacaoDoCliente({ ...futura, agendadoParaEm: null }, AGORA), null, 'para já')
  assert.equal(confirmacaoDoCliente({ ...futura, status: 'entregue' }, AGORA), null)
  assert.equal(confirmacaoDoCliente({ ...futura, status: 'cancelado', confirmadoPeloClienteEm: '2026-10-27T17:00:00Z' }, AGORA), null)
})

confere('roteiro do dia leva a confirmação e o horário para a linha do pedido', () => {
  const r = roteiroDoDia({
    data: '2026-10-28', viagens: [], entregadores: [], janelas: [], bloqueios: [],
    pedidos: [{ id: 'p1', numeroCurto: 'AB12CD34', status: 'aprovado_baba', agendadoParaEm: '2026-10-28T13:00:00Z', confirmadoPeloClienteEm: '2026-10-27T17:00:00Z', criadoEm: '2026-10-26T12:00:00Z' }],
  })
  const linha = r.grupos.flatMap((g) => g.pedidos)[0]
  assert.equal(linha.confirmadoPeloClienteEm, '2026-10-27T17:00:00Z')
  assert.equal(linha.agendadoParaEm, '2026-10-28T13:00:00Z')
  assert.equal(confirmacaoDoCliente(linha, AGORA), 'confirmada')
})

confere('impressão automática: só pendentes novas, uma vez, das mais antigas', () => {
  const lista = [
    { id: 'c', pedidoId: 'P3', status: 'pendente', criadaEm: '2026-10-27T18:05:00Z' },
    { id: 'a', pedidoId: 'P1', status: 'pendente', criadaEm: '2026-10-27T18:01:00Z' },
    { id: 'velha', pedidoId: 'P0', status: 'pendente', criadaEm: '2026-10-27T10:00:00Z' },
    { id: 'b', pedidoId: 'P2', status: 'impressa', criadaEm: '2026-10-27T18:02:00Z' },
  ]
  const desde = '2026-10-27T18:00:00.000Z'
  assert.deepEqual(pendentesParaImprimir(lista, { desde }).map((i) => i.id), ['a', 'c'])
  assert.deepEqual(pendentesParaImprimir(lista, { desde, tentadas: new Set(['a']) }).map((i) => i.id), ['c'])
  assert.deepEqual(pendentesParaImprimir(lista).map((i) => i.id), ['velha', 'a', 'c'])
  assert.deepEqual(pendentesParaImprimir(null), [])
})

confere('impressão automática: preferência por aparelho, trocar o papel não desliga', () => {
  const ligada = trocarCanhotoAutomatico(null, true, AGORA)
  assert.equal(ligada.canhotoAutomatico, true)
  assert.equal(ligada.canhotoAutomaticoDesde, new Date(AGORA).toISOString())
  // Religar não empurra o "desde": o que ficou na fila depois de ligar ainda sai.
  assert.equal(trocarCanhotoAutomatico(ligada, true, AGORA + 60000).canhotoAutomaticoDesde, ligada.canhotoAutomaticoDesde)
  const papel = trocarPapelPadrao(ligada, 'cozinha', 'cupom-58')
  assert.equal(papel.canhotoAutomatico, true)
  assert.equal(papel.papeis.cozinha, 'cupom-58')
  const desligada = trocarCanhotoAutomatico(papel, false, AGORA)
  assert.deepEqual([desligada.canhotoAutomatico, desligada.canhotoAutomaticoDesde], [false, null])
  assert.equal(normalizarPreferenciasImpressao({ canhotoAutomatico: 'sim' }).canhotoAutomatico, false)
})

confere('contagem do dia: soma, zera na troca de dia e sobrevive a storage que nega', () => {
  assert.deepEqual(somarImpressa(somarImpressa(null, '2026-10-27'), '2026-10-27'), { dia: '2026-10-27', total: 2 })
  assert.deepEqual(contagemDoDia({ dia: '2026-10-26', total: 9 }, '2026-10-27'), { dia: '2026-10-27', total: 0 })
  assert.equal(textoDaContagem(1), '1 canhoto hoje')
  assert.equal(textoDaContagem(3), '3 canhotos hoje')
  const dados = new Map()
  const armazenamento = { getItem: (k) => dados.get(k) ?? null, setItem: (k, v) => dados.set(k, String(v)) }
  gravarContagemAutomatica({ dia: '2026-10-27', total: 4 }, armazenamento)
  assert.equal(JSON.parse(dados.get(CHAVE_CONTAGEM_AUTOMATICA)).total, 4)
  assert.equal(lerContagemAutomatica('2026-10-27', armazenamento).total, 4)
  const nega = { getItem: () => { throw new Error('negado') }, setItem: () => { throw new Error('negado') } }
  gravarContagemAutomatica({ dia: '2026-10-27', total: 5 }, nega)
  assert.equal(lerContagemAutomatica('2026-10-27', nega).total, 5, 'vale a visita')
})

confere('atalho do Chrome com --kiosk-printing e o endereço do console', () => {
  assert.equal(
    atalhoDoChrome('https://console.exemplo/'),
    '"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe" --kiosk-printing --app=https://console.exemplo/',
  )
})

console.log(`\n${passou} verificações passaram.`)
