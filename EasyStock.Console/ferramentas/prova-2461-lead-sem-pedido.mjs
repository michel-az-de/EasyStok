// #2461: quem tem cadastro mas nunca fez pedido aparece como Lead na conversa.
//   `relacionamento` ('sem-cadastro' | 'lead' | 'cliente') vem do `temPedido` da API; `conta` não muda de sentido.
//   Selo "Lead" no cartão da lista e "Cadastrado, ainda sem pedido" no cartão do cliente.
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

const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { aplicarVinculoConfirmado, preservarVinculoConfirmado } = await import('../src/dominio/vinculoCliente.js')
const { mesclarDoServidor } = await import('../src/aplicacao/reducer/mesclaDoServidor.js')
const { ehLead } = await import('../src/dominio/conversa.js')
const { marcaDoPedido } = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const { CartaoDoCliente } = await import('../src/features/ficha-cliente/CartaoDoCliente.jsx')
const { ContextoCatalogo } = await import('../src/aplicacao/contextos.js')

let total = 0
function provar(nome, fn) { fn(); total++; console.log('ok ' + nome) }

const resumo = { id: 'c1', contatoNome: 'Pessoa de teste', contatoIdExterno: '5511900000000', canal: 'WhatsApp', situacao: 'Aberta' }
const daApi = (campos) => conversaDaApi({ ...resumo, ...campos }, [], { id: 'dona' })

const semCadastro = daApi({ clienteId: null, temPedido: null })
const lead = daApi({ clienteId: 'cad-1', temPedido: false, vinculoRevisao: 1 })
const cliente = daApi({ clienteId: 'cad-2', temPedido: true, vinculoRevisao: 1 })
// D1: a API já devolve `true` para quem só tem pedido cancelado; a tela não distingue.
const soCancelado = daApi({ clienteId: 'cad-3', temPedido: true, vinculoRevisao: 1 })

provar('a lista traduz os três estados a partir do temPedido da API', () => {
  assert.equal(semCadastro.relacionamento, 'sem-cadastro')
  assert.equal(lead.relacionamento, 'lead')
  assert.equal(cliente.relacionamento, 'cliente')
  assert.equal(soCancelado.relacionamento, 'cliente')
})

provar('API antiga (sem temPedido) cai no comportamento de hoje', () => {
  assert.equal(daApi({ clienteId: 'cad-1' }).relacionamento, 'cliente')
  assert.equal(daApi({ clienteId: 'cad-1', temPedido: null }).relacionamento, 'cliente')
  assert.equal(daApi({ clienteId: null }).relacionamento, 'sem-cadastro')
  assert.equal(daApi({}).relacionamento, 'sem-cadastro')
})

provar('conta não muda de sentido: lead com cadastro segue fora da captura e da conversão no Pix', () => {
  assert.equal(semCadastro.conta, 'lead')
  assert.equal(lead.conta, 'cliente')
  assert.equal(cliente.conta, 'cliente')
  assert.equal(ehLead(lead), false)
  assert.equal(ehLead(semCadastro), true)
})

provar('selo do cartão da lista: Lead, Contato ou nenhum', () => {
  assert.equal(marcaDoPedido(lead, [], { fonteApi: true })?.texto, 'Lead')
  assert.equal(marcaDoPedido(semCadastro, [], { fonteApi: true })?.texto, 'Contato')
  assert.equal(marcaDoPedido(cliente, [], { fonteApi: true }), null)
  assert.equal(marcaDoPedido(soCancelado, [], { fonteApi: true }), null)
  // Conversa de demonstração ou lida antes do campo existir: sem `relacionamento`, nada de selo novo.
  assert.equal(marcaDoPedido({ conta: 'cliente', clienteId: 'x', pedido: null }, []), null)
  assert.equal(marcaDoPedido({ conta: 'lead', pedido: null }, [])?.texto, 'Contato')
})

provar('correção do vínculo já traz o relacionamento do cadastro novo, sem esperar a polling', () => {
  const vinculada = aplicarVinculoConfirmado(semCadastro, { clienteId: 'cad-1', clienteNome: 'Pessoa de teste', temPedido: false, vinculoRevisao: 1 })
  assert.equal(vinculada.relacionamento, 'lead')
  assert.equal(vinculada.conta, 'cliente')
  assert.equal(marcaDoPedido(vinculada, [], { fonteApi: true })?.texto, 'Lead')
  assert.equal(aplicarVinculoConfirmado(semCadastro, { clienteId: 'cad-2', temPedido: true, vinculoRevisao: 1 }).relacionamento, 'cliente')
  assert.equal(aplicarVinculoConfirmado(lead, { clienteId: null, temPedido: null, vinculoRevisao: 2 }).relacionamento, 'sem-cadastro')
  // Cadastro rápido e API antiga não informam temPedido: vale o fallback até a próxima leitura.
  assert.equal(aplicarVinculoConfirmado(semCadastro, { clienteId: 'cad-9', vinculoRevisao: 1 }).relacionamento, 'cliente')
})

provar('polling atrasada não desfaz o relacionamento confirmado; a leitura da mesma revisão atualiza', () => {
  const vinculada = aplicarVinculoConfirmado(semCadastro, { clienteId: 'cad-1', temPedido: false, vinculoRevisao: 1 })
  const atrasada = preservarVinculoConfirmado(vinculada, daApi({ clienteId: null, temPedido: null, vinculoRevisao: 0 }))
  assert.equal(atrasada.relacionamento, 'lead')
  assert.equal(atrasada.clienteId, 'cad-1')
  // O primeiro pedido vira "cliente" na próxima leitura, mesmo com o vínculo confirmado nesta sessão.
  const comPedido = mesclarDoServidor(vinculada, daApi({ clienteId: 'cad-1', temPedido: true, vinculoRevisao: 1 }))
  assert.equal(comPedido.relacionamento, 'cliente')
  assert.equal(marcaDoPedido(comPedido, [], { fonteApi: true }), null)
})

const cartao = (conversa) => renderToStaticMarkup(h(ContextoCatalogo.Provider, { value: { restricoes: [] } },
  h(CartaoDoCliente, { conversa, fonteApi: false, aoAbrir() {}, mostrarEntrega: false })))

provar('cartão do cliente: texto do lead, do contato sem cadastro e nada para o cliente', () => {
  assert.match(cartao(lead), /Cadastrado, ainda sem pedido/)
  assert.doesNotMatch(cartao(lead), /Contato sem cadastro/)
  assert.match(cartao(semCadastro), /Contato sem cadastro/)
  assert.doesNotMatch(cartao(semCadastro), /Cadastrado, ainda sem pedido/)
  assert.doesNotMatch(cartao(cliente), /Cadastrado, ainda sem pedido|Contato sem cadastro/)
  assert.doesNotMatch(cartao(soCancelado), /Cadastrado, ainda sem pedido|Contato sem cadastro/)
})

console.log(`ok prova-2461-lead-sem-pedido (${total} provas)`)
