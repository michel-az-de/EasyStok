/* eslint-disable no-console */
// Prova da #2496: a checagem do Assistente lê GET api/atendimento/conversas/{id}/assistente, mostra cada etapa como
// passa, barra ou não se aplica, e a chave do Assistente na loja grava só `ativo` no PUT da configuração (o resto
// da configuração fica). Roda com: node ferramentas/prova-2496-checagem-assistente.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(s, c, next) {
    if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução padrão */ } }
    return next(s, c)
  },
  load(url, c, next) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return next(url, c)
  },
})
const storage = new Map()
globalThis.sessionStorage = { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) }
sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'sintetico', empresa: { id: 'a' }, usuario: { id: 'u' }, expiraEm: Date.now() + 60000 }))

const { checarAssistente } = await import('../src/infra/api/conversasApi.js')
const { salvarAtendimentoAutomatico } = await import('../src/infra/api/configuracaoApi.js')
const {
  contagemDasEtapas, estadoDaEtapa, podeLigarAssistente, vereditoDaChecagem,
} = await import('../src/dominio/checagemAssistente.js')

// Domínio: os três estados que a API serializa (JsonStringEnumConverter) e um desconhecido.
assert.equal(estadoDaEtapa({ estado: 'Passa' }).chave, 'passa')
assert.equal(estadoDaEtapa({ estado: 'Barra' }).chave, 'barra')
assert.equal(estadoDaEtapa({ estado: 'NaoSeAplica' }).chave, 'fora')
assert.equal(estadoDaEtapa({ estado: 'Outro' }).chave, 'fora', 'estado desconhecido não vira "passa"')
for (const estado of ['Passa', 'Barra', 'NaoSeAplica']) assert.ok(estadoDaEtapa({ estado }).rotulo, 'a cor nunca vai sem rótulo')
assert.deepEqual(
  contagemDasEtapas([{ estado: 'Passa' }, { estado: 'Barra' }, { estado: 'Passa' }, { estado: 'NaoSeAplica' }]),
  { passa: 2, barra: 1, fora: 1 })
assert.deepEqual(contagemDasEtapas(undefined), { passa: 0, barra: 0, fora: 0 })

const equipe = 'Recebe os avisos da equipe: o Assistente só responde depois que alguém devolver esta conversa a ele.'
assert.deepEqual(vereditoDaChecagem({ responde: false, bloqueio: equipe, entradasPendentes: 3 }),
  { tom: 'perigo', titulo: 'O Assistente não responde esta conversa.', detalhe: equipe })
assert.equal(vereditoDaChecagem({ responde: true, entradasPendentes: 1 }).detalhe, '1 mensagem do cliente sem resposta.')
assert.equal(vereditoDaChecagem({ responde: true, entradasPendentes: 3 }).detalhe, '3 mensagens do cliente sem resposta.')
assert.equal(vereditoDaChecagem({ responde: true, entradasPendentes: 0 }).detalhe, null)
assert.equal(vereditoDaChecagem(null), null)

assert.equal(podeLigarAssistente({ editarAtendimento: true }), true)
assert.equal(podeLigarAssistente({ editarAtendimento: false }), false)
assert.equal(podeLigarAssistente(undefined), false, 'sem a matriz de ações, a chave fica travada')

// HTTP: a checagem é um GET da conversa; a chave é um PUT com `ativo` e nada mais.
const chamadas = []
globalThis.fetch = async (url, options = {}) => {
  chamadas.push({ url: String(url), metodo: options.method ?? 'GET', corpo: typeof options.body === 'string' ? JSON.parse(options.body) : null })
  return new Response(JSON.stringify({ data: { ok: true } }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
await checarAssistente('c1', new AbortController().signal)
await salvarAtendimentoAutomatico(true)
await salvarAtendimentoAutomatico(0)
assert.equal(chamadas[0].url, '/api/atendimento/conversas/c1/assistente')
assert.equal(chamadas[0].metodo, 'GET')
assert.equal(chamadas[1].url, '/api/atendimento/configuracao')
assert.equal(chamadas[1].metodo, 'PUT')
assert.deepEqual(chamadas[1].corpo, { ativo: true }, 'só a chave: tom, saudações e mensagens não vão no corpo')
assert.deepEqual(chamadas[2].corpo, { ativo: false })

// Tela: o item do menu existe só no modo API e em conversa aberta (concluída fica sem menu, #2185) e abre a
// modal; a modal pergunta antes de desligar a loja toda. A matriz do menu está em prova-2185-menu-de-acoes.
const painel = readFileSync(new URL('../src/features/atendimento/PainelAtendimento.jsx', import.meta.url), 'utf8')
assert.match(painel, /fonteApi && !encerrada && \{ chave: 'assistente', rotulo: 'Checar o Assistente'/)
assert.match(painel, /modalAberto === 'assistente' && <ModalAssistente/)
const modal = readFileSync(new URL('../src/features/atendimento/ModalAssistente.jsx', import.meta.url), 'utf8')
assert.match(modal, /if \(!ativo && !window\.confirm\(PERGUNTA_AO_DESLIGAR\)\) return/)
assert.match(modal, /role="switch"/)
console.log('prova-2496-checagem-assistente: ok')
