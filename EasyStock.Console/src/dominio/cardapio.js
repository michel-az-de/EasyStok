// Domínio puro: não importa infraestrutura. O catálogo chega por parâmetro.

export const itemPorSku = (cardapio, sku) => cardapio.find((i) => i.sku === sku) ?? null

// Saldo zero não impede a venda. Ele muda o tom e abre alerta.
export function situacaoDeEstoque(item) {
  if (item.estoque === 0) return { tom: 'perigo', rotulo: 'sem saldo', alerta: true }
  if (item.estoque <= 2) return { tom: 'aviso', rotulo: item.estoque + ' em estoque', alerta: false }
  return { tom: 'ok', rotulo: item.estoque + ' em estoque', alerta: false }
}

export const textoDoAlerta = (item) =>
  item.nome + ' está com saldo zero no sistema. A venda foi aceita. '
  + 'Confira o congelador e lance a produção quando puder.'

// Saldo que não desce é pior que saldo nenhum, porque ela confia nele.
export function baixarSaldo(cardapio, sku, quantidade = 1) {
  return cardapio.map((item) => (item.sku === sku
    ? { ...item, estoque: Math.max(item.estoque - quantidade, 0) }
    : item))
}

export function devolverSaldo(cardapio, sku, quantidade = 1) {
  return cardapio.map((item) => (item.sku === sku
    ? { ...item, estoque: item.estoque + quantidade }
    : item))
}

// ---------------------------------------------------------------------------
// Rodada 2 · cardápio do dia. Acrescentado no fim do arquivo.
//
// Duas coisas diferentes que a tela confundia numa só: SALDO é quanto tem no
// congelador, DISPONIBILIDADE é se a casa vende isso hoje. Ela pode ter dez
// porções e não querer vender, e pode vender sem saldo lançado.
//
// RN-16 manda manter toda opção sempre visível. O WhatsApp Business esconde o
// esgotado, o iFood pausa o item. Aqui vence a RN-16: o item fica na lista, com
// o estado escrito e uma alternativa ao lado.
// ---------------------------------------------------------------------------

// Item da massa antiga não tem o campo. Ausente significa disponível, senão
// trinta conversas de teste nasceriam com o cardápio inteiro desligado.
export const disponivelHoje = (item) => item?.disponivelHoje !== false

// Situação completa do item: junta disponibilidade e saldo numa resposta só, com
// rótulo em texto. Cor nunca informa sozinha.
export function situacaoDoItem(item) {
  if (!disponivelHoje(item)) {
    return {
      chave: 'fora-do-dia', tom: 'neutro', rotulo: 'Fora do cardápio de hoje',
      vendavel: false, alerta: false,
    }
  }
  if (item.estoque === 0) {
    return {
      chave: 'esgotado', tom: 'perigo', rotulo: 'Esgotado hoje',
      vendavel: true, alerta: true,
    }
  }
  if (item.estoque <= 2) {
    return {
      chave: 'pouco', tom: 'aviso', rotulo: item.estoque + ' porções',
      vendavel: true, alerta: false,
    }
  }
  return {
    chave: 'disponivel', tom: 'ok', rotulo: item.estoque + ' porções',
    vendavel: true, alerta: false,
  }
}

// Vendável não é o mesmo que disponível: saldo zero continua vendendo à mão
// (D7 e RN-48), item desligado do dia não. Quem desligou foi ela, de propósito.
export const podeVender = (item) => situacaoDoItem(item).vendavel

export function alternarDisponibilidade(cardapio, sku) {
  return cardapio.map((item) => (item.sku === sku
    ? { ...item, disponivelHoje: !disponivelHoje(item) }
    : item))
}

// Acerto de saldo pela dona, na mão. Diferente de baixar saldo por venda: aqui
// ela está contando o congelador, e o número dela vence o do sistema.
export function ajustarSaldo(cardapio, sku, delta) {
  return cardapio.map((item) => (item.sku === sku
    ? { ...item, estoque: Math.max(item.estoque + delta, 0) }
    : item))
}

// Alternativa para quem ouviu não. O erro caro não é o item acabar, é a conversa
// morrer em "não tem". Mesma linha de produto primeiro, porque é o que resolve
// para quem ia comer hoje.
export function alternativasPara(cardapio, sku, quantas = 2) {
  const alvo = itemPorSku(cardapio, sku)
  if (!alvo) return []
  const abertos = cardapio.filter((i) => i.sku !== sku && !i.removidoEm
    && situacaoDoItem(i).chave !== 'fora-do-dia' && i.estoque > 0)
  const mesmaLinha = abertos.filter((i) => i.linha === alvo.linha)
  return [...mesmaLinha, ...abertos.filter((i) => i.linha !== alvo.linha)].slice(0, quantas)
}

export const contarDisponiveis = (cardapio) =>
  cardapio.filter((item) => situacaoDoItem(item).chave !== 'fora-do-dia').length

// Adicional é SKU do próprio cardápio: tem preço, porção e saldo como qualquer
// item, e baixa estoque igual. O mapa chega por parâmetro, o domínio não conhece
// o catálogo.
export function adicionaisDoItem(cardapio, mapa, sku) {
  return (mapa?.[sku] ?? []).map((codigo) => itemPorSku(cardapio, codigo)).filter(Boolean)
}

// ---------------------------------------------------------------------------
// Rodada 5 · cardápio editável (US-020, RN-15, RN-16, RN-17, RN-19, áudio 12).
//
// Incluir item não é escrever uma linha nova: RN-15 só deixa entrar produto
// que já vendeu mais de uma vez (foi assim que rondelli e canelone ficaram de
// fora). Item criado por aqui nasce agora, venda zero, então nasce EM
// VALIDAÇÃO — ela decide depois se confirma (virou clássico, como aconteceu
// com avioli, lasanha e sorrentino) ou tira. "Novidade da casa" é outra
// coisa: rótulo de vitrine com prazo (áudio 12: "pitutini... é uma
// novidade"), independente de já estar confirmado.
//
// "Tirar do cardápio" nunca apaga a linha: pedido antigo continua achando o
// item pelo sku, porque `itemPorSku` não filtra removido. Só sai da lista que
// vende e da lista de gerir o dia.
// ---------------------------------------------------------------------------

export const estaRemovido = (item) => Boolean(item.removidoEm)

export const itensAtivos = (cardapio) => cardapio.filter((item) => !estaRemovido(item))

export const itensRemovidos = (cardapio) => cardapio.filter(estaRemovido)

export const estaEmValidacao = (item) => Boolean(item.emValidacao)

// RN-15 na boca do automático (integração 44): item em validação só é
// oferecido depois que a dona confirma, e item tirado do cardápio nunca.
export const oferecivelPeloAutomatico = (item) =>
  item.estoque > 0 && !estaRemovido(item) && !estaEmValidacao(item)

export const ehNovidade = (item, agora) => {
  if (!item.novidadeAte) return false
  return new Date(item.novidadeAte).getTime() >= agora
}

// Sku novo a partir do nome: até três iniciais de até três palavras, com
// sufixo numérico só se colidir. Ela pensa em prato, não em código de
// produto, então não digita sku nenhum.
export function gerarSkuNovoItem(cardapio, nome) {
  const base = nome
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toUpperCase().replace(/[^A-Z ]/g, '')
    .split(' ').filter(Boolean).slice(0, 3).map((p) => p.slice(0, 3)).join('-') || 'ITEM'
  let sku = base
  let n = 2
  while (cardapio.some((i) => i.sku === sku)) { sku = `${base}-${n}`; n += 1 }
  return sku
}

// Novo item sempre nasce em validação (RN-15) e sem saldo lançado: ela ainda
// não produziu. `novidadeAte` é opcional, prazo em ISO ou `null`.
export function incluirItemCardapio(cardapio, dados) {
  const item = {
    sku: gerarSkuNovoItem(cardapio, dados.nome),
    nome: dados.nome.trim(),
    linha: dados.linha,
    porcao: dados.porcao.trim(),
    preco: dados.preco,
    estoque: 0,
    disponivelHoje: true,
    emValidacao: true,
    novidadeAte: dados.novidadeAte ?? null,
    removidoEm: null,
  }
  return { cardapio: [...cardapio, item], sku: item.sku }
}

// Edição pontual (nome, linha, porção, preço, novidade). `sku` nunca muda
// depois de criado: pedido antigo aponta pra ele.
export function editarItemCardapio(cardapio, sku, dados) {
  return cardapio.map((item) => (item.sku === sku ? { ...item, ...dados } : item))
}

// Ela confirma que o item vale ficar (RN-15): some o rótulo de validação,
// sem mexer em mais nada.
export function confirmarValidacaoItem(cardapio, sku) {
  return cardapio.map((item) => (item.sku === sku ? { ...item, emValidacao: false } : item))
}

// Tira sem apagar (mesma linha continua no array, achável por sku) e repõe
// pela mesma ação: alterna. `agoraIso` grava quando saiu, pra a dona saber a
// data se perguntar depois.
export function alternarRemocaoItem(cardapio, sku, agoraIso) {
  return cardapio.map((item) => (item.sku === sku
    ? { ...item, removidoEm: estaRemovido(item) ? null : agoraIso }
    : item))
}

// Adicionais possíveis para oferecer no formulário de incluir/editar: os SKUs
// que algum item do cardápio já usa como adicional (o mapa é a única fonte,
// não existe campo "isto é adicional" no item), sem repetir e sem o próprio
// item de quem está editando.
export function catalogoDeAdicionais(cardapio, mapa, skuDoItem = null) {
  const skusUnicos = [...new Set(Object.values(mapa ?? {}).flat())]
  return skusUnicos.filter((sku) => sku !== skuDoItem).map((sku) => itemPorSku(cardapio, sku)).filter(Boolean)
}
