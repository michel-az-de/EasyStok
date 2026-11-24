/* eslint-disable no-console */
// Prova da #2810: os 12 Minor da revisão da PR #2806 (#2792), um caso por item da issue.
//
// - Limites da tela iguais aos do servidor: nome do prato 150 (CardapioItem.LimiteNome), peso da porção 50
//   (CardapioItemVariacao.NormalizarPeso) e placa 10 (Entregador.PlacaTamanhoMaximo).
// - Editor de prato, porções e observações da comanda no padrão AnuncioDoLimite: a região viva nasce montada
//   e diz uma frase estável; a contagem fica num parágrafo sem role.
// - Campo com `erro`: a frase do limite continua na tela e a dica não some quando passa do limite.
// - O catálogo de variáveis do modelo só zera quando a requisição não foi cancelada.
// - Anel sem horário com texto na tela (o aria-label num span sem role não é lido).
// - "Sem leitura" visível no Pulso do Hall e nenhum traço no número vazio do Painel Agora.
//
// Componentes reais renderizados no Node, mesmo gancho de `prova-2236-entrega-sem-janela.mjs`.
//
//   node ferramentas/prova-2810-minors.mjs

import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

let passou = 0
const falhas = []
const caso = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const fonte = (caminho) => readFileSync(join(raiz, caminho), 'utf8')
const semComentario = (texto) => texto.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
  .split(/\r?\n/).map((linha) => linha.replace(/(^|[^:'"`\\])\/\/.*$/, '$1')).join('\n')
// Número literal ou constante do mesmo arquivo (`const X = 150`).
const valorDe = (texto, simbolo) => /^\d+$/.test(simbolo) ? Number(simbolo) : Number(new RegExp(`const ${simbolo} = (\\d+)`).exec(texto)?.[1])
const semTags = (html) => html.replace(/<[^>]+>/g, '')
// Todo id citado no aria-describedby existe na página.
const descricoesExistem = (html) => {
  for (const [, lista] of html.matchAll(/aria-describedby="([^"]*)"/g)) {
    for (const id of lista.split(' ')) assert.ok(html.includes(`id="${id}"`), `aria-describedby aponta para "${id}", que não existe`)
  }
}

// --- 1. Limites iguais aos do servidor ---------------------------------------------------
await caso('1. Nome do prato com limite 150 no campo e na trava do Salvar', () => {
  const editor = fonte('features/cardapio/EditorItemCardapio.jsx')
  const doCampo = /rotulo="Nome do prato" limite=\{(\w+)\}/.exec(editor)?.[1]
  const daTrava = /algumPassaDoLimite\(\[nome, (\w+)\]/.exec(editor)?.[1]
  assert.ok(doCampo && daTrava, 'não achei o limite do Nome do prato')
  assert.equal(valorDe(editor, doCampo), 150, 'campo')
  assert.equal(valorDe(editor, daTrava), 150, 'trava do Salvar')
})

await caso('2. Peso da porção com limite 50', async () => {
  const { LIMITE_PESO, porcoesPassamDoLimite } = await import('../src/features/cardapio/limitesDaPorcao.js')
  assert.equal(LIMITE_PESO, 50)
  assert.equal(porcoesPassamDoLimite([{ rotulo: '', peso: 'x'.repeat(50) }]), false, '50 caracteres cabem')
  assert.equal(porcoesPassamDoLimite([{ rotulo: '', peso: 'x'.repeat(51) }]), true)
})

await caso('2. Placa do entregador com limite 10', () => {
  const escolha = fonte('componentes/EscolhaDeEntregador.jsx')
  assert.equal(valorDe(escolha, 'LIMITE_PLACA'), 10)
})

// --- 3. Padrão AnuncioDoLimite -----------------------------------------------------------
const { EditorPorcoes } = await import('../src/features/cardapio/EditorPorcoes.jsx')
const porcoes = (peso) => renderToStaticMarkup(h(EditorPorcoes, {
  linhas: [{ chave: 'a', id: null, rotulo: 'Individual', peso, preco: '10,00', disponivel: true, padrao: true, sku: null }],
  aoMudar: () => {},
}))

await caso('3. Porções: a região viva nasce montada, antes de passar do limite', () => {
  const html = porcoes('300 g')
  assert.equal((html.match(/role="status"/g) ?? []).length, 2, 'uma região por campo (nome e peso), mesmo sem passar')
})

await caso('3. Porções: passou do limite, a região diz a frase estável e a contagem fica sem role', () => {
  const html = porcoes('x'.repeat(55))
  assert.match(html, /role="status"[^>]*>Passou do limite de 50 caracteres\.</)
  const contagem = /<small[^>]*>Use até 50 caracteres\. Agora tem 55\.<\/small>/.exec(html)?.[0]
  assert.ok(contagem, 'sem a contagem na tela')
  assert.doesNotMatch(contagem, /role=/, 'a contagem muda a cada tecla: não pode ser região viva')
  descricoesExistem(html)
})

await caso('3. Editor de prato e observações da comanda usam AnuncioDoLimite', () => {
  const editor = semComentario(fonte('features/cardapio/EditorItemCardapio.jsx'))
  assert.match(editor, /<AnuncioDoLimite /, 'editor de prato')
  assert.doesNotMatch(editor, /role="status">\{passou\}/, 'editor de prato: a contagem ainda é região viva')
  const comanda = semComentario(fonte('features/ficha-cliente/ComandaDoCliente.jsx'))
  assert.equal((comanda.match(/<AnuncioDoLimite /g) ?? []).length, 2, 'observação do prato e do pedido')
})

// --- 4. Campo com erro e limite ----------------------------------------------------------
const { CampoTexto } = await import('../src/componentes/Campo.jsx')
await caso('4. Com erro no campo, a frase do limite continua na tela', () => {
  const html = renderToStaticMarkup(h(CampoTexto, { rotulo: 'Descrição', limite: 3, value: 'abcde', onChange() {}, erro: 'Escreva a descrição.' }))
  assert.match(html, /Escreva a descrição\./)
  assert.match(html, /Use até 3 caracteres\. Agora tem 5\./, 'o botão segue travado: a frase precisa aparecer')
  assert.match(html, /role="status"[^>]*>Passou do limite de 3 caracteres\.</)
  descricoesExistem(html)
})

await caso('4. Passou do limite, a dica continua na tela', () => {
  const html = renderToStaticMarkup(h(CampoTexto, { rotulo: 'Descrição', dica: 'O que aparece no extrato.', limite: 3, value: 'abcde', onChange() {} }))
  assert.match(html, /O que aparece no extrato\./)
  assert.match(html, /Use até 3 caracteres\. Agora tem 5\./)
  descricoesExistem(html)
})

// --- 5. Catálogo de variáveis ------------------------------------------------------------
await caso('5. Requisição cancelada não zera o catálogo de variáveis', () => {
  const modelos = semComentario(fonte('features/gestao/cadastros/ModelosCadastro.jsx'))
  const chamada = /variaveisDoModeloCadastro\([^\n]*/.exec(modelos)?.[0] ?? ''
  assert.match(chamada, /signal\.aborted/, 'a falha zera o catálogo sem olhar o sinal')
})

// --- 6. Anel sem horário -----------------------------------------------------------------
const { CartaoEntrega } = await import('../src/features/entregas/CartaoEntrega.jsx')
await caso('6. Cartão de entrega sem janela: o anel mostra texto, não um vazio', () => {
  const html = renderToStaticMarkup(h(CartaoEntrega, {
    conversa: { id: '200', nome: 'Cliente 200', cliente: { endereco: 'Rua Cerro Corá, 1.500' },
      pedido: { numero: '2026-0200', estado: 'aguardando', janela: null, entregador: null, itens: [{ sku: 'LAS-CLA', qtd: 1 }], pagamentos: [] } },
    grupoDaViagem: null, janelas: [], agora: Date.parse('2026-11-04T11:05:00-03:00'),
    constantes: { minutosTrecho: 20, minutosChegadaEntregador: 15, minutosConferir: 10 }, cardapio: [],
    viagensDisponiveis: [], acoes: {}, aoAbrirAgendamento: () => {}, opcoesDespacho: {},
  }))
  const centro = /<span class="centro">([\s\S]*?)<\/span><\/span>/.exec(html)?.[1] ?? /class="centro">([\s\S]*?)<\/div>/.exec(html)?.[1] ?? ''
  assert.match(semTags(centro), /Sem hora/, `centro do anel: "${semTags(centro)}"`)
})

await caso('6. Painel da viagem sem horário de saída: texto na tela e nada de aria-label em span sem role', () => {
  const painel = semComentario(fonte('features/entregas/PainelViagem.jsx'))
  const anel = /<Anel [\s\S]*?<\/Anel>/.exec(painel)?.[0] ?? ''
  assert.match(anel, /Sem hora/, 'o anel sem horário fica em branco na tela')
  for (const [span] of anel.matchAll(/<span[^>]*>/g)) {
    if (/aria-label=/.test(span)) assert.match(span, /role=/, `aria-label sem role não é lido: ${span}`)
  }
})

// --- 7 e 8. Pulso do Hall e Painel Agora -------------------------------------------------
await caso('7. "Sem leitura" visível no Pulso do Hall', () => {
  const pulso = semComentario(fonte('features/hall/PulsoDoHall.jsx'))
  assert.match(pulso, /Sem leitura/)
  assert.doesNotMatch(pulso, /className=\{css\.oculto\}>Sem leitura/, 'o texto só existe para o leitor de tela')
})

await caso('8. Painel Agora sem traço no número vazio', () => {
  const agora = semComentario(fonte('features/agora/PainelAgora.jsx'))
  assert.doesNotMatch(agora, /[–—]/)
})

console.log(`\n${passou} ok, ${falhas.length} falharam`)
if (falhas.length) process.exit(1)
