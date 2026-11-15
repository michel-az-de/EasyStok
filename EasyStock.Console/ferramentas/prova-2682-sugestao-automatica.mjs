/* eslint-disable no-console */
// Prova da #2682 (A1, homologação #1769): em Respostas automáticas, "Escrever texto" só sugere o texto da
// demonstração quando ele passa na mesma checagem de variáveis que trava o Salvar. Antes, Primeira resposta e
// Fora do horário abriam com {linkCardapio} e {abre}, que a tela recusa: o Salvar nascia desabilitado.
//
// Roda com: node ferramentas/prova-2682-sugestao-automatica.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2682-sugestao-automatica.mjs
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { pathToFileURL } from 'node:url'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const { GATILHOS_DA_API, REGRAS_PADRAO, bloqueioDaAutomatica } = await import(new URL('dominio/automacao.js', base).href)
const { regraDaApi } = await import(new URL('infra/api/traducaoRespostas.js', base).href)

let falhas = 0
const caso = (nome, fn) => { try { fn(); console.log(`ok   ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n     ${e.message}`) } }

const semRegra = Object.keys(GATILHOS_DA_API).map((gatilho) => regraDaApi({ gatilho, ligada: false, texto: null }))

caso('A1: nenhuma sugestão nasce bloqueada pelo Salvar', () => {
  for (const r of semRegra) assert.equal(bloqueioDaAutomatica(r.sugestao ?? ''), null, `${r.nome}: sugestão ${JSON.stringify(r.sugestao)}`)
})

caso('A1: as regras com texto de demonstração preenchível seguem com a sugestão', () => {
  const preenchiveis = REGRAS_PADRAO.filter((p) => p.texto && !bloqueioDaAutomatica(p.texto)).map((p) => p.gatilho)
  assert.ok(preenchiveis.length >= 4, 'pelo menos as 4 regras sem marcador solto')
  for (const r of semRegra.filter((r) => preenchiveis.includes(r.gatilho))) {
    assert.equal(r.sugestao, REGRAS_PADRAO.find((p) => p.gatilho === r.gatilho).texto, `${r.nome} mantém a sugestão`)
  }
})

caso('A1: regra com texto salvo não recebe sugestão', () => {
  const [gatilho] = Object.keys(GATILHOS_DA_API)
  assert.equal(regraDaApi({ gatilho, ligada: true, texto: 'Oi {nome}!' }).sugestao, null)
})

console.log(falhas ? `\n${falhas} falha(s)` : '\nprova-2682 ok')
process.exit(falhas ? 1 : 0)
