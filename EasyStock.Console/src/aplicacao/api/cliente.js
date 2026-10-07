import * as acao from '../acoes'
import { adicionarNotaCliente, cadastrarClienteDaConversa, obterDossie } from '../../infra/api/conversasApi'
import { clienteDoDossie, enderecoParaApi } from '../../infra/api/traducaoCliente'
import { textoNaoLigado } from './naoLigadas'

// Cadastro do cliente da conversa no modo API (#1276). Sem cliente vinculado o EasyStok não
// gera pedido; aqui a dona cadastra nome, telefone e endereço pela Ficha. Nada muda na tela
// antes da resposta: o que aparece depois é o dossiê relido, a mesma fonte de quem abre a
// conversa em outra aba.
export function criarAcoesClienteApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null

  async function carregar(id) {
    const cliente = clienteDoDossie(await obterDossie(id))
    if (cliente) despachar({ tipo: acao.CLIENTE_DA_API, id, ...cliente })
  }

  function cadastrar(id, { nome = null, telefone = null, endereco = null }) {
    const corpo = { nome: nome?.trim() || null, telefone: telefone || null, endereco: endereco ? enderecoParaApi(endereco) : null }
    return cadastrarClienteDaConversa(id, corpo)
      .then((salvo) => {
        // O telefone já era de outro cadastro: a conversa foi ligada a ele, com o nome dele.
        if (salvo && salvo.novo === false && corpo.nome && salvo.nome !== corpo.nome) {
          avisar(`Este telefone já é de ${salvo.nome}: a conversa foi ligada a esse cadastro.`)
        }
        if (salvo?.dentroDaArea === false) {
          avisar(`Cadastro salvo, mas o endereço está fora da área de entrega. ${salvo.mensagemForaArea ?? ''}`.trim())
        }
        return carregar(id)
      })
      .catch((erro) => avisar(`Cadastro do cliente: ${erro.message}`))
  }

  return {
    // O nome que vale é o que a dona escreveu no lápis (rascunho do lead). No chat do site o
    // nome do contato é genérico ("Visitante do site"): sem o dela, não cadastra.
    salvarCadastroRapido: (id, dados) => {
      const c = conversaDe(id)
      if (c?.canal === 'Chat do site' && !c.nomeDaDona) {
        avisar('Escreva o nome do cliente (lápis ao lado do nome) antes de salvar o cadastro.')
        return undefined
      }
      return cadastrar(id, { ...dados, nome: c?.nomeDaDona ? c.nome : dados?.nome })
    },

    // Endereço que o automático capturou na conversa; no WhatsApp o telefone vem do canal.
    cadastrarEndereco: (id) => {
      const c = conversaDe(id)
      const capturado = c?.cliente?.enderecoCapturado
      if (!capturado) return undefined
      const telefone = c.cliente.telefone || (c.canal === 'WhatsApp' ? c.cliente.telefoneCanal : null)
      return cadastrar(id, { telefone, endereco: capturado })
    },

    // Nome, telefone e endereço vão ao EasyStok; os avisos do cliente ainda não têm endpoint.
    // Lead sem cadastro: o nome fica como rascunho até o "Salvar cadastro" (que traz o telefone).
    editarDadoCliente: (id, campo, valor) => {
      if (!['nome', 'telefone', 'endereco'].includes(campo)) {
        avisar(textoNaoLigado('Avisos do cliente'))
        return undefined
      }
      if (campo === 'nome' && !conversaDe(id)?.clienteId) {
        despachar({ tipo: acao.RENOMEAR_LEAD_API, id, nome: valor })
        return undefined
      }
      return cadastrar(id, { [campo]: valor })
    },

    // #1436: nota interna vai ao cadastro do cliente; a Ficha relê o dossiê. Lead não tem cadastro.
    salvarNota: (id, texto) => {
      const clienteId = conversaDe(id)?.clienteId
      if (!clienteId) {
        avisar('Cadastre o cliente (Salvar cadastro na Ficha) antes de anotar.')
        return undefined
      }
      return adicionarNotaCliente(clienteId, texto.trim())
        .then(() => carregar(id))
        .catch((erro) => avisar(`Nota do cliente: ${erro.message}`))
    },

    carregarClienteDaConversa: (id) => carregar(id).catch(() => {}),
  }
}
