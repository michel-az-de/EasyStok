import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
  return proximo(spec, contexto)
} })

const { buscarDestinosDoConsole, modulosDoHall } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')
const api = modulosDoHall({ fonteApi: true })
const destinos = (modulos, busca) => buscarDestinosDoConsole(modulos, busca).map((item) => item.href)

assert.deepEqual(destinos(api, '  IMPRESSAO  '), ['#/m/configuracoes/impressao'])
assert.deepEqual(destinos(api, 'caixa'), ['#/m/financeiro/caixa'])
assert.deepEqual(destinos(api, 'produção receitas'), ['#/m/producao/receitas'])
assert.deepEqual(destinos(api, 'janelas entrega'), ['#/m/entregas/janelas'])
// #2095: o financeiro gerencial foi descongelado; a área oferece todas as suas telas vivas.
assert.deepEqual(destinos(api, 'financeiro'), ['caixa', 'visao-geral', 'pagar', 'receber', 'extrato', 'relatorios', 'categorias', 'centros-custo'].map((tela) => `#/m/financeiro/${tela}`), 'o nome da área oferece suas telas vivas')
assert.deepEqual(destinos(api, 'extrato'), ['#/m/financeiro/extrato'], 'extrato descongelado aparece na busca (#2095)')
assert.deepEqual(destinos(modulosDoHall({ fonteApi: true, acoes: { gerenciarCaixa: true } }), 'extrato'), ['#/m/financeiro/caixa'], 'perfil sem contas não acha o extrato, só a área viva (#2095, F17)')
assert.deepEqual(destinos(api, 'pratos preços'), [
  '#/m/cardapio/itens', '#/m/cardapio/categorias', '#/m/cardapio/imagens',
], 'busca pelo resumo preserva a descoberta da área')
assert.deepEqual(destinos(api.filter((modulo) => modulo.id === 'atendimento'), 'impressão'), [])
assert.deepEqual(destinos(modulosDoHall(), 'impressão'), [], 'tela só da API não surge na demonstração')
// #1753: a tela de Usuários passou a existir no Console; a busca leva a ela.
assert.deepEqual(destinos(api, 'usuários'), ['#/m/configuracoes/usuarios'], 'a busca acha a tela de Usuários')
assert.deepEqual(destinos(api, 'fornecedores'), [], 'a busca não promete uma tela inexistente')
for (const busca of ['', '   ', 'nenhum-destino-com-este-nome']) assert.deepEqual(destinos(api, busca), [])

const antes = JSON.stringify(api)
for (const busca of ['impressão', 'produção', 'integrações', 'financeiro']) {
  for (const destino of buscarDestinosDoConsole(api, busca)) {
    const rota = rotaDaHash(destino.href, { fonteApi: true })
    assert.equal(rota.modulo, destino.modulo.id)
    assert.equal(rota.tela, destino.tela.id, 'cada resultado abre exatamente a tela anunciada')
  }
}
assert.equal(JSON.stringify(api), antes, 'a busca não muda o catálogo nem a ordem escolhida')
// #1891: as telas "Em preparação" (cozinha/producao e campanhas/fidelidade) estão congeladas e não
// chegam ao hall nem à busca.
assert.deepEqual(api.flatMap((modulo) => modulo.telas.filter((tela) => tela.indisponivelNaApi).map((tela) => `${modulo.id}/${tela.id}`)), [])
assert.deepEqual(destinos(api, 'fidelidade'), [], 'Campanhas congelado não aparece na busca')
assert.deepEqual(destinos(api, 'cupons'), [], 'nem pelo resumo do módulo')
console.log('Busca #1653: destinos diretos, acentos, contexto, permissões, modo demonstração, rotas e catálogo preservados.')
