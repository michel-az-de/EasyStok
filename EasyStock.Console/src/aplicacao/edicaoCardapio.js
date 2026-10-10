import { detalheDaApi, editarItem, enviarFotoDoPrato, incluirItem, obterItem } from '../infra/api/cardapioApi'
import { listarSecoes, secaoDaApi } from '../infra/api/secoesApi'

// O editor mantém o erro junto do rascunho e recebe o id criado antes de enviar fotos.
export const lerFichaDoPrato = async (id) => detalheDaApi(await obterItem(id))
export const lerCategoriasDoPrato = async () => ((await listarSecoes()) ?? []).map(secaoDaApi)
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
