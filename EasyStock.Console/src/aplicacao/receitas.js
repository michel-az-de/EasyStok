import { detalheDaReceitaDaApi, marcarBaixaAutomatica, obterReceita, receitaDaApi, listarReceitas, salvarReceita } from '../infra/api/producaoApi'

// Receitas no modo API (M2.4a, #1498). A receita é gravada inteira (o EasyStok substitui as linhas
// numa transação e guarda o diff). Valida antes o que a API recusaria, para avisar em português.
export const UNIDADES_RECEITA = ['G', 'Kg', 'Ml', 'L', 'Un']

export const lerReceitas = async () => ((await listarReceitas()) ?? []).map(receitaDaApi)
export const lerReceita = async (produtoId) => detalheDaReceitaDaApi(await obterReceita(produtoId))

export function erroDaReceita({ rendimento, linhas }) {
  if (!(Number(rendimento) > 0)) return 'Diga quanto a receita rende (maior que zero).'
  if (linhas.some((l) => !l.insumoId)) return 'Escolha o insumo de cada linha.'
  if (linhas.some((l) => !(Number(l.quantidade) > 0))) return 'Quantidade de cada insumo maior que zero.'
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
          rendimentoBase: Number(rascunho.rendimento),
          rendimentoUnidade: rascunho.unidadeRendimento ?? 'Un',
          unidadeMedidaBase: rascunho.unidadeMedidaBase ?? 'Un',
          linhas: rascunho.linhas.map((l) => ({ insumoId: l.insumoId, quantidade: Number(l.quantidade), unidade: l.unidade })),
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
