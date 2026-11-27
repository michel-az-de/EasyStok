// #2232 (banca UX, L10): contato salvo sem nome chega com o telefone cru no lugar do nome
// ("555434619500"). Na tela ele aparece com a máscara do Brasil, "+55 (54) 3461-9500". Nome de
// verdade passa intacto.

import { mascaraTelefone } from './formato'
import { CODIGOS, ROTULOS } from './rotulos'

export function nomeDoContato(nome) {
  const texto = String(nome ?? '').trim()
  if (!/^\+?[\d\s().-]+$/.test(texto)) return texto
  const digitos = texto.replace(/\D/g, '')
  if (/^55\d{10,11}$/.test(digitos)) return `+55 ${mascaraTelefone(digitos.slice(2))}`
  // Com "+" o país vem escrito: fora do 55, o número estrangeiro fica como veio.
  if (texto.startsWith('+')) return texto
  if (/^\d{10,11}$/.test(digitos)) return mascaraTelefone(digitos)
  return texto
}

// #2276 (06·1), #2944 (E1-13): o padrão do lead sem nome de perfil ("Cliente WhatsApp") não é nome de ninguém. Quando há
// telefone, a tela mostra o número formatado; sem telefone, a conversa mostra o nome como veio e as demais telas
// "Contato sem nome". Uma regra só para a conversa, o cadastro, a escolha do cliente e a Ficha.
const ehTelefone = (texto) => /^\+?[\d\s().-]+$/.test(texto) && /^\d{10,13}$/.test(texto.replace(/\D/g, ''))
const semNomeDeVerdade = (nome) => nome.trim() === '' || nome.trim().toLowerCase() === CODIGOS.NOME_PADRAO_DO_LEAD

function nomeDoTelefone(nome, telefone) {
  const contato = String(telefone ?? '').trim()
  return semNomeDeVerdade(nome) && ehTelefone(contato) ? nomeDoContato(contato) : null
}

// Cliente do cadastro: { nome, telefone }. Telefone formatado no lugar do padrão; a tela põe o selo "Sem nome".
export function nomeExibido(cliente) {
  const nome = String(cliente?.nome ?? '')
  return nomeDoTelefone(nome, cliente?.telefone) ?? (semNomeDeVerdade(nome) ? ROTULOS.SEM_NOME : nome)
}

export const clienteSemNome = (cliente) => semNomeDeVerdade(String(cliente?.nome ?? '')) && ehTelefone(String(cliente?.telefone ?? '').trim())

export function nomeDaConversa(conversa) {
  const nome = String(conversa?.nome ?? '')
  if (nome.trim().toLowerCase() !== CODIGOS.NOME_PADRAO_DO_LEAD) return nome
  return nomeDoTelefone(nome, conversa?.contato) ?? nome
}

// #2276: o primeiro nome para frases ao usuário ("Passar a conversa de Maria"). Contato que é só telefone sai
// inteiro: o primeiro pedaço do número ("+55") não diz quem é.
export function primeiroNomeDaConversa(conversa) {
  const nome = nomeDaConversa(conversa)
  return /^\+?[\d\s().-]+$/.test(nome) ? nome : nome.split(' ')[0]
}

// #2258 (auditoria de UX, T4 e T5): o nome do contato em três tamanhos e com a cápsula que explica
// o que falta nele. Puro, para a cápsula da tela e a prova lerem a mesma regra.
//
//   completo  o que cabe quando há largura
//   curto     a forma abreviada quando o espaço aperta ("Thais R.", "Notelet Eletr.")
//   capsula   null, ou { texto, tom, acao? } ao lado do nome: "Completar nome" (âmbar, só o primeiro
//             nome), "Sem nome" (cinza, só telefone) ou "#6" (cinza, visitante com comanda)
//   titulo    o texto inteiro para o `title`
//   rotulo    uma linha só, para botão e frase: "Visitante #6", "Thais R."

const VISITANTE = 'Visitante'
const FORMAS_LEGAIS = new Set(['ltda', 'me', 'epp', 'eireli', 'mei', 'sa', 's/a', 'cia'])
const PARTICULAS = new Set(['da', 'de', 'do', 'das', 'dos', 'e'])
const LIMITE_DA_PALAVRA_DE_EMPRESA = 7
const SEM_NOME = { texto: 'Sem nome', tom: 'neutro' }
const COMPLETAR_NOME = { texto: 'Completar nome', tom: 'ambar', acao: 'completar-nome' }

const temLetraOuNumero = (ficha) => /[\p{L}\p{N}]/u.test(ficha)
const semMoldura = (palavra) => palavra.replace(/[[\](){},;]/g, '').toLowerCase()

function numeroDaComanda(nome, comanda) {
  const dado = String(comanda ?? '').trim()
  if (/^\d+$/.test(dado)) return dado
  return /^comanda\s+(\d+)$/i.exec(nome)?.[1] ?? null
}

const ehVisitante = (nome) => nome === '' || /^visitante(\s+do\s+site)?$/i.test(nome) || /^comanda\s+\d+$/i.test(nome)

function formaCurta(palavras, ehEmpresa) {
  const [primeira, ...resto] = palavras.filter((p, i) => i === 0 || !PARTICULAS.has(p.toLowerCase()))
  const ultima = resto.at(-1)
  if (!ultima) return primeira
  // Revisão da #3381: empresa não vira inicial ("Padaria Q." perdia o nome); fica inteira e o CSS corta com reticências.
  if (ehEmpresa || ultima.length <= 1) return `${primeira} ${ultima}`
  // #3381: uma regra só, primeiro nome inteiro e inicial do último; o que ainda não couber, o CSS corta com reticências.
  return `${primeira} ${ultima[0].toUpperCase()}.`
}

export function apresentacaoDoNome(nome, { comanda = null } = {}) {
  const texto = String(nome ?? '').trim()
  const numero = numeroDaComanda(texto, comanda)

  if (ehVisitante(texto)) {
    if (texto === '' && !numero) return { completo: 'Contato', curto: 'Contato', capsula: SEM_NOME, titulo: 'Contato', rotulo: 'Contato' }
    return {
      completo: VISITANTE,
      curto: VISITANTE,
      capsula: numero ? { texto: `#${numero}`, tom: 'neutro' } : null,
      titulo: numero ? `${VISITANTE} do site · ${ROTULOS.PEDIDO} ${numero}` : `${VISITANTE} do site`,
      rotulo: numero ? `${VISITANTE} #${numero}` : VISITANTE,
    }
  }

  if (/^\+?[\d\s().-]+$/.test(texto)) {
    const completo = nomeDoContato(texto).replace(/^\+55\s/, '')
    return { completo, curto: completo, capsula: SEM_NOME, titulo: nomeDoContato(texto), rotulo: completo }
  }

  // Emoji e símbolo no fim ficam no fim e não contam como palavra.
  const fichas = texto.split(/\s+/)
  let fim = fichas.length
  while (fim > 1 && !temLetraOuNumero(fichas[fim - 1])) fim--
  const enfeite = fichas.slice(fim).join(' ')
  let palavras = fichas.slice(0, fim)
  const ehEmpresa = palavras.length > 1 && FORMAS_LEGAIS.has(semMoldura(palavras.at(-1)))
  if (ehEmpresa) palavras = palavras.slice(0, -1)
  const comEnfeite = (parte) => (enfeite ? `${parte} ${enfeite}` : parte)

  if (palavras.length === 1) {
    return {
      completo: texto, curto: texto, titulo: texto, rotulo: texto,
      capsula: ehEmpresa ? null : COMPLETAR_NOME,
    }
  }
  const curta = comEnfeite(formaCurta(palavras, ehEmpresa))
  const curto = curta.length < texto.length ? curta : texto
  return { completo: texto, curto, capsula: null, titulo: texto, rotulo: curto }
}
