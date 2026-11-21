import { useId, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Popover } from '../../componentes/Popover'
import { buscarEmojis } from '../../dominio/acoesNaMensagem'
import css from './atendimento.module.css'

// #2833: os emojis mais usados na loja, com busca por nome (`:massa`, como no WhatsApp Web). Escolher insere no
// cursor do campo e mantém o seletor aberto, para mandar dois ou três seguidos; Esc ou clicar fora fecha.
export function SeletorDeEmoji({ aoEscolher, aoFechar }) {
  const [termo, setTermo] = useState('')
  const idBusca = useId()
  const achados = buscarEmojis(termo)
  return (
    <Popover rotulo="Emoji" papel="dialog" posicao="acima" alinhamento="esquerda" largura={320} portal aoFechar={aoFechar}>
      <div className={css.seletorEmoji}>
        <label className="sr" htmlFor={idBusca}>Buscar emoji</label>
        <input
          id={idBusca} type="search" className={css.buscaEmoji} value={termo} placeholder="Digite :nome para buscar"
          onChange={(evento) => setTermo(evento.target.value)}
          onKeyDown={(evento) => {
            if (evento.key !== 'Enter' || !achados.length) return
            evento.preventDefault()
            aoEscolher(achados[0].emoji)
          }}
        />
        <p className={css.tituloEmoji}>Mais usados na loja</p>
        {achados.length ? (
          <div className={css.gradeEmoji}>
            {achados.map(({ emoji, nome }) => (
              <Botao key={emoji} variante="texto" className={css.emoji} title={nome} aria-label={nome} onClick={() => aoEscolher(emoji)}>
                <span aria-hidden="true">{emoji}</span>
              </Botao>
            ))}
          </div>
        ) : <p className={css.semEmoji}>Nenhum emoji com esse nome.</p>}
      </div>
    </Popover>
  )
}
