import { Icone } from '../../componentes/Icone'
import css from './bolhaAvaliacao.module.css'

// Rodada 10 (registro 79) · US-046, RN-34, RN-35: avaliação de um toque,
// nunca texto livre (achado P2.7 comparava com a Reclamação e via a MESMA
// bolha por baixo). `avaliacaoPedido` é a pergunta que a casa manda sozinha
// depois da entrega; `avaliacaoResposta` é o toque do cliente, com ícone e
// cor de acordo com a nota, nunca uma bolha de texto comum. Vive ao lado de
// `Balao.jsx` (quem monta a bolha), mesmo desenho de `BolhaAnexo.jsx`.
export function BolhaAvaliacaoPedido({ mensagem }) {
  return (
    <p className={css.pedido}>
      <span className={css.icone} aria-hidden="true"><Icone nome="sparkles" /></span>
      {mensagem.texto}
    </p>
  )
}

export function BolhaAvaliacaoResposta({ mensagem }) {
  const negativa = mensagem.valor === 'negativa'
  return (
    <p className={`${css.resposta} ${negativa ? css.negativa : css.positiva}`}>
      <span className={css.icone} aria-hidden="true">
        <Icone nome={negativa ? 'thumbs-down' : 'thumbs-up'} />
      </span>
      <strong>{negativa ? 'Avaliação negativa' : 'Avaliação positiva'}</strong>
    </p>
  )
}
