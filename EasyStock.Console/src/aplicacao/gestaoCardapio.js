import { ordemAoMover } from '../dominio/cardapio'
import { definirDisponibilidade, definirOrdem, definirVisivel, itemGestaoDaApi, listarGestao } from '../infra/api/cardapioApi'

// Gestão do cardápio no modo API (M1.1, #1481). A lista vem do EasyStok e é relida depois de cada
// gravação, certa ou errada: a tela mostra o que ficou salvo. Cada ação devolve `true` quando
// gravou e entrega o erro a `aoErro` quando não. Separado do hook para a prova rodar sem React.
export const lerGestao = async () => ((await listarGestao()) ?? []).map(itemGestaoDaApi)

export function criarGestaoCardapio({ obterItens, recarregar, aoErro }) {
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
  const itemDe = (sku) => obterItens().find((i) => i.sku === sku) ?? null

  return {
    alternarHoje: (sku) => {
      const item = itemDe(sku)
      return item ? gravar(() => definirDisponibilidade(sku, !item.hoje)) : Promise.resolve(false)
    },
    alternarNoSite: (sku) => {
      const item = itemDe(sku)
      return item ? gravar(() => definirVisivel(sku, !item.noSite)) : Promise.resolve(false)
    },
    mover: (sku, direcao) => {
      const lista = obterItens()
      const novaOrdem = ordemAoMover(lista, lista.findIndex((i) => i.sku === sku), direcao)
      return novaOrdem === null ? Promise.resolve(false) : gravar(() => definirOrdem(sku, novaOrdem))
    },
  }
}
