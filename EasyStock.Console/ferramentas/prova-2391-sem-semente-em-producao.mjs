/* eslint-disable no-console */
// Prova da #2391: o build de produção (`VITE_FONTE_DADOS=api`) não leva as sementes fictícias de fidelidade
// (cupons BABA15 e de boas-vindas, recompensas, sorteio) nem os entregadores de exemplo, que moram só na demonstração.
// O placeholder "Ex.: BEMVINDA10" do formulário de cupom é texto de UI e fica fora da busca.
// O build de demonstração leva (a busca não passa em branco).
//
//   node ferramentas/prova-2391-sem-semente-em-producao.mjs

import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import { compilarAlcancavel } from './bundle-alcancavel.mjs'

const raiz = fileURLToPath(new URL('../', import.meta.url))
const SEMENTES = ['cupom-bemvinda', 'BABA15', 'recompensa-parmesao', 'Sorteio de outubro', 'José Ribamar', 'Marcos Vinícius', 'FQR3C21']

const producao = await compilarAlcancavel({ raiz, api: true })
const demonstracao = await compilarAlcancavel({ raiz, api: false })
for (const semente of SEMENTES) {
  assert.ok(demonstracao.includes(semente), `o build de demonstração deveria levar "${semente}" (busca em branco)`)
  assert.ok(!producao.includes(semente), `o build de produção não pode levar a semente fictícia "${semente}"`)
}
console.log(`prova-2391 ok: ${SEMENTES.length} sementes fora do bundle de produção e presentes na demonstração`)
