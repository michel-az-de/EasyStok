import { obterEstoqueDoDia, estoqueDoDiaDaApi } from '../infra/api/producaoApi'
import { ajustarSaldoDoItem } from '../infra/api/cardapioApi'

// Estoque do dia no modo API (M2.1, #1490). Lê do EasyStok e relê depois de cada ajuste. O ajuste
// é a contagem que ela fez (absoluta) com o motivo, pela rota do prato (#1241), que resolve o produto.
export const lerEstoqueDoDia = async () => estoqueDoDiaDaApi(await obterEstoqueDoDia())

export function criarEstoqueDoDia({ recarregar, aoErro }) {
  return {
    ajustar: async (sku, contagem, motivo) => {
      if (!sku) {
        aoErro('Este produto não está no cardápio: ajuste pelo cadastro do estoque.')
        return false
      }
      // Campo vazio não é zero: Number('') daria 0 e zeraria o saldo sem ela ter contado.
      const texto = String(contagem ?? '').trim()
      const quantidade = Number(texto)
      if (texto === '' || !Number.isFinite(quantidade) || quantidade < 0) {
        aoErro('Informe quantas porções você contou.')
        return false
      }
      if (!motivo?.trim()) {
        aoErro('Diga o motivo do ajuste.')
        return false
      }
      try {
        await ajustarSaldoDoItem(sku, quantidade, motivo.trim())
        return true
      } catch (erro) {
        aoErro(erro.message)
        return false
      } finally {
        await recarregar()
      }
    },
  }
}
