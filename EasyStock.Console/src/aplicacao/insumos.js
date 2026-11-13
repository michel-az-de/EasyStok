import { atualizarInsumo, criarInsumo, insumoDaApi, listarInsumos } from '../infra/api/producaoApi'

// Insumos no modo API (M2.3, #1496). Grava, relê sempre e devolve `true` quando gravou; o erro vai
// para `aoErro`. Separado do hook para a prova rodar sem React.
export const UNIDADES_INSUMO = ['G', 'Kg', 'Ml', 'L', 'Un']

// #2258 (auditoria P7): o valor enviado à API segue como está (`G`, `Kg`...); na tela a unidade
// aparece como se escreve: g, kg, ml, L, un. Unidade que não está na lista passa como veio.
const UNIDADE_NA_TELA = { G: 'g', Kg: 'kg', Ml: 'ml', L: 'L', Un: 'un' }
export const rotuloDaUnidade = (unidade) => UNIDADE_NA_TELA[unidade] ?? String(unidade ?? '')

export const lerInsumos = async () => ((await listarInsumos()) ?? []).map(insumoDaApi)

// Onde o insumo é usado. M1.4e (#1552): a embalagem escolhida numa porção não entra em receita, mas
// desce do estoque a cada porção produzida; dizer "não entra em receita" e parar aí seria enganar.
const tantas = (n, um, varios) => (n === 1 ? `1 ${um}` : `${n} ${varios}`)
export function ondeOInsumoEUsado({ receitas = 0, porcoes = 0 }) {
  const usos = [
    receitas > 0 ? `usado em ${tantas(receitas, 'receita', 'receitas')}` : null,
    porcoes > 0 ? `embalagem de ${tantas(porcoes, 'porção', 'porções')}` : null,
  ].filter(Boolean)
  return usos.length > 0 ? usos.join(' · ') : 'não entra em receita'
}

// A embalagem da receita desce em toda produção do prato; a da porção, só quando aquela porção é produzida.
export const rotuloDaEmbalagem = ({ receitas = 0, porcoes = 0 }) =>
  (porcoes > 0 && receitas === 0 ? 'Embalagem de porção, uma por porção produzida' : 'Embalagem usada em toda produção')

const numeroOuNulo = (texto) => (String(texto ?? '').trim() === '' ? null : Number(String(texto).replace(',', '.')))

// #2081 (E9): o custo segue a leitura do campo de dinheiro (`lerMoeda`, #1510): "1.500" é mil e
// quinhentos, não 1,5 como antes. A leitura não arredonda: a validação abaixo impede que o ERP
// grave em centavos um custo diferente do que foi digitado.
export function lerCusto(texto) {
  const limpo = String(texto ?? '').replace(/[R$\s]/g, '')
  if (limpo === '') return null
  if (!/^\d[\d.,]*$/.test(limpo)) return NaN
  const virgula = limpo.lastIndexOf(',')
  if (virgula >= 0) return Number(`${limpo.slice(0, virgula).replace(/\./g, '') || '0'}.${limpo.slice(virgula + 1)}`)
  const partes = limpo.split('.')
  const milhar = partes.length > 1 && partes.slice(1).every((p) => p.length === 3) && /[1-9]/.test(partes[0])
  return Number(milhar ? partes.join('') : limpo)
}

// #2363: o custo é por unidade-base com até 6 casas (R$ 45 por 10 kg = R$ 0,0045 por g); em
// centavos ele viraria zero. Quem compra pensa no preço pago: a unidade de compra é da mesma
// família da unidade do insumo, e o fator leva a compra para a unidade-base.
const FATOR_DA_COMPRA = { G: { Kg: 1000, G: 1 }, Kg: { Kg: 1, G: 0.001 }, Ml: { L: 1000, Ml: 1 }, L: { L: 1, Ml: 0.001 }, Un: { Un: 1 } }
const CASAS_DO_CUSTO = 1e6
const arredondarCusto = (n) => Math.round(n * CASAS_DO_CUSTO) / CASAS_DO_CUSTO
const temAteDuasCasas = (n) => Math.abs(Math.round(n * 100) - n * 100) < 1e-6

export const unidadesDeCompra = (unidade) => Object.keys(FATOR_DA_COMPRA[unidade] ?? { [unidade]: 1 })

// Custo por unidade-base a partir da compra, sem arredondamento intermediário. NaN = compra inválida.
export function custoPelaCompra({ preco, quantidade, unidadeCompra, unidade }) {
  const fator = FATOR_DA_COMPRA[unidade]?.[unidadeCompra] ?? (unidadeCompra === unidade ? 1 : NaN)
  if (!(quantidade > 0) || !Number.isFinite(preco) || preco < 0) return NaN
  return arredondarCusto(preco / (quantidade * fator))
}

// O caminho de volta, para a edição e a lista: o custo no formato de compra, na maior unidade da
// família e com o preço em centavos exatos (R$ 0,0045/g vira "R$ 4,50 por 1 kg").
export function compraDoCusto(custo, unidade) {
  if (custo == null) return null
  for (const unidadeCompra of unidadesDeCompra(unidade)) {
    for (const quantidade of [1, 10, 100, 1000, 10000]) {
      const preco = custo * quantidade * (FATOR_DA_COMPRA[unidade]?.[unidadeCompra] ?? 1)
      if (temAteDuasCasas(preco)) return { preco: Math.round(preco * 100) / 100, quantidade, unidadeCompra }
    }
  }
  return { preco: custo, quantidade: 1, unidadeCompra: unidade }
}

const vazio = (texto) => String(texto ?? '').trim() === ''

// O preço pago é dinheiro (centavos); o custo sai da conta. Sem preço, vale o custo direto (API antiga e provas).
function custoInformado({ preco, quantidade, unidadeCompra, unidade, custo }) {
  if (vazio(preco)) return { custo: lerCusto(custo) }
  const valor = lerCusto(preco)
  if (!Number.isFinite(valor) || valor < 0) return { erro: 'Preço pago inválido.' }
  if (!temAteDuasCasas(valor)) return { erro: 'Informe o preço pago com até duas casas decimais.' }
  const qtd = numeroOuNulo(quantidade)
  if (!(qtd > 0)) return { erro: 'Informe quanto veio na compra.' }
  return { custo: custoPelaCompra({ preco: valor, quantidade: qtd, unidadeCompra: unidadeCompra || unidade, unidade }) }
}

export function criarGestaoInsumos({ recarregar, aoErro }) {
  const gravar = async (chamada) => {
    try {
      await chamada()
      return true
    } catch (erro) {
      aoErro(erro.message)
      return false
    } finally {
      await recarregar()
    }
  }

  const validar = (minimo, custo) => {
    if (minimo != null && (!Number.isInteger(minimo) || minimo < 0)) return 'Mínimo em número inteiro, sem negativo.'
    if (custo != null && (!Number.isFinite(custo) || custo < 0)) return 'Custo inválido.'
    if (custo != null && arredondarCusto(custo) !== custo) return 'Informe o custo com até seis casas decimais.'
    return null
  }

  return {
    criar: ({ nome, unidade, minimo, custo, preco, quantidade, unidadeCompra, saldo, embalagem = false }) => {
      if (!nome?.trim()) { aoErro('Informe o nome do insumo.'); return Promise.resolve(false) }
      const un = embalagem ? 'Un' : (unidade || 'Un')
      const m = numeroOuNulo(minimo)
      const { custo: c, erro: erroDaCompra } = custoInformado({ preco, quantidade, unidadeCompra: embalagem ? 'Un' : unidadeCompra, unidade: un, custo })
      const s = numeroOuNulo(saldo)
      const erro = erroDaCompra ?? validar(m, c) ?? (s != null && (!Number.isFinite(s) || s < 0) ? 'Quanto tem hoje: informe um número, sem negativo.' : null)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      // #2363: saldo vazio ou zero não cria movimentação.
      return gravar(() => criarInsumo({
        nome: nome.trim(), unidade: un, minimo: m, custo: c, ...(s > 0 ? { saldoInicial: s } : {}), ...(embalagem ? { embalagem: true } : {}),
      }))
    },
    atualizar: (id, { minimo, custo, preco, quantidade, unidadeCompra, unidade }) => {
      const m = numeroOuNulo(minimo)
      const { custo: c, erro: erroDaCompra } = custoInformado({ preco, quantidade, unidadeCompra, unidade, custo })
      const erro = erroDaCompra ?? validar(m, c)
      if (erro) { aoErro(erro); return Promise.resolve(false) }
      // #1563: na API `minimo: null` é "não mexe" e zero vence a herança da categoria. Campo apagado
      // limpa de forma explícita; API antiga ignora o campo e se comporta como antes.
      // #1600: o custo tem o mesmo contrato; apagado volta a "sem custo informado".
      return gravar(() => atualizarInsumo(id, {
        minimo: m, custo: c, ...(m == null ? { limparMinimo: true } : {}), ...(c == null ? { limparCusto: true } : {}),
      }))
    },
    // M2.7 (#1523): marcar ou desmarcar como embalagem; mínimo e custo ficam como estão.
    marcarEmbalagem: (id, embalagem) => gravar(() => atualizarInsumo(id, { minimo: null, custo: null, embalagem })),
  }
}
