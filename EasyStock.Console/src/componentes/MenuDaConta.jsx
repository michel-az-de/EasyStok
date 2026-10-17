import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { useEscape } from '../hooks/useEscape'
import { Avatar } from './Avatar'
import { Botao } from './Botao'
import css from './MenuDaConta.module.css'

// Onde a camada abre, em coordenadas da janela. `fixed` porque o trilho rola (overflow) e
// recortaria uma camada absoluta que sai pela direita dele.
function posicaoDaCamada(gatilho, lado) {
  const r = gatilho.getBoundingClientRect()
  if (lado === 'abaixo') return { top: r.bottom + 8, right: Math.max(8, window.innerWidth - r.right) }
  return { left: r.right + 10, bottom: Math.max(8, window.innerHeight - r.bottom) }
}

// #1870: o "Sair" mora num lugar só, atrás do avatar de quem está logado: no pé do trilho do
// Balcão e dos módulos, e no canto do hall, que não tem trilho. Quem é e em qual casa saem
// do rodapé fixo e vêm para cá. `lado` diz para onde a camada abre: à direita do trilho ou
// abaixo do canto do hall. `children` entra antes do Sair (o tema, no trilho do Balcão). Sem
// `aoSair` (modo demonstração) a camada mostra só o nome e o que vier em `children`.
export function MenuDaConta({ nome, detalhe, aoSair, rotuloSair = 'Sair', tituloSair, lado = 'direita', children }) {
  const [aberto, setAberto] = useState(false)
  const [posicao, setPosicao] = useState(null)
  const caixaRef = useRef(null)
  const botaoRef = useRef(null)
  const id = useId()
  const fechar = useCallback(() => {
    setAberto(false)
    botaoRef.current?.focus()
  }, [])
  useEscape(aberto, fechar)
  useLayoutEffect(() => {
    if (!aberto || !botaoRef.current) return undefined
    const medir = () => setPosicao(posicaoDaCamada(botaoRef.current, lado))
    medir()
    window.addEventListener('resize', medir)
    return () => window.removeEventListener('resize', medir)
  }, [aberto, lado])
  useEffect(() => {
    if (!aberto) return undefined
    const aoTocarFora = (evento) => { if (!caixaRef.current?.contains(evento.target)) setAberto(false) }
    document.addEventListener('pointerdown', aoTocarFora)
    return () => document.removeEventListener('pointerdown', aoTocarFora)
  }, [aberto])
  if (!nome) return null
  return (
    <div ref={caixaRef} className={`${css.conta} ${css[lado]}`}>
      <button ref={botaoRef} type="button" className={css.gatilho} aria-expanded={aberto} aria-controls={id}
        title={`${nome}: conta e sair`} onClick={() => setAberto((atual) => !atual)}>
        <Avatar nome={nome} tamanho="medio" />
        <span className="sr">Conta de {nome}</span>
      </button>
      {aberto && (
        <div id={id} className={css.camada} role="group" aria-label="Conta" style={posicao ?? { visibility: 'hidden' }}>
          <p className={css.quem}>
            <strong>{nome}</strong>
            {detalhe && <small>{detalhe}</small>}
          </p>
          {children}
          {aoSair && <Botao icone="log-out" className={css.sair} title={tituloSair} onClick={aoSair}>{rotuloSair}</Botao>}
        </div>
      )}
    </div>
  )
}
