import { useCallback, useEffect, useRef, useState } from 'react'
import {
  adiarConfiguracaoInicial, atualizarMeuNome, concluirConfiguracaoInicial, marcarPassoDaConfiguracaoInicial, obterConfiguracaoInicial,
} from '../infra/api/configuracaoInicialApi'
import { gravarSessao, lerSessao } from '../infra/api/sessao'

// Primeiros passos (#2070) no modo API, no padrão de `useIntegracoesApi`: fora do `useAcoes`, porque a entrada e o
// hall montam fora do Balcão. Cada ação relê o estado depois; o erro da ação volta para quem chamou mostrar.
export function useConfiguracaoInicialApi({ ativo = true } = {}) {
  const [estado, setEstado] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(null)
  const vivo = useRef(true)
  useEffect(() => {
    vivo.current = true
    return () => { vivo.current = false }
  }, [])

  const recarregar = useCallback(() => obterConfiguracaoInicial()
    .then((lido) => {
      if (vivo.current) { setEstado(lido); setErro(null) }
      return lido
    })
    .catch((e) => {
      if (vivo.current) setErro(e.message)
      return null
    }), [])

  useEffect(() => { if (ativo) recarregar() }, [ativo, recarregar])

  const executar = useCallback(async (qual, chamada) => {
    setOcupado(qual)
    try {
      await chamada()
      return { ok: true }
    } catch (e) {
      return { ok: false, erro: e }
    } finally {
      await recarregar()
      if (vivo.current) setOcupado(null)
    }
  }, [recarregar])

  return {
    estado, erro, ocupado, recarregar,
    marcar: (passo, situacao) => executar(passo, () => marcarPassoDaConfiguracaoInicial(passo, situacao)),
    adiar: () => executar('adiar', adiarConfiguracaoInicial),
    concluir: () => executar('concluir', concluirConfiguracaoInicial),
  }
}

// Boas-vindas: a dona confirma o próprio nome. A sessão passa a mostrar o nome novo (topo, hall, saudação).
export async function confirmarMeuNome(nome) {
  const salvo = (await atualizarMeuNome(nome))?.nome ?? nome
  const sessao = lerSessao()
  if (sessao?.usuario) gravarSessao({ ...sessao, usuario: { ...sessao.usuario, nome: salvo } })
  return salvo
}
