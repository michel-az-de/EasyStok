import {
  buscarClientes, iniciarConversaComModelo, listarModelos, enviarModelo, tentativaModelo, limparTentativaModelo,
} from '../../infra/api/modelosApi'
import { mensagemDaApi } from '../../infra/api/traducaoConversas'
import * as acao from '../acoes'

const SAIU = ['Enviada', 'Entregue', 'Lida']
// #1408: a tentativa do "Chamar no WhatsApp" é do cliente (a conversa ainda não existe quando ela nasce).
const chaveDoCliente = (clienteId) => `cliente:${clienteId}`

export function criarAcoesModelosApi({ despachar }) {
  const pendentes = new Map()
  // Uma tentativa por chave: dupla ação espera a mesma; resposta perdida repete a mesma; só a saída confirmada libera.
  const comTentativa = (chave, corpo, chamar) => {
    const tentativaId = tentativaModelo(chave, corpo)
    if (pendentes.has(tentativaId)) return pendentes.get(tentativaId)
    const envio = chamar(tentativaId).finally(() => pendentes.delete(tentativaId))
    pendentes.set(tentativaId, envio)
    return envio
  }
  const corpoDoModelo = (dados) => ({ nome: dados.nome, idioma: dados.idioma, parametros: dados.parametros.map(p => p.trim()) })
  return {
    listarModelos,
    prepararOutroModelo: limparTentativaModelo,
    prepararOutroModeloDoCliente: (clienteId) => limparTentativaModelo(chaveDoCliente(clienteId)),
    enviarModelo: (id, dados) => {
      const corpo = corpoDoModelo(dados)
      return comTentativa(id, corpo, (tentativaId) => enviarModelo(id, { ...corpo, tentativaId }).then(m => {
        despachar({ tipo: acao.RECEBER_MODELO_API, id, mensagem: mensagemDaApi(m, id) })
        if (SAIU.includes(m.status)) limparTentativaModelo(id)
        return m
      }))
    },
    // #1408: clientes cadastrados para a busca do Balcão oferecer "Chamar no WhatsApp".
    buscarClientesParaChamar: (termo) => buscarClientes(termo).then((lista) => lista ?? []),
    iniciarConversaComModelo: (cliente, dados) => {
      const chave = chaveDoCliente(cliente.id)
      const corpo = corpoDoModelo(dados)
      return comTentativa(chave, corpo, (tentativaId) =>
        iniciarConversaComModelo({ clienteId: cliente.id, ...corpo, tentativaId }).then(r => {
          if (SAIU.includes(r.mensagem?.status)) limparTentativaModelo(chave)
          return r
        }))
    },
  }
}
