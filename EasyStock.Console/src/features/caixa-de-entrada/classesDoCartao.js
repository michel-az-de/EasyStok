// Classes do cartão do Balcão por estado (#1427). Puro e sem JSX para a
// prova (`ferramentas/prova-1427-sla-resposta.mjs`) conferir a mesma regra
// que a tela usa: o tom do motivo pinta o fundo e o SLA de resposta
// estourado acende o pisca, independente de qual motivo venceu a marca.
export function classesDoCartao(css, { motivo = null, slaEstourado = false } = {}) {
  return [
    css.cartao,
    motivo?.tom === 'aviso' ? css.precisa : '',
    motivo?.tom === 'perigo' ? css.atrasada : '',
    slaEstourado ? css.slaEstourado : '',
  ].filter(Boolean).join(' ')
}
