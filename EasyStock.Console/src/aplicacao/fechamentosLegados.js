import { useCallback, useEffect, useState } from 'react'
import { listarFechamentosLegados } from '../infra/api/fechamentosLegadosApi'
import { fechamentosLegadosDaApi } from '../dominio/fechamentosLegados'

// #2813 (B4): leitura dos fechamentos do aparelho antigo. Só lê; não há caso de escrita aqui.
export const carregarFechamentosLegados = async (sinal) => fechamentosLegadosDaApi(await listarFechamentosLegados(sinal))

const CARREGANDO = { carregando: true, erro: null, itens: [], ignorados: 0 }

// Lê ao abrir a tela e de novo em "Tentar de novo". Sair da tela cancela a leitura em curso, e resposta de leitura
// cancelada não mexe no estado. Lista vazia sem leitura concluída é "carregando" ou "erro", nunca "nenhum fechamento".
export function useFechamentosLegados() {
  const [estado, setEstado] = useState(CARREGANDO)
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    const controle = new AbortController()
    carregarFechamentosLegados(controle.signal)
      .then((lista) => { if (!controle.signal.aborted) setEstado({ carregando: false, erro: null, ...lista }) })
      .catch((falha) => {
        if (controle.signal.aborted) return
        setEstado({ ...CARREGANDO, carregando: false, erro: falha?.message || 'Não deu certo. Tente de novo.' })
      })
    return () => controle.abort()
  }, [tentativa])
  const tentarDeNovo = useCallback(() => {
    setEstado(CARREGANDO)
    setTentativa((n) => n + 1)
  }, [])
  return { ...estado, tentarDeNovo }
}
