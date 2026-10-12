import * as acao from '../acoes'
import { obterDossie } from '../../infra/api/conversasApi'
import { adicionarTagCliente, removerTagCliente } from '../../infra/api/tagsClienteApi'
import { clienteDoDossie } from '../../infra/api/traducaoCliente'

// Tags do cliente no modo API (#1441, S24). A tag é do cadastro: lead sem cadastro não
// tem onde guardar. A Ficha muda na hora (mesmo despacho do modo demonstração) e depois
// relê o dossiê, que traz a tag do jeito que o EasyStok normalizou.
export function criarAcoesTagsApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const clienteDe = (id) => estadoRef.current.conversas.find((c) => c.id === id)?.clienteId ?? null

  async function reler(id) {
    const cliente = clienteDoDossie(await obterDossie(id))
    if (cliente) despachar({ tipo: acao.CLIENTE_DA_API, id, ...cliente })
  }

  function comCadastro(id, local, chamada) {
    const clienteId = clienteDe(id)
    if (!clienteId) {
      avisar('Cadastre o cliente (Salvar cadastro na Ficha) antes de marcar tags.')
      return Promise.resolve()
    }
    despachar(local)
    return chamada(clienteId)
      .catch((erro) => avisar(`Tags do cliente: ${erro.message}`))
      .then(() => reler(id))
      .catch(() => {})
  }

  return {
    adicionarTag: (id, tag) => {
      const limpa = String(tag ?? '').trim()
      if (!limpa) return Promise.resolve()
      return comCadastro(id, { tipo: acao.ADICIONAR_TAG, id, tag: limpa }, (clienteId) => adicionarTagCliente(clienteId, limpa))
    },
    removerTag: (id, tag) =>
      comCadastro(id, { tipo: acao.REMOVER_TAG, id, tag }, (clienteId) => removerTagCliente(clienteId, tag)),
    // A API não renomeia: tira a antiga e põe a nova.
    editarTag: (id, tagAntiga, tagNova) =>
      comCadastro(id, { tipo: acao.EDITAR_TAG, id, tagAntiga, tagNova }, async (clienteId) => {
        await removerTagCliente(clienteId, tagAntiga)
        await adicionarTagCliente(clienteId, String(tagNova).trim())
      }),
  }
}
