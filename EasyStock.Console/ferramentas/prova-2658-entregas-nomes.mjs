/* eslint-disable no-console */
// Prova da #2658 (homologação #1769, Entregas E2 e E3):
//   - E2: os botões de cada cartão do Painel do dia e dos Chamados nomeiam a entrega ou o chamado
//     (com dois pedidos na mesma janela, "Alterar agendamento" repetido não diz de quem é);
//   - E3: o aviso "Chamado aberto. Acompanhe ... na lista abaixo." só aparece com chamado aberto.
// Componentes reais, renderizados no Node (mesmo gancho de `prova-2227-lista-carregando.mjs`):
//
//   node ferramentas/prova-2658-entregas-nomes.mjs

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
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${erro.message.split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const { PedidoDoDia } = await import('../src/features/entregas/EntregasDoDia.jsx')
const { AvisoDoChamado, Chamados } = await import('../src/features/entregas/ConteudoEntregasApi.jsx')
const acoes = { confirmarPeloCliente() {}, desfazerConfirmacaoDoCliente() {}, incluirParada() {}, abrirChamado() {}, atenderChamado() {}, cancelarChamado() {} }
const agora = Date.parse('2026-11-15T10:00:00Z')
const pedido = (extra) => ({
  id: 'p1', numero: '001', cliente: 'Ana Souza', status: 'pronto', endereco: 'Rua A, 1',
  agendadoParaEm: '2026-11-17T17:00:00Z', podePorNaViagem: true, ...extra,
})
const cartao = (p) => renderToStaticMarkup(h('ul', null, h(PedidoDoDia, {
  pedido: p, agora, montando: [{ id: 'v1' }], ocupado: false, acoes, aoVerViagens() {}, aoReagendar() {},
})))
const botao = (html, texto) => html.match(new RegExp(`<button[^>]*>(?:(?!</button>).)*${texto}(?:(?!</button>).)*</button>`, 's'))?.[0] ?? ''
const nome = (html, texto) => botao(html, texto).match(/aria-label="([^"]+)"/)?.[1] ?? '(sem aria-label)'

await confere('E2: Alterar agendamento nomeia a cliente', () => {
  assert.equal(nome(cartao(pedido()), 'Alterar agendamento'), 'Alterar agendamento da entrega de Ana Souza')
})

await confere('E2: Cliente confirmou nomeia a cliente', () => {
  assert.equal(nome(cartao(pedido()), 'Cliente confirmou'), 'Cliente confirmou a entrega de Ana Souza')
})

await confere('E2: Desfazer confirmação nomeia a cliente', () => {
  const html = cartao(pedido({ confirmadoPeloClienteEm: '2026-11-15T09:00:00Z' }))
  assert.equal(nome(html, 'Desfazer confirmação'), 'Desfazer confirmação da entrega de Ana Souza')
})

await confere('E2: Pôr na viagem nomeia a cliente', () => {
  assert.equal(nome(cartao(pedido()), 'Pôr na viagem'), 'Pôr na viagem 1 a entrega de Ana Souza')
})

await confere('E2: Concluir e Cancelar chamado nomeiam o chamado', () => {
  const html = renderToStaticMarkup(h(Chamados, {
    chamados: [{ id: 'c1', texto: 'Buscar o pedido 12 na loja', abertoEm: '2026-11-15T10:00:00Z' }], ocupado: false, acoes,
  }))
  assert.equal(nome(html, 'Concluir chamado'), 'Concluir chamado: Buscar o pedido 12 na loja')
  assert.equal(nome(html, 'Cancelar chamado'), 'Cancelar chamado: Buscar o pedido 12 na loja')
})

await confere('E3: sem chamado aberto o aviso de "lista abaixo" não aparece', () => {
  const texto = 'Chamado aberto. Acompanhe o atendimento na lista abaixo.'
  assert.equal(renderToStaticMarkup(h(AvisoDoChamado, { texto, abertos: 0 })), '')
  assert.match(renderToStaticMarkup(h(AvisoDoChamado, { texto, abertos: 1 })), /lista abaixo/)
  assert.equal(renderToStaticMarkup(h(AvisoDoChamado, { texto: null, abertos: 1 })), '')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
