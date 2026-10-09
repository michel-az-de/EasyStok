import { gerarListaDeCompras, obterSugestao, planejamentoDaApi, planejarProducao, sugestaoDaApi } from '../infra/api/producaoApi'

// Planejamento da produção no modo API (M2.5, #1502). A sugestão vem do EasyStok
// (D-M2-05: mínimo + agendados + descoberto − saldo); ela muda as porções, calcula os insumos e
// gera a lista de compras no próprio console. Separado do componente para a prova rodar sem React.
export const lerSugestao = async (ate) => {
  const r = await obterSugestao(ate)
  return { ate: r?.ate ?? ate ?? null, pratos: (r?.pratos ?? []).map(sugestaoDaApi) }
}

const porcoesDe = (texto) => (String(texto ?? '').trim() === '' ? 0 : Number(String(texto).replace(',', '.')))

export function erroDasPorcoes(linhas) {
  if (linhas.some((l) => !(porcoesDe(l.porcoes) >= 0) || !Number.isInteger(porcoesDe(l.porcoes)))) return 'Porções em número inteiro, sem negativo.'
  if (!linhas.some((l) => porcoesDe(l.porcoes) > 0)) return 'Diga quantas porções de ao menos um prato.'
  return null
}

export const planejar = async (linhas) => planejamentoDaApi(await planejarProducao(
  linhas.filter((l) => porcoesDe(l.porcoes) > 0).map((l) => ({ produtoId: l.produtoId, porcoes: porcoesDe(l.porcoes) }))))

// O que comprar: a falta do planejamento e, para o insumo que não falta, o que repõe o mínimo.
export function itensDeCompra(insumosPlanejados, insumosCadastrados) {
  const itens = insumosPlanejados
    .filter((i) => i.falta > 0)
    .map((i) => ({ texto: i.nome, produtoId: i.insumoId, quantidade: i.falta, unidade: i.unidade, categoria: 'Produção' }))
  const jaNaLista = new Set(itens.map((i) => i.produtoId))
  for (const i of insumosCadastrados ?? []) {
    if (!i.comprar || jaNaLista.has(i.id) || i.minimo == null) continue
    itens.push({ texto: i.nome, produtoId: i.id, quantidade: i.minimo - i.saldo, unidade: i.unidade, categoria: 'Mínimo' })
  }
  return itens
}

const diaCurto = (iso) => (iso ? iso.split('-').reverse().slice(0, 2).join('/') : 'hoje')

export async function gerarCompras(itens, ate) {
  if (itens.length === 0) return { ok: false, erro: 'Nada a comprar: nenhum insumo falta nem está abaixo do mínimo.' }
  try {
    const lista = await gerarListaDeCompras({ nome: `Compras da produção de ${diaCurto(ate)}`, itens })
    return { ok: true, id: lista?.id ?? null, nome: lista?.nome ?? null, itens: itens.length }
  } catch (e) {
    return { ok: false, erro: e.message }
  }
}

// "Lançar como produção": as porções planejadas viram linhas da produção do dia (M2.2). Fica em
// memória e é retirada uma vez pela tela de produção.
let planejadas = null
export const levarParaProducao = (linhas) => {
  planejadas = linhas.filter((l) => porcoesDe(l.porcoes) > 0).map((l) => ({ sku: l.sku, porcoes: String(porcoesDe(l.porcoes)) }))
  return planejadas.length
}
export const retirarProducaoPlanejada = () => {
  const r = planejadas
  planejadas = null
  return r
}
