// #2833: emoji no Composer, reação e apagar para todos no atendimento. Regras puras; a tela só desenha.

// Os mais usados numa loja de comida, na ordem do seletor. O nome é o que a busca `:nome` procura.
export const EMOJIS_DA_LOJA = [
  { emoji: '😊', nome: 'sorriso' }, { emoji: '🙏', nome: 'obrigado' }, { emoji: '❤️', nome: 'coração' },
  { emoji: '👍', nome: 'joinha' }, { emoji: '😋', nome: 'delícia' }, { emoji: '🍝', nome: 'massa' },
  { emoji: '🥟', nome: 'nhoque' }, { emoji: '🍰', nome: 'torta' }, { emoji: '🛵', nome: 'entrega' },
  { emoji: '✅', nome: 'confirmado' }, { emoji: '💳', nome: 'cartão' }, { emoji: '💰', nome: 'pagamento' },
  { emoji: '⏰', nome: 'horário' }, { emoji: '📍', nome: 'endereço' }, { emoji: '🎉', nome: 'festa' },
  { emoji: '😉', nome: 'piscadinha' }, { emoji: '🤤', nome: 'vontade' }, { emoji: '🔥', nome: 'quente' },
  { emoji: '⭐', nome: 'estrela' }, { emoji: '📦', nome: 'pacote' }, { emoji: '🧀', nome: 'queijo' },
  { emoji: '🍗', nome: 'frango' }, { emoji: '😅', nome: 'ops' }, { emoji: '👋', nome: 'tchau' },
]

export const REACOES_RAPIDAS = [
  { emoji: '👍', nome: 'joinha' }, { emoji: '❤️', nome: 'coração' }, { emoji: '😂', nome: 'risada' },
  { emoji: '😮', nome: 'surpresa' }, { emoji: '🙏', nome: 'obrigado' }, { emoji: '✅', nome: 'confirmado' },
]

const semAcento = (texto) => texto.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

// `:nome` também vale, o atalho do WhatsApp Web.
export function buscarEmojis(termo, lista = EMOJIS_DA_LOJA) {
  const procurado = semAcento(String(termo ?? '').trim().replace(/^:/, ''))
  return procurado ? lista.filter((e) => semAcento(e.nome).includes(procurado)) : lista
}

// O emoji entra no cursor (ou troca a seleção) e o cursor fica logo depois dele.
export function inserirNoCursor(texto, inicio, fim, emoji) {
  const atual = texto ?? ''
  const de = Number.isInteger(inicio) ? Math.min(Math.max(inicio, 0), atual.length) : atual.length
  const ate = Number.isInteger(fim) ? Math.min(Math.max(fim, de), atual.length) : de
  return { texto: atual.slice(0, de) + emoji + atual.slice(ate), cursor: de + emoji.length }
}

// Mesmo prazo do domínio (Mensagem.PrazoApagarParaTodos): o WhatsApp não apaga para todos depois disso.
export const PRAZO_APAGAR_PARA_TODOS_MS = 48 * 60 * 60 * 1000

export const MOTIVO_SEM_APAGAR_NA_META =
  'Na conexão oficial (Meta) o WhatsApp não deixa apagar para todos. Só a conexão por QR apaga.'

const noWhatsApp = (m) => Boolean(m && (m.dir === 'in' || m.dir === 'out') && m.noCanal && !m.apagadaEm)

export const podeReagir = (mensagem) => noWhatsApp(mensagem)

// Só a mensagem da loja que já está no WhatsApp, até 48 h, e só a que saiu pela conexão por QR (`pelaPonte`, do id
// externo). Na Meta o item some e a linha do menu diz por quê; na mensagem do cliente nem isso, porque ninguém
// apaga a fala do cliente. A regra é da mensagem, não da conexão de agora: a que saiu pela Meta continua sem apagar.
export function apagarParaTodos(mensagem, agora) {
  if (!noWhatsApp(mensagem) || mensagem.dir !== 'out') return { mostra: false }
  if (!mensagem.pelaPonte) return { mostra: false, motivo: MOTIVO_SEM_APAGAR_NA_META }
  const idade = new Date(agora).getTime() - Date.parse(mensagem.em)
  return { mostra: Number.isFinite(idade) && idade <= PRAZO_APAGAR_PARA_TODOS_MS }
}

export const fraseDaApagada = (mensagem, hora) =>
  mensagem?.apagadaEm ? `Você apagou esta mensagem para todos · ${hora(mensagem.apagadaEm)}` : null
