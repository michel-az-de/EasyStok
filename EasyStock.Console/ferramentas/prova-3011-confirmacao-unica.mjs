/* eslint-disable no-console */
// Prova da #3011: ação destrutiva do Console confirma num modal só (ModalConfirmar sobre o Modal), nunca no
// window.confirm nativo. O botão leva o verbo da ação, o foco inicial vai para o botão seguro e Esc cancela
// (o Modal liga o cancel nativo ao aoFechar, coberto pelas provas do Modal). Roda: node ferramentas/prova-3011-confirmacao-unica.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { fileURLToPath } from 'node:url'

const raiz = fileURLToPath(new URL('..', import.meta.url))
const ler = (caminho) => readFileSync(new URL(`../${caminho}`, import.meta.url), 'utf8')

// 1. As três confirmações da tela saíram do navegador e usam o componente comum.
const telas = {
  'ModalAssistente': ler('src/features/atendimento/ModalAssistente.jsx'),
  'AbaFluxo': ler('src/features/gestao/atendimento/AbaFluxo.jsx'),
}
for (const [nome, fonte] of Object.entries(telas)) {
  assert.doesNotMatch(fonte, /window\.confirm|[^.\w]confirm\(/, `${nome}: sem window.confirm`)
  assert.match(fonte, /<ModalConfirmar/, `${nome}: confirma pelo ModalConfirmar`)
}
assert.match(telas.AbaFluxo, /rotuloConfirmar="Desligar ferramenta"/, 'AbaFluxo: o verbo da ferramenta')
assert.match(telas.AbaFluxo, /rotuloConfirmar="Voltar ao padrão"/, 'AbaFluxo: o verbo do roteiro')
assert.match(telas.ModalAssistente, /rotuloConfirmar="Desligar o Assistente"/, 'ModalAssistente: o verbo da chave')

// 2. O componente: verbo no botão, foco inicial no seguro, X e Esc ligados ao cancelar.
const servidor = await createServer({ root: raiz, logLevel: 'silent', appType: 'custom', server: { middlewareMode: true } })
try {
  const { ModalConfirmar } = await servidor.ssrLoadModule('/src/componentes/ModalConfirmar.jsx')
  const html = renderToStaticMarkup(createElement(ModalConfirmar, {
    titulo: 'Desligar a ferramenta?', descricao: 'O Assistente deixa de usar.', rotuloConfirmar: 'Desligar ferramenta', aoConfirmar() {}, aoFechar() {},
  }))
  assert.match(html, /<h2[^>]*>Desligar a ferramenta\?<\/h2>/, 'título')
  assert.match(html, /<button[^>]*data-foco-inicial[^>]*>Continuar<\/button>/, 'foco inicial no botão seguro (Continuar)')
  assert.doesNotMatch(html, /data-foco-inicial[^>]*>Desligar ferramenta/, 'o destrutivo não recebe o foco')
  assert.match(html, />Desligar ferramenta<\/button>/, 'o botão leva o verbo da ação, não "OK"')
  assert.doesNotMatch(html, />OK<\/button>/)
  assert.match(html, /Fechar janela/, 'o X do Modal cancela')
  console.log('prova-3011-confirmacao-unica: ok')
} finally { await servidor.close() }
