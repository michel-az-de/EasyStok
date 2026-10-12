import { useTema } from '../hooks/useTema'
import { Botao } from './Botao'
import { Icone } from './Icone'
import css from './InterruptorTema.module.css'

const OPCOES = [
  { valor: 'light', rotulo: 'Claro', icone: 'sun' },
  { valor: 'dark', rotulo: 'Escuro', icone: 'moon' },
]

export function InterruptorTema({ vertical = false }) {
  const [tema, trocarTema] = useTema()
  const aoTeclar = (evento) => {
    const proximo = tema === 'light' ? 'dark' : 'light'
    const destino = { ArrowLeft: proximo, ArrowUp: proximo, Home: 'light', ArrowRight: proximo, ArrowDown: proximo, End: 'dark' }[evento.key]
    if (!destino) return
    evento.preventDefault()
    trocarTema(destino)
    evento.currentTarget.querySelector(`[data-tema="${destino}"]`)?.focus()
  }
  return (
    <div className={`${css.tema} ${vertical ? css.vertical : ''}`} role="radiogroup" aria-label="Tema" tabIndex={-1} onKeyDown={aoTeclar}>
      {OPCOES.map((opcao) => (
        <Botao key={opcao.valor} role="radio" aria-checked={tema === opcao.valor}
          tabIndex={tema === opcao.valor ? 0 : -1} data-tema={opcao.valor}
          onClick={() => { if (tema !== opcao.valor) trocarTema(opcao.valor) }}>
          <Icone nome={opcao.icone} tamanho={18} />
          <span>{opcao.rotulo}</span>
        </Botao>
      ))}
    </div>
  )
}
