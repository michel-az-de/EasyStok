import { useCallback, useEffect, useState } from 'react'
import * as api from '../infra/api/entregasApi'

// Cadastro de entrega da loja (S45, policy Admin): janelas, zonas de frete e
// bloqueios dos próximos 60 dias. Operador sem Admin recebe 403 e a tela diz isso.
const DIAS_BLOQUEIO = 60
const dataIso = (d) => d.toISOString().slice(0, 10)

export function useCadastroEntregaApi() {
  const [janelas, setJanelas] = useState(null)
  const [zonas, setZonas] = useState([])
  const [bloqueios, setBloqueios] = useState([])
  const [erro, setErro] = useState(null)

  const recarregar = useCallback(() => {
    const hoje = new Date()
    const ate = new Date(hoje.getTime() + DIAS_BLOQUEIO * 86400000)
    return Promise.all([api.listarJanelas(), api.listarZonas(), api.listarBloqueios(dataIso(hoje), dataIso(ate))])
      .then(([j, z, b]) => {
        setJanelas(j ?? [])
        setZonas(z ?? [])
        setBloqueios(b ?? [])
        setErro(null)
      })
      .catch((e) => setErro(`O cadastro não carregou: ${e.message}`))
  }, [])

  useEffect(() => { recarregar() }, [recarregar])

  const executar = (chamada, falha) => chamada()
    .then(() => recarregar())
    .catch((e) => setErro(`${falha}: ${e.message}`))

  const acoes = {
    criarJanela: (corpo) => executar(() => api.criarJanela(corpo), 'A janela não foi criada'),
    alternarJanela: (j) => executar(() => api.definirJanelaAtiva(j.id, !j.ativa), 'A janela não mudou'),
    criarZona: (corpo) => executar(() => api.criarZona(corpo), 'A zona não foi criada'),
    alternarZona: (z) => executar(() => api.definirZonaAtiva(z.id, !z.ativa), 'A zona não mudou'),
    criarBloqueio: (corpo) => executar(() => api.criarBloqueio(corpo), 'O bloqueio não foi criado'),
    removerBloqueio: (id) => executar(() => api.removerBloqueio(id), 'O bloqueio não foi removido'),
  }

  return { janelas, zonas, bloqueios, erro, acoes, limparErro: () => setErro(null) }
}
