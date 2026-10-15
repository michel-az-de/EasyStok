import { detalheDaApi, editarItem, enviarFotoDoPrato, incluirItem, obterItem } from '../infra/api/cardapioApi'
import { listarSecoes, secaoDaApi } from '../infra/api/secoesApi'
import { lerInsumos } from './insumos'
import { listarImagens, publicarImagem } from '../infra/api/acervoApi'

// O editor mantém o erro junto do rascunho e recebe o id criado antes de enviar fotos.
export const lerFichaDoPrato = async (id) => detalheDaApi(await obterItem(id))
export const lerCategoriasDoPrato = async () => ((await listarSecoes()) ?? []).map(secaoDaApi)
export const lerFotosDoAcervo = (pagina, busca) => listarImagens(pagina, { busca, natureza: 'Produto' })
export const salvarFotoDoAcervo = (itemId, foto, galeria = false) => publicarImagem(foto.id, {
  itemId, galeria, capaEsperada: foto.capaEsperada, operacaoId: foto.operacaoId,
})

// M1.4e (#1552): as embalagens que uma porção pode usar (insumo marcado como embalagem, contado por
// unidade). Quem não lê Insumos fica sem a escolha no editor; o resto da ficha abre do mesmo jeito.
export async function lerEmbalagensDaPorcao() {
  try {
    return (await lerInsumos()).filter((i) => i.embalagem && i.unidade === 'Un').map((i) => ({ id: i.id, nome: i.nome }))
  } catch {
    return []
  }
}
export async function salvarFichaDoPrato(id, dados) {
  if (id) { await editarItem(id, dados); return id }
  const criado = await incluirItem(dados)
  if (!criado?.itemId) throw new Error('O cadastro não devolveu a confirmação. Recarregue o cardápio antes de tentar novamente.')
  return criado.itemId
}

export async function salvarFotoDoPrato(id, arquivo, galeria = false) {
  if (!arquivo || !['image/jpeg', 'image/png', 'image/webp'].includes(arquivo.type)) throw new Error('Escolha uma foto JPG, PNG ou WebP.')
  if (arquivo.size > 6 * 1024 * 1024) throw new Error('A foto deve ter até 6 MB.')
  await enviarFotoDoPrato(id, arquivo, galeria)
}
