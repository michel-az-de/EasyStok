import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { useAcoes } from '../../aplicacao/contextos'
import css from './ficha.module.css'

const MENSAGENS = {
  em_processamento: 'O provedor ainda está processando o pagamento. Confira novamente em instantes.',
  nao_encontrado: 'O provedor ainda não confirmou esse pagamento. Aguarde a confirmação.',
  aguardando_credencial: 'Conecte o Mercado Pago para conferir o pagamento.',
  sem_cobranca_online: 'Não há cobrança online para conferir. Atualize o pedido.',
  nao_conciliado: 'O pagamento não pôde ser confirmado para este pedido. Confira a conta do Mercado Pago.',
}

export function ConferirPagamento({ conversaId }) {
  const { conferirPagamento } = useAcoes()
  const [conferindo, setConferindo] = useState(false)
  const [resposta, setResposta] = useState(null)
  const emVoo = useRef(false)
  async function conferir() {
    if (emVoo.current) return
    emVoo.current = true
    setConferindo(true)
    setResposta(null)
    try {
      const resultado = await conferirPagamento(conversaId)
      const texto = resultado?.erro ?? (resultado?.confirmado
        ? resultado.situacao === 'ja_conferido' ? 'Pagamento já conferido.' : 'Pagamento conferido no provedor.'
        : MENSAGENS[resultado?.situacao] ?? 'A consulta não confirmou o pagamento. Confira novamente.')
      setResposta({ texto, erro: Boolean(resultado?.erro) })
    } catch {
      setResposta({ texto: 'Não foi possível conferir o pagamento agora. Tente novamente.', erro: true })
    } finally {
      emVoo.current = false
      setConferindo(false)
    }
  }
  if (typeof conferirPagamento !== 'function') return null
  return (
    <div className={css.acoesCobranca}>
      <Botao largo variante="secundario" disabled={conferindo} onClick={conferir}>
        {conferindo ? 'Conferindo pagamento…' : 'Conferir pagamento'}
      </Botao>
      {resposta && <p className={css.corpoBloco} role={resposta.erro ? 'alert' : 'status'}>{resposta.texto}</p>}
    </div>
  )
}
