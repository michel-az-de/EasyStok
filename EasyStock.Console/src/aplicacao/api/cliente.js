import * as acao from '../acoes'
import { cadastrarClienteDaConversa, obterDossie } from '../../infra/api/conversasApi'
import { clienteDoDossie, contatoInformadoDoDossie, enderecoParaApi } from '../../infra/api/traducaoCliente'
import { textoNaoLigado } from './naoLigadas'

// Cadastro do cliente da conversa no modo API (#1276). Sem cliente vinculado o EasyStok não
// gera pedido; aqui a dona cadastra nome, telefone e endereço pela Ficha. Nada muda na tela
// antes da resposta: o que aparece depois é o dossiê relido, a mesma fonte de quem abre a
// conversa em outra aba.
// Ao abrir a conversa: cliente ainda não lido do EasyStok (#1276) ou lead do chat do site, cujo
// contato informado pelo visitante só vem no dossiê (#1430).
export const precisaLerFicha = (c) => Boolean(c) && (c.clienteId
  ? !c.cliente?.daApi
  : c.canal === 'Chat do site' && !c.contatoInformado)

export function criarAcoesClienteApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null

  async function carregar(id) {
    const dossie = await obterDossie(id)
    const cliente = clienteDoDossie(dossie)
    if (cliente) despachar({ tipo: acao.CLIENTE_DA_API, id, ...cliente })
    const contato = contatoInformadoDoDossie(dossie)
    if (contato) despachar({ tipo: acao.CONTATO_INFORMADO_API, id, contato })
  }

  // E-mail só vai quando há (#1430): o corpo das outras edições continua o mesmo.
  function cadastrar(id, { nome = null, telefone = null, endereco = null, email = null }) {
    const corpo = { nome: nome?.trim() || null, telefone: telefone || null, endereco: endereco ? enderecoParaApi(endereco) : null }
    if (email?.trim()) corpo.email = email.trim()
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
    // O nome que vale é o que a dona escreveu no lápis (rascunho do lead). No chat do site sem
    // o formulário (#1430) o nome do contato é genérico ("Visitante do site"): sem o dela, não cadastra.
    salvarCadastroRapido: (id, dados) => {
      const c = conversaDe(id)
      if (c?.canal === 'Chat do site' && !c.nomeDaDona && !c.contatoInformado) {
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

    carregarClienteDaConversa: (id) => carregar(id).catch(() => {}),

  }
}
