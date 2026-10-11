import { atualizarInsumo, criarInsumo, insumoDaApi, listarInsumos } from '../infra/api/producaoApi'

// Insumos no modo API (M2.3, #1496). Grava, relê sempre e devolve `true` quando gravou; o erro vai
// para `aoErro`. Separado do hook para a prova rodar sem React.
export const UNIDADES_INSUMO = ['G', 'Kg', 'Ml', 'L', 'Un']

export const lerInsumos = async () => ((await listarInsumos()) ?? []).map(insumoDaApi)

// Onde o insumo é usado. M1.4e (#1552): a embalagem escolhida numa porção não entra em receita, mas
// desce do estoque a cada porção produzida; dizer "não entra em receita" e parar aí seria enganar.
const tantas = (n, um, varios) => (n === 1 ? `1 ${um}` : `${n} ${varios}`)
export function ondeOInsumoEUsado({ receitas = 0, porcoes = 0 }) {
  const usos = [
    receitas > 0 ? `usado em ${tantas(receitas, 'receita', 'receitas')}` : null,
    porcoes > 0 ? `embalagem de ${tantas(porcoes, 'porção', 'porções')}` : null,
  ].filter(Boolean)
  return usos.length > 0 ? usos.join(' · ') : 'não entra em receita'
}

// A embalagem da receita desce em toda produção do prato; a da porção, só quando aquela porção é produzida.
export const rotuloDaEmbalagem = ({ receitas = 0, porcoes = 0 }) =>
  (porcoes > 0 && receitas === 0 ? 'Embalagem de porção, uma por porção produzida' : 'Embalagem usada em toda produção')

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
    criar: ({ nome, unidade, minimo, custo, embalagem = false }) => {
      if (!nome?.trim()) { aoErro('Informe o nome do insumo.'); return Promise.resolve(false) }
      const m = numeroOuNulo(minimo)
      const c = numeroOuNulo(custo)
      const erro = validar(m, c)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      return gravar(() => criarInsumo({
        nome: nome.trim(), unidade: embalagem ? 'Un' : (unidade || 'Un'), minimo: m, custo: c, ...(embalagem ? { embalagem: true } : {}),
      }))
    },
    atualizar: (id, { minimo, custo }) => {
      const m = numeroOuNulo(minimo)
      const c = numeroOuNulo(custo)
      const erro = validar(m, c)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      return gravar(() => atualizarInsumo(id, { minimo: m, custo: c }))
    },
    // M2.7 (#1523): marcar ou desmarcar como embalagem; mínimo e custo ficam como estão.
    marcarEmbalagem: (id, embalagem) => gravar(() => atualizarInsumo(id, { minimo: null, custo: null, embalagem })),
  }
}
