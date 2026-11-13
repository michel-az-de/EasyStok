// Impressão automática do canhoto (#2096): montada uma vez no App (modo API), em qualquer tela.
// Só faz algo no aparelho em que foi ligada em Configurações › Impressão; ali mostra quantos
// canhotos saíram sozinhos hoje.
import { useImpressaoAutomatica } from '../../aplicacao/useImpressaoAutomatica'
import { imprimirHtml } from '../../componentes/htmlNoNavegador'
import {
  AVISO_SEM_IMPRESSAO_SILENCIOSA, textoDaContagem, textoDeConferir, textoFilaParada,
} from '../../dominio/impressaoAutomatica'
import css from './impressaoAutomatica.module.css'

const imprimirSemClique = (html, { sinal }) => imprimirHtml(html, { sinal, semAbaNova: true })

export function ImpressaoAutomatica() {
  const { ligada, nestaAba, contagem, falhas, conferir, fila, resolverConferencia } = useImpressaoAutomatica({ imprimir: imprimirSemClique })
  if (!ligada) return null
  const parada = fila.falhas > 0
  return (
    <output className={css.indicador} aria-live="polite">
      <span className={nestaAba && !parada ? css.ponto : `${css.ponto} ${css.espera}`} aria-hidden="true" />
      <span>
        Impressão automática ligada · {textoDaContagem(contagem)}
        {!nestaAba && ' · imprimindo em outra aba'}
        {parada && <span className={css.falhou}> · {textoFilaParada(fila.desde)}</span>}
        {falhas > 0 && <span className={css.falhou}> · {falhas === 1 ? '1 falhou' : `${falhas} falharam`}</span>}
      </span>
      {conferir.length > 0 && (
        <span className={css.conferir}>
          <span className={css.falhou}>{AVISO_SEM_IMPRESSAO_SILENCIOSA}</span>
          {conferir.map((item) => (
            <span key={item.id} className={css.conferirLinha}>
              {textoDeConferir({ numero: item.numero ?? item.pedidoNumero })}
              <button type="button" className={css.acao} onClick={() => resolverConferencia(item, 'saiu')}>Saiu</button>
              <button type="button" className={css.acao} onClick={() => resolverConferencia(item, 'denovo')}>Imprimir de novo</button>
            </span>
          ))}
        </span>
      )}
    </output>
  )
}
