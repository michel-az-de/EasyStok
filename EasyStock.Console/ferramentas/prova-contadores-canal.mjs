/* eslint-disable no-console */
// Prova do contador por canal do Balcão (issue #2): importa a MESMA função que
// a tela usa (`contarPorCanal`, dominio/conversa.js) e confere a conta.
//
//   node ferramentas/prova-contadores-canal.mjs
//
// O domínio importa sem extensão (`./automatico`), como o Vite aceita. O
// gancho abaixo só repete essa resolução no Node, para a prova rodar o código
// real e não uma cópia.

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

const { contarPorCanal } = await import('../src/dominio/conversa.js')
const { CANAIS } = await import('../src/infra/catalogo.js')

const AGORA = new Date('2026-09-22T11:05:00-03:00')

// Conversa mínima. `passagem` não assumida é o motivo mais direto de
// "Precisa de você" no domínio (automatico.js, motivoDePrecisar).
let proximoId = 1
const conversa = (canal, { estado = 'Em atendimento', precisa = false, bloqueada = false } = {}) => ({
  id: `c${proximoId++}`,
  nome: `Cliente ${proximoId}`,
  canal,
  estado,
  ultimaEm: AGORA.toISOString(),
  mensagens: [],
  passagem: precisa ? { assumida: false, motivo: 'Teste' } : null,
  bloqueio: bloqueada ? { motivo: 'Teste', em: AGORA.toISOString(), por: 'Teste' } : null,
})

const porNome = (contagem) => Object.fromEntries(contagem.map((c) => [c.nome, { abertas: c.abertas, precisam: c.precisam }]))
const contar = (conversas) => porNome(contarPorCanal(conversas, CANAIS, AGORA, {}, true, []))

let casos = 0
function caso(titulo, verificar) {
  verificar()
  casos += 1
  console.log(`ok  ${titulo}`)
}

const base = [
  conversa('WhatsApp'),
  conversa('WhatsApp', { precisa: true }),
  conversa('Instagram'),
  conversa('Instagram', { estado: 'Encerrado', precisa: true }),
  conversa('Chat do site', { bloqueada: true, precisa: true }),
]

caso('um contador por canal do catálogo, na ordem do catálogo', () => {
  assert.deepEqual(contarPorCanal(base, CANAIS, AGORA).map((c) => c.nome), ['WhatsApp', 'Instagram', 'Chat do site'])
})

caso('conta abertas e, dentro delas, quantas precisam de você', () => {
  assert.deepEqual(contar(base), {
    WhatsApp: { abertas: 2, precisam: 1 },
    Instagram: { abertas: 1, precisam: 0 },
    'Chat do site': { abertas: 0, precisam: 0 },
  })
})

caso('encerrada não conta, nem em abertas nem em precisam', () => {
  const soEncerradas = [conversa('WhatsApp', { estado: 'Encerrado', precisa: true })]
  assert.deepEqual(contar(soEncerradas).WhatsApp, { abertas: 0, precisam: 0 })
})

caso('bloqueada não conta (vai para o grupo recolhido, fora da lista de cima)', () => {
  assert.equal(contar(base)['Chat do site'].abertas, 0)
})

caso('conversa nova de um canal soma só naquele canal', () => {
  const antes = contar(base)
  const depois = contar([...base, conversa('Instagram', { precisa: true })])
  assert.deepEqual(depois.Instagram, { abertas: antes.Instagram.abertas + 1, precisam: antes.Instagram.precisam + 1 })
  assert.deepEqual(depois.WhatsApp, antes.WhatsApp)
  assert.deepEqual(depois['Chat do site'], antes['Chat do site'])
})

caso('encerrar uma conversa baixa só o canal dela', () => {
  const alvo = base[1]
  const depois = contar(base.map((c) => (c === alvo ? { ...c, estado: 'Encerrado' } : c)))
  assert.deepEqual(depois.WhatsApp, { abertas: 1, precisam: 0 })
  assert.deepEqual(depois.Instagram, contar(base).Instagram)
})

caso('lista vazia dá zero em todo canal', () => {
  assert.ok(Object.values(contar([])).every((c) => c.abertas === 0 && c.precisam === 0))
})

console.log(`\nContador por canal: ${casos} casos, todos ok.`)
