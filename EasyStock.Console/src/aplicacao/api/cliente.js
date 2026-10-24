import * as acao from '../acoes'
import {
  adicionarNotaCliente, bloquearClienteApi, cadastrarClienteDaConversa, desbloquearClienteApi, obterDossie,
} from '../../infra/api/conversasApi'
import { clienteDoDossie, contatoInformadoDoDossie, enderecoParaApi } from '../../infra/api/traducaoCliente'
import { deveCarregarDossie } from '../planoDeSincronizacao'
import { textoNaoLigado } from './naoLigadas'
import { nomeConhecidoDoLead, nomeGenerico, recusaDoEndereco } from '../../dominio/cliente'

// Cadastro do cliente da conversa no modo API (#1276). Sem cliente vinculado o EasyStok não
// gera pedido; aqui a dona cadastra nome, telefone e endereço pela Ficha. Nada muda na tela
// antes da resposta: o que aparece depois é o dossiê relido, a mesma fonte de quem abre a
// conversa em outra aba.
// Ao abrir a conversa: cliente ainda não lido do EasyStok (#1276, regra única em
// `deveCarregarDossie`) ou lead do chat do site, cujo contato informado pelo visitante só vem
// no dossiê (#1430).
export const precisaLerFicha = (c) => deveCarregarDossie(c)
  || Boolean(c && !c.clienteId && c.canal === 'Chat do site' && !c.contatoInformado)

// #2005 (console-03): o dossiê que falha avisa e tenta de novo enquanto a conversa seguir aberta e sem ficha
// (o efeito do Provider depende de um booleano e não rodaria outra vez). A primeira tentativa mais quatro.
const ESPERA_DO_DOSSIE_MS = 8000
const RETENTATIVAS_DO_DOSSIE = 4

// Código do 409 de `POST .../cliente` quando o telefone informado pelo visitante já é de um cadastro (#1430).
const TELEFONE_JA_CADASTRADO = 'TELEFONE_JA_CADASTRADO'

export function criarAcoesClienteApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null
  const lendoDossie = new Map()

  async function carregar(id) {
    const dossie = await obterDossie(id)
    const cliente = clienteDoDossie(dossie)
    if (cliente) despachar({ tipo: acao.CLIENTE_DA_API, id, ...cliente })
    const contato = contatoInformadoDoDossie(dossie)
    if (contato) despachar({ tipo: acao.CONTATO_INFORMADO_API, id, contato })
  }

  // #1474 (R2): bloquear e desbloquear vão ao cadastro no EasyStok (todos os canais); a Ficha
  // relê o dossiê, que traz o bloqueio e o motivo. A tela ainda chama pelo nome (`BlocoCliente`).
  function mudarBloqueio(conversaId, fazer, rotulo) {
    const c = conversaDe(conversaId)
    if (!c?.clienteId) {
      avisar(`Salve o cadastro do cliente antes de ${rotulo}.`)
      return undefined
    }
    return fazer(c.clienteId)
      .then(() => carregar(c.id))
      .catch((erro) => avisar(`Bloqueio do cliente: ${erro.message}`))
  }

  // #1586: a recusa volta como `{ erro }` para a Ficha mostrar no campo ou ao lado do botão; a
  // tela só diz "Salvo" depois desta resposta.
  // E-mail só vai quando há (#1430): o corpo das outras edições continua o mesmo.
  // `confirmarVinculoExistente` (#1430): o telefone que o visitante informou já é de um cadastro e
  // a dona, depois de conferir com o cliente, manda ligar a conversa a ele. Sem isso a API não
  // liga: responde de quem é o telefone, e isso volta em `jaCadastrado` para o cartão perguntar.
  function cadastrar(id, { nome = null, telefone = null, endereco = null, email = null, confirmarVinculoExistente = false }) {
    // #1761: texto que não vira endereço não vai como nulo no lugar do que ela digitou.
    const recusa = recusaDoEndereco(endereco)
    if (recusa) return Promise.resolve({ erro: recusa })
    const corpo = { nome: nome?.trim() || null, telefone: telefone || null, endereco: endereco ? enderecoParaApi(endereco) : null }
    if (email?.trim()) corpo.email = email.trim()
    if (confirmarVinculoExistente) corpo.confirmarVinculoExistente = true
    return cadastrarClienteDaConversa(id, corpo)
      .then(async (salvo) => {
        // O telefone já era de outro cadastro: a conversa foi ligada a ele, com o nome dele.
        // Quando foi a dona que mandou ligar, ela já sabe.
        if (!confirmarVinculoExistente && salvo && salvo.novo === false && corpo.nome && salvo.nome !== corpo.nome) {
          avisar(`Este telefone já é de ${salvo.nome}: a conversa foi ligada a esse cadastro.`)
        }
        if (salvo?.dentroDaArea === false) {
          avisar(`Cadastro salvo, mas o endereço está fora da área de entrega. ${salvo.mensagemForaArea ?? ''}`.trim())
        }
        // O cadastro já foi gravado: falha na leitura não é recusa (mesma regra da nota, abaixo).
        try { await carregar(id) }
        catch (erro) { avisar(`Cadastro salvo, mas a ficha não atualizou: ${erro.message}`) }
        return undefined
      }, (erro) => {
        // Não é falha, é pergunta: nada foi ligado e a faixa fica quieta.
        if (erro.codigo === TELEFONE_JA_CADASTRADO) return { erro: erro.message, jaCadastrado: { nome: erro.dados?.nome ?? null } }
        avisar(`Cadastro do cliente: ${erro.message}`)
        return { erro: erro.message }
      })
  }

  // #1474: o clique em `selecionar` e o efeito da conversa restaurada podem pedir o mesmo
  // dossiê juntos; com um em voo, o segundo recebe a mesma promessa.
  function carregarClienteDaConversa(id, retentativas = RETENTATIVAS_DO_DOSSIE) {
    if (!lendoDossie.has(id)) {
      lendoDossie.set(id, carregar(id).then(() => true, () => {
        // Releitura de uma Ficha que já está na tela (SSE) que falha fica quieta, como antes: nada a avisar.
        if (!precisaLerFicha(conversaDe(id))) return false
        const vaiTentar = retentativas > 0
        avisar(vaiTentar
          ? 'A ficha do cliente não carregou. O EasyStok tenta de novo em instantes.'
          : 'A ficha do cliente não carregou. Abra a conversa de novo para tentar.')
        if (vaiTentar) {
          setTimeout(() => {
            if (estadoRef.current.selecionadaId === id && precisaLerFicha(conversaDe(id))) {
              carregarClienteDaConversa(id, retentativas - 1)
            }
          }, ESPERA_DO_DOSSIE_MS)
        }
        return false
      }).finally(() => lendoDossie.delete(id)))
    }
    return lendoDossie.get(id)
  }

  return {
    bloquearCliente: (_nome, motivo, conversaId) => mudarBloqueio(conversaId, (clienteId) => bloquearClienteApi(clienteId, motivo?.trim() || null), 'bloquear'),
    desbloquearCliente: (_nome, conversaId) => mudarBloqueio(conversaId, desbloquearClienteApi, 'desbloquear'),

    // O nome que vale é o que a dona escreveu no lápis (rascunho do lead). No chat do site sem
    // o formulário (#1430) o nome do contato é genérico ("Visitante do site"): sem o dela, não cadastra.
    salvarCadastroRapido: (id, dados) => {
      const c = conversaDe(id)
      if (c?.canal === 'Chat do site' && !c.nomeDaDona && !c.contatoInformado) {
        const mensagem = 'Escreva o nome do cliente (lápis ao lado do nome) antes de salvar o cadastro.'
        avisar(mensagem)
        return { erro: mensagem }
      }
      // #1966: nome genérico (o "Visitante do site" que a Ficha manda quando o visitante não disse o nome) não vira
      // nome do cadastro: vai o do cartão "Informado pelo visitante" ou nulo, e a API usa o do contato (PR #1962).
      const nome = c?.nomeDaDona ? c.nome : nomeGenerico(dados?.nome) ? nomeConhecidoDoLead(c) : dados.nome
      return cadastrar(id, { ...dados, nome })
    },

    // Endereço que o automático capturou na conversa; no WhatsApp o telefone vem do canal.
    cadastrarEndereco: (id) => {
      const c = conversaDe(id)
      const capturado = c?.cliente?.enderecoCapturado
      if (!capturado) return undefined
      const telefone = c.cliente.telefone || (c.canal === 'WhatsApp' ? c.cliente.telefoneCanal : null)
      return cadastrar(id, { nome: c.clienteId ? null : nomeConhecidoDoLead(c), telefone, endereco: capturado })
    },

    // Nome, telefone e endereço vão ao EasyStok; os avisos do cliente ainda não têm endpoint.
    // Lead sem cadastro: o nome fica como rascunho até o "Salvar cadastro" (que traz o telefone).
    editarDadoCliente: (id, campo, valor) => {
      if (!['nome', 'telefone', 'endereco'].includes(campo)) {
        avisar(textoNaoLigado('Avisos do cliente'))
        return undefined
      }
      const c = conversaDe(id)
      if (campo === 'nome' && !c?.clienteId) {
        despachar({ tipo: acao.RENOMEAR_LEAD_API, id, nome: valor })
        return undefined
      }
      // #1936: lead sem cadastro cadastra junto o contato que já se tem (o do formulário do chat do
      // site ou o telefone do WhatsApp). Sem isso a API recusava ("Informe o telefone") e o endereço sumia.
      if (!c?.clienteId && campo === 'endereco') {
        const telefone = c?.contatoInformado?.telefone || (c?.canal === 'WhatsApp' ? c?.cliente?.telefoneCanal : null) || null
        if (telefone) {
          return cadastrar(id, {
            nome: nomeConhecidoDoLead(c),
            telefone, email: c?.contatoInformado?.email ?? null, endereco: valor,
          })
        }
      }
      // #1643 (item 3): lead sem cadastro leva o nome conhecido; sem ele a API gravava "Cliente WhatsApp".
      return cadastrar(id, { ...(c?.clienteId ? null : { nome: nomeConhecidoDoLead(c) }), [campo]: valor })
    },

    // #1436: nota interna vai ao cadastro do cliente; a Ficha relê o dossiê. Lead não tem cadastro.
    salvarNota: (id, texto) => {
      const clienteId = conversaDe(id)?.clienteId
      if (!clienteId) {
        avisar('Cadastre o cliente (Salvar cadastro na Ficha) antes de anotar.')
        return false
      }
      return adicionarNotaCliente(clienteId, texto.trim())
        .then(async () => {
          // A nota já foi gravada: falha na leitura não deve incentivar outro POST.
          try { await carregar(id) }
          catch (erro) { avisar(`Nota salva, mas a ficha não atualizou: ${erro.message}`) }
          return true
        })
        .catch((erro) => {
          avisar(`Nota do cliente: ${erro.message}`)
          return false
        })
    },

    carregarClienteDaConversa,
  }
}
