import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'
import { dataIsoNoFuso } from '../dominio/formato'

// Cadastro de entrega da loja (S45, policy Admin): janelas, zonas de frete e
// bloqueios dos próximos 60 dias. Operador sem Admin recebe 403 e a tela diz isso.
const DIAS_BLOQUEIO = 60
// Dia da loja (F07, item 8): em UTC, depois das 21 h o "hoje" já era amanhã.
const dataIso = (d) => dataIsoNoFuso(d.getTime())

export function useCadastroEntregaApi() {
  const [janelas, setJanelas] = useState(null)
  const [zonas, setZonas] = useState([])
  const [bloqueios, setBloqueios] = useState([])
  const [erro, setErro] = useState(null)
  // #1510: um envio por vez; toque duplo em "Criar janela" duplicava a janela (overbooking).
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

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

  // Devolve se deu certo: o formulário só limpa (ou fecha) quando a API aceitou.
  // A lista recarrega também na falha: na criação em série parte das janelas já entrou.
  const executar = async (chamada, falha) => {
    if (enviandoRef.current) return false
    enviandoRef.current = true
    setEnviando(true)
    let motivo = null
    try {
      await chamada()
    } catch (e) {
      motivo = `${falha}: ${e.message}`
    } finally {
      await recarregar()
      if (motivo) setErro(motivo)
      enviandoRef.current = false
      setEnviando(false)
    }
    return !motivo
  }

  const acoes = {
    criarJanela: (corpo) => executar(() => api.criarJanela(corpo), 'A janela não foi criada'),
    alternarJanela: (j) => executar(() => api.definirJanelaAtiva(j.id, !j.ativa), 'A janela não mudou'),
    // #1440: uma janela por dia marcado, em sequência (a lista recarrega no fim, com o que entrou).
    criarJanelas: (corpos) => executar(
      () => corpos.reduce((anterior, corpo) => anterior.then(() => api.criarJanela(corpo)), Promise.resolve()),
      'A janela não foi criada',
    ),
    atualizarJanela: (id, corpo) => executar(() => api.atualizarJanela(id, corpo), 'A janela não mudou'),
    excluirJanela: (id) => executar(() => api.excluirJanela(id), 'A janela não foi excluída'),
    criarZona: (corpo) => executar(() => api.criarZona(corpo), 'A zona não foi criada'),
    alternarZona: (z) => executar(() => api.definirZonaAtiva(z.id, !z.ativa), 'A zona não mudou'),
    criarBloqueio: (corpo) => executar(() => api.criarBloqueio(corpo), 'O bloqueio não foi criado'),
    removerBloqueio: (id) => executar(() => api.removerBloqueio(id), 'O bloqueio não foi removido'),
  }

  return { janelas, zonas, bloqueios, erro, enviando, acoes, limparErro: () => setErro(null) }
}
