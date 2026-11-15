import { useCallback, useEffect, useState } from 'react'
import { obterLojaDaEmpresa } from '../infra/api/impressosApi'
import { gravarPixDaLoja, obterPixDaLoja, removerPixDaLoja } from '../infra/api/pixDiretoApi'

// #2233: cadastro da chave Pix da loja (aba Integrações). A tela nunca guarda a chave depois de
// salvar: o que volta da API é só o tipo e o final.
export function useChavePixLoja() {
  const [estado, setEstado] = useState({ lojaId: null, pix: null, erro: null, ocupado: false })

  useEffect(() => {
    let vivo = true
    obterLojaDaEmpresa()
      .then((loja) => obterPixDaLoja(loja.id).then((pix) => { if (vivo) setEstado((e) => ({ ...e, lojaId: loja.id, pix })) }))
      .catch((erro) => { if (vivo) setEstado((e) => ({ ...e, erro: erro.message })) })
    return () => { vivo = false }
  }, [])

  const rodar = useCallback(async (acao) => {
    setEstado((e) => ({ ...e, ocupado: true, erro: null }))
    try {
      const pix = await acao(estado.lojaId)
      setEstado((e) => ({ ...e, pix, ocupado: false }))
      return true
    } catch (erro) {
      setEstado((e) => ({ ...e, erro: erro.message, ocupado: false }))
      return false
    }
  }, [estado.lojaId])

  return {
    ...estado,
    salvar: (dados) => rodar((lojaId) => gravarPixDaLoja(lojaId, dados)),
    remover: () => rodar((lojaId) => removerPixDaLoja(lojaId)),
  }
}
