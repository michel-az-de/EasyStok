// Casos de reducer da tarefa Área de entrega (rodada 5, RN-09/RN-10/RN-11,
// UC-02). Arquivo próprio, no mesmo molde das frentes de
// `aplicacao/casos/<tema>.js`: nenhuma outra frente mexe aqui, e este arquivo
// nunca mexe em `reducer.js` além da linha de import e a linha que espalha
// `casosAreaEntrega` no objeto composto.
import * as acao from '../acoes'
import { DECISOES_DE_EXCECAO, motivoDaDecisao } from '../../dominio/areaEntrega'
import { dataHora } from '../../dominio/formato'

const ATENDENTE = 'Thatiane'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Liberar cadastra o endereço capturado como endereço de entrega, do mesmo
// jeito que CADASTRAR_ENDERECO (reducer.js) faz no caso comum: a exceção
// muda o motivo registrado na nota, não o que acontece com o cadastro.
// Encomenda agendada marca a tag e não mexe no endereço (a entrega de hoje
// não é viável, a encomenda é). Recusar não muda nada no cadastro: o lead
// continua lead, sem endereço de entrega.
//
// RN-03/UC-02 (rodada 10, item P1.4): a própria pós-condição do UC-02 chama a
// pessoa de "lead" mesmo depois da exceção liberada — continua lead até a
// primeira compra paga, do mesmo jeito que CADASTRAR_ENDERECO não promove
// mais (reducer.js).
function aplicarDecisao(conversa, decisao) {
  if (decisao === DECISOES_DE_EXCECAO.LIBERAR) {
    return {
      ...conversa,
      cliente: { ...conversa.cliente, endereco: conversa.cliente.enderecoCapturado, enderecoCapturado: null },
    }
  }
  if (decisao === DECISOES_DE_EXCECAO.ENCOMENDA_AGENDADA) {
    const tags = conversa.cliente.tags.includes('Encomenda agendada')
      ? conversa.cliente.tags
      : [...conversa.cliente.tags, 'Encomenda agendada']
    return { ...conversa, cliente: { ...conversa.cliente, tags } }
  }
  return conversa
}

export const casosAreaEntrega = {
  // Pós-condição do UC-02: decisão registrada no cadastro do lead, com
  // motivo. Nota interna (nunca chega ao cliente), igual ao SALVAR_NOTA de
  // reducer.js.
  [acao.DECIDIR_AREA_ENTREGA]: (estado, { id, decisao, agora, notaId }) =>
    mapear(estado, id, (c) => {
      const decidido = aplicarDecisao(c, decisao)
      return {
        ...decidido,
        cliente: {
          ...decidido.cliente,
          notas: [
            { id: notaId, autor: ATENDENTE, em: dataHora(agora), texto: motivoDaDecisao(decisao) },
            ...decidido.cliente.notas,
          ],
        },
      }
    }),
}
