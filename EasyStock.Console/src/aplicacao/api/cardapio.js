import * as acao from '../acoes'
import { estaRemovido, itemPorSku, semControleDeSaldo, disponivelHoje } from '../../dominio/cardapio'
import { listarCardapio } from '../../infra/api/comandaApi'
import {
  ajustarSaldoDoItem, alertaDaApi, definirArquivado, definirDisponibilidade, detalheDaApi, editarItem, incluirItem,
  itemForaDaApi, listarDesacertos, listarFora, obterItem, validarItem,
} from '../../infra/api/cardapioApi'
import { listarSecoes, secaoDaApi } from '../../infra/api/secoesApi'

// Cardápio no modo API (#1241, F11, S45/S17/S22). Antes, ligar/desligar, o saldo e o item só
// mudavam a memória do navegador e voltavam na releitura do minuto seguinte. Agora tudo grava no
// EasyStok e relê o cardápio, certo ou errado: se o EasyStok recusar, a releitura devolve a tela ao
// que ficou salvo e a faixa avisa. Dia e saldo respondem na hora (mesmo caso local); item novo não
// inventa sku local, espera o id do EasyStok.
const MOTIVO_DO_BALCAO = 'Ajuste de saldo pelo balcão (console)'

// Cardápio de hoje + os itens tirados (para o "Repor"). A lista de fora é do Gerente: para quem
// não pode, fica só o de dentro, sem aviso (não é falha, é permissão).
export async function lerCardapio({ incluirFora = true } = {}) {
  const [dentro, fora] = await Promise.all([listarCardapio(), incluirFora ? listarFora().catch(() => []) : []])
  const skus = new Set(dentro.map((i) => i.sku))
  return [...dentro, ...(fora ?? []).map(itemForaDaApi).filter((i) => !skus.has(i.sku))]
}

// Produtos vendidos sem saldo (S22). Sem a permissão de estoque, nenhum alerta.
export async function lerAlertasDeEstoque() {
  try {
    return ((await listarDesacertos()) ?? []).map(alertaDaApi)
  } catch {
    return []
  }
}

export function criarAcoesCardapioApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const itemDe = (sku) => itemPorSku(estadoRef.current.catalogo?.cardapio ?? [], sku)
  // #1510: −/+ seguidos do mesmo sku entram em fila; cada toque conta a partir do resultado do
  // anterior e só sai depois dele (antes iam juntos com a mesma contagem e o último a chegar valia).
  const filaDoSaldo = new Map() // sku -> { alvo, fim }

  async function recarregarCardapio() {
    try {
      despachar({ tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: await lerCardapio() })
    } catch (erro) {
      avisar(`Cardápio: ${erro.message}`)
    }
  }

  const gravar = async (rotulo, chamada) => {
    try {
      await chamada()
      return true
    } catch (erro) {
      avisar(`${rotulo}: ${erro.message}`)
      return false
    } finally {
      await recarregarCardapio()
    }
  }

  return {
    recarregarCardapio,

    alternarDisponibilidade: (sku) => {
      const item = itemDe(sku)
      if (!item) return Promise.resolve(false)
      const disponivel = !disponivelHoje(item)
      despachar({ tipo: acao.ALTERNAR_DISPONIBILIDADE, sku })
      return gravar('Cardápio do dia', () => definirDisponibilidade(sku, disponivel))
    },

    // Os botões −/+ mandam o passo; a API quer a contagem (ela está contando o congelador).
    ajustarSaldo: (sku, delta) => {
      const item = itemDe(sku)
      if (!item) return Promise.resolve(false)
      if (semControleDeSaldo(item)) {
        avisar(`${item.nome}: este item não controla saldo no EasyStok.`)
        return Promise.resolve(false)
      }
      const pendente = filaDoSaldo.get(sku)
      const contada = Math.max((pendente ? pendente.alvo : item.estoque) + delta, 0)
      despachar({ tipo: acao.AJUSTAR_SALDO, sku, delta })
      const fim = (pendente?.fim ?? Promise.resolve())
        .then(() => gravar('Saldo', () => ajustarSaldoDoItem(sku, contada, MOTIVO_DO_BALCAO)))
      const registro = { alvo: contada, fim }
      filaDoSaldo.set(sku, registro)
      fim.then(() => { if (filaDoSaldo.get(sku) === registro) filaDoSaldo.delete(sku) })
      return fim
    },

    // Adicionais e "novidade até" não têm campo no EasyStok: ficam de fora do corpo.
    incluirItemCardapio: (dados) => gravar('Cardápio', () => incluirItem(dados)),
    editarItemCardapio: (sku, dados) => gravar('Cardápio', () => editarItem(sku, dados)),

    // Tira ou repõe pela mesma ação, como na tela. M1.2 (D-M1-07): tirar arquiva, nunca apaga, e
    // não mexe no "no site" (ocultar fica na gestão do cardápio).
    alternarRemocaoItemCardapio: (sku) => {
      const item = itemDe(sku)
      if (!item) return Promise.resolve(false)
      return gravar('Cardápio', () => definirArquivado(sku, !estaRemovido(item)))
    },

    // RN-15: a dona confirma o item novo; o agente passa a oferecer.
    confirmarValidacaoItem: (sku) => gravar('Cardápio', () => validarItem(sku)),

    // M1.3 (#1483): as categorias para o seletor do formulário. Sem permissão (não Gerente), nenhuma.
    listarCategoriasCardapio: () => listarSecoes().then((l) => (l ?? []).map(secaoDaApi)).catch(() => []),

    // O formulário de edição precisa da ficha, que o cardápio da comanda não traz.
    obterItemCardapio: (sku) => obterItem(sku).then(detalheDaApi).catch((erro) => {
      avisar(`Cardápio: ${erro.message}`)
      return null
    }),
  }
}
