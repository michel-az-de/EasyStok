import { createContext, useContext, useEffect, useState } from 'react'
import { chamarApi } from '../infra/api/cliente'
import { FONTE_API } from '../infra/fonteDados'
import { entradaDoPerfil } from '../dominio/acessoModulos'

export const ContextoAcessoModulos = createContext({ permite: () => !FONTE_API, acoes: {}, aoSair: null })
export const useAcessoModulos = () => useContext(ContextoAcessoModulos)

export function useModulosDaSessao(sessao) {
  const [estado, setEstado] = useState({ dados: null, erro: null })
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    let ativo = true
    chamarApi('/api/auth/me/modulos').then((dados) => {
      if (!ativo) return
      if (!Array.isArray(dados?.modulos)) throw new Error('A lista de módulos não foi recebida.')
      if (!window.location.hash || window.location.hash === '#/') window.location.hash = entradaDoPerfil(dados)
      setEstado({ dados, erro: null })
    }).catch((erro) => { if (ativo) setEstado({ dados: null, erro: erro.message }) })
    return () => { ativo = false }
  }, [sessao.token, tentativa])
  return { ...estado, tentarNovamente: () => {
    setEstado({ dados: null, erro: null })
    setTentativa((t) => t + 1)
  } }
}
