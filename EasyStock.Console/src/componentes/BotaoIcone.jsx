import { forwardRef } from 'react'
import { Botao } from './Botao'
import { Icone } from './Icone'
import { Tooltip } from './Tooltip'
import css from './BotaoIcone.module.css'

// #2146 (plano #2130, seção 3.3): o botão só com ícone. `rotulo` é obrigatório e vira o nome
// acessível (aria-label) e a dica do Tooltip, sem ninguém precisar lembrar dos dois.
//
// É um `Botao` (mesmas variantes: `tom` = texto, secundario, primario, perigo) em formato quadrado
// redondo, o desenho do X do Modal e do aviso. `dica` troca o texto do Tooltip quando há algo a
// mais a dizer (o motivo de estar desligado, por exemplo); o nome acessível segue o `rotulo`.
// `pressionado` vira aria-pressed (botão de liga e desliga).
//
// O alvo mínimo é `--toque` (40 px, 44 px no toque e no celular) e vai no `style`: é contrato do
// componente, e uma folha de feature que encolha o botão não o reduz.
export const BotaoIcone = forwardRef(function BotaoIcone(
  { rotulo, icone, atalho, dica, tom = 'texto', pressionado, tamanhoIcone = 20, className, style, ...resto }, ref,
) {
  // oxlint-disable-next-line no-console -- aviso só de desenvolvimento, para o botão não nascer sem nome.
  if (!rotulo && import.meta.env?.DEV) console.warn(`BotaoIcone sem rotulo (ícone "${icone}"): o botão fica sem nome.`)
  return (
    <Tooltip texto={dica ?? rotulo} atalho={atalho}>
      <Botao
        ref={ref}
        variante={tom}
        aria-label={rotulo}
        aria-pressed={pressionado === undefined ? undefined : Boolean(pressionado)}
        className={[css.botaoIcone, className].filter(Boolean).join(' ')}
        style={{ minWidth: 'var(--toque)', minHeight: 'var(--toque)', ...style }}
        data-botao-icone
        {...resto}
      >
        <Icone nome={icone} tamanho={tamanhoIcone} />
      </Botao>
    </Tooltip>
  )
})
