/* eslint-disable no-console */
// Prova da #1600 (sobra da #1544 no console):
//   1. apagar o custo do insumo manda `limparCusto: true` (null é "não mexe"); com valor, o corpo
//      é o de antes;
//   2. hora de parede e "início do dia" do domínio são os DA LOJA: janela de entrega, prazo da
//      rota, próxima abertura, separador de dia da conversa, "chegou há N dias", alcance por bairro
//      e dia do caixa dão o mesmo resultado com o aparelho em UTC, em São Paulo ou em Tóquio,
//      inclusive perto da meia-noite de Brasília (21:00 a 03:00 UTC);
//   3. conversa reaberta: a nota de reabertura é a última palavra, como na lista da API (#1570).
//      A entrada de antes do encerramento não volta como "sem resposta"; a que chega depois conta.
// Cada catraca foi provada por mutação (desfazer a correção reprova a linha correspondente).
//
//   node ferramentas/prova-1600-custo-fuso-reabertura.mjs

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
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }

const { criarGestaoInsumos } = await import('../src/aplicacao/insumos.js')
const formato = await import('../src/dominio/formato.js')
const entrega = await import('../src/dominio/entrega.js')
const viagem = await import('../src/dominio/viagem.js')
const ocorrencia = await import('../src/dominio/ocorrencia.js')
const funcionamento = await import('../src/dominio/funcionamento.js')
const cliente = await import('../src/dominio/cliente.js')
const alcance = await import('../src/dominio/alcance.js')
const caixa = await import('../src/dominio/caixa.js')
const { mensagensSemRespostaReal } = await import('../src/dominio/mensagem.js')
const { precisaDeVoce } = await import('../src/dominio/automatico.js')
const { pendentesDaLinha } = await import('../src/dominio/linhaDoBalcao.js')
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')

const RAIZ = new URL('../src/', import.meta.url)
// No Windows o checkout vem com CRLF; a prova lê como o CI (LF).
const ler = (caminho) => readFileSync(new URL(caminho, RAIZ), 'utf8').replace(/\r\n/g, '\n')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' ')}`)
  }
}

// --- 1. Apagar o custo do insumo ----------------------------------------------------------
function montarGestao() {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ?? null })
    return new Response(null, { status: 204 })
  }
  const erros = []
  const gestao = criarGestaoInsumos({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  const corpoDoPut = () => JSON.parse(chamadas.find((c) => c.metodo === 'PUT').corpo)
  return { gestao, corpoDoPut, erros }
}

await confere('custo apagado manda limparCusto; o mínimo informado segue igual', async () => {
  const m = montarGestao()
  assert.equal(await m.gestao.atualizar('p1', { minimo: '2500', custo: '' }), true)
  assert.deepEqual(m.corpoDoPut(), { minimo: 2500, custo: null, limparCusto: true })
  const soEspacos = montarGestao()
  await soEspacos.gestao.atualizar('p1', { minimo: '10', custo: '   ' })
  assert.deepEqual(soEspacos.corpoDoPut(), { minimo: 10, custo: null, limparCusto: true })
})
await confere('custo com valor (inclusive zero digitado) vai como antes, sem o campo novo', async () => {
  const m = montarGestao()
  await m.gestao.atualizar('p1', { minimo: '2500', custo: '0,05' })
  assert.deepEqual(m.corpoDoPut(), { minimo: 2500, custo: 0.05 })
  const zero = montarGestao()
  await zero.gestao.atualizar('p1', { minimo: '2500', custo: '0' })
  assert.deepEqual(zero.corpoDoPut(), { minimo: 2500, custo: 0 })
})
await confere('os dois apagados limpam os dois; nunca limpar e valor do mesmo campo juntos', async () => {
  const m = montarGestao()
  await m.gestao.atualizar('p1', { minimo: '', custo: '' })
  assert.deepEqual(m.corpoDoPut(), { minimo: null, custo: null, limparMinimo: true, limparCusto: true })
})
await confere('marcar embalagem não limpa o custo nem o mínimo', async () => {
  const m = montarGestao()
  await m.gestao.marcarEmbalagem('p1', true)
  assert.deepEqual(m.corpoDoPut(), { minimo: null, custo: null, embalagem: true })
})

// --- 2. Hora e dia são os da loja ---------------------------------------------------------
// O `process.env.TZ` troca o fuso do processo na hora. A primeira verificação prova que a troca
// vale: sem ela, as seguintes passariam sem olhar nada.
const FUSOS = ['UTC', 'America/Sao_Paulo', 'Asia/Tokyo']
const emCadaFuso = (fn) => FUSOS.map((fuso) => { process.env.TZ = fuso; return fn() })
const todos = (valor) => FUSOS.map(() => valor)
const fusoOriginal = process.env.TZ
const iso = (ms) => (ms == null ? null : new Date(ms).toISOString())

// A loja é UTC-3. Os instantes ficam em volta da meia-noite de Brasília, onde o dia do UTC e o de
// Tóquio já não são o da loja.
const MEIO_DIA = Date.parse('2026-10-08T15:00:00Z') //  12:00 de quinta, dia 8 (em Tóquio já é dia 9)
const NOITE = Date.parse('2026-10-09T01:30:00Z') //     22:30 de quinta, dia 8 (no UTC já é dia 9)
const VIRADA = Date.parse('2026-10-09T02:59:00Z') //    23:59 de quinta, dia 8
const MADRUGADA = Date.parse('2026-10-09T03:30:00Z') // 00:30 de sexta, dia 9

await confere('a troca de fuso do aparelho vale nesta prova (senão ela não mede nada)', () => {
  const doAparelho = emCadaFuso(() => new Date(2026, 9, 8, 9, 30).toISOString())
  assert.equal(new Set(doAparelho).size, FUSOS.length, `fuso do aparelho não mudou: ${doAparelho.join(' ')}`)
})

await confere('início do dia e hora de parede saem do relógio da loja', () => {
  assert.deepEqual(emCadaFuso(() => iso(formato.inicioDoDiaNoFuso(NOITE))), todos('2026-10-08T03:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(formato.inicioDoDiaNoFuso(VIRADA))), todos('2026-10-08T03:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(formato.inicioDoDiaNoFuso(MADRUGADA))), todos('2026-10-09T03:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(formato.noDiaDaLojaAs(NOITE, 11, 30))), todos('2026-10-08T14:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => formato.diasDeCalendarioEntre('2026-10-09T01:00:00Z', MADRUGADA)), todos(1))
  assert.deepEqual(emCadaFuso(() => formato.diasDeCalendarioEntre(MEIO_DIA, VIRADA)), todos(0))
})

await confere('janela de entrega: "11h30 às 12h30" é do dia da loja, às 11h30 da loja', () => {
  const faixa = '11h30 às 12h30'
  assert.deepEqual(emCadaFuso(() => iso(entrega.inicioDaFaixa(faixa, NOITE))), todos('2026-10-08T14:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(entrega.inicioDaFaixa(faixa, MEIO_DIA))), todos('2026-10-08T14:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(entrega.inicioDaFaixa(faixa, MADRUGADA))), todos('2026-10-09T14:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(entrega.inicioDaFaixa('18h às 19h', VIRADA))), todos('2026-10-08T21:00:00.000Z'))
  assert.equal(entrega.inicioDaFaixa('a combinar', NOITE), null)
})
await confere('texto da janela ao cliente respeita o respiro contando na hora da loja', () => {
  // 22:30 na loja, janela 22h45: com 40 min de respiro a promessa vira 23h10.
  assert.deepEqual(emCadaFuso(() => entrega.faixaParaCliente('22h45 às 23h45', NOITE)), todos('23h10 às 0h10'))
  // 12:00 na loja, janela das 18h: longe, sai a faixa crua.
  assert.deepEqual(emCadaFuso(() => entrega.faixaParaCliente('18h às 19h', MEIO_DIA)), todos('18h às 19h'))
  // 12:00 na loja, janela 12h30: dentro do respiro.
  assert.deepEqual(emCadaFuso(() => entrega.faixaParaCliente('12h30 às 13h30', MEIO_DIA)), todos('12h40 às 13h40'))
})
await confere('corte da janela fecha pela hora da loja', () => {
  const janela = { id: 'j1', faixa: '13h às 14h', capacidade: 5 }
  const estado = (agora) => entrega.inicioDoPedidoAtivo({ pedido: { estado: 'preparo', janela: 'j1' } }, [janela], agora)
  assert.deepEqual(emCadaFuso(() => iso(estado(MEIO_DIA))), todos('2026-10-08T16:00:00.000Z'))
})
await confere('prazo da rota: fim da janela e marcos no relógio da loja', () => {
  assert.deepEqual(emCadaFuso(() => iso(viagem.fimDaFaixa('11h30 às 12h30', NOITE))), todos('2026-10-08T15:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(viagem.fimDaFaixa('23h às 23h30', MADRUGADA))), todos('2026-10-10T02:30:00.000Z'))
  const constantes = { minutosTrecho: 10, minutosChegadaEntregador: 5, minutosConferir: 5 }
  const marcos = emCadaFuso(() => JSON.stringify(viagem.marcosDaViagem([{ id: 'p1', faixa: '11h30 às 12h30' }], NOITE, constantes)))
  assert.equal(new Set(marcos).size, 1, marcos.join(' | '))
})
await confere('entrega passou do prazo: decide pelo fim da janela na hora da loja', () => {
  const janelas = [{ id: 'j1', faixa: '11h30 às 12h30' }]
  const conversa = { pedido: { estado: 'entrega', janela: 'j1' } }
  // 22:30 na loja: a janela do almoço passou faz tempo.
  assert.deepEqual(emCadaFuso(() => ocorrencia.entregaPassouDoPrazo(conversa, janelas, NOITE)), todos(true))
  // 12:00 na loja: ainda dentro.
  assert.deepEqual(emCadaFuso(() => ocorrencia.entregaPassouDoPrazo(conversa, janelas, MEIO_DIA)), todos(false))
  // 12:31 na loja: passou por um minuto.
  const passouUmMinuto = Date.parse('2026-10-08T15:31:00Z')
  assert.deepEqual(emCadaFuso(() => ocorrencia.entregaPassouDoPrazo(conversa, janelas, passouUmMinuto)), todos(true))
})

await confere('próxima abertura: dia e hora da loja, coerente com dentroDoHorario', () => {
  const padrao = funcionamento.FUNCIONAMENTO_PADRAO // 08:00 às 22:00 todo dia
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(NOITE, padrao))), todos('2026-10-09T11:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(MADRUGADA, padrao))), todos('2026-10-09T11:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(MEIO_DIA, padrao))), todos('2026-10-09T11:00:00.000Z'))
  const cedo = Date.parse('2026-10-08T10:00:00Z') // 07:00 na loja
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(cedo, padrao))), todos('2026-10-08T11:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => funcionamento.dentroDoHorario(funcionamento.proximaAbertura(NOITE, padrao), padrao)), todos(true))
})
await confere('"hoje", "amanhã" e o dia da semana da abertura são os da loja', () => {
  const padrao = funcionamento.FUNCIONAMENTO_PADRAO
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(NOITE, padrao)), todos('amanhã às 08:00'))
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(VIRADA, padrao)), todos('amanhã às 08:00'))
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(MADRUGADA, padrao)), todos('hoje às 08:00'))
  const fechado = { fechado: true }
  const soEm = (chave, abre) => ({
    domingo: fechado, segunda: fechado, terca: fechado, quarta: fechado, quinta: fechado, sexta: fechado, sabado: fechado,
    [chave]: { abre, fecha: '22:00', fechado: false },
  })
  // Quinta 22:30 na loja. Só abre sexta: é amanhã (no UTC e em Tóquio "hoje" já seria sexta).
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(NOITE, soEm('sexta', '10:00'))), todos('amanhã às 10:00'))
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(NOITE, soEm('sexta', '10:00')))), todos('2026-10-09T13:00:00.000Z'))
  // Só abre sábado: depois de amanhã, pelo nome do dia.
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(NOITE, soEm('sabado', '10:00'))), todos('Sábado às 10:00'))
  // Sábado às 22h da loja já é domingo no UTC e em Tóquio: o nome do dia é o da loja.
  assert.deepEqual(emCadaFuso(() => funcionamento.descreverProximaAbertura(NOITE, soEm('sabado', '22:00'))), todos('Sábado às 22:00'))
  // Só abre quinta e já passou das 8h de quinta: a próxima é a quinta que vem.
  assert.deepEqual(emCadaFuso(() => iso(funcionamento.proximaAbertura(NOITE, soEm('quinta', '08:00')))), todos('2026-10-15T11:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => funcionamento.proximaAbertura(NOITE, soEm('quinta', null))), todos(null))
})

await confere('separador de dia da conversa: Hoje, Ontem e a data são os da loja', () => {
  // Mensagem das 22:00 de quinta lida à 00:30 de sexta.
  assert.deepEqual(emCadaFuso(() => formato.rotuloDoDia('2026-10-09T01:00:00Z', MADRUGADA)), todos('Ontem'))
  // Mensagem do meio-dia lida às 22:30 do mesmo dia.
  assert.deepEqual(emCadaFuso(() => formato.rotuloDoDia('2026-10-08T15:00:00Z', NOITE)), todos('Hoje'))
  // 23:00 do dia 4 na loja (no UTC já é dia 5).
  assert.deepEqual(emCadaFuso(() => formato.rotuloDoDia('2026-10-05T02:00:00Z', NOITE)), todos('04 de outubro'))
})
await confere('o fio quebra o dia no mesmo critério do rótulo', () => {
  const fio = ler('features/atendimento/Thread.jsx')
  assert.ok(fio.includes("import { inicioDoDiaNoFuso, rotuloDoDia } from '../../dominio/formato'"))
  assert.ok(fio.includes('const diaDaMensagem = inicioDoDiaNoFuso(m.em)'))
  assert.ok(fio.includes('if (i === 0 || diaDaMensagem !== diaAnterior) {'))
  assert.ok(!fio.includes('.getDate()'), 'o fio voltou a ler o dia do aparelho')
  assert.deepEqual(emCadaFuso(() => iso(formato.inicioDoDiaNoFuso('2026-10-09T01:00:00Z'))), todos('2026-10-08T03:00:00.000Z'))
})
await confere('"chegou hoje / ontem / há N dias" conta dias de calendário da loja', () => {
  assert.deepEqual(emCadaFuso(() => cliente.diasEntre('2026-10-09T01:00:00Z', MADRUGADA)), todos(1))
  assert.deepEqual(emCadaFuso(() => cliente.diasEntre('2026-10-08T15:00:00Z', NOITE)), todos(0))
  assert.deepEqual(emCadaFuso(() => cliente.diasEntre('2026-10-05T02:00:00Z', NOITE)), todos(4))
  const conversa = { canal: 'Instagram', mensagens: [{ em: '2026-10-09T01:00:00Z' }] }
  assert.deepEqual(emCadaFuso(() => cliente.chegouQuando(conversa, MADRUGADA)), todos('chegou ontem pelo Instagram'))
})
await confere('alcance por bairro: "hoje" e o período anterior são dias da loja', () => {
  const com = (em) => [{ id: 'c1', cliente: { endereco: 'Rua A, 10, Centro' }, historico: [{ numero: 'P-1', em, total: 50, estado: 'entregue' }] }]
  const hoje = (conversas, agora) => {
    const r = alcance.alcancePorBairro(conversas, { agora, dias: 1, cardapio: [] })
    return `${r.totalPedidos} hoje, ${r.bairros[0].anterior} antes`
  }
  // Pedido das 22:00 de quinta, olhado à 00:30 de sexta: é de ontem.
  assert.deepEqual(emCadaFuso(() => hoje(com('2026-10-09T01:00:00Z'), MADRUGADA)), todos('0 hoje, 1 antes'))
  // O mesmo pedido, olhado às 22:30 de quinta: é de hoje.
  assert.deepEqual(emCadaFuso(() => hoje(com('2026-10-09T01:00:00Z'), NOITE)), todos('1 hoje, 0 antes'))
  // Data sem hora ("2026-10-08") é o dia 8 da loja em qualquer aparelho.
  assert.deepEqual(emCadaFuso(() => hoje(com('2026-10-08'), NOITE)), todos('1 hoje, 0 antes'))
  assert.deepEqual(emCadaFuso(() => hoje(com('2026-10-08'), MADRUGADA)), todos('0 hoje, 1 antes'))
  assert.deepEqual(emCadaFuso(() => hoje(com('2026-10-07'), NOITE)), todos('0 hoje, 1 antes'))
  // Aparelho 14 h à frente do UTC: o meio-dia DELE do dia 8 ainda é dia 7 na loja.
  process.env.TZ = 'Pacific/Kiritimati'
  assert.equal(hoje(com('2026-10-08'), NOITE), '1 hoje, 0 antes')
})
await confere('dia do caixa (demonstração) é o dia da loja', () => {
  const movimentos = [{ id: 'm1', tipo: caixa.TIPOS_MOVIMENTO.ABERTURA, criadoEm: Date.parse('2026-10-09T01:00:00Z') }]
  assert.deepEqual(emCadaFuso(() => caixa.movimentosDoDia(movimentos, NOITE).length), todos(1))
  assert.deepEqual(emCadaFuso(() => caixa.movimentosDoDia(movimentos, VIRADA).length), todos(1))
  assert.deepEqual(emCadaFuso(() => caixa.movimentosDoDia(movimentos, MADRUGADA).length), todos(0))
  assert.deepEqual(emCadaFuso(() => caixa.caixaAberto(movimentos, MADRUGADA)), todos(false))
})
await confere('registro sem data não derruba a tela: dá NaN e fica fora do dia, como antes', () => {
  assert.ok(Number.isNaN(formato.inicioDoDiaNoFuso(undefined)))
  assert.ok(Number.isNaN(formato.noDiaDaLojaAs('não é data', 9, 30)))
  assert.equal(entrega.inicioDaFaixa('11h30 às 12h30', undefined), NaN)
  assert.equal(caixa.movimentosDoDia([{ id: 'm1', tipo: caixa.TIPOS_MOVIMENTO.ABERTURA }], NOITE).length, 0)
  assert.ok(Number.isNaN(cliente.diasEntre(undefined, NOITE)))
})
await confere('nenhum setHours no domínio fora do relógio do painel de simulações', () => {
  const comSetHours = ['entrega', 'funcionamento', 'ocorrencia', 'viagem', 'alcance', 'caixa', 'cliente', 'formato', 'simulacoes']
    .filter((nome) => ler(`dominio/${nome}.js`).split('\n').some((linha) => !linha.trim().startsWith('//') && linha.includes('.setHours(')))
  assert.deepEqual(comSetHours, ['simulacoes'])
})
if (fusoOriginal === undefined) delete process.env.TZ
else process.env.TZ = fusoOriginal

// --- 3. Conversa reaberta: a nota de reabertura é a última palavra ------------------------
const PREFIXO = 'Conversa reaberta por '
const AGORA = Date.parse('2026-10-10T15:00:00Z')
const entradaDaApi = (id, em, texto = 'Oi, tem lasanha?') => ({ id, direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto, status: 'Recebida', enviadaEm: em })
const notaDaApi = (id, em, texto, usuario = 'u-1') => ({
  id, direcao: 'Saida', autor: 'Sistema', tipoConteudo: 'Texto', texto, status: 'Pendente', externoId: null, enviadaPorUsuarioId: usuario, enviadaEm: em,
})
const reabertura = notaDaApi('n1', '2026-10-10T14:00:00', `${PREFIXO}Thatiane. Encerramento anterior: 08/10/2026 16:00:00 UTC.`)
const conversaCom = (...daApi) => ({ id: 'c1', estado: 'Em atendimento', canal: 'WhatsApp', mensagens: daApi.map((m) => mensagemDaApi(m, 'c1')) })

await confere('o prefixo é o mesmo que a API grava e que a lista usa (#1570)', () => {
  const caso = readFileSync(new URL('../../EasyStock.Application/UseCases/Atendimento/Inbox/ReabrirConversaUseCase.cs', import.meta.url), 'utf8')
  assert.ok(caso.includes(`public const string PrefixoNotaReabertura = "${PREFIXO}";`))
  assert.ok(ler('infra/api/traducaoConversas.js').includes(`'${PREFIXO}'`))
})
await confere('a nota de reabertura chega marcada; outra nota de sistema, não', () => {
  assert.deepEqual(mensagemDaApi(reabertura, 'c1'), { id: 'n1', dir: 'sistema', texto: reabertura.texto, em: '2026-10-10T14:00:00Z', reabertura: true })
  // Mesmo critério da API: nota de sistema, com autor humano e com o prefixo.
  assert.equal('reabertura' in mensagemDaApi(notaDaApi('n2', '2026-10-10T14:00:00', reabertura.texto, null), 'c1'), false)
  assert.equal('reabertura' in mensagemDaApi(notaDaApi('n3', '2026-10-10T14:00:00', 'Escalado para a dona: pergunta fora do roteiro.'), 'c1'), false)
  assert.equal('reabertura' in mensagemDaApi(notaDaApi('n4', '2026-10-10T14:00:00', `Aviso: ${PREFIXO}Thatiane.`), 'c1'), false)
  // Texto igual digitado pelo cliente é mensagem dele, não nota.
  assert.equal('reabertura' in mensagemDaApi(entradaDaApi('m9', '2026-10-10T14:00:00', `${PREFIXO}engano`), 'c1'), false)
})
await confere('reaberta sem entrada nova: nada de "sem resposta" nem de "esperando você"', () => {
  const conversa = conversaCom(entradaDaApi('m1', '2026-10-08T15:00:00'), reabertura)
  assert.deepEqual(mensagensSemRespostaReal(conversa), [])
  assert.equal(pendentesDaLinha(conversa), 0)
  assert.equal(precisaDeVoce(conversa, AGORA, true), false)
})
await confere('entrada que chega depois da reabertura conta, e só ela', () => {
  const conversa = conversaCom(
    entradaDaApi('m1', '2026-10-08T15:00:00'), reabertura, entradaDaApi('m2', '2026-10-10T14:30:00', 'Voltei, ainda dá tempo?'),
  )
  assert.deepEqual(mensagensSemRespostaReal(conversa).map((m) => m.id), ['m2'])
  assert.equal(pendentesDaLinha(conversa), 1)
  assert.equal(precisaDeVoce(conversa, AGORA, true), true)
})
await confere('nota de sistema comum continua transparente: não responde ao cliente', () => {
  const conversa = conversaCom(
    entradaDaApi('m1', '2026-10-10T14:30:00'), notaDaApi('n3', '2026-10-10T14:31:00', 'Escalado para a dona: pergunta fora do roteiro.'),
  )
  assert.deepEqual(mensagensSemRespostaReal(conversa).map((m) => m.id), ['m1'])
  assert.equal(precisaDeVoce(conversa, AGORA, true), true)
})
await confere('demonstração (mensagem sem a marca) segue como antes', () => {
  const conversa = { id: 'c2', estado: 'Em atendimento', mensagens: [
    { id: 'a', dir: 'in', texto: 'Oi', em: '2026-10-10T14:30:00Z' },
    { id: 'b', dir: 'sistema', texto: 'Cadastro criado', em: '2026-10-10T14:31:00Z' },
  ] }
  assert.deepEqual(mensagensSemRespostaReal(conversa).map((m) => m.id), ['a'])
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
