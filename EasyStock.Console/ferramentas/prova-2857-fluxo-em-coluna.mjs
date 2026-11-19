import assert from 'node:assert/strict'
import { GRUPOS, NO_LARGURA, ORIENTACOES, RECUO_DA_COLUNA, desenharFluxo } from '../src/dominio/desenhoDoFluxo.js'

// #2857: no celular o vertical vira uma coluna; nada cresce para o lado, só para baixo.
const fluxo = {
  ativo: true, prompt: 'abc',
  portoes: [{ codigo: 'a', titulo: 'A' }, { codigo: 'b', titulo: 'B', escala: true }],
  ferramentas: [{ nome: 'x', ligada: true }, { nome: 'y', ligada: false }, { nome: 'escalar_para_dona', ligada: true, travada: true }],
  roteiro: [{ nome: 'Acolher', ativo: true }, { nome: 'Fechar', ativo: false }],
}
const todos = ['portoes', 'roteiro', 'ferramentas']
const coluna = desenharFluxo({ fluxo, orientacao: ORIENTACOES.vertical, abertos: todos, compacto: true })
assert.equal(coluna.compacto, true)
assert.equal(coluna.orientacao, ORIENTACOES.vertical)

// Uma linha por nó: y sempre cresce e nenhum par divide a mesma altura.
const ys = coluna.nos.map((n) => n.y)
ys.forEach((y, i) => { if (i > 0) assert.ok(y > ys[i - 1], `y cresce em ${coluna.nos[i].id}`) })

// Largura total: no máximo dois recuos mais um nó, o que cabe em 375 px sem encolher o texto.
const xs = new Set(coluna.nos.map((n) => n.x))
assert.deepEqual([...xs].sort((a, b) => a - b), [0, RECUO_DA_COLUNA, 2 * RECUO_DA_COLUNA])
assert.ok(2 * RECUO_DA_COLUNA + NO_LARGURA <= 260)

// Ordem do turno e recuo: o item de um grupo vem logo abaixo dele, um nível para dentro.
const ordem = coluna.nos.map((n) => n.id)
const nivel = (id) => coluna.nos.find((n) => n.id === id).x / RECUO_DA_COLUNA
assert.deepEqual(ordem.filter((id) => nivel(id) === 0), ['entrada', GRUPOS.portoes, 'assistente', 'saida'])
assert.equal(ordem.indexOf('portao:a'), ordem.indexOf(GRUPOS.portoes) + 1)
assert.equal(ordem.indexOf('roteiro:0'), ordem.indexOf(GRUPOS.roteiro) + 1)
assert.equal(ordem.indexOf('ferramenta:x'), ordem.indexOf(GRUPOS.ferramentas) + 1)
assert.equal(nivel('roteiro:1'), 2)
assert.equal(nivel('ferramenta:y'), 2)
assert.equal(ordem.at(-1), 'equipe')

// Arestas: o trilho liga o turno, o tronco liga grupo a item sem seta, e só a Equipe recebe as do corredor.
const aresta = (id) => coluna.arestas.find((a) => a.id === id)
assert.deepEqual([aresta('entrada>grupo:portoes').origemLado, aresta('assistente>saida').destinoLado], ['left', 'left'])
assert.equal(aresta('grupo:roteiro>roteiro:1').origemLado, 'tronco')
assert.equal(aresta('grupo:roteiro>roteiro:1').semSeta, true)
assert.deepEqual(coluna.arestas.filter((a) => a.tom === 'equipe').map((a) => a.origem), [GRUPOS.portoes, 'assistente'])
assert.equal(coluna.arestas.filter((a) => a.origemLado === 'tronco').length, 2 + 2 + 3 + 3)

// Fechado, só os nós fixos; sem `compacto` (ou no horizontal) o desenho largo continua igual.
assert.equal(desenharFluxo({ fluxo, orientacao: ORIENTACOES.vertical, compacto: true }).nos.length, 8)
assert.equal(desenharFluxo({ fluxo, orientacao: ORIENTACOES.vertical, abertos: todos }).compacto, undefined)
assert.equal(desenharFluxo({ fluxo, orientacao: ORIENTACOES.horizontal, abertos: todos, compacto: true }).compacto, undefined)

console.log('#2857 fluxo em coluna: ok')
