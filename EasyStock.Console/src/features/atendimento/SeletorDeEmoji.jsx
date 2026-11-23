import { useId, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Popover } from '../../componentes/Popover'
import { CATEGORIAS_DE_EMOJI, buscarEmTodasAsCategorias } from '../../dominio/emojis'
import css from './SeletorDeEmoji.module.css'

// #2833 + #3210: emoji por categoria, com busca por nome em todas (`:massa`, como no WhatsApp Web). Escolher insere
// no cursor do campo e mantém o seletor aberto, para mandar dois ou três seguidos; Esc ou clicar fora fecha.
// Com termo digitado a grade mostra o resultado e as abas ficam sem seleção; escolher uma aba limpa a busca.
export function SeletorDeEmoji({ aoEscolher, aoFechar }) {
  const [termo, setTermo] = useState('')
  const [indice, setIndice] = useState(0)
  const idBusca = useId()
  const abasRef = useRef(null)
  const buscando = termo.trim().replace(/^:/, '') !== ''
  const categoria = CATEGORIAS_DE_EMOJI[indice]
  const achados = buscando ? buscarEmTodasAsCategorias(termo) : categoria.emojis

  function irParaAba(novo, focar) {
    setIndice(novo)
    setTermo('')
    if (focar) abasRef.current?.querySelectorAll('[role="tab"]')[novo]?.focus()
  }
  function teclaNaAba(evento) {
    const passo = { ArrowRight: 1, ArrowLeft: -1 }[evento.key]
    if (!passo) return
    evento.preventDefault()
    const total = CATEGORIAS_DE_EMOJI.length
    irParaAba((indice + passo + total) % total, true)
  }

  return (
    <Popover rotulo="Emoji" papel="dialog" posicao="acima" alinhamento="esquerda" largura={320} portal aoFechar={aoFechar}>
      <div className={css.seletor}>
        <label className="sr" htmlFor={idBusca}>Buscar emoji</label>
        <input
          id={idBusca} type="search" className={css.busca} value={termo} placeholder="Digite :nome para buscar"
          onChange={(evento) => setTermo(evento.target.value)}
          onKeyDown={(evento) => {
            if (evento.key !== 'Enter' || !achados.length) return
            evento.preventDefault()
            aoEscolher(achados[0].emoji)
          }}
        />
        <div ref={abasRef} role="tablist" tabIndex={-1} aria-label="Categorias de emoji" className={css.abas} onKeyDown={teclaNaAba}>
          {CATEGORIAS_DE_EMOJI.map((c, i) => {
            const ativa = !buscando && i === indice
            return (
              <Botao
                key={c.id} variante="texto" role="tab" className={css.aba} title={c.nome} aria-label={c.nome}
                aria-selected={ativa} tabIndex={i === indice ? 0 : -1} onClick={() => irParaAba(i, false)}
              >
                <span aria-hidden="true">{c.simbolo}</span>
              </Botao>
            )
          })}
        </div>
        <p className={css.titulo}>{buscando ? 'Resultado da busca' : categoria.nome}</p>
        <div className={css.corpo}>
          {achados.length ? (
            <div className={css.grade} role={buscando ? undefined : 'tabpanel'} aria-label={buscando ? undefined : categoria.nome}>
              {achados.map(({ emoji, nome }) => (
                <Botao key={emoji} variante="texto" className={css.emoji} title={nome} aria-label={nome} onClick={() => aoEscolher(emoji)}>
                  <span aria-hidden="true">{emoji}</span>
                </Botao>
              ))}
            </div>
          ) : <p className={css.vazio}>Nenhum emoji encontrado</p>}
        </div>
      </div>
    </Popover>
  )
}
