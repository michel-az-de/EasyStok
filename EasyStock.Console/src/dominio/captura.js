// Rodada 11 · atendimento automático visível (issue #8, registro 92).
// Domínio puro: nenhum import fora de `dominio`, o dado sempre chega por
// parâmetro (regra da fronteira de camada, `ferramentas/verificar-camadas.mjs`).
//
// O pedido do dono: "atendimento ao cliente deveria ser automatizado cadastro
// do cliente conversas eu nao vi isso funcionar". Antes desta rodada o lead
// novo recebia a boas-vindas e mais nada: ninguém pedia nome nem telefone, o
// endereço só virava um banner esperando a dona clicar, e o cadastro nunca
// nascia sozinho. Este arquivo decide duas coisas:
//   1. o que uma fala do cliente traz de dado (nome, telefone, endereço,
//      gosto, itens do cardápio): `extrairDados`;
//   2. o que o automático responde depois disso: `respostaDaCaptura`.
// Quem aplica no estado é `aplicacao/casos/simulacao.js` (SIMULAR_MENSAGEM).
//
// Âncoras: UC-01 passos 3 a 8 (pede rua, número e CEP; extrai; confirma o
// endereço lido; anota o pedido), US-011 (extrair endereço em texto livre e
// gravar no cadastro do lead), US-003 (conversa em linguagem livre, nunca
// menu numérico), RN-03 (lead só vira cliente na primeira compra paga: o
// cadastro nasce, a conta continua `lead`).

import { mascaraCep, mascaraTelefone, moeda } from './formato'
import { disponivelHoje, oferecivelPeloAutomatico } from './cardapio'

export const CAMPOS = { NOME: 'nome', TELEFONE: 'telefone', ENDERECO: 'endereco', PEDIDO: 'pedido' }

// Marca que a ficha mostra ao lado de cada campo preenchido pelo automático.
export const SELO_CAPTADO = 'captado da conversa'

export const TEXTO_CADASTRO_CRIADO = 'Cadastro criado pelo automático com nome, telefone e endereço captados da conversa.'

const semAcento = (texto) => String(texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '')

// --- Nome ---------------------------------------------------------------------
// Só o que a pessoa AFIRMA ser: "me chamo", "meu nome é", "aqui é a/o". "Sou"
// sozinho fica de fora de propósito ("sou celíaca", "sou eu de novo"). Quando
// o automático acabou de perguntar o nome, a resposta curta com cara de nome
// próprio ("Rafael Tavares") também vale.
const PALAVRA_DE_NOME = "[A-ZÀ-Ý][a-zà-ÿ']+"
const LIGA_DE_NOME = '(?:d[aeo]s?|e)'
const NOME_PROPRIO = `${PALAVRA_DE_NOME}(?:\\s+(?:${LIGA_DE_NOME}\\s+)?${PALAVRA_DE_NOME}){0,3}`
const PISTAS_DE_NOME = [
  new RegExp(`\\b[Mm]e chamo\\s+(${NOME_PROPRIO})`),
  new RegExp(`\\b[Mm]eu nome (?:é|e)\\s+(${NOME_PROPRIO})`),
  new RegExp(`\\b[Aa]qui (?:é|e)\\s+(?:[oa]\\s+)?(${NOME_PROPRIO})`),
]
const SO_UM_NOME = new RegExp(`^(${NOME_PROPRIO})[.!]?$`)
// Palavras que começam com maiúscula mas não são nome de gente. Lookahead de
// letra em vez de `\b`: `\b` não enxerga fronteira depois de letra acentuada.
const NAO_E_NOME = /^(?:Oi|Ola|Olá|Bom|Boa|Sim|Nao|Não|Claro|Obrigad[oa]|Tudo|Rua|Avenida|Casa|Quero)(?![a-zà-ÿ])/

export function extrairNome(texto, { aguardandoNome = false } = {}) {
  const limpo = String(texto ?? '').trim()
  for (const pista of PISTAS_DE_NOME) {
    const achado = pista.exec(limpo)
    if (achado && !NAO_E_NOME.test(achado[1])) return achado[1].trim()
  }
  if (aguardandoNome) {
    const achado = SO_UM_NOME.exec(limpo)
    if (achado && !NAO_E_NOME.test(achado[1])) return achado[1].trim()
  }
  return null
}

// --- Telefone -----------------------------------------------------------------
// Celular ou fixo com DDD, com ou sem parênteses, espaço ou hífen: "(11)
// 98765-4321", "11 98765-4321", "11987654321". Sem DDD não vale (o cadastro
// rápido da ficha exige o mesmo, `BlocoCliente.jsx`: 10 dígitos ou mais).
const PADRAO_TELEFONE = /\(?\b(\d{2})\)?[\s.-]*(9?\d{4})[\s.-]?(\d{4})\b/

export function extrairTelefone(texto) {
  const achado = PADRAO_TELEFONE.exec(String(texto ?? ''))
  if (!achado) return null
  const digitos = `${achado[1]}${achado[2]}${achado[3]}`
  if (digitos.length < 10 || digitos.length > 11) return null
  return mascaraTelefone(digitos)
}

// --- Endereço (US-011, UC-01 passos 4 e 5) --------------------------------------
// "Rua, número e CEP" numa mensagem só, que é o que o automático pede. Sem
// logradouro, sem número ou sem CEP não é endereço de entrega (US-011: "a
// operação é 100% entrega"): devolve null e o automático pede de novo. O
// formato de saída é o do endereço capturado que o Simular já usava ("Rua X,
// 300, apto 12, Bairro, CEP 00000-000"), que `situacaoDoCep` e
// `faixasDeDistancia` (dominio/areaEntrega.js) leem sem conversão.
const LOGRADOURO = /\b(Rua|R\.|Avenida|Av\.?|Alameda|Al\.|Travessa|Tv\.|Praça|Estrada|Rodovia|Largo)\s+[^,\d]+,?\s*(?:n[º°o.]?\s*)?\d+[^]*$/i
const PADRAO_CEP = /\b(?:CEP\s*)?(\d{5})-?(\d{3})\b/i

export function extrairEndereco(texto) {
  const bruto = String(texto ?? '')
  const cepAchado = PADRAO_CEP.exec(bruto)
  const inicio = LOGRADOURO.exec(bruto)
  if (!cepAchado || !inicio) return null
  const cep = mascaraCep(`${cepAchado[1]}${cepAchado[2]}`)
  const semCep = bruto.slice(inicio.index).replace(PADRAO_CEP, '')
  const pedacos = semCep.split(',').map((p) => p.trim().replace(/[.!]+$/, '')).filter(Boolean)
  if (pedacos.length < 2) {
    // "Rua Girassol 300" sem vírgula: separa o número do nome da rua.
    const colado = /^(.*?)\s+(\d+\S*)\s*(.*)$/.exec(pedacos[0] ?? '')
    if (!colado) return null
    pedacos.splice(0, 1, colado[1], colado[2], ...(colado[3] ? [colado[3]] : []))
  }
  return `${pedacos.join(', ')}, CEP ${cep}`
}

// --- Gosto (preferência que vira tag) -----------------------------------------
// "adoro lasanha", "amo ravióli", "gosto muito de massa fresca": vira a tag
// "Gosta de X", a MESMA forma que `dominio/simulacoes.js: favoritoDasTags` já
// lê para a saudação do recorrente (US-002, RN-07). Restrição e alergia nunca
// entram aqui: essas passam para a dona antes (RN-52, `dominio/agente.js`).
const PADRAO_GOSTO = /\b(?:adoro|amo|gosto (?:muito )?de|sou f[ãa] de)\s+([a-zà-ÿ]+(?:\s+[a-zà-ÿ]+)?)/i
const PALAVRAS_SOLTAS = new Set(['a', 'o', 'as', 'os', 'um', 'uma', 'de', 'da', 'do', 'e'])

export function extrairGosto(texto) {
  const achado = PADRAO_GOSTO.exec(String(texto ?? ''))
  if (!achado) return null
  const palavras = achado[1].toLowerCase().split(/\s+/).filter((p) => !PALAVRAS_SOLTAS.has(p))
  return palavras.length ? palavras.join(' ') : null
}

export const tagDeGosto = (gosto) => `Gosta de ${gosto}`

// --- Itens do cardápio citados (UC-01 passo 7, "segue anotando por conversa") -----
// Nome inteiro do item ou uma palavra exclusiva dele com 5 letras ou mais,
// mesma regra de `dominio/agente.js: itensSemSaldoCitados` para "lasanha"
// sozinha não decidir entre a clássica e a verde. Só entra o que o
// automático pode oferecer (`oferecivelPeloAutomatico`: com saldo, nem
// removido nem em validação, RN-15) e está no cardápio de hoje. Saldo zero
// vende só à mão (RN-48, RN-53): essa fala já passou para a dona antes.
const palavrasChave = (nome) => semAcento(nome).toLowerCase().split(/[^a-z]+/)
  .filter((p) => p.length > 2 && !PALAVRAS_SOLTAS.has(p) && p !== 'extra')

export function itensCitados(texto, cardapio = []) {
  const alvo = semAcento(texto).toLowerCase()
  if (!alvo.trim()) return []
  const uso = new Map()
  cardapio.forEach((item) => palavrasChave(item.nome).forEach((p) => uso.set(p, (uso.get(p) ?? 0) + 1)))
  return cardapio.filter((item) => {
    if (!oferecivelPeloAutomatico(item) || !disponivelHoje(item)) return false
    const palavras = palavrasChave(item.nome)
    if (palavras.length === 0) return false
    const porInteiro = palavras.every((p) => alvo.includes(p))
    const porApelido = palavras.some((p) => p.length >= 5 && uso.get(p) === 1 && alvo.includes(p))
    return porInteiro || porApelido
  })
}

// --- Tudo junto ---------------------------------------------------------------
// `aguardando` é o campo que o automático acabou de pedir: só ele libera a
// leitura de um nome solto. `cardapio` só é lido quando o cadastro já está
// pronto e o automático está esperando o pedido.
export function extrairDados(texto, { aguardando = null, cardapio = [] } = {}) {
  return {
    nome: extrairNome(texto, { aguardandoNome: aguardando === CAMPOS.NOME }),
    telefone: extrairTelefone(texto),
    endereco: extrairEndereco(texto),
    gosto: extrairGosto(texto),
    itens: aguardando === CAMPOS.PEDIDO ? itensCitados(texto, cardapio) : [],
  }
}

// O primeiro dado que ainda falta na ficha, na ordem de UC-01: nome, telefone,
// endereço, e depois o pedido. Telefone do canal (WhatsApp) já conta: é o
// próprio identificador do contato ("dado de WhatsApp serve para contato",
// US-011). Endereço fora da área (`enderecoCapturado` pendente) não é
// "faltando": a decisão é da dona (UC-02), o automático não pergunta de novo.
export function proximoCampo(conversa) {
  const cliente = conversa?.cliente ?? {}
  if (!cliente.captado?.nome) return CAMPOS.NOME
  if (!cliente.telefone && !cliente.telefoneCanal) return CAMPOS.TELEFONE
  if (cliente.enderecoCapturado) return null
  if (!cliente.endereco) return CAMPOS.ENDERECO
  if (!conversa.pedido) return CAMPOS.PEDIDO
  return null
}

export const cadastroCompleto = (conversa) => {
  const cliente = conversa?.cliente ?? {}
  return Boolean(cliente.captado?.nome && (cliente.telefone || cliente.telefoneCanal) && cliente.endereco)
}

const primeiro = (nome) => String(nome ?? '').split(' ')[0] || 'você'

const PERGUNTAS = {
  [CAMPOS.NOME]: () => 'Pra eu já deixar seu cadastro pronto: como você se chama?',
  [CAMPOS.TELEFONE]: (nome) => `Prazer, ${primeiro(nome)}! Me passa um telefone com DDD pra contato?`,
  [CAMPOS.ENDERECO]: () => 'Anotado! Agora me diz rua, número e CEP, pra eu ver se entregamos aí.',
  [CAMPOS.PEDIDO]: () => 'O que vai ser hoje? É só escrever o nome do prato que eu anoto.',
}

export const perguntaPara = (campo, nome) => PERGUNTAS[campo]?.(nome) ?? null

// A resposta do automático depois de capturar (ou não) alguma coisa. Devolve
// o texto ou null (null = o automático não tem o que dizer, e o resto do
// fluxo decide: pergunta fora do roteiro passa para a dona, P2.6).
//   itensAnotados: [{ nome, preco }] que acabaram de entrar na comanda
//   cadastroCriado: o cadastro nasceu nesta mensagem
//   captouAlgo: esta fala trouxe pelo menos um dado novo
export function respostaDaCaptura({ conversa, itensAnotados = [], cadastroCriado = false, captouAlgo = false }) {
  const nome = conversa.nome
  if (itensAnotados.length > 0) {
    const lista = itensAnotados.map((i) => `1× ${i.nome}`).join(', ')
    const total = itensAnotados.reduce((soma, i) => soma + (i.preco ?? 0), 0)
    return `Anotei: ${lista}, total ${moeda(total)}. Quer mais alguma coisa ou posso fechar o pedido?`
  }
  if (cadastroCriado) {
    // UC-01 passo 6: o automático confirma o endereço lido de volta, inteiro,
    // com o CEP, para o cliente conferir o que foi gravado.
    return `Prontinho, ${primeiro(nome)}, seu cadastro está feito e entregamos aí sim: ${conversa.cliente.endereco}. `
      + perguntaPara(CAMPOS.PEDIDO, nome)
  }
  if (!captouAlgo) return null
  const campo = proximoCampo(conversa)
  return campo ? perguntaPara(campo, nome) : null
}

// Quanto tempo a casa fica "digitando" antes da resposta automática sair
// (registro 92): proporcional ao tamanho do texto, com piso e teto, para a
// frase curta não piscar e a boas-vindas longa não travar a conversa.
export const tempoDigitandoMs = (texto) => Math.min(2200, 700 + String(texto ?? '').length * 10)

// O automático está de fato conduzindo esta conversa agora? Falso com ela no
// controle (pausado por Assumir ou por escrever, US-004), com a conversa
// passada para ela e ainda não assumida (restrição, área, pergunta fora do
// roteiro), com endereço fora da área esperando a decisão dela (UC-02), com
// o cadastro bloqueado ou com a conversa encerrada. A ficha usa isto para não
// dizer "o automático está pedindo" quando ele parou.
export const automaticoConduzindo = (conversa, pausado = false) => Boolean(conversa?.captura)
  && !pausado
  && !conversa.bloqueio
  && !(conversa.passagem && !conversa.passagem.assumida)
  && !conversa.cliente?.enderecoCapturado
  && conversa.estado !== 'Encerrado'
