// Mensagem padrão (#3206): lógica pura do formulário da Gestão > Respostas. Espelha as regras do servidor
// (ModeloMensagemLocal): nome técnico, cabeçalho, corpo com {{1}}, {{2}}… e rodapé. Sem React, para testar sozinha.
import { PARAMETRO, nomeLegivel } from '../../../dominio/mensagensPadrao'

// Nome técnico -> título legível ("pedido_pronto" -> "Pedido pronto"): a regra mora no domínio.
export { nomeLegivel as tituloDoNome }

export const NOME_MAXIMO = 512
export const CABECALHO_MAXIMO = 60
export const CORPO_MAXIMO = 1024
export const RODAPE_MAXIMO = 60
export const IDIOMA = 'pt_BR'
export const CATEGORIA = 'UTILITY'

const EXEMPLOS = ['Maria', '#1234', '18:30', 'Rua das Flores, 120', 'R$ 45,90', '12/12']

// Título livre -> nome técnico: minúsculo, sem acento, espaços e símbolos viram "_".
export function nomeTecnico(titulo) {
  return String(titulo ?? '')
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '')
    .slice(0, NOME_MAXIMO).replace(/_+$/g, '')
}

export const numerosDoCorpo = (corpo) => [...String(corpo ?? '').matchAll(PARAMETRO)].map((m) => Number(m[1]))

// Próximo {{n}} em sequência (1 para corpo sem campo).
export const proximoCampo = (corpo) => Math.max(0, ...numerosDoCorpo(corpo)) + 1
export const acrescentarCampo = (corpo) => `${corpo ?? ''}{{${proximoCampo(corpo)}}}`

const umaLinhaSemCampo = (valor, rotulo, maximo) => {
  const t = String(valor ?? '').trim()
  if (!t) return null
  if (t.length > maximo) return `${rotulo} passa de ${maximo} caracteres.`
  if (/[\r\n]/.test(t)) return `${rotulo} deve ter uma linha só.`
  if (t.includes('{{')) return `${rotulo} não pode ter campo {{n}}.`
  return null
}

export function erroDoCorpo(corpo) {
  const t = String(corpo ?? '').trim()
  if (!t) return 'Escreva o texto da mensagem.'
  if (t.length > CORPO_MAXIMO) return `O texto passa de ${CORPO_MAXIMO} caracteres.`
  const semCampos = t.replace(PARAMETRO, '')
  if (semCampos.includes('{{') || semCampos.includes('}}')) return 'Use as chaves só nos campos {{1}}, {{2}} e assim por diante.'
  const numeros = [...new Set(numerosDoCorpo(t))].sort((a, b) => a - b)
  if (numeros.some((n, i) => n !== i + 1)) return 'Os campos devem seguir em ordem, {{1}}, {{2}}, {{3}}, sem pular número.'
  if (/^\{\{[1-9][0-9]?\}\}/.test(t) || /\{\{[1-9][0-9]?\}\}$/.test(t)) return 'A mensagem não pode começar nem terminar com um campo. Ponha um texto antes do primeiro e depois do último.'
  return null
}

// Primeiro erro de cada campo. `titulo` é o texto livre; o nome técnico sai dele.
export function validarMensagemPadrao({ titulo, cabecalho, corpo, rodape }) {
  const erros = {}
  if (!String(titulo ?? '').trim()) erros.titulo = 'Dê um nome para a mensagem.'
  else if (!nomeTecnico(titulo)) erros.titulo = 'Use letras ou números no nome.'
  const c = umaLinhaSemCampo(cabecalho, 'O cabeçalho', CABECALHO_MAXIMO)
  if (c) erros.cabecalho = c
  const e = erroDoCorpo(corpo)
  if (e) erros.corpo = e
  const r = umaLinhaSemCampo(rodape, 'O rodapé', RODAPE_MAXIMO)
  if (r) erros.rodape = r
  return erros
}

export const temErro = (erros) => Object.keys(erros).length > 0

// Troca cada {{n}} por um exemplo, só para a pessoa ver como a mensagem fica.
export const exemploDoCampo = (n) => EXEMPLOS[(n - 1) % EXEMPLOS.length]
export const previaDoCorpo = (texto) => String(texto ?? '').replace(PARAMETRO, (_, n) => exemploDoCampo(Number(n)))

export function corpoParaApi({ titulo, cabecalho, corpo, rodape }) {
  return {
    nome: nomeTecnico(titulo), idioma: IDIOMA, categoria: CATEGORIA,
    cabecalho: String(cabecalho ?? '').trim() || null, corpo: String(corpo ?? '').trim(), rodape: String(rodape ?? '').trim() || null,
  }
}

// Só avisos do pedido (categoria UTILITY): nada de promoção.
export const SUGESTOES = [
  { id: 'pronto', titulo: 'Pedido pronto para retirada', corpo: 'Olá {{1}}, seu pedido {{2}} está pronto para retirada. Pode vir buscar quando quiser.', rodape: 'Obrigado pela preferência' },
  { id: 'entrega', titulo: 'Pedido saiu para entrega', corpo: 'Olá {{1}}, seu pedido {{2}} saiu para entrega e chega por volta das {{3}}. Fique de olho no portão.', rodape: '' },
  { id: 'endereco', titulo: 'Confirmar endereço de entrega', corpo: 'Olá {{1}}, para entregar seu pedido {{2}} precisamos confirmar o endereço: {{3}}. Está correto? Responda por aqui.', rodape: '' },
  { id: 'retomar', titulo: 'Retomar atendimento', corpo: 'Olá {{1}}, vimos que seu pedido {{2}} ficou sem finalizar. Responda esta mensagem para continuarmos de onde paramos.', rodape: '' },
  { id: 'pagamento', titulo: 'Pagamento recebido', corpo: 'Olá {{1}}, recebemos o pagamento de {{2}} do seu pedido {{3}}. Já estamos preparando tudo.', rodape: 'Obrigado pela preferência' },
]
