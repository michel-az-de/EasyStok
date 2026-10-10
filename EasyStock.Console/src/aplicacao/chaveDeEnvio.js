// Idempotency-Key que vale para o mesmo corpo até a API confirmar (#1540). Repetir depois de uma
// queda de rede devolve o que já foi gravado em vez de gravar de novo; corpo diferente ou envio
// confirmado ganha chave nova. A API só guarda resposta 2xx, então erro de regra pode ser reenviado.
export function criarChaveDeEnvio(gerarChave = () => crypto.randomUUID()) {
  let atual = null

  return {
    para: (corpo) => {
      const assinatura = JSON.stringify(corpo)
      if (atual?.assinatura !== assinatura) atual = { assinatura, chave: gerarChave() }
      return atual.chave
    },
    confirmar: (chave) => {
      if (atual?.chave === chave) atual = null
    },
  }
}
