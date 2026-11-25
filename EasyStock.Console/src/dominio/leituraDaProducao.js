// #3301: a leitura que a Produção do dia faz dos dados que ela já recebe (saldo, venda sem estoque, pedidos
// agendados, mínimo e validade dos lotes) para dizer o que produzir primeiro. Regra fixa, sem IA: o mesmo dado
// dá sempre a mesma leitura e o motivo fala a língua da loja. Não conhece o lote da tela: tocar nos cartões
// não muda a leitura, só um estoque ou uma sugestão novos mudam.
//
// Níveis: urgente (acabou ou vendeu sem ter, e há demanda), repor (tem saldo, mas a sugestão pede mais),
// vence (lote vencido ou perto de vencer; pede atenção, não produção), semEstoque (o prato ainda não tem
// estoque ligado) e ok.
import { plural } from './formato.js'

const PEDEM = { urgente: 0, repor: 1 }
export const pedeProducao = (leitura) => leitura?.nivel in PEDEM

const maiuscula = (texto) => texto.charAt(0).toUpperCase() + texto.slice(1)
export const diaCurto = (iso) => (iso ? iso.split('-').reverse().slice(0, 2).join('/') : 'amanhã')

// O lote que mais pesa: o vencido (já fora do saldo) e, entre os que vencem, o mais próximo.
function avisoDoLote(lotes) {
  if (lotes.some((l) => l.vencido)) return 'Lote vencido'
  const dias = lotes.filter((l) => l.vencendo).map((l) => l.diasParaVencer ?? 0)
  if (dias.length === 0) return null
  const logo = Math.min(...dias)
  return logo <= 0 ? 'Lote vence hoje' : logo === 1 ? 'Lote vence amanhã' : `Lote vence em ${logo} dias`
}

// Cartão de tamanho único lê o prato inteiro. Cartão de porção lê só a porção dele: a venda sem estoque é
// da porção, o lote é o que traz o rótulo dela, e a sugestão do prato não é de nenhum tamanho (a conta é por
// prato e o tamanho não se adivinha).
function leituraDoCartao(cartao, estoque, sugerida, ate) {
  const unico = !cartao.variacaoId
  const saldo = cartao.saldo
  const descoberto = unico
    ? (estoque?.descoberto ?? sugerida?.descoberto ?? 0)
    : (estoque?.porcoes?.find((p) => p.variacaoId === cartao.variacaoId)?.descoberto ?? 0)
  const falta = unico ? (sugerida?.sugestao ?? 0) : 0
  const lote = avisoDoLote((estoque?.lotes ?? []).filter((l) => unico || l.porcao === cartao.tamanho))

  if (descoberto > 0 || falta > 0) {
    const urgente = saldo === 0 || descoberto > 0
    const partes = [
      saldo === 0 && 'Acabou',
      descoberto > 0 && `${plural(descoberto, 'vendida', 'vendidas')} sem estoque`,
      falta > 0 && sugerida?.agendados > 0 && `${sugerida.agendados} em pedidos até ${ate}`,
      falta > 0 && saldo != null && sugerida?.minimo > 0 && saldo < sugerida.minimo && `abaixo do mínimo (${sugerida.minimo})`,
    ].filter(Boolean)
    return { nivel: urgente ? 'urgente' : 'repor', falta, tom: urgente ? 'perigo' : 'atencao', motivo: maiuscula(partes.join(' · ') || `Faltam ${plural(falta, 'porção', 'porções')}`) }
  }
  if (lote) return { nivel: 'vence', falta: 0, tom: 'atencao', motivo: lote }
  return { nivel: saldo == null ? 'semEstoque' : 'ok', falta: 0, tom: 'neutro', motivo: null }
}

// Um Map chave do cartão -> leitura. `estoque` é `estoque.pratos` da tela; `sugestao`, o que `lerSugestao`
// devolve. Sem sugestão (sem permissão ou rede fora), a leitura continua só com o estoque.
export function leituraDaGrade(cartoes, { estoque = [], sugestao = null } = {}) {
  const doPrato = new Map((estoque ?? []).map((p) => [p.sku, p]))
  const daSugestao = new Map((sugestao?.pratos ?? []).map((s) => [s.sku, s]))
  const ate = diaCurto(sugestao?.ate)
  return new Map((cartoes ?? []).map((c) => [c.chave, { ...leituraDoCartao(c, doPrato.get(c.sku), daSugestao.get(c.sku), ate), sku: c.sku }]))
}

// Quem pede produção sobe para o primeiro bloco: o que acabou antes, e dentro de cada nível o que mais falta;
// o resto segue a ordem do cardápio. Sem nada que peça, a grade fica como era (`pedem` vazio).
export function agruparPelaLeitura(cartoes, leituras) {
  const pedem = []
  const emDia = []
  cartoes.forEach((cartao, ordem) => {
    const leitura = leituras.get(cartao.chave)
    ;(pedeProducao(leitura) ? pedem : emDia).push({ cartao, leitura, ordem })
  })
  pedem.sort((a, b) => PEDEM[a.leitura.nivel] - PEDEM[b.leitura.nivel] || b.leitura.falta - a.leitura.falta || a.ordem - b.ordem)
  return { pedem: pedem.map((i) => i.cartao), emDia: emDia.map((i) => i.cartao) }
}

// A frase do topo. "Tudo em dia" só quando a sugestão veio: sem ela não há como saber dos pedidos.
// `falta` é a lista da faixa "Falta produzir" (`{ sku, quantidade }`): o total e a contagem batem com ela, e o
// prato com tamanhos, que não tem cartão que peça, entra pela sugestão. Sem ela, vale a soma dos cartões.
export function resumoDaLeitura(leituras, { sugestaoCarregada = false, falta = null } = {}) {
  const lista = [...leituras.values()]
  if (lista.length === 0) return null
  const pedem = lista.filter(pedeProducao)
  const daFaixa = (falta ?? pedem.map((l) => ({ sku: l.sku, quantidade: l.falta }))).filter((i) => i.quantidade > 0)
  const pratos = new Set([...pedem.map((l) => l.sku), ...daFaixa.map((i) => i.sku)])
  if (pratos.size > 0) {
    const faltam = daFaixa.reduce((soma, i) => soma + i.quantidade, 0)
    return {
      texto: `${plural(pratos.size, 'prato pede', 'pratos pedem')} produção${faltam > 0 ? ` · faltam ${plural(faltam, 'porção', 'porções')}` : ''}`,
      tom: pedem.some((l) => l.nivel === 'urgente') ? 'perigo' : 'atencao',
    }
  }
  const vencem = lista.filter((l) => l.nivel === 'vence').length
  if (vencem > 0) {
    return { texto: `${sugestaoCarregada ? 'Nada pede produção agora. ' : ''}${plural(vencem, 'prato tem', 'pratos têm')} lote vencido ou perto de vencer.`, tom: 'atencao' }
  }
  return sugestaoCarregada ? { texto: 'Tudo em dia: nada pede produção agora.', tom: 'ok' } : null
}
