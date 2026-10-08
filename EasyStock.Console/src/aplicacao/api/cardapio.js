import * as acao from '../acoes'
import { itemPorSku, semControleDeSaldo, disponivelHoje } from '../../dominio/cardapio'
import { listarCardapio } from '../../infra/api/comandaApi'
import { ajustarSaldoDoItem, definirDisponibilidade } from '../../infra/api/cardapioApi'

// Cardápio do dia no modo API (#1241, F11, S45/S17). Antes, ligar/desligar e o saldo só mudavam
// a memória do navegador e voltavam na releitura do minuto seguinte. Agora: a tela responde na
// hora (mesmo caso local), grava no EasyStok e relê o cardápio, certo ou errado. Se o EasyStok
// recusar, a releitura devolve a tela ao que ficou salvo e a faixa avisa.
const MOTIVO_DO_BALCAO = 'Ajuste de saldo pelo balcão (console)'

export function criarAcoesCardapioApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const itemDe = (sku) => itemPorSku(estadoRef.current.catalogo?.cardapio ?? [], sku)

  async function recarregarCardapio() {
    try {
      despachar({ tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: await listarCardapio() })
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
      const contada = Math.max(item.estoque + delta, 0)
      despachar({ tipo: acao.AJUSTAR_SALDO, sku, delta })
      return gravar('Saldo', () => ajustarSaldoDoItem(sku, contada, MOTIVO_DO_BALCAO))
    },
  }
}
