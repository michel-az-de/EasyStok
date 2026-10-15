import {
  criarSecao, definirSecaoVisivel, excluirSecao, listarSecoes, migrarCategorias, moverSecao, previaDaApi,
  previaMigracaoCategorias, renomearSecao, secaoDaApi,
} from '../infra/api/secoesApi'

// Categorias do cardápio no modo API (M1.3, #1483). Mesmo molde da gestão de itens: grava, relê
// sempre e devolve `true` quando gravou; o erro vai para `aoErro`. Separado do hook para a prova.
export const lerSecoes = async () => ((await listarSecoes()) ?? []).map(secaoDaApi)
export const lerPreviaMigracao = async () => previaDaApi(await previaMigracaoCategorias())

const contar = (n, um, varios) => `${n} ${n === 1 ? um : varios}`

// #1803: sem seção, a loja ainda tem as categorias em texto livre nos pratos. O resumo diz quais
// são e o que "Converter" faz, em vez de "nenhuma categoria". null quando não há nada a converter.
export function resumoCategoriasAntigas(previa) {
  if (!previa?.pratos || !previa.categorias?.length) return null
  const { categorias, pratos } = previa
  const novas = categorias.filter((c) => !c.existe).length
  const texto = `Os pratos já usam ${contar(categorias.length, 'categoria antiga', 'categorias antigas')}: `
    + `${categorias.map((c) => c.nome).join(', ')} (${contar(pratos, 'prato', 'pratos')}).`
  const liga = pratos === 1 ? 'liga o prato' : `liga os ${pratos} pratos`
  const acao = novas
    ? `cria ${contar(novas, 'categoria', 'categorias')} e ${liga}${novas < categorias.length ? ', usando as que já existem com o mesmo nome' : ''}`
    : `${liga} às categorias que já existem com o mesmo nome`
  const efeito = `Converter ${acao}. Uma categoria por nome; o texto antigo fica guardado e dá para repetir sem duplicar.`
  return { texto, efeito, novas, pratos }
}

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
