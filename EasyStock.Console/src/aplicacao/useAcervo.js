import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/acervoApi'
import { listarGestao, obterItem } from '../infra/api/cardapioApi'
import { urlDeExibicaoDaFoto } from '../dominio/vitrineCardapio'
import { API_BASE } from '../infra/fonteDados'

export function useAcervo() {
  const [imagens, setImagens] = useState([])
  const [itens, setItens] = useState([])
  const [solicitacoes, setSolicitacoes] = useState([])
  const [pagina, setPagina] = useState(0)
  const [erro, setErro] = useState('')
  const [carregando, setCarregando] = useState(true)
  const [ocupado, setOcupado] = useState(false)
  const [aviso, setAviso] = useState('')
  const requisicao = useRef(0)
  const trava = useRef(false)
  const carregar = useCallback(async () => {
    const atual = ++requisicao.current
    setCarregando(true)
    try {
      const [lista, cardapio, pedidos] = await Promise.all([api.listarImagens(pagina), listarGestao(), api.listarSolicitacoes()])
      if (atual === requisicao.current) { setImagens(lista); setItens(cardapio); setSolicitacoes(pedidos); setAviso('') }
    } finally { if (atual === requisicao.current) setCarregando(false) }
  }, [pagina])
  useEffect(() => {
    let ativo = true
    carregar().catch((e) => { if (ativo) setErro(e.message) })
    return () => { ativo = false; requisicao.current++ }
  }, [carregar])
  async function fazer(acao) {
    if (trava.current) return false
    trava.current = true
    setOcupado(true); setErro('')
    try {
      const r = await acao()
      try { await carregar() } catch { setAviso('Operação concluída. Não foi possível atualizar a lista; use Atualizar acervo.') }
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
  return { imagens, itens, solicitacoes, pagina, setPagina, erro, setErro, aviso, carregar, carregando, ocupado, fazer, baixar,
    api, obterItem, fotoUrl: (url) => urlDeExibicaoDaFoto(url, API_BASE) }
}

export function useMiniaturaAcervo(id) {
  const [resultado, setResultado] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    let ativo = true; let objectUrl
    setResultado(null)
    if (!id) return undefined
    api.miniaturaImagem(id).then((blob) => {
      if (ativo) { objectUrl = URL.createObjectURL(blob); setResultado({ id, url: objectUrl, erro: false }) }
    }).catch(() => { if (ativo) setResultado({ id, url: null, erro: true }) })
    return () => { ativo = false; if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [id, tentativa])
  return { url: resultado && resultado.id === id ? resultado.url : null, erro: Boolean(resultado && resultado.id === id && resultado.erro),
    tentar: () => setTentativa((n) => n + 1) }
}
