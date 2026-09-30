/* eslint-disable no-console */
// Roda a massa de conversas contra o agente e dá nota.
//
//   node ferramentas/avaliar-agente.mjs --modo simulado            (grátis, sem servidor)
//   node ferramentas/avaliar-agente.mjs --modo cli --so c4,c10,c22  (precisa de npm run agente)
//   opções: --paralelo 3   --servidor http://127.0.0.1:5245
//
// Nota por conversa: a ação obtida bate com a esperada? Alguma frase proibida
// saiu no texto? Quantos itens de "deveConter" apareceram? O relatório vai para
// dados/avaliacoes/<modo>-<data>.json e um resumo sai no console.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  acaoSugerida, classificarIntencao, interpretarResposta, montarPrompt, rascunhoSugerido,
} from '../src/dominio/agente.js'
import { CARDAPIO, JANELAS_ENTREGA, PREFIXOS_CEP_ATENDIDOS } from '../src/infra/catalogo.js'

const RAIZ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

function lerArgumentos(lista) {
  const opcoes = { modo: 'simulado', so: null, paralelo: 3, servidor: 'http://127.0.0.1:5245' }
  for (let i = 0; i < lista.length; i += 1) {
    const chave = lista[i].replace(/^--/, '')
    if (!(chave in opcoes)) continue
    const valor = lista[i + 1]
    i += 1
    if (chave === 'so') opcoes.so = valor.split(',').map((s) => s.trim())
    else if (chave === 'paralelo') opcoes.paralelo = Number(valor)
    else opcoes[chave] = valor
  }
  return opcoes
}

const normalizar = (texto) => String(texto ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')

async function consultar(modo, conversa, catalogo, servidor) {
  const leitura = classificarIntencao(conversa)
  if (modo === 'simulado') {
    return {
      acao: acaoSugerida(leitura, conversa, catalogo),
      texto: rascunhoSugerido(leitura, conversa, catalogo),
      intencao: leitura.intencao.chave,
      tokens: null, custoUsd: null, latenciaMs: 0, estruturada: true,
    }
  }
  const prompt = montarPrompt(conversa, catalogo)
  const resposta = await fetch(servidor + '/api/agente', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ prompt, transporte: modo }),
  })
  const corpo = await resposta.json()
  if (!resposta.ok) throw new Error(corpo.erro ?? `servidor respondeu ${resposta.status}`)
  const lida = interpretarResposta(corpo.texto)
  return {
    acao: lida.acao, texto: lida.texto, intencao: leitura.intencao.chave, estruturada: lida.estruturada,
    tokens: corpo.tokens ?? null, custoUsd: corpo.custoUsd ?? null, latenciaMs: corpo.latenciaMs ?? null,
  }
}

function avaliar(conversa, obtido) {
  const { esperado } = conversa
  const texto = normalizar(obtido.texto)
  const acaoOk = esperado.acao === 'nada' ? null : obtido.acao === esperado.acao
  const proibidosQueSairam = (esperado.proibido ?? []).filter((p) => texto.includes(normalizar(p)))
  const deveConter = esperado.deveConter ?? []
  const contidos = deveConter.filter((d) => texto.includes(normalizar(d)))
  return { acaoOk, proibidosQueSairam, contidos: contidos.length, deveConter: deveConter.length }
}

async function emLotes(itens, tamanho, fn) {
  const resultados = []
  for (let i = 0; i < itens.length; i += tamanho) {
    const lote = itens.slice(i, i + tamanho)
    resultados.push(...await Promise.all(lote.map(fn)))
  }
  return resultados
}

const opcoes = lerArgumentos(process.argv.slice(2))
const massa = JSON.parse(readFileSync(path.join(RAIZ, 'dados', 'massa-conversas.json'), 'utf8'))
const catalogo = { cardapio: CARDAPIO, janelas: JANELAS_ENTREGA, prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS }
const conversas = massa.conversas.filter((c) => !opcoes.so || opcoes.so.includes(c.id))

console.log(`modo ${opcoes.modo} · ${conversas.length} conversas · paralelo ${opcoes.paralelo}`)

const linhas = await emLotes(conversas, opcoes.modo === 'simulado' ? 30 : opcoes.paralelo, async (conversa) => {
  try {
    const obtido = await consultar(opcoes.modo, conversa, catalogo, opcoes.servidor)
    const nota = avaliar(conversa, obtido)
    const marca = nota.acaoOk === null ? ' · ' : nota.acaoOk ? ' ok' : 'ERR'
    const proib = nota.proibidosQueSairam.length ? ` PROIBIDO: ${nota.proibidosQueSairam.join(' | ')}` : ''
    console.log(`${marca} ${conversa.id.padEnd(4)} ${conversa.esperado.cenario.slice(0, 44).padEnd(44)} `
      + `esp ${conversa.esperado.acao.padEnd(16)} obt ${obtido.acao.padEnd(16)}${proib}`)
    return { id: conversa.id, cenario: conversa.esperado.cenario, esperado: conversa.esperado.acao, obtido, nota }
  } catch (erro) {
    console.log(`ERR ${conversa.id.padEnd(4)} falhou: ${erro.message}`)
    return { id: conversa.id, cenario: conversa.esperado.cenario, esperado: conversa.esperado.acao, erro: erro.message }
  }
})

const comNota = linhas.filter((l) => l.nota && l.nota.acaoOk !== null)
const acertos = comNota.filter((l) => l.nota.acaoOk).length
const violacoes = linhas.filter((l) => l.nota && l.nota.proibidosQueSairam.length > 0).length
const falhas = linhas.filter((l) => l.erro).length
const custo = linhas.reduce((s, l) => s + (l.obtido?.custoUsd ?? 0), 0)
const tokens = linhas.reduce((s, l) => s + (l.obtido?.tokens ?? 0), 0)
const latencias = linhas.map((l) => l.obtido?.latenciaMs).filter((v) => v != null)
const latenciaMedia = latencias.length ? Math.round(latencias.reduce((s, v) => s + v, 0) / latencias.length) : 0

const resumo = {
  modo: opcoes.modo, em: new Date().toISOString(), conversas: linhas.length,
  acaoAcertos: acertos, acaoAvaliadas: comNota.length, violacoesProibido: violacoes, falhas,
  custoUsd: Number(custo.toFixed(4)), tokens, latenciaMediaMs: latenciaMedia,
}
console.log('')
console.log(`ação certa: ${acertos} de ${comNota.length} · proibido saiu em ${violacoes} · falhas ${falhas}`
  + (opcoes.modo === 'simulado' ? '' : ` · US$ ${custo.toFixed(4)} · ${tokens} tokens · ${latenciaMedia} ms por resposta`))

const pasta = path.join(RAIZ, 'dados', 'avaliacoes')
mkdirSync(pasta, { recursive: true })
const arquivo = path.join(pasta, `${opcoes.modo}-${resumo.em.replace(/[:.]/g, '-')}.json`)
writeFileSync(arquivo, JSON.stringify({ resumo, linhas }, null, 2))
console.log('relatório:', path.relative(RAIZ, arquivo))
