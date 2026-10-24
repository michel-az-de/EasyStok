import * as acao from '../acoes'
import { mapear } from '../reducer/auxiliares'

// Tags do cliente como chips. O modo API também despacha estes casos (`aplicacao/api/tags.js`).

export const casosTags = {
  // Tags como chips (seção 2). Nomes de tag são o próprio identificador: sem
  // duplicar ao adicionar, sem sobra ao editar ou remover. A UI já corta tag
  // vazia antes de despachar (TagsDoCliente.jsx); a trava aqui é a mesma
  // regra valendo pra quem chamar a ação direto, sem passar pela tela.
  [acao.ADICIONAR_TAG]: (estado, { id, tag }) => {
    const tagLimpa = (tag ?? '').trim()
    if (!tagLimpa) return estado
    return mapear(estado, id, (c) => (
      c.cliente.tags.includes(tagLimpa)
        ? c
        : { ...c, cliente: { ...c.cliente, tags: [...c.cliente.tags, tagLimpa] } }
    ))
  },

  [acao.REMOVER_TAG]: (estado, { id, tag }) =>
    mapear(estado, id, (c) => ({
      ...c, cliente: { ...c.cliente, tags: c.cliente.tags.filter((t) => t !== tag) },
    })),

  [acao.EDITAR_TAG]: (estado, { id, tagAntiga, tagNova }) =>
    mapear(estado, id, (c) => ({
      ...c,
      cliente: { ...c.cliente, tags: c.cliente.tags.map((t) => (t === tagAntiga ? tagNova : t)) },
    })),
}
