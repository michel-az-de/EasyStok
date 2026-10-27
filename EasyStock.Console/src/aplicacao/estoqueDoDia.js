import { obterEstoqueDoDia, estoqueDoDiaDaApi, listarLotesRecentes, loteDaListaDaApi } from '../infra/api/producaoApi'
import { ajustarSaldoDoItem } from '../infra/api/cardapioApi'
import { lerSessao } from '../infra/api/sessao'
import { FUSO } from '../dominio/formato'

// Estoque do dia no modo API (M2.1, #1490). Lê do EasyStok e relê depois de cada ajuste. O ajuste
// é a contagem que ela fez (absoluta) com o motivo, pela rota do prato (#1241), que resolve o produto.
// #2081 (A8, A10): cada lote ganha o id (para reimprimir as etiquetas), a origem, quem registrou e a
// fabricação, lidos da lista de lotes. Sem a lista, o estoque continua servindo, sem esses dados.
export async function lerEstoqueDoDia() {
  const [estoque, lista] = await Promise.all([
    obterEstoqueDoDia(),
    listarLotesRecentes().catch(() => null),
  ])
  const r = estoqueDoDiaDaApi(estoque)
  const porCodigo = new Map((lista ?? []).map(loteDaListaDaApi).map((l) => [l.codigo, l]))
  const usuario = lerSessao()?.usuario?.id ?? null
  return {
    ...r,
    pratos: r.pratos.map((p) => ({
      ...p,
      lotes: p.lotes.map((l) => {
        const daLista = l.codigo ? porCodigo.get(l.codigo) : null
        return daLista ? { ...l, id: daLista.id, origem: origemDoLote(daLista, usuario), deAparelho: daLista.origem === 'mobile' } : l
      }),
    })),
  }
}

// "Aparelho · Rosa · fab. 25/10" ou "Console · você · fab. 26/10". O aparelho manda o nome de quem
// operou; o console grava o usuário, então só dá para dizer quando foi a própria pessoa.
const ORIGENS = { mobile: 'Aparelho', producao: 'Console', web: 'Painel', entrada: 'Entrada de estoque' }
const diaCurto = (iso) => new Date(iso).toLocaleDateString('pt-BR', { timeZone: FUSO, day: '2-digit', month: '2-digit' })
export function origemDoLote({ origem, operadorNome, operadorUserId, fabricadoEm }, usuarioId = null) {
  const quem = operadorNome?.trim() || (usuarioId && operadorUserId === usuarioId ? 'você' : null)
  return [ORIGENS[origem] ?? null, quem, fabricadoEm ? `fab. ${diaCurto(fabricadoEm)}` : null].filter(Boolean).join(' · ') || null
}

// #2081 (A8): só o lote que tem etiquetas no EasyStok reimprime. O do aparelho imprime no aparelho.
export const podeReimprimir = (lote) => Boolean(lote?.id) && !lote.deAparelho

// #2081 (A16): "Lasanha 1 kg" com a porção "1 kg" embaixo dizia a mesma coisa duas vezes.
export const porcaoQueNaoRepete = (nome, porcao) => {
  const p = String(porcao ?? '').trim()
  return p && !String(nome ?? '').trim().toLowerCase().endsWith(p.toLowerCase()) ? p : ''
}

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
