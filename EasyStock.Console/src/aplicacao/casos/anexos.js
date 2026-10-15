// Casos de reducer da frente Anexos (rodada 7, pedido do dono 24/09/2026
// 04h12). Ações previstas em `aplicacao/acoes.js`: INCLUIR_PECA, EDITAR_PECA,
// TIRAR_PECA. Mexe só em `estado.catalogo.galeria`, mesmo molde de
// `casos/cardapio.js` para `estado.catalogo.cardapio`.
import * as acao from '../acoes'
import {
  editarPeca, incluirPeca, tirarPeca,
} from '../../dominio/anexos'

export const casosAnexos = {
  [acao.INCLUIR_PECA]: (estado, { dados, agora }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, galeria: incluirPeca(estado.catalogo.galeria, dados, agora) },
  }),

  [acao.EDITAR_PECA]: (estado, { id, dados }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, galeria: editarPeca(estado.catalogo.galeria, id, dados) },
  }),

  [acao.TIRAR_PECA]: (estado, { id }) => ({
    ...estado,
    catalogo: { ...estado.catalogo, galeria: tirarPeca(estado.catalogo.galeria, id) },
  }),
}
