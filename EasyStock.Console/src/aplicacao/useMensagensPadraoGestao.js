import { useCallback, useEffect, useState } from 'react'
import { arquivarMensagemPadrao, criarMensagemPadrao, editarMensagemPadrao, listarMensagensPadrao } from '../infra/api/mensagensPadraoApi'

// Lista e grava as mensagens padrão da Gestão > Respostas (#3206). Cada gravação devolve { ok, erro }: o erro é o
// texto que o servidor mandou (400 da regra, 409 de nome repetido), para a tela mostrar como veio.
export function useMensagensPadraoGestao(ativo) {
  const [itens, setItens] = useState(null)
  const [erroDaLista, setErroDaLista] = useState('')

  const recarregar = useCallback(async () => {
    try {
      setItens((await listarMensagensPadrao()) ?? [])
      setErroDaLista('')
    } catch (erro) {
      setErroDaLista(erro?.message || 'Não foi possível carregar as mensagens padrão.')
    }
  }, [])

  useEffect(() => { if (ativo) recarregar() }, [ativo, recarregar])

  const gravar = useCallback(async (chamada) => {
    try { await chamada(); await recarregar(); return { ok: true } }
    catch (erro) { return { ok: false, erro: erro?.message || 'Não foi possível salvar.' } }
  }, [recarregar])

  return {
    itens, erroDaLista, recarregar,
    criar: (corpo) => gravar(() => criarMensagemPadrao(corpo)),
    editar: (id, corpo) => gravar(() => editarMensagemPadrao(id, corpo)),
    arquivar: (id, arquivado) => gravar(() => arquivarMensagemPadrao(id, arquivado)),
  }
}
