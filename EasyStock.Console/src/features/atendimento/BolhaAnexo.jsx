// Conteúdo de bolha dos formatos da frente Anexos (rodada 7): arquivo (PDF),
// áudio (gravado ou recebido) e peça da galeria. Vive em `features/atendimento`,
// não em `features/anexos`, porque só quem monta a bolha é `Balao.jsx`, e
// feature nenhuma importa outra feature (ferramentas/verificar-camadas.mjs) —
// mesmo motivo que já vale para o picker de "Prato" dentro do Composer.
import { useRef, useState } from 'react'
import { Icone } from '../../componentes/Icone'
import { formatarDuracao, formatarTamanho, fracaoTocada } from '../../dominio/anexos'
import css from './bolhaAnexo.module.css'

const BARRAS_ONDA = 18

export function BolhaArquivo({ mensagem }) {
  return (
    <div className={css.arquivo}>
      <span className={css.arquivoIcone} aria-hidden="true"><Icone nome="nota" /></span>
      <span className={css.arquivoTexto}>
        <strong>{mensagem.nomeArquivo ?? mensagem.texto}</strong>
        <span>PDF · {formatarTamanho(mensagem.tamanhoArquivo ?? 0)}</span>
      </span>
    </div>
  )
}

export function BolhaPeca({ mensagem }) {
  return (
    <figure className={css.peca}>
      <img src={mensagem.arte} alt="" />
      <figcaption>
        <strong>{mensagem.nome}</strong>
        <span>{mensagem.descricao}</span>
      </figcaption>
    </figure>
  )
}

// Bug conhecido do Chrome: <audio src="data:...">.duration costuma vir
// Infinity. Por isso o progresso usa `mensagem.duracaoMs` (conhecido de
// antemão, seção Áudio de `dominio/anexos.js`), nunca `audioRef.current.duration`.
export function BolhaAudio({ mensagem }) {
  const audioRef = useRef(null)
  const [tocando, setTocando] = useState(false)
  const [atualMs, setAtualMs] = useState(0)
  const duracaoTotal = mensagem.duracaoMs ?? 0

  const alternar = () => {
    const el = audioRef.current
    if (!el) return
    if (el.paused) el.play()
    else el.pause()
  }

  const fracao = fracaoTocada(atualMs, duracaoTotal)

  return (
    <div className={css.audio}>
      <button
        type="button"
        className={css.audioBotao}
        onClick={alternar}
        aria-label={tocando ? 'Pausar áudio' : 'Tocar áudio'}
      >
        <Icone nome={tocando ? 'pause' : 'play'} />
      </button>
      <span className={css.audioOnda} aria-hidden="true">
        {Array.from({ length: BARRAS_ONDA }, (_, indice) => (
          <span
            key={indice}
            className={indice < Math.round(fracao * BARRAS_ONDA) ? css.ondaPassada : css.ondaFutura}
          />
        ))}
      </span>
      <span className={css.audioTempo}>{formatarDuracao(atualMs > 0 ? atualMs : duracaoTotal)}</span>
      <audio
        ref={audioRef}
        src={mensagem.arte}
        preload="metadata"
        aria-label={mensagem.texto ?? 'Mensagem de áudio'}
        onPlay={() => setTocando(true)}
        onPause={() => setTocando(false)}
        onEnded={() => { setTocando(false); setAtualMs(0) }}
        onTimeUpdate={(evento) => setAtualMs(evento.currentTarget.currentTime * 1000)}
      />
    </div>
  )
}
