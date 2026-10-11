import { obterEstoqueDoDia, estoqueDoDiaDaApi } from '../infra/api/producaoApi'
import { ajustarSaldoDoItem } from '../infra/api/cardapioApi'

// Estoque do dia no modo API (M2.1, #1490). Lê do EasyStok e relê depois de cada ajuste. O ajuste
// é a contagem que ela fez (absoluta) com o motivo, pela rota do prato (#1241), que resolve o produto.
export const lerEstoqueDoDia = async () => estoqueDoDiaDaApi(await obterEstoqueDoDia())

// M1.4d (#1542): em prato com porções o saldo é de cada porção, então a contagem também. Só entra
// porção que já tem lote (a contagem cai nos lotes dela; sem lote, quem cria é a produção); o que
// ficou sem porção tem chave própria. No fim vem sempre o prato inteiro, a contagem de antes: é a
// saída para lote de porção que saiu do cardápio e para quando nenhuma porção tem lote.
const SEM_PORCAO = 'sem-porcao'
const PRATO_INTEIRO = 'prato-inteiro'
export function porcoesParaAjuste(prato) {
  // API anterior à contagem por porção ignoraria a porção e contaria o prato inteiro sem avisar.
  if (!prato?.contagemPorPorcao || (prato.porcoes ?? []).length === 0) return []
  return [
    ...prato.porcoes.filter((s) => s.temLote).map((s) => ({
      chave: s.variacaoId ?? SEM_PORCAO, rotulo: s.rotulo, saldo: s.saldo, descoberto: s.descoberto, fisico: s.fisico,
      corpo: s.variacaoId ? { variacaoId: s.variacaoId } : { semPorcao: true },
    })),
    {
      chave: PRATO_INTEIRO, rotulo: 'O prato inteiro, sem separar por porção',
      saldo: prato.saldo, descoberto: prato.descoberto, fisico: prato.fisico, corpo: null,
    },
  ]
}

// A opção em que o formulário abre: a única porção com lote, ou a única com venda sem saldo. Sem
// porção nenhuma com lote, só resta o prato inteiro.
export function porcaoSugerida(opcoes) {
  const porcoes = opcoes.filter((s) => s.corpo)
  if (porcoes.length === 0) return opcoes[0]?.chave ?? ''
  if (porcoes.length === 1) return porcoes[0].chave
  const comVendaSemSaldo = porcoes.filter((s) => s.descoberto > 0)
  return comVendaSemSaldo.length === 1 ? comVendaSemSaldo[0].chave : ''
}

// A contagem é do que está guardado, vencido incluído: é assim que a API reparte entre os lotes.
// O saldo da tela não conta vencido, então o formulário começa pelo guardado e diz quantas são.
export const contagemInicial = (alvo) => (alvo ? alvo.fisico ?? alvo.saldo ?? '' : '')
export const vencidasNaContagem = (alvo) => Math.max((alvo?.fisico ?? 0) - (alvo?.saldo ?? 0), 0)

export function criarEstoqueDoDia({ recarregar, aoErro }) {
  return {
    ajustar: async (sku, contagem, motivo, { porcoes = [], porcao = '' } = {}) => {
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
      const escolhida = porcoes.find((s) => s.chave === porcao) ?? null
      if (porcoes.length > 0 && !escolhida) {
        aoErro('Escolha a porção que você contou.')
        return false
      }
      try {
        await ajustarSaldoDoItem(sku, quantidade, motivo.trim(), escolhida?.corpo ?? null)
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
