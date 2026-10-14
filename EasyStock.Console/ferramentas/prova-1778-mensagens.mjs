/* eslint-disable no-console */
// Regressões da banca autenticada: botão de avaliação sem texto e data UTC na nota interna.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolve original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { notaDeReabertura } = await import('../src/dominio/motivoDeEscalada.js')
const PEDIDO = '00000000-0000-4000-8000-000000000001'
const entrada = { id: 'm1', direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Botao', texto: null, enviadaEm: '2026-10-13T14:05:28Z' }
const textoUtc = 'Conversa reaberta por Atendente. Encerramento anterior: 14/10/2026 01:30:28 UTC.'
const nota = { ...entrada, direcao: 'Saida', autor: 'Sistema', tipoConteudo: 'Texto', status: 'Pendente', texto: textoUtc, enviadaPorUsuarioId: 'u1' }
let passou = 0
const confere = (nome, fn) => { fn(); passou += 1; console.log(`ok ${nome}`) }

confere('avaliações positiva e negativa usam a bolha existente e conservam o horário', () => {
  for (const valor of ['positiva', 'negativa']) {
    const m = mensagemDaApi({ ...entrada, botaoId: `acao:avaliacao:${valor}:${PEDIDO}` })
    assert.equal(m.formato, 'avaliacaoResposta')
    assert.equal(m.valor, valor)
    assert.equal(m.texto, `Avaliação ${valor}`)
    assert.equal(m.em, entrada.enviadaEm)
  }
})
confere('botão desconhecido, incompleto ou com payload inválido não inventa avaliação', () => {
  for (const botaoId of [null, 'acao:pedido:confirmar', `acao:avaliacao:neutra:${PEDIDO}`, 'acao:avaliacao:positiva:invalido', `acao:avaliacao:negativa:${PEDIDO}:extra`]) {
    const m = mensagemDaApi({ ...entrada, botaoId })
    assert.equal(m.texto, 'Resposta a um botão')
    assert.equal(m.formato, undefined)
  }
})
confere('entrada textual e saída não viram avaliação só por conterem um id', () => {
  const botaoId = `acao:avaliacao:positiva:${PEDIDO}`
  for (const m of [{ ...entrada, tipoConteudo: 'Texto', texto: ' Gostei! ', botaoId }, { ...entrada, direcao: 'Saida', texto: 'Como foi?', botaoId }]) {
    const traduzida = mensagemDaApi(m)
    assert.equal(traduzida.texto, m.texto)
    assert.equal(traduzida.formato, undefined)
  }
})
confere('texto ausente e conteúdo não reconhecido têm fallback visível sem adivinhar o conteúdo', () => {
  for (const tipoConteudo of ['Texto', 'Outro', 'Futuro', null]) {
    for (const texto of [null, '', ' \n ']) {
      assert.equal(mensagemDaApi({ ...entrada, tipoConteudo, texto }).texto, 'Conteúdo não disponível nesta mensagem.')
    }
  }
})
confere('mídia e transcrição continuam encaminhadas ao renderizador existente', () => {
  const m = mensagemDaApi({ ...entrada, tipoConteudo: 'Audio', midiaChave: 'audio', midiaMime: 'audio/ogg', transcricao: 'Oi!' }, 'c1')
  assert.equal(m.texto, 'Áudio')
  assert.deepEqual(m.midia, { conversaId: 'c1', mensagemId: 'm1', mime: 'audio/ogg' })
  assert.equal(m.transcricao, 'Oi!')
})
confere('nota interna usa data e hora da loja, inclusive virada do dia, em qualquer fuso do aparelho', () => {
  const antes = structuredClone(nota)
  const fusoOriginal = process.env.TZ
  try {
    for (const fuso of ['UTC', 'America/Sao_Paulo', 'Asia/Tokyo']) {
      process.env.TZ = fuso
      const m = mensagemDaApi(nota)
      assert.equal(m.texto, 'Conversa reaberta por Atendente. Encerramento anterior: 13/10/2026, 22:30 (horário da loja).')
      assert.equal(m.dir, 'sistema')
      assert.equal(m.reabertura, true)
    }
  } finally {
    if (fusoOriginal === undefined) delete process.env.TZ
    else process.env.TZ = fusoOriginal
  }
  assert.deepEqual(nota, antes)
})
confere('mesmo texto enviado pelo cliente ou sem autoria interna não é reescrito', () => {
  assert.equal(mensagemDaApi({ ...entrada, tipoConteudo: 'Texto', texto: textoUtc }).texto, textoUtc)
  assert.equal(mensagemDaApi({ ...nota, enviadaPorUsuarioId: null }).texto, textoUtc)
  assert.equal(mensagemDaApi({ ...nota, autor: 'Humano' }).texto, textoUtc)
})
confere('nota inválida ou fora do contrato conserva o histórico literal', () => {
  for (const texto of [textoUtc.replace('14/10/2026', '31/02/2026'), textoUtc.replace('01:30:28', '24:30:28'), textoUtc.replace('UTC.', 'UTC. comentário'), textoUtc.replace('UTC.', 'GMT.'), 'Outro evento 14/10/2026 01:30:28 UTC.']) {
    assert.equal(notaDeReabertura(texto), texto)
  }
})

console.log(`${passou} verificações passaram.`)
