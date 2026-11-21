// #2832: painel "Agora" da Gestão da loja (Turno de hoje). Regras puras sobre o cardápio do catálogo, o caixa do
// dia da API e a contagem dos pedidos de hoje: o que está no ar, o recebido por forma, a situação do caixa, o que
// precisa da loja agora e as telas que a pessoa mais usa. A tela só desenha; nada aqui conhece React nem a API.
import { disponivelHoje, estaEmValidacao, estaRemovido, situacaoDoItem } from './cardapio'
import { nomeDoMetodoCaixa, normalizarMetodoCaixa } from './caixa'

// Os quatro montes dos pedidos de hoje. `status` é o filtro da API (lista por vírgula), na ordem da vida do pedido.
export const GRUPOS_DE_PEDIDOS = [
  { chave: 'pagamento', rotulo: 'esperando pagamento', status: 'aguardando_pagamento,aguardando_aprovacao_baba', tom: 'aviso' },
  { chave: 'cozinha', rotulo: 'na cozinha', status: 'aguardando,aprovado_baba,preparando,pronto', tom: 'neutro' },
  { chave: 'entrega', rotulo: 'saindo para entrega', status: 'saiu_para_entrega', tom: 'neutro' },
  { chave: 'entregues', rotulo: 'entregues', status: 'entregue', tom: 'ok' },
]

// O que a loja oferece hoje: sem o item removido e sem o novo em validação (esse ainda não foi para o cliente).
// "No ar" é o que vende hoje, com saldo zero incluído (RN-48: saldo zero vende à mão).
export function cardapioDoDia(cardapio) {
  const itens = (cardapio ?? [])
    .filter((item) => !estaRemovido(item) && !estaEmValidacao(item))
    .map((item) => {
      const situacao = situacaoDoItem(item)
      return { sku: item.sku, nome: item.nome, noAr: disponivelHoje(item), rotulo: situacao.rotulo, tom: situacao.tom }
    })
  return { itens, noAr: itens.filter((i) => i.noAr).length }
}

// Vendas e pagamentos de pedido de hoje por forma (as linhas do caixa da API; o estorno vem negativo).
export function recebidoPorForma(caixa) {
  const porForma = new Map()
  for (const linha of caixa?.linhasExtras ?? []) {
    const metodo = normalizarMetodoCaixa(linha.meio ?? '') || 'outro'
    porForma.set(metodo, (porForma.get(metodo) ?? 0) + (Number(linha.valor) || 0))
  }
  return [...porForma.entries()]
    .map(([metodo, valor]) => ({ metodo, nome: nomeDoMetodoCaixa(metodo), valor: Math.round(valor * 100) / 100 }))
    .filter((f) => f.valor !== 0)
    .sort((a, b) => b.valor - a.valor)
}

const diaMesDaData = (iso) => (/^\d{4}-\d{2}-\d{2}/.test(iso ?? '') ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : null)

export function situacaoDoCaixa(caixa) {
  if (!caixa) return { rotulo: 'Sem leitura', tom: 'neutro' }
  if (caixa.esquecidoAberto) {
    const desde = diaMesDaData(caixa.abertoDesde)
    return { rotulo: desde ? `Aberto desde ${desde}` : 'Aberto de outro dia', tom: 'perigo' }
  }
  if (caixa.fechado) return { rotulo: 'Fechado', tom: 'neutro' }
  if (caixa.aberto) return { rotulo: 'Aberto', tom: 'ok' }
  return { rotulo: 'Não aberto', tom: 'aviso' }
}

const plural = (n, um, varios) => `${n} ${n === 1 ? um : varios}`
const MAXIMO_DE_NOMES = 3

// O que precisa da loja agora, do mais grave ao mais leve. Cada pendência abre uma tela (`aba`) e só aparece
// para quem pode abrir essa tela (`abas`, as abas da Gestão que a pessoa tem).
export function pendenciasDoTurno({ caixa, pedidos, cardapio, abas }) {
  const pode = (aba) => abas?.has(aba)
  const lista = []
  if (caixa && pode('caixa')) {
    if (caixa.esquecidoAberto) {
      const desde = diaMesDaData(caixa.abertoDesde)
      lista.push({ chave: 'caixa-esquecido', tom: 'perigo', aba: 'caixa', acao: 'Fechar',
        titulo: 'Caixa de outro dia ainda aberto', detalhe: `${desde ? `Aberto desde ${desde}. ` : ''}Feche antes de vender hoje.` })
    } else if (caixa.aberturaAConfirmar) {
      lista.push({ chave: 'caixa-conferir', tom: 'aviso', aba: 'caixa', acao: 'Conferir',
        titulo: 'Confira a abertura do caixa', detalhe: 'Abriu sozinho no primeiro pagamento em dinheiro.' })
    } else if (!caixa.aberto && !caixa.fechado) {
      lista.push({ chave: 'caixa-nao-aberto', tom: 'neutro', aba: 'caixa', acao: 'Abrir',
        titulo: 'Caixa ainda não aberto', detalhe: 'Conte a gaveta e abra o caixa do dia.' })
    }
  }
  const esperando = pedidos?.pagamento ?? 0
  if (esperando > 0 && pode('cadastros-pedidos')) {
    lista.push({ chave: 'pedidos-pagamento', tom: 'aviso', aba: 'cadastros-pedidos', acao: 'Ver pedidos',
      titulo: `${plural(esperando, 'pedido', 'pedidos')} esperando pagamento`, detalhe: 'Confira o Pix ou cobre de novo.' })
  }
  const semSaldo = (cardapio ?? []).filter((item) => !estaRemovido(item) && !estaEmValidacao(item)
    && disponivelHoje(item) && item.estoque === 0)
  if (semSaldo.length > 0 && pode('estoque-do-dia')) {
    const nomes = semSaldo.slice(0, MAXIMO_DE_NOMES).map((i) => i.nome).join(', ')
    const resto = semSaldo.length > MAXIMO_DE_NOMES ? ` e mais ${semSaldo.length - MAXIMO_DE_NOMES}` : ''
    lista.push({ chave: 'sem-saldo', tom: 'aviso', aba: 'estoque-do-dia', acao: 'Ver estoque',
      titulo: `${plural(semSaldo.length, 'prato', 'pratos')} sem saldo no ar`, detalhe: `${nomes}${resto}. Lance a produção ou marque Esgotou.` })
  }
  return lista
}

// "Você mais usa": contagem de aberturas por aba, guardada no navegador. Contagem estragada recomeça do zero.
export function registrarUso(contagem, aba) {
  const atual = Number.isInteger(contagem?.[aba]) ? contagem[aba] : 0
  return { ...contagem, [aba]: atual + 1 }
}

export function maisUsadas(contagem, telas, quantas = 6) {
  if (!contagem) return []
  return (telas ?? [])
    .filter((tela) => Number.isInteger(contagem[tela.aba]) && contagem[tela.aba] > 0)
    .sort((a, b) => contagem[b.aba] - contagem[a.aba])
    .slice(0, quantas)
}
