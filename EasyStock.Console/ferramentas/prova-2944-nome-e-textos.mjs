/* eslint-disable no-console */
// Prova da #2944 (E1-13, E1-14, E1-15, E1-16): nome por telefone, vazio da Ficha com ação, texto da escolha do
// cliente e rótulo honesto do estado da conversa.
//   node ferramentas/prova-2944-nome-e-textos.mjs
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

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const { nomeExibido, nomeDaConversa, clienteSemNome } = await import('../src/dominio/nomeDoContato.js')
const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { AUTOMATICO_DESLIGADO } = await import('../src/dominio/motivoDeEscalada.js')

// E1-13
assert.equal(nomeExibido({ nome: 'Cliente WhatsApp', telefone: '+5511988887777' }), '+55 (11) 98888-7777')
assert.equal(nomeExibido({ nome: '', telefone: '5511988887777' }), '+55 (11) 98888-7777')
assert.equal(nomeExibido({ nome: 'Cliente WhatsApp', telefone: null }), 'Contato sem nome')
assert.equal(nomeExibido({ nome: 'Maria Souza', telefone: '+5511988887777' }), 'Maria Souza')
assert.equal(clienteSemNome({ nome: 'Cliente WhatsApp', telefone: '+5511988887777' }), true)
assert.equal(clienteSemNome({ nome: 'Maria', telefone: '+5511988887777' }), false)
assert.equal(nomeDaConversa({ nome: 'Cliente WhatsApp', contato: '5511988887777' }), '+55 (11) 98888-7777')
assert.equal(nomeDaConversa({ nome: 'Cliente WhatsApp', contato: 'sem telefone' }), 'Cliente WhatsApp')
for (const arquivo of ['features/gestao/cadastros/PainelCadastros.jsx', 'features/gestao/cadastros/EscolherClienteDoPedido.jsx', 'features/gestao/cadastros/ResumoDoCliente.jsx']) {
  assert.match(fonte(arquivo), /nomeExibido\(/, `${arquivo} usa nomeExibido`)
}
assert.match(fonte('features/ficha-cliente/CartaoDoCliente.jsx'), /nomeDaConversa\(conversa\)/)

// E1-14
const historico = fonte('features/ficha-cliente/ModalHistorico.jsx')
assert.match(historico, /Use Novo pedido para começar/)
assert.match(historico, />Novo pedido<\/Botao>/)

// E1-15
assert.doesNotMatch(fonte('features/gestao/cadastros/EscolherClienteDoPedido.jsx'), /no Atendimento/)

// E1-16: o rótulo vale para o estado. Com o Assistente desligado, ele não responde.
const ligado = { id: 'c1', estado: 'Aberto', mensagens: [] }
assert.equal(resumoDoModo(ligado).rotulo, 'Assistente respondendo')
const desligado = { ...ligado, mensagens: [{ dir: 'sistema', texto: `Passou para você: ${AUTOMATICO_DESLIGADO}` }] }
assert.equal(resumoDoModo(desligado).rotulo, 'Conversa assumida por você')

console.log('prova-2944-nome-e-textos: ok')
