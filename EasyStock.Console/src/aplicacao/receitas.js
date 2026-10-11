import { detalheDaReceitaDaApi, marcarBaixaAutomatica, obterReceita, receitaDaApi, listarReceitas, salvarReceita } from '../infra/api/producaoApi'

// Receitas no modo API (M2.4a, #1498). A receita é gravada inteira (o EasyStok substitui as linhas
// numa transação e guarda o diff). Valida antes o que a API recusaria, para avisar em português.
export const UNIDADES_RECEITA = ['G', 'Kg', 'Ml', 'L', 'Un']

// M1.4e (#1552): em que a receita rende. Em peso, a produção baixa o insumo pelo peso produzido, e por
// isso acompanha o tamanho da porção. Litro e mililitro não entram: a produção é pesada em gramas.
export const UNIDADES_RENDIMENTO = [
  { valor: 'Un', rotulo: 'porções' },
  { valor: 'Kg', rotulo: 'kg' },
  { valor: 'G', rotulo: 'g' },
]
export const rotuloDoRendimento = (unidade) => UNIDADES_RENDIMENTO.find((u) => u.valor === unidade)?.rotulo ?? unidade

// O que a receita deste prato ainda não acompanha no tamanho da porção, dito onde ela resolve.
export function orientacoesDaReceita({ prato, unidadeRendimento, linhas = [], insumos = [] }) {
  const tamanhos = prato?.porcoes ?? 0
  const comEmbalagem = prato?.porcoesComEmbalagem ?? 0
  const embalagens = linhas
    .map((l) => insumos.find((s) => s.id === l.insumoId))
    .filter((s) => s?.embalagem)
    .map((s) => s.nome)
  const lista = []

  if (tamanhos > 1 && unidadeRendimento === 'Un') {
    lista.push('Este prato tem porções de tamanhos diferentes. Com o rendimento em porções, a produção baixa o mesmo insumo para qualquer tamanho. Informe quanto a receita rende em peso (kg) para a baixa acompanhar o tamanho.')
  }
  if (embalagens.length === 0) return lista

  const nomes = embalagens.join(', ')
  if (comEmbalagem > 0 && comEmbalagem >= tamanhos) {
    // Não manda tirar da receita: a produção lançada sem porção (a do aplicativo do celular) só conhece a receita.
    lista.push(`As porções deste prato têm embalagem própria, escolhida no cardápio. A embalagem desta receita (${nomes}) não desce na produção delas; continua valendo para a produção lançada sem porção, como a do aplicativo do celular.`)
  } else if (comEmbalagem > 0) {
    lista.push(`${comEmbalagem} de ${tamanhos} porções deste prato têm embalagem própria, escolhida no cardápio. A embalagem desta receita (${nomes}) só desce na produção das outras e na lançada sem porção.`)
  } else if (tamanhos > 1 && unidadeRendimento !== 'Un') {
    lista.push(`Com o rendimento em peso, a embalagem desta receita (${nomes}) desce pelo peso, e não uma por porção. Escolha a embalagem de cada porção no cardápio.`)
  }
  return lista
}

export const lerReceitas = async () => ((await listarReceitas()) ?? []).map(receitaDaApi)
export const lerReceita = async (produtoId) => detalheDaReceitaDaApi(await obterReceita(produtoId))

// O teclado decimal em pt-BR só tem vírgula: "1,2" é um vírgula dois.
const numero = (texto) => (String(texto ?? '').trim() === '' ? NaN : Number(String(texto).replace(',', '.')))

export function erroDaReceita({ rendimento, linhas }) {
  if (!(numero(rendimento) > 0)) return 'Diga quanto a receita rende (maior que zero).'
  if (linhas.some((l) => !l.insumoId)) return 'Escolha o insumo de cada linha.'
  if (linhas.some((l) => !(numero(l.quantidade) > 0))) return 'Quantidade de cada insumo maior que zero.'
  const ids = linhas.map((l) => l.insumoId)
  if (new Set(ids).size !== ids.length) return 'O mesmo insumo apareceu duas vezes: some numa linha só.'
  return null
}

export function criarGestaoReceitas({ recarregar, aoErro }) {
  return {
    salvar: async (produtoId, rascunho) => {
      const erro = erroDaReceita(rascunho)
      if (erro) { aoErro(erro); return false }
      try {
        await salvarReceita(produtoId, {
          rendimentoBase: numero(rascunho.rendimento),
          rendimentoUnidade: rascunho.unidadeRendimento ?? 'Un',
          unidadeMedidaBase: rascunho.unidadeMedidaBase ?? 'Un',
          linhas: rascunho.linhas.map((l) => ({ insumoId: l.insumoId, quantidade: numero(l.quantidade), unidade: l.unidade })),
        })
        return true
      } catch (e) {
        aoErro(e.message)
        return false
      } finally {
        await recarregar()
      }
    },
    // D-M2-01 (#1499): ligada, a produção baixa os insumos; faltou insumo, avisa e não trava.
    marcarBaixa: async (produtoId, ligada) => {
      try {
        await marcarBaixaAutomatica(produtoId, ligada)
        return true
      } catch (e) {
        aoErro(e.message)
        return false
      } finally {
        await recarregar()
      }
    },
  }
}
