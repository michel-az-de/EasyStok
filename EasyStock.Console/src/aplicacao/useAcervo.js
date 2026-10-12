import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/acervoApi'
import { listarGestao, obterItem } from '../infra/api/cardapioApi'
import { urlDeExibicaoDaFoto } from '../dominio/vitrineCardapio'
import { API_BASE } from '../infra/fonteDados'

export function useAcervo() {
  const [imagens, setImagens] = useState([])
  const [itens, setItens] = useState([])
  const [solicitacoes, setSolicitacoes] = useState([])
  const [briefings, setBriefings] = useState([])
  const [cobertura, setCobertura] = useState([])
  const [usos, setUsos] = useState([])
  const [filtros, definirFiltros] = useState({})
  const [pagina, setPagina] = useState(0)
  const [erro, setErro] = useState('')
  const [carregando, setCarregando] = useState(true)
  const [ocupado, setOcupado] = useState(false)
  const [aviso, setAviso] = useState('')
  const requisicao = useRef(0)
  const trava = useRef(false)
  const carregar = useCallback(async (aposOperacao = false) => {
    if (trava.current && !aposOperacao) return
    const atual = ++requisicao.current
    setCarregando(true)
    try {
      const [lista, cardapio, pedidos, briefs, mapa, usosConfirmados] = await Promise.all([api.listarImagens(pagina, filtros), listarGestao(), api.listarSolicitacoes(), api.listarBriefings(), api.listarCobertura(), api.listarUsos()])
      if (atual === requisicao.current) { setImagens(lista); setItens(cardapio); setSolicitacoes(pedidos); setBriefings(briefs); setCobertura(mapa); setUsos(usosConfirmados); setAviso(''); setErro('') }
    } catch (e) { if (atual === requisicao.current) setErro(e.message); throw e }
    finally { if (atual === requisicao.current) setCarregando(false) }
  }, [pagina, filtros])
  function setFiltros(novo) { setPagina(0); definirFiltros(novo) }
  useEffect(() => {
    carregar().catch(() => {})
    return () => { requisicao.current++ }
  }, [carregar])
  async function fazer(acao) {
    if (trava.current) return false
    trava.current = true
    requisicao.current++
    setCarregando(false)
    setOcupado(true); setErro('')
    try {
      const r = await acao()
      try { await carregar(true) } catch { setAviso('Operação concluída. Não foi possível atualizar a lista; use Atualizar acervo.') }
      return r ?? true
    }
    catch (e) { setErro(e.message); return false }
    finally { trava.current = false; setOcupado(false) }
  }
  async function baixar(id, nome) {
    return fazer(async () => {
      const blob = await api.arquivoImagem(id)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a'); link.href = url
      link.download = `${nome.replace(/[^a-z0-9-]/gi, '_')}.${blob.type === 'image/png' ? 'png' : blob.type === 'image/webp' ? 'webp' : 'jpg'}`
      link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000)
    })
  }
  return { imagens, itens, solicitacoes, briefings, cobertura, usos, filtros, setFiltros, pagina, setPagina, erro, setErro, aviso, carregar, carregando, ocupado, fazer, baixar,
    api, obterItem, fotoUrl: (url) => urlDeExibicaoDaFoto(url, API_BASE) }
}

export function useMiniaturaAcervo(id) {
  const [resultado, setResultado] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    let ativo = true; let objectUrl
    setResultado(null)
    if (!id) return undefined
    api.miniaturaImagem(id).then(async (blob) => {
      if (!ativo) return
      objectUrl = URL.createObjectURL(blob)
      const imagem = new Image()
      imagem.src = objectUrl
      await imagem.decode()
      if (ativo) setResultado({ id, url: objectUrl, erro: false })
    }).catch(() => {
      if (objectUrl) URL.revokeObjectURL(objectUrl)
      if (ativo) setResultado({ id, url: null, erro: true })
    })
    return () => { ativo = false; if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [id, tentativa])
  return { url: resultado && resultado.id === id ? resultado.url : null, erro: Boolean(resultado && resultado.id === id && resultado.erro),
    tentar: () => setTentativa((n) => n + 1) }
}
