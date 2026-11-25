import { useState } from 'react'
import { BotaoIcone } from './BotaoIcone'
import { Botao } from './Botao'
import { Popover } from './Popover'
import css from './AjusteLargura.module.css'

// #3322 (WCAG 2.5.7): a `AlcaLargura` só muda a largura arrastando (o teclado já tinha setas). Este botão
// abre um controle deslizante com os mesmos limites e a mesma função da alça, no padrão da Disposição
// (`Moldura.jsx`). `nome` completa o rótulo: "Largura do assistente", "Largura do cardápio".
export function PainelLargura({ nome, valor, min, max, padrao, aoMudar }) {
  return (
    <div className={css.ajuste}>
      <label>Largura <output>{valor} px</output>
        <input aria-label={`Largura ${nome}`} type="range" min={min} max={max} step="1" value={valor}
          onChange={(e) => aoMudar(Number(e.target.value))} />
      </label>
      <Botao variante="texto" onClick={() => aoMudar(padrao)}>Restaurar largura</Botao>
      <small>Você também pode arrastar a borda do painel.</small>
    </div>
  )
}

export function AjusteLargura(props) {
  const [aberto, setAberto] = useState(false)
  return (
    <span className={css.ancora}>
      <BotaoIcone rotulo={`Largura ${props.nome}`} dica="Largura" icone="largura" aria-expanded={aberto} aria-haspopup="dialog"
        onClick={() => setAberto(!aberto)} />
      {aberto && (
        <Popover rotulo={`Largura ${props.nome}`} papel="dialog" portal posicao="abaixo" largura={280} remedir={props.valor} aoFechar={() => setAberto(false)}>
          <PainelLargura {...props} />
        </Popover>
      )}
    </span>
  )
}
