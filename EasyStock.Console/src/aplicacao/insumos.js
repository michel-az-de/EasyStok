import { atualizarInsumo, criarInsumo, insumoDaApi, listarInsumos } from '../infra/api/producaoApi'

// Insumos no modo API (M2.3, #1496). Grava, relê sempre e devolve `true` quando gravou; o erro vai
// para `aoErro`. Separado do hook para a prova rodar sem React.
export const UNIDADES_INSUMO = ['G', 'Kg', 'Ml', 'L', 'Un']

export const lerInsumos = async () => ((await listarInsumos()) ?? []).map(insumoDaApi)

const numeroOuNulo = (texto) => (String(texto ?? '').trim() === '' ? null : Number(String(texto).replace(',', '.')))

export function criarGestaoInsumos({ recarregar, aoErro }) {
  const gravar = async (chamada) => {
    try {
      await chamada()
      return true
    } catch (erro) {
      aoErro(erro.message)
      return false
    } finally {
      await recarregar()
    }
  }

  const validar = (minimo, custo) => {
    if (minimo != null && (!Number.isInteger(minimo) || minimo < 0)) return 'Mínimo em número inteiro, sem negativo.'
    if (custo != null && (!Number.isFinite(custo) || custo < 0)) return 'Custo inválido.'
    return null
  }

  return {
    criar: ({ nome, unidade, minimo, custo }) => {
      if (!nome?.trim()) { aoErro('Informe o nome do insumo.'); return Promise.resolve(false) }
      const m = numeroOuNulo(minimo)
      const c = numeroOuNulo(custo)
      const erro = validar(m, c)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      return gravar(() => criarInsumo({ nome: nome.trim(), unidade: unidade || 'Un', minimo: m, custo: c }))
    },
    atualizar: (id, { minimo, custo }) => {
      const m = numeroOuNulo(minimo)
      const c = numeroOuNulo(custo)
      const erro = validar(m, c)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      return gravar(() => atualizarInsumo(id, { minimo: m, custo: c }))
    },
  }
}
