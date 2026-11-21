// #3076: o "Novo pedido" aberto pelo Cadastro não seleciona a conversa, e só a seleção lia o dossiê. Sem ele a janela
// não sabia do endereço Padrão nem do celular: o Revisar travava em "Falta cadastrar o endereço" e dizia que a
// cliente não seria avisada. (1) abrir a janela lê o dossiê; (2) o endereço que chega depois preenche o campo vazio,
// sem apagar o que a pessoa digitou. HTTP controlado, sem servidor.
//
//   node ferramentas/prova-3076-dossie-da-janela.mjs
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
  load(url, c, next) { return url.endsWith('/infra/fonteDados.js') ? { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" } : next(url, c) },
})
globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 60000, empresa: { id: 'e' } }), setItem() {}, removeItem() {} }

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { enderecoQueChegou } = await import('../src/aplicacao/janelaDoPedido.js')

const CLIENTE = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const dossie = {
  cliente: { id: CLIENTE, nome: 'Gabi Prova', telefone: '+5511987651234' },
  enderecos: [{ id: 'e1', padrao: true, logradouro: 'Rua das Flores', numero: '100', complemento: 'apto 12', bairro: 'Pinheiros', cep: '05422010' }],
  totalPedidos: 0,
}

// 1. Abrir a janela do pedido lê o dossiê e grava o endereço e o celular do cadastro na conversa.
{
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET' } = {}) => {
    chamadas.push({ metodo: method, url })
    if (url.endsWith('/dossie')) return new Response(JSON.stringify({ data: dossie }), { status: 200 })
    return new Response(JSON.stringify({ data: null }), { status: 200 })
  }
  const despachados = []
  const estadoRef = { current: { conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, funcionamento: {}, lojaAberta: true, sincronizacao: { estado: 'ok', aviso: null } } }
  const despachar = (a) => despachados.push(a)
  const agoraRef = { current: Date.parse('2026-11-21T12:00:00Z') }
  const locais = criarAcoes({ despachar, agoraRef, estadoRef, pendentes: { current: [] }, consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {} })
  comApi(locais, { despachar, agoraRef, estadoRef }).abrirJanelaDoPedido({ id: 'conv-1', clienteId: CLIENTE, contatoNome: 'Gabi Prova' }, { clienteId: CLIENTE, nome: 'Gabi Prova' })
  await new Promise((r) => setTimeout(r, 20))
  assert.ok(chamadas.some((c) => c.url.endsWith('/conversas/conv-1/dossie')), 'abrir a janela não leu o dossiê')
  const cliente = despachados.find((a) => a.tipo === acao.CLIENTE_DA_API)
  assert.ok(cliente, 'o dossiê não chegou à conversa')
  assert.match(cliente.cliente.endereco, /^Rua das Flores, 100/, 'o endereço Padrão vem do dossiê')
  assert.ok(cliente.cliente.telefone, 'o celular do cadastro vem do dossiê')
}

// 2. O endereço que chega depois preenche o campo só enquanto ninguém digitou.
{
  assert.equal(enderecoQueChegou('', 'Rua das Flores, 100'), 'Rua das Flores, 100')
  assert.equal(enderecoQueChegou('   ', 'Rua das Flores, 100'), 'Rua das Flores, 100')
  assert.equal(enderecoQueChegou('Av. Paulista, 1000', 'Rua das Flores, 100'), null, 'não apaga o que a pessoa digitou')
  assert.equal(enderecoQueChegou('', null), null)
}

console.log('prova-3076-dossie-da-janela: ok')
