import { corpoDoTrecho, trechosAtivos } from '../dominio/cadernoDoAgente'
import {
  arquivarTrechoDoCaderno, criarTrechoDoCaderno, editarTrechoDoCaderno, listarTrechosDoCaderno,
} from '../infra/api/cadernoApi'

// Caderno do agente (#2070): trechos ativos; salvar cria ou edita; remover arquiva.
export const carregarCaderno = async () => trechosAtivos(await listarTrechosDoCaderno())

export const salvarTrecho = (form, trecho = null) => (trecho
  ? editarTrechoDoCaderno(trecho.id, corpoDoTrecho(form))
  : criarTrechoDoCaderno(corpoDoTrecho(form)))

export const removerTrecho = (id) => arquivarTrechoDoCaderno(id)
