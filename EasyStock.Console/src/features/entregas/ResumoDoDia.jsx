import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { moeda, plural } from '../../dominio/formato'
import { resumoDoDia, textoDoResumoDoDia } from '../../dominio/resumoDoDia'
import css from './resumoDoDia.module.css'

// Rodada 12 (issue #16), dúvida da Thatiane no áudio de 26/09/2026: o total
// dos entregues "vai para o fluxo de caixa?". Não vai, e a tela diz isso na
// mesma linha do número. "Copiar resumo do dia" leva o texto para ela colar
// onde confere o caixa (planilha, caderno, WhatsApp).
export function ResumoDoDia({ conversas, cardapio, agora }) {
  const [copiado, setCopiado] = useState(null)
  const resumo = resumoDoDia(conversas, cardapio)

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(textoDoResumoDoDia(resumo, agora))
      setCopiado('ok')
    } catch {
      setCopiado('falhou')
    }
  }

  return (
    <section className={css.resumo} aria-label="Resumo do dia">
      <p className={css.texto}>
        <strong>
          Entregues hoje: {plural(resumo.quantidade, 'pedido', 'pedidos')} · {moeda(resumo.totalPedidos)}
        </strong>
        <span>
          Total para conferir no caixa. Este resumo não lança nada no caixa.
          {resumo.aReceber > 0 && ` ${moeda(resumo.aReceber)} sem pagamento registrado.`}
        </span>
      </p>
      <Botao variante="texto" icone="copy" disabled={resumo.quantidade === 0} onClick={copiar}>
        {copiado === 'ok' ? 'Resumo copiado' : 'Copiar resumo do dia'}
      </Botao>
      {copiado === 'falhou' && (
        <p className={css.erro} role="alert">O navegador não deixou copiar. Tente de novo.</p>
      )}
    </section>
  )
}
