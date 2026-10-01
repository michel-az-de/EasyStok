// Formatação. Funções puras, sem React e sem dependência de outra camada.

// Fuso da loja, explícito (F07, item 8): a hora da tela e o "hoje" não dependem do
// fuso da máquina de quem abre o console.
export const FUSO = 'America/Sao_Paulo'

export const moeda = (v) =>
  v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

export const horaCurta = (iso) =>
  new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: FUSO })

const DIA_DA_SEMANA = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 }
const PARTES = new Intl.DateTimeFormat('en-US', {
  timeZone: FUSO, hourCycle: 'h23', weekday: 'short',
  year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
})

// Relógio de parede da loja: ano, mês, dia, hora, minuto e dia da semana (0 = domingo).
export function partesNoFuso(ms) {
  const p = Object.fromEntries(PARTES.formatToParts(new Date(ms)).map(({ type, value }) => [type, value]))
  return {
    ano: Number(p.year), mes: Number(p.month), dia: Number(p.day),
    horas: Number(p.hour), minutos: Number(p.minute), diaDaSemana: DIA_DA_SEMANA[p.weekday],
  }
}

// "2026-09-30" do dia da loja, não do UTC (depois das 21 h o UTC já é amanhã).
export const dataIsoNoFuso = (ms) => {
  const { ano, mes, dia } = partesNoFuso(ms)
  return `${ano}-${String(mes).padStart(2, '0')}-${String(dia).padStart(2, '0')}`
}

// Hora em mono sem dois-pontos ("12h30"), para o contexto do cartão do Balcão
// (seção 1, rodada 5) e o sininho. Mesma conta de `horaComH` em dominio/lembrete.js,
// exportada aqui porque os dois passam a precisar dela.
export const horaMono = (ms) => {
  const { horas, minutos } = partesNoFuso(ms)
  return `${horas}h${String(minutos).padStart(2, '0')}`
}

export const dataHora = (ms) =>
  new Date(ms).toLocaleString('pt-BR', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: FUSO,
  })

// "maio de 2026": mesmo formato de `cliente.desde` na ficha (rodada 10, item
// P1.4). Fonte única para quem precisar marcar desde quando o cadastro é
// cliente, em vez de cada chamador escrever a própria conta de mês por
// extenso.
export const mesPorExtenso = (ms) =>
  new Date(ms).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric', timeZone: FUSO })

export function haQuantoTempo(iso, agora) {
  const minutos = Math.floor((agora - new Date(iso).getTime()) / 60000)
  if (minutos < 1) return 'agora'
  if (minutos < 60) return minutos + ' min'
  const horas = Math.floor(minutos / 60)
  if (horas < 24) return horas + ' h'
  return Math.floor(horas / 24) + ' d'
}

// Rodada 2 · plural. "1 porções" é o tipo de descuido que faz a tela parecer
// rascunho, e aparece em toda contagem do painel de entregas.
export const plural = (n, singular, plural_) => n + ' ' + (n === 1 ? singular : plural_)
// Lista em português: vírgula entre os primeiros e "e" antes do último. Existe
// para nenhuma tela montar "a, b, c" e ler como fala de robô.
export function listaEmPortugues(itens) {
  if (itens.length === 0) return ''
  if (itens.length === 1) return itens[0]
  return `${itens.slice(0, -1).join(', ')} e ${itens.at(-1)}`
}

// ---------------------------------------------------------------------------
// Rodada 5 · passo zero da direção visual (seção 8). Formatos novos que o
// Balcão, a Ficha e a Cobrança vão usar. Tudo puro, tempo e texto chegam por
// parâmetro, ninguém aqui olha Date.now().
// ---------------------------------------------------------------------------

// Duração por extenso (seção 1): "agora", "8 min", "1 h 12", "23 h", "2 d".
// Minuto cru acima de 59 nunca mais aparece em lugar nenhum da tela.
export function duracao(ms) {
  const minutos = Math.max(0, Math.round(ms / 60000))
  if (minutos < 1) return 'agora'
  if (minutos < 60) return minutos + ' min'
  const horas = Math.floor(minutos / 60)
  if (horas < 24) {
    const resto = minutos % 60
    return resto > 0 ? `${horas} h ${resto}` : `${horas} h`
  }
  return Math.floor(horas / 24) + ' d'
}

// Moeda guardada em centavos por dentro (seção 4): a tela só vê o texto
// formatado, "R$ 1.234,56".
export const mascaraMoeda = (centavos) => moeda((centavos ?? 0) / 100)

// Acima disto o campo de moeda avisa em vez de aceitar (seção 4):
// "Valor alto demais" em --parado, no lugar do valor.
export const LIMITE_MOEDA_CENTAVOS = 9999999
export const moedaAltaDemais = (centavos) => (centavos ?? 0) > LIMITE_MOEDA_CENTAVOS

// Cola de "68,5", "68.50" ou "R$ 68,50": o último separador (vírgula ou
// ponto) é sempre o decimal, o resto é parte inteira. Sem separador nenhum, o
// texto inteiro é reais cheios ("68" vira R$ 68,00).
export function lerMoeda(texto) {
  const semSimbolo = String(texto ?? '').replace(/[^\d.,]/g, '')
  if (!semSimbolo) return 0
  const posSeparador = Math.max(semSimbolo.lastIndexOf(','), semSimbolo.lastIndexOf('.'))
  if (posSeparador === -1) return Math.round(Number(semSimbolo) * 100)
  const inteiro = semSimbolo.slice(0, posSeparador).replace(/[.,]/g, '')
  const decimal = (semSimbolo.slice(posSeparador + 1).replace(/[.,]/g, '') + '00').slice(0, 2)
  return Number(inteiro || '0') * 100 + Number(decimal || '0')
}

// Telefone (seção 2, cadastro rápido): celular "(11) 98765-4321", fixo
// "(11) 3456-7890". Guarda só dígitos por dentro, formata na leitura.
export function mascaraTelefone(valor) {
  const digitos = String(valor ?? '').replace(/\D/g, '').slice(0, 11)
  if (digitos.length <= 2) return digitos ? `(${digitos}` : ''
  const ddd = digitos.slice(0, 2)
  const resto = digitos.slice(2)
  if (resto.length <= 4) return `(${ddd}) ${resto}`
  const tamanhoLinha = digitos.length <= 10 ? 4 : 5
  return `(${ddd}) ${resto.slice(0, tamanhoLinha)}-${resto.slice(tamanhoLinha)}`
}

// CEP (seção 2): "05433-001".
export function mascaraCep(valor) {
  const digitos = String(valor ?? '').replace(/\D/g, '').slice(0, 8)
  return digitos.length <= 5 ? digitos : `${digitos.slice(0, 5)}-${digitos.slice(5)}`
}

// Endereço em string só, formato da massa: "Rua X, 412, apto 71, Bairro,
// CEP" (seção 2). `partesDoEndereco` separa para o cadastro rápido editar
// campo a campo; `enderecoDasPartes` junta de volta na mesma ordem.
export function partesDoEndereco(texto) {
  if (!texto) return { rua: '', numero: '', complemento: '', bairro: '', cep: '' }
  const partes = texto.split(',').map((p) => p.trim())
  const cep = /^\d{5}-?\d{3}$/.test(partes.at(-1) ?? '') ? partes.pop() : ''
  const bairro = partes.pop() ?? ''
  const rua = partes.shift() ?? ''
  const numero = partes[0] && /^\d/.test(partes[0]) ? partes.shift() : ''
  return { rua, numero, complemento: partes.join(', '), bairro, cep }
}

export const enderecoDasPartes = ({ rua, numero, complemento, bairro, cep }) =>
  [rua, numero, complemento, bairro, cep].filter(Boolean).join(', ')

// Rótulo do dia para o separador do fio da conversa (achado 8, pendência 18
// da banca 64): "Hoje", "Ontem" ou a data por extenso, sempre contra o dia de
// `agora` (o relógio simulado da tela), nunca o dia real da máquina.
const inicioDoDiaMs = (ms) => {
  const d = new Date(ms)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
}
export function rotuloDoDia(iso, agora) {
  const diffDias = Math.round((inicioDoDiaMs(agora) - inicioDoDiaMs(new Date(iso).getTime())) / 86400000)
  if (diffDias === 0) return 'Hoje'
  if (diffDias === 1) return 'Ontem'
  return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'long' })
}
