// #2320/#2334: autoria confirmada por ID, inclusive em mensagens avulsas.
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(e, c, p) { if (e.startsWith('.') && !/\.[a-z]+$/i.test(e)) { try { return p(e + '.js', c) } catch { /* resolução padrão */ } } return p(e, c) },
  load(u, c, p) {
    if (u.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API=true; export const API_BASE='';" }
    if (u.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(u), 'utf8') }
    return p(u, c)
  },
})
const { conversaDaApi, mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')
const resumo = { id: 'c', canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'u-ana', assumidaPorNome: 'Ana', contatoNome: 'Maria' }
const usuario = { id: 'u-eu', nome: 'Ana' }
const saida = (por) => ({ id: 'm', direcao: 'Saida', autor: 'Dona', status: 'Enviada', texto: 'Oi!', enviadaEm: '2026-11-11T12:00:00Z', enviadaPorUsuarioId: por })
const previa = (por, sessao = usuario, dados = resumo) => previaDaConversa(conversaDaApi(dados, [saida(por)], sessao), sessao)

assert.equal(previa('u-eu').prefixo, 'Você: ', 'a identidade da sessão identifica minha mensagem')
assert.equal(previa('u-ana').prefixo, 'Ana: ', 'o nome da outra autora vem da API e IDs distintos prevalecem sobre nomes iguais')
assert.equal(previa('u-bia').prefixo, 'Equipe: ', 'não atribui à responsável a mensagem de outro ID')
assert.equal(previa(null).prefixo, 'Equipe: ', 'sem autoria gravada não atribui a mim')
assert.equal(previa('u-ana', null).prefixo, 'Equipe: ', 'sem sessão não confirma quem é outro autor')
assert.equal(previa('u-ana', usuario, { ...resumo, assumidaPorNome: null }).prefixo, 'Equipe: ', 'sem nome recebido não inventa uma autora')
const avulsa = conversaDaApi(resumo, [], usuario)
avulsa.mensagens = [mensagemDaApi(saida('u-ana'))]
assert.equal(previaDaConversa(avulsa, usuario).prefixo, 'Ana: ', 'mensagens avulsas e anteriores conservam a identificação por ID')
assert.equal(previaDaConversa({ situacaoApi: 'Assumida', mensagens: avulsa.mensagens }, usuario).prefixo, 'Equipe: ', 'sem nome confirmado a mensagem avulsa continua sendo da equipe')

console.log('OK: autoria por ID, nome confirmado e mensagens avulsas.')
