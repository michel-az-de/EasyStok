// #2495 (S4, I3): regras da tela Cadastros › Categorias de produto. O GET /api/categorias devolve as raízes
// com as filhas de um nível, e a API não impede ciclo nem terceiro nível: as guardas ficam aqui.
const porNome = (a, b) => a.nome.localeCompare(b.nome, 'pt-BR', { sensitivity: 'base' })
const tipo = (categoria) => categoria?.categoriaPaiId ? 'Subcategoria' : 'Categoria'
const temFilhas = (categoria) => !categoria.categoriaPaiId && (categoria.subCategorias?.length ?? 0) > 0

export const arvoreDeCategorias = (categorias) => (categorias ?? [])
  .filter(c => !c.categoriaPaiId)
  .map(r => ({ ...r, subCategorias: [...(r.subCategorias ?? [])].sort(porNome) }))
  .sort(porNome)

// Destino é sempre uma raiz, nunca a própria nem a atual. Raiz com filhas não se move: viraria terceiro nível.
export const destinosParaMover = (arvore, categoria) => temFilhas(categoria) ? [] : arvore
  .filter(r => r.id !== categoria.id && r.id !== categoria.categoriaPaiId)
  .map(r => ({ valor: r.id, rotulo: r.nome }))

// Medido no Postgres (#2504): excluir raiz com filhas viola a FK do pai. A tela para antes.
export const bloqueioDeExclusao = (categoria) => temFilhas(categoria) ? 'Mova ou exclua as subcategorias antes.' : null

// O PUT troca tudo: leva a descrição e o pai atuais para não apagar o que a tela não edita.
export const corpoDaCategoria = (categoria, mudancas, empresaId) => ({
  id: categoria.id, empresaId, nome: (mudancas.nome ?? categoria.nome).trim(), descricao: categoria.descricao ?? null,
  categoriaPaiId: 'categoriaPaiId' in mudancas ? mudancas.categoriaPaiId : categoria.categoriaPaiId ?? null,
})
export const corpoDaNovaCategoria = (nome, categoriaPaiId, empresaId) => ({ empresaId, nome: nome.trim(), descricao: null, categoriaPaiId: categoriaPaiId ?? null })

// O banco guarda até 120 caracteres (CategoriaConfiguration). O limite vira erro no campo, sem cortar o colado.
export function erroDoNomeDaCategoria(nome) {
  const tamanho = String(nome ?? '').trim().length
  if (!tamanho) return 'Informe o nome.'
  return tamanho > 120 ? `Use até 120 caracteres. Agora tem ${tamanho}.` : null
}

// A API checa produto só por CategoriaId; subcategoria em uso cai na FK e volta 500 (#2504).
export function mensagemDaCategoria(acao, categoria, erro) {
  if (erro?.status === 403) return 'Seu acesso não permite mudar categorias. Peça a quem administra a loja.'
  if (acao === 'excluir' && erro?.status >= 500) return categoria?.categoriaPaiId
    ? `Não deu para excluir ${categoria.nome}. Se algum produto usa esta subcategoria, troque a subcategoria dele antes.`
    : `Não deu para excluir ${categoria?.nome ?? 'esta categoria'}. Tente de novo.`
  return erro?.message || 'Não deu para concluir agora. Tente de novo.'
}

export function sucessoDaCategoria({ acao, categoria, pai }, valor, arvore) {
  const nome = String(valor ?? '').trim()
  if (acao === 'criar') return pai ? `Subcategoria ${nome} criada em ${pai.nome}.` : `Categoria ${nome} criada.`
  if (acao === 'renomear') return `${categoria.nome} agora se chama ${nome}.`
  if (acao === 'mover') return `Subcategoria ${categoria.nome} movida para ${arvore.find(r => r.id === valor)?.nome ?? 'outra categoria'}.`
  return `${tipo(categoria)} ${categoria.nome} excluída.`
}
