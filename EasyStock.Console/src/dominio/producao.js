// Domínio puro: Produção (rodada 13, issue #43, áudios 13/14, UC-08, UC-09,
// RN-44 a RN-51, D7). Não importa infraestrutura nem React.
//
// Nomes do mapa EasyStok (mapa-easystok-r13.md) com um desvio registrado: lá
// a produção física é `Lote`/`LoteItem`. Aqui o nome vira `LoteProducao`
// porque "Lote" já é a Lote de papel do protótipo (rodada 8,
// dominio/loteDePapel.js, casos/lote.js) — mesma palavra, entidade
// totalmente diferente, teria colidido. `QuantidadeDescoberta` mantém o
// nome do EasyStok (ItemEstoque): venda acima do saldo não bloqueia nem fica
// negativa, fica "descoberta" até a contagem física repor (RN-48, D7).

// Peso sempre em grama nesta rodada (UnidadeMedida do EasyStok tem Mg/G/Kg/
// Ml/L/Un/Dz/Cx; produção de prato pronto usa G). Registrado como limite.
export const UNIDADE_PESO = 'G'

// RN-17/RN-18: o EasyStok não tem enum para "comer agora" x "congelado"
// (mapa: "o protótipo pode usar variação/rótulo"). O cardápio já tem um
// campo `linha` (LINHAS_PRODUTO: servir/casa) que categoriza o PRATO — é
// outra coisa. `destino` aqui é do que sai da porção vendida: proposta de
// enum para a Thati, valor inicial ajustável nesta tela.
export const DESTINOS_PORCAO = [
  { valor: 'comer-agora', rotulo: 'Para comer agora' },
  { valor: 'congelar', rotulo: 'Congelado, preparar em casa' },
]

export const rotuloDestino = (destino) =>
  DESTINOS_PORCAO.find((d) => d.valor === destino)?.rotulo ?? destino

export const PADRAO_DIAS_AVISO_VENCIMENTO = 1 // RN-51: proposta de prazo para a Thati, ajustável.

// ---------------------------------------------------------------------------
// Lote de produção (UC-08). `porcoes` já chega em porção de venda (RN-46),
// não em peso bruto: cada linha é { destino, quantidade, pesoPorcaoG, rotulo }.
// `saldo` nasce igual a `quantidade` e só ele desce (RN-45 mantém os dois
// campos distintos: peso real produzido nunca muda depois de criado).
// ---------------------------------------------------------------------------

export function criarLoteProducao({
  id, sku, identificador, pesoRealG, porcoes, validadeDias, produzidoEmIso, insumo = false,
}) {
  const validadeEm = validadeDias != null
    ? new Date(new Date(produzidoEmIso).getTime() + validadeDias * 86_400_000).toISOString()
    : null
  const pesoPorcionadoG = porcoes.reduce((soma, p) => soma + p.quantidade * p.pesoPorcaoG, 0)
  return {
    id,
    sku,
    identificador: identificador?.trim() || id,
    insumo, // UC-08 alt A: insumo intermediário (molho, massa laminada), não vende no cardápio.
    produzidoEm: produzidoEmIso,
    validadeEm,
    unidade: UNIDADE_PESO,
    pesoRealG,
    sobraG: Math.max(pesoRealG - pesoPorcionadoG, 0),
    porcoes: porcoes.map((p, indice) => ({
      id: `${id}-p${indice}`,
      destino: p.destino,
      rotulo: (p.rotulo ?? '').trim() || `${p.pesoPorcaoG} g`,
      pesoPorcaoG: p.pesoPorcaoG,
      quantidade: p.quantidade,
      saldo: p.quantidade,
    })),
  }
}

export const saldoLote = (lote) => lote.porcoes.reduce((soma, p) => soma + p.saldo, 0)

// Só lote de produto vendável entra na disponibilidade do cardápio; insumo
// intermediário fica de fora (alt A do UC-08).
export const lotesDoSku = (lotes, sku) => lotes.filter((l) => l.sku === sku && !l.insumo)

const ordenarPorData = (lista) => [...lista].sort(
  (a, b) => new Date(a.produzidoEm).getTime() - new Date(b.produzidoEm).getTime(),
)

// RN-47: cardápio mostra o saldo agregando todos os lotes do sku (vínculo
// N para N na prática, um sku pode ter vários lotes abertos ao mesmo tempo).
export const saldoEmPorcoes = (lotes, sku) =>
  lotesDoSku(lotes, sku).reduce((soma, l) => soma + saldoLote(l), 0)

// RN-50: baixa de estoque usa sempre o lote mais antigo com saldo.
export const loteMaisAntigoComSaldo = (lotes, sku) =>
  ordenarPorData(lotesDoSku(lotes, sku)).find((l) => saldoLote(l) > 0) ?? null

// RN-51: lote perto do vencimento aparece destacado.
export function situacaoDeVencimento(lote, agoraMs, diasAviso = PADRAO_DIAS_AVISO_VENCIMENTO) {
  if (!lote.validadeEm) return 'ok'
  const restanteMs = new Date(lote.validadeEm).getTime() - agoraMs
  if (restanteMs < 0) return 'vencido'
  if (restanteMs <= diasAviso * 86_400_000) return 'perto'
  return 'ok'
}

// Baixa FIFO entre lotes (RN-50) e, dentro do lote, entre as porções com
// saldo. A venda de hoje não escolhe destino (fora do escopo desta rodada,
// registrado como limite): a baixa desce da porção com saldo na ordem em que
// foi cadastrada. Retorna o que não coube em lote nenhum como `descoberto`
// (RN-48, QuantidadeDescoberta do EasyStok).
export function baixarLotesFifo(lotes, sku, quantidade) {
  let falta = quantidade
  const alterados = new Map()
  for (const lote of ordenarPorData(lotesDoSku(lotes, sku))) {
    if (falta <= 0) break
    const porcoes = lote.porcoes.map((p) => ({ ...p }))
    for (const p of porcoes) {
      if (falta <= 0) break
      const consumo = Math.min(p.saldo, falta)
      p.saldo -= consumo
      falta -= consumo
    }
    alterados.set(lote.id, { ...lote, porcoes })
  }
  return { lotes: lotes.map((l) => alterados.get(l.id) ?? l), descoberto: Math.max(falta, 0) }
}

// Devolução (item removido da comanda): repõe no lote mais antigo do sku,
// até o teto do que aquela porção já teve (nunca "inventa" quantidade nova).
// Não tenta lembrar de qual lote a unidade saiu — limite de protótipo,
// registrado na decisão da frente.
export function devolverLotesFifo(lotes, sku, quantidade) {
  let sobra = quantidade
  const alterados = new Map()
  for (const lote of ordenarPorData(lotesDoSku(lotes, sku))) {
    if (sobra <= 0) break
    const porcoes = lote.porcoes.map((p) => ({ ...p }))
    for (const p of porcoes) {
      if (sobra <= 0) break
      const espaco = p.quantidade - p.saldo
      const devolve = Math.min(espaco, sobra)
      p.saldo += devolve
      sobra -= devolve
    }
    alterados.set(lote.id, { ...lote, porcoes })
  }
  return { lotes: lotes.map((l) => alterados.get(l.id) ?? l), restante: Math.max(sobra, 0) }
}

// Ajuste manual de contagem (UC-09 passo 6/7, "estorno de estoque" do
// EasyStok: nunca deleta, sempre com motivo). Quando já existe lote do sku,
// o delta some/sobra no lote mais antigo (contagem física vence o sistema,
// mesmo texto de `dominio/cardapio.js: ajustarSaldo`). Sem lote nenhum
// (ela nunca lançou produção, só vendeu descoberto), não há onde encostar o
// ajuste: quem chama decide o saldo direto no cardápio.
export function ajustarContagemLotes(lotes, sku, novoSaldoTotal) {
  const doSku = lotesDoSku(lotes, sku)
  if (doSku.length === 0) return { lotes, aplicadoEmLote: false }
  const atual = saldoEmPorcoes(lotes, sku)
  const delta = novoSaldoTotal - atual
  if (delta === 0) return { lotes, aplicadoEmLote: true }
  if (delta < 0) return { lotes: baixarLotesFifo(lotes, sku, -delta).lotes, aplicadoEmLote: true }
  const alvo = ordenarPorData(doSku)[0]
  const lotesComAjuste = lotes.map((l) => (l.id !== alvo.id ? l : {
    ...l,
    porcoes: l.porcoes.map((p, indice) => (indice !== 0 ? p : {
      ...p, saldo: p.saldo + delta, quantidade: p.quantidade + delta,
    })),
  }))
  return { lotes: lotesComAjuste, aplicadoEmLote: true }
}

// ---------------------------------------------------------------------------
// Descoberto (RN-48, RN-49, UC-09): um registro por sku, cumulativo e
// persistente — não um alerta por unidade vendida. Fecha só quando ela conta
// e lança o ajuste (aceite da issue), não sozinho.
// ---------------------------------------------------------------------------

export function registrarDescoberto(descobertos, sku, quantidade, agoraIso) {
  if (quantidade <= 0) return descobertos
  const atual = descobertos[sku]?.quantidade ?? 0
  return { ...descobertos, [sku]: { quantidade: atual + quantidade, ultimaVendaEm: agoraIso } }
}

export function fecharDescoberto(descobertos, sku) {
  if (!(sku in descobertos)) return descobertos
  const { [sku]: _fora, ...resto } = descobertos
  return resto
}

// RN-49: o alerta descreve o desacerto em texto, não só número negativo.
// Exemplo do pedido do Felipe (27/09/2026): "Vendeu 2 Lasanha clássica 800 g
// sem produção lançada."
export function textoDescoberto(nome, porcao, quantidade) {
  return `Vendeu ${quantidade} ${nome}${porcao ? ' ' + porcao : ''} sem produção lançada.`
}
