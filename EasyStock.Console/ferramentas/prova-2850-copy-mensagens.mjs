// Prova da #2850: a mensagem de fábrica não promete o que não vai junto e não vaza instrução interna.
//  - "Segue o link" ou "Segue nosso cardápio" só com o link de verdade ({linkCardapio}) no texto.
//  - Nenhuma automática manda ao cliente a anotação "sem prometer prazo".
//  - Nenhuma resposta rápida traz horário fixo de janela: a janela muda na loja e o texto ficaria errado.
//
// Roda com: node ferramentas/prova-2850-copy-mensagens.mjs
// Contra o master (Red): PROVA_SRC=<pasta src> node ferramentas/prova-2850-copy-mensagens.mjs

import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

// O src importa sem extensão, como o Vite aceita (mesmo hook da prova-1441).
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const raiz = process.env.PROVA_SRC ? process.env.PROVA_SRC.replace(/[\/]$/, '') : fileURLToPath(new URL('../src', import.meta.url))
const importar = (caminho) => import(pathToFileURL(join(raiz, caminho)).href)

const { RESPOSTAS_RAPIDAS, TEMPLATES_INTERNOS } = await importar('infra/catalogo.js')
const { REGRAS_PADRAO } = await importar('dominio/automacao.js')

const mensagens = [
  ...RESPOSTAS_RAPIDAS.map((r) => ({ origem: `rápida "${r.titulo}"`, texto: r.texto })),
  ...TEMPLATES_INTERNOS.map((t) => ({ origem: `interno "${t.nome}"`, texto: t.texto })),
  ...REGRAS_PADRAO.map((r) => ({ origem: `automática "${r.nome}"`, texto: r.texto })),
]

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok += 1; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHOU  ${nome}\n    ${erro.message}`) }
}

caso('promessa de link só com o link no texto', () => {
  const prometem = mensagens.filter((m) => /segue (o|nosso) (link|cardápio)/i.test(m.texto) && !m.texto.includes('{linkCardapio}'))
  assert.deepEqual(prometem.map((m) => m.origem), [])
})

caso('automática sem instrução interna', () => {
  const vazam = mensagens.filter((m) => /sem prometer/i.test(m.texto))
  assert.deepEqual(vazam.map((m) => m.origem), [])
})

caso('resposta rápida sem horário fixo de janela', () => {
  const fixas = RESPOSTAS_RAPIDAS.filter((r) => /\b\d{1,2}h\d{2}\b/.test(r.texto))
  assert.deepEqual(fixas.map((r) => r.titulo), [])
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
process.exit(falhas.length === 0 ? 0 : 1)
