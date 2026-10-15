import { useEffect, useState } from 'react'
import { Botao } from '../componentes/Botao'
import css from './atualizacao.module.css'

export function AtualizacaoDoConsole() {
  const [novaVersao, setNovaVersao] = useState(null)
  useEffect(() => {
    const atual = document.querySelector('meta[name="easystok-console-sha"]')?.content
    if (!/^[a-f0-9]{40}$/.test(atual ?? '')) return
    let ativo = true, pendente = false, ultimaConsulta = 0, controlador
    const verificar = async () => {
      if (document.hidden || pendente || Date.now() - ultimaConsulta < 5000) return
      ultimaConsulta = Date.now()
      pendente = true
      controlador = new AbortController()
      const limite = setTimeout(() => controlador.abort(), 10000)
      try {
        const resposta = await fetch('/versao.json', { cache: 'no-store', credentials: 'omit', signal: controlador.signal })
        if (!resposta.ok) return
        const { sha } = await resposta.json()
        if (ativo && /^[a-f0-9]{40}$/.test(sha ?? '')) setNovaVersao(sha !== atual ? sha : null)
      } catch { /* Sem rede, a tela e os formulários continuam disponíveis. */ }
      finally { clearTimeout(limite); pendente = false }
    }
    verificar()
    const intervalo = setInterval(verificar, 60000)
    window.addEventListener('focus', verificar)
    window.addEventListener('hashchange', verificar)
    document.addEventListener('visibilitychange', verificar)
    return () => {
      ativo = false
      controlador?.abort()
      clearInterval(intervalo)
      window.removeEventListener('focus', verificar)
      window.removeEventListener('hashchange', verificar)
      document.removeEventListener('visibilitychange', verificar)
    }
  }, [])
  if (!novaVersao) return null
  return <aside className={css.aviso} aria-label="Atualização do sistema">
    <p role="status">Há uma nova versão. Conclua o que está preenchendo e atualize.</p>
    <Botao onClick={() => {
      const destino = new URL(window.location.href)
      destino.searchParams.set('versao', novaVersao)
      window.location.replace(destino.href)
    }}>Atualizar sistema</Botao>
  </aside>
}
