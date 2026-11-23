// Prova pura da #3208 (S83): categoria nas respostas rápidas. Agrupamento do seletor, busca pela categoria,
// validação da categoria e a tradução com a API. Sem navegador: roda com `node`.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// O src importa sem extensão, como o Vite aceita (mesmo hook das outras provas).
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const dom = await import('../src/dominio/respostas.js')
const traducao = await import('../src/infra/api/traducaoRespostas.js')

let falhas = 0
function caso(nome, fn) {
  try { fn(); console.log(`ok  ${nome}`) } catch (e) { falhas += 1; console.error(`ERRO ${nome}\n  ${e.message}`) }
}

const r = (id, titulo, categoria, extra = {}) => ({ id, titulo, categoria, atalho: `/${id}`, texto: `texto ${titulo}`, arquivada: false, ...extra })

caso('normalizarCategoria apara, junta espaços e devolve null para vazio', () => {
  assert.equal(dom.normalizarCategoria('  Entrega   e  prazo '), 'Entrega e prazo')
  assert.equal(dom.normalizarCategoria('   '), null)
  assert.equal(dom.normalizarCategoria(null), null)
  assert.equal(dom.normalizarCategoria(undefined), null)
})

caso('motivoDaCategoria só recusa o que passa de 40 caracteres', () => {
  assert.equal(dom.CATEGORIA_MAXIMA, 40)
  assert.equal(dom.motivoDaCategoria('a'.repeat(40)), null)
  assert.equal(dom.motivoDaCategoria(''), null)
  assert.match(dom.motivoDaCategoria('a'.repeat(41)), /40/)
})

caso('categoriasUsadas: únicas sem repetir por caixa ou acento, ordem pt-BR, sem vazias', () => {
  const lista = [r('a', 'A', 'Pagamento'), r('b', 'B', 'pagamento'), r('c', 'C', null), r('d', 'D', 'Entrega'), r('e', 'E', '  '), r('f', 'F', 'Água')]
  assert.deepEqual(dom.categoriasUsadas(lista), ['Água', 'Entrega', 'Pagamento'])
})

caso('seletor: sem nenhuma categoria a ordem e a forma são as de hoje (sem grupo)', () => {
  const lista = [r('b', 'Beta', null), r('a', 'Alfa', null)]
  const itens = dom.respostasDoSeletor({ respostasProntas: lista })
  assert.deepEqual(itens.map((x) => x.id), ['a', 'b'])
  const agrupado = dom.agruparRespostasDoSeletor(itens)
  assert.equal(agrupado.agrupado, false)
  assert.equal(agrupado.grupos.length, 1)
  assert.deepEqual(agrupado.grupos[0].itens.map((x) => x.indice), [0, 1])
})

caso('seletor: agrupa por categoria em ordem alfabética e deixa "Sem categoria" por último', () => {
  const lista = [r('s1', 'Sem um', null), r('p1', 'Pix', 'Pagamento'), r('e1', 'Frete', 'Entrega'), r('p2', 'Cartão', 'Pagamento'), r('s2', 'Sem dois', '')]
  const itens = dom.respostasDoSeletor({ respostasProntas: lista })
  assert.deepEqual(itens.map((x) => x.id), ['e1', 'p2', 'p1', 's2', 's1'])
  const { agrupado, grupos } = dom.agruparRespostasDoSeletor(itens)
  assert.equal(agrupado, true)
  assert.deepEqual(grupos.map((g) => g.rotulo), ['Entrega', 'Pagamento', 'Sem categoria'])
  // O índice de cada item é a posição na lista plana: o teclado anda pelos grupos sem pular.
  assert.deepEqual(grupos.flatMap((g) => g.itens.map((x) => x.indice)), [0, 1, 2, 3, 4])
  assert.deepEqual(grupos[1].itens.map((x) => x.resposta.id), ['p2', 'p1'])
})

caso('busca casa também com a categoria, sem caixa nem acento', () => {
  const lista = [r('p1', 'Pix', 'Pagamento'), r('e1', 'Frete', 'Entrega')]
  assert.deepEqual(dom.respostasDoSeletor({ respostasProntas: lista, termo: 'pagamento' }).map((x) => x.id), ['p1'])
  assert.deepEqual(dom.respostasDoSeletor({ respostasProntas: lista, termo: 'ENTR' }).map((x) => x.id), ['e1'])
  assert.deepEqual(dom.respostasDoSeletor({ respostasProntas: lista, termo: 'inexistente' }), [])
})

caso('busca por título continua valendo e arquivada continua fora', () => {
  const lista = [r('p1', 'Pix', 'Pagamento'), r('x', 'Pix antigo', 'Pagamento', { arquivada: true })]
  assert.deepEqual(dom.respostasDoSeletor({ respostasProntas: lista, termo: 'pix' }).map((x) => x.id), ['p1'])
})

caso('API: lê a categoria (nula vira null) e envia no corpo, limpa quando vazia', () => {
  assert.equal(traducao.respostaDaApi({ id: 'x', titulo: 'T', atalho: 'x', texto: 'y', categoria: 'Pagamento' }).categoria, 'Pagamento')
  assert.equal(traducao.respostaDaApi({ id: 'x', titulo: 'T', atalho: 'x', texto: 'y', categoria: null }).categoria, null)
  assert.equal(traducao.respostaDaApi({ id: 'x', titulo: 'T', atalho: 'x', texto: 'y' }).categoria, null)
  assert.equal(traducao.corpoDaResposta({ titulo: 'T', atalho: 'x', texto: 'y', categoria: '  Entrega ' }).categoria, 'Entrega')
  assert.equal(traducao.corpoDaResposta({ titulo: 'T', atalho: 'x', texto: 'y', categoria: '  ' }).categoria, null)
  assert.equal(traducao.corpoDaResposta({ titulo: 'T', atalho: 'x', texto: 'y' }).categoria, null)
})

if (falhas) { console.error(`\n${falhas} caso(s) falharam`); process.exit(1) }
console.log('\nprova-3208-respostas-categoria: tudo verde')
