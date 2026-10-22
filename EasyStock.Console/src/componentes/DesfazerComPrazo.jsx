import { useEffect, useState } from 'react'
import { Botao } from './Botao'

// #1686 (item 5): a mudança de etapa que avisa o cliente fica alguns segundos esperando, com
// "Desfazer" e a contagem à vista (Cozinha e Entregas). `ate` é o instante em que o envio sai;
// `texto` diz o que vai acontecer ("Vai para Em preparo").
export function DesfazerComPrazo({ ate, texto, aoDesfazer, largo = false, className = '' }) {
  const [agora, setAgora] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setAgora(Date.now()), 250)
    return () => clearInterval(id)
  }, [])
  const restam = Math.max(0, Math.ceil((ate - agora) / 1000))
  return (
    <div className={className} data-desfazer-etapa>
      <output aria-live="polite">{texto} em {restam} s</output>
      <Botao largo={largo} variante="secundario" icone="arrow-left" onClick={aoDesfazer}>Desfazer · {restam}</Botao>
    </div>
  )
}
