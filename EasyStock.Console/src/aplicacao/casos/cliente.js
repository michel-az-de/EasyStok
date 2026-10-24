// Casos de reducer da Frente 2 · Ficha do lead e do cliente (rodada 5, seção
// 2). Arquivo da F2: nenhuma outra frente edita este arquivo, e a F2 nunca
// edita `reducer.js` (ele já importa e espalha `casosCliente` no objeto
// composto). Ações previstas em `aplicacao/acoes.js`: SALVAR_CADASTRO_RAPIDO,
// EDITAR_DADO_CLIENTE, MUDAR_ENDERECO_DO_PEDIDO, ADICIONAR_TAG, REMOVER_TAG,
// EDITAR_TAG.
import * as acao from '../acoes'
import { numeroCurto } from '../../dominio/pedido'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Mesma forma de `comMensagem` do `reducer.js` (não exportado de lá: nenhuma
// frente edita `reducer.js`). Registro de sistema não mexe no relógio da
// conversa, porque ninguém respondeu nada ao cliente.
function comMensagem(conversa, mensagem, { id, agora }) {
  const paraOCliente = mensagem.dir !== 'sistema'
  return {
    ...conversa,
    atrasada: paraOCliente ? false : conversa.atrasada,
    ultimaEm: paraOCliente ? new Date(agora).toISOString() : conversa.ultimaEm,
    mensagens: [...conversa.mensagens, { id, em: new Date(agora).toISOString(), ...mensagem }],
  }
}

export const casosCliente = {
  // Cadastro rápido de lead, em linha (seção 2): nome e telefone bastam,
  // endereço é opcional aqui (só "Gerar cobrança", da F3/F4, exige CEP).
  // `dados.endereco` já chega pronto (string ou null): quem monta a tela
  // decide o formato, o reducer só grava. Nunca vira `conta: 'cliente'`:
  // RN-03 só converte no primeiro pedido fechado, e essa conversão é da F5
  // (seção 5), não daqui.
  [acao.SALVAR_CADASTRO_RAPIDO]: (estado, { id, dados, agora, mensagemId }) =>
    mapear(estado, id, (c) => comMensagem({
      ...c,
      nome: dados.nome?.trim() || c.nome,
      cliente: {
        ...c.cliente,
        // P0 (banca3/e1-e3, achado 1): mesma cópia do telefone do canal que
        // CADASTRAR_ENDERECO (reducer.js) faz, para o cadastro rápido de um
        // lead do WhatsApp também não nascer sem telefone.
        telefone: dados.telefone || c.cliente.telefone || (c.canal === 'WhatsApp' ? c.cliente.telefoneCanal : null) || null,
        endereco: dados.endereco || null,
        enderecoCapturado: null,
      },
    }, {
      dir: 'sistema',
      texto: 'Cadastro rápido salvo pela dona.',
    }, { id: mensagemId, agora })),

  // Edição em linha de nome, telefone e endereço (seção 2). `nome` mora na
  // conversa; os outros dois moram em `cliente`. Puro dado, sem mensagem: só
  // muda o cadastro, não fala com o cliente.
  [acao.EDITAR_DADO_CLIENTE]: (estado, { id, campo, valor }) =>
    mapear(estado, id, (c) => (
      campo === 'nome'
        ? { ...c, nome: valor }
        : { ...c, cliente: { ...c.cliente, [campo]: valor } }
    )),

  // "Mudar entrega" (seção 2): registro interno de que o pedido em aberto
  // segue para o endereço novo, já salvo por EDITAR_DADO_CLIENTE. Dir
  // "sistema" porque é anotação para a própria casa reler, não fala nova ao
  // cliente (a conversa real dela já sabe: foi ela quem mandou o endereço).
  [acao.MUDAR_ENDERECO_DO_PEDIDO]: (estado, { id, agora, mensagemId }) =>
    mapear(estado, id, (c) => {
      if (!c.pedido) return c
      return comMensagem(c, {
        dir: 'sistema',
        texto: `Endereço de entrega do pedido ${numeroCurto(c.pedido.numero)} atualizado: ${c.cliente.endereco}.`,
      }, { id: mensagemId, agora })
    }),
}
