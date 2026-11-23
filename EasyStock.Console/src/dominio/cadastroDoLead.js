import { SITUACOES } from './areaEntrega'
import { telefoneNaTela } from './cadastros'
import { mascaraCep, partesDoEndereco } from './formato'

// #3207 (S82): converter lead em cliente tem um caminho só. A lógica mora aqui, sem tela.

export const ROTULO_CADASTRAR_CLIENTE = 'Cadastrar cliente'
export const ROTULO_VINCULAR_LEAD = 'Já é cliente? Vincular a um cadastro'
export const ROTULO_CONFERIR_IDENTIDADE = 'Conferir identidade'

// Lead é a conversa de quem ainda não tem cadastro. Na demonstração o cliente antigo também não
// traz clienteId, por isso a conta decide e o clienteId só confirma que não há cadastro ligado.
export const leadSemCadastro = (conversa) => conversa?.conta === 'lead' && !conversa.clienteId

// Qual ação o cartão mostra. Lead: "Cadastrar cliente" é a principal e vincular a um cadastro que já
// existe fica como ação discreta (só com o servidor, que é quem confere os candidatos). Cliente:
// só a conferência de identidade, como antes.
export function acoesDoCartao(conversa, { fonteApi = false } = {}) {
  const cadastrar = leadSemCadastro(conversa)
  return {
    cadastrar,
    vincular: Boolean(fonteApi),
    rotuloDoVinculo: cadastrar ? ROTULO_VINCULAR_LEAD : ROTULO_CONFERIR_IDENTIDADE,
  }
}

const digitos = (valor) => String(valor ?? '').replace(/\D/g, '')

// O que a conversa já captou, na ordem de confiança: cadastro da conversa, contato informado pelo
// visitante do site, número do canal (só no WhatsApp, onde o id do contato é o telefone; nos outros
// canais é um id externo, como no cadastrarEndereco). Nada sobrescreve o que a dona digitar.
function telefoneDaConversa(conversa) {
  const doCanal = conversa.canal === 'WhatsApp' ? conversa.contato : null
  const candidatos = [conversa.cliente?.telefone, conversa.contatoInformado?.telefone, doCanal]
  const achado = candidatos.find((c) => digitos(c).length >= 10)
  return achado ? telefoneNaTela(achado) : ''
}

// Endereço é um campo só na tela (rua, número, complemento e bairro, sem o CEP); o CEP tem a própria
// validação de área. Usa o endereço captado da conversa e, na falta, o já gravado.
export function valoresIniciaisDoCadastro(conversa) {
  const partes = partesDoEndereco(conversa.cliente?.enderecoCapturado || conversa.cliente?.endereco || '')
  return {
    nome: conversa.contatoInformado?.nome || conversa.nome || '',
    telefone: telefoneDaConversa(conversa),
    enderecoSemCep: [partes.rua, partes.numero, partes.complemento, partes.bairro].filter(Boolean).join(', '),
    cep: partes.cep ? mascaraCep(partes.cep) : '',
  }
}

// Aberta pelo botão, a Ficha já é o caminho de cadastrar: o banner do endereço capturado só fica quando
// oferece uma decisão de área (fora ou no limite, na demonstração), que o formulário não tem.
export const bannerDoEnderecoCapturado = (cadastrarLead, situacaoArea) =>
  !cadastrarLead || situacaoArea === SITUACOES.FORA || situacaoArea === SITUACOES.LIMITE
