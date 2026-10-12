import {
  criarSecao, definirSecaoVisivel, excluirSecao, listarSecoes, migrarCategorias, moverSecao, renomearSecao, secaoDaApi,
} from '../infra/api/secoesApi'

// Categorias do cardápio no modo API (M1.3, #1483). Mesmo molde da gestão de itens: grava, relê
// sempre e devolve `true` quando gravou; o erro vai para `aoErro`. Separado do hook para a prova.
export const lerSecoes = async () => ((await listarSecoes()) ?? []).map(secaoDaApi)

export function criarGestaoSecoes({ obterSecoes, recarregar, aoErro, aoAviso = () => {} }) {
  const gravar = async (chamada) => {
    try {
      return (await chamada()) ?? true
    } catch (erro) {
      aoErro(erro.message)
      return false
    } finally {
      await recarregar()
    }
  }
  const secaoDe = (id) => obterSecoes().find((s) => s.id === id) ?? null

  return {
    criar: (nome) => (nome?.trim() ? gravar(() => criarSecao(nome.trim()).then(() => true)) : Promise.resolve(false)),
    renomear: (id, nome) => (nome?.trim() ? gravar(() => renomearSecao(id, nome.trim()).then(() => true)) : Promise.resolve(false)),
    alternarVisivel: (id) => {
      const secao = secaoDe(id)
      return secao ? gravar(() => definirSecaoVisivel(id, !secao.visivel).then(() => true)) : Promise.resolve(false)
    },
    mover: (id, direcao) => gravar(() => moverSecao(id, direcao).then(() => true)),
    excluir: (id) => gravar(() => excluirSecao(id).then(() => true)),
    migrar: () => gravar(async () => {
      const r = await migrarCategorias()
      aoAviso(r && (r.secoesCriadas || r.itensLigados)
        ? `${r.secoesCriadas} categoria(s) criada(s) e ${r.itensLigados} prato(s) ligado(s).`
        : 'Nada a converter: os pratos já estão nas categorias.')
      return true
    }),
  }
}
