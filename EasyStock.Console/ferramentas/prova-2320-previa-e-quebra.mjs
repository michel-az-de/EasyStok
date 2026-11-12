// #2320: prévia do cartão com a autora da mensagem (ATD-6) e quebra de linha no aviso do sistema (ATD-4).
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
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')

const resumo = { id: 'c', canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'u-ana', assumidaPorNome: 'Ana', contatoNome: 'Maria' }
const saida = (por) => ({ id: 'm', direcao: 'Saida', autor: 'Dona', status: 'Enviada', texto: 'Oi!', enviadaEm: '2026-11-11T12:00:00Z', enviadaPorUsuarioId: por })
const previa = (por, usuario) => previaDaConversa(conversaDaApi(resumo, [saida(por)], usuario))

assert.equal(previa('u-eu', { id: 'u-eu', nome: 'Eu' }).prefixo, 'Você: ', 'minha mensagem segue "Você:"')
assert.equal(previa('u-ana', { id: 'u-eu', nome: 'Eu' }).prefixo, 'Ana: ', 'outra atendente identificada pelo nome')
assert.equal(previa('u-bia', { id: 'u-eu', nome: 'Eu' }).prefixo, 'Outra atendente: ', 'outra atendente sem nome conhecido')
assert.equal(previa(null, { id: 'u-eu', nome: 'Eu' }).prefixo, 'Você: ', 'sem autora gravada mantém "Você:"')

const css = readFileSync(new URL('../src/features/atendimento/atendimento.module.css', import.meta.url), 'utf8')
const bloco = /\n\.sistema \{([^}]*)\}/.exec(css)?.[1] ?? ''
assert.match(bloco, /white-space:\s*pre-wrap/, 'aviso do sistema preserva as quebras de linha')
console.log('OK: prévia com autora e quebra de linha no aviso do sistema.')
