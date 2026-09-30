// Criadores de ação da Frente 2 · Ficha do lead e do cliente (rodada 5,
// seção 2). `AtendimentoProvider.jsx` chama `criarAcoesCliente(despachar)` e
// espalha o resultado no objeto `acoes` do contexto; a F2 só mexe aqui
// dentro, nunca no Provider. `despachar` já é o `dispatch` do reducer.
// `agora` chega por parâmetro de quem chama (mesmo molde de
// `acoes/areaEntrega.js`): esta função não tem `agoraRef` próprio.
import * as acao from '../acoes'
import { proximoId } from '../../infra/repositorioConversas'

export function criarAcoesCliente(despachar) {
  return {
    // Nome e telefone bastam para salvar (RN-09 só trava "Gerar cobrança",
    // que é da F3/F4). Lead continua lead: a conversão em cliente é da F5,
    // no primeiro pedido fechado (seção 5), nunca aqui.
    salvarCadastroRapido: (id, dados, agora) => despachar({
      tipo: acao.SALVAR_CADASTRO_RAPIDO, id, dados, agora, mensagemId: proximoId('sis'),
    }),
    editarDadoCliente: (id, campo, valor, agora) => despachar({
      tipo: acao.EDITAR_DADO_CLIENTE, id, campo, valor, agora,
    }),
    // "Mudar entrega" (seção 2, edição em linha): só chamada depois que o
    // endereço já foi salvo por `editarDadoCliente`, quando a dona confirma
    // que o pedido em aberto deve seguir para o endereço novo.
    mudarEnderecoDoPedido: (id, agora) => despachar({
      tipo: acao.MUDAR_ENDERECO_DO_PEDIDO, id, agora, mensagemId: proximoId('sis'),
    }),
    adicionarTag: (id, tag) => despachar({ tipo: acao.ADICIONAR_TAG, id, tag }),
    removerTag: (id, tag) => despachar({ tipo: acao.REMOVER_TAG, id, tag }),
    editarTag: (id, tagAntiga, tagNova) => despachar({
      tipo: acao.EDITAR_TAG, id, tagAntiga, tagNova,
    }),
  }
}
