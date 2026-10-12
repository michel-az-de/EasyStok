import { indiceDoPasso, PASSOS } from '../../dominio/esteira'

export function marcoDaComanda(pedido, conversaId) {
  return {
    chave: JSON.stringify([conversaId ?? null, pedido.pedidoId ?? pedido.numero ?? null]),
    estado: pedido.estado,
    indice: indiceDoPasso(pedido.estado),
    total: PASSOS.length,
  }
}

// O progresso acompanha o pedido recebido, nunca o clique ou a resolução de uma Promise.
export const houveAvancoConfirmado = (anterior, atual) => anterior.chave === atual.chave
  && anterior.indice >= 0 && atual.indice > anterior.indice

const falhaDaAcao = erro => erro?.message || 'Não foi possível confirmar a alteração. Confira o estado do pedido.'

export function criarExecucaoDaEtapa() {
  let emVoo = false
  return (fazer, { aoOcupar, aoFalhar, aoLiberar }) => {
    if (emVoo) return undefined
    emVoo = true
    let ida
    try { ida = fazer() }
    catch (erro) { emVoo = false; aoFalhar(falhaDaAcao(erro)); return undefined }
    if (!ida?.then) { emVoo = false; return ida }
    aoOcupar()
    return Promise.resolve(ida).then(
      resultado => { if (resultado?.erro) aoFalhar(resultado.erro); return resultado },
      erro => { const texto = falhaDaAcao(erro); aoFalhar(texto); return { erro: texto } },
    ).finally(() => { emVoo = false; aoLiberar() })
  }
}
