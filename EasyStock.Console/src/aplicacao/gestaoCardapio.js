import { podeMover } from '../dominio/cardapio'
import {
  definirArquivado, definirDisponibilidade, definirVisivel, itemGestaoDaApi, listarGestao, moverItem, validarItem,
} from '../infra/api/cardapioApi'

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
    // M1.2 (#1482): tirar arquiva e repor volta; validar libera o item novo para o agente.
    alternarArquivado: (sku) => {
      const item = itemDe(sku)
      return item ? gravar(() => definirArquivado(sku, !item.arquivado)) : Promise.resolve(false)
    },
    validar: (sku) => gravar(() => validarItem(sku)),
    mover: (sku, direcao) => {
      const lista = obterItens()
      return podeMover(lista, lista.findIndex((i) => i.sku === sku), direcao)
        ? gravar(() => moverItem(sku, direcao))
        : Promise.resolve(false)
    },
  }
}
