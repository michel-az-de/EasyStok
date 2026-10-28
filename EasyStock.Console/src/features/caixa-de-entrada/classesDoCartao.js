// Classes do cartão do Balcão por estado (#1427). Puro e sem JSX para a
// prova (`ferramentas/prova-1427-sla-resposta.mjs`) conferir a mesma regra
// que a tela usa: o SLA de resposta estourado acende o pisca, independente
// de qual motivo venceu a marca.
//
// #2135: quem tem motivo de precisar tem marca, de qualquer tom, porque o chip "Precisa de você"
// conta pelo motivo e não pelo tom (antes só `aviso` ganhava trilho: `perigo` apontava para uma
// classe que o CSS já não tinha e `neutro`, a cobrança vencida, não tinha nenhuma, e o chip dava 11
// com 8 cartões marcados). `perigo` é o trilho vermelho; os demais, o âmbar.
export function classesDoCartao(css, { motivo = null, slaEstourado = false } = {}) {
  return [
    css.cartao,
    motivo && motivo.tom !== 'perigo' ? css.precisa : '',
    motivo?.tom === 'perigo' ? css.atrasada : '',
    slaEstourado ? css.slaEstourado : '',
  ].filter(Boolean).join(' ')
}
