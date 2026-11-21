// #2232 (banca UX/UI, Tela 2): o que o balão faz com o texto da mensagem. Puro, sem React:
// a tela renderiza, a regra mora aqui. Cobre B2 (comanda), C2 (link de pagamento) e C1 (resumo do pedido).
//
// Extensão .js em toda importação daqui: o Node resolve este arquivo direto nas provas.

// --- Dinheiro ------------------------------------------------------------------------------------
const DINHEIRO = /R\$\s*\d[\d.]*,\d{2}/g
const UM_DINHEIRO = /R\$\s*(\d[\d.]*,\d{2})/
// Só normaliza o espaço do trecho já escrito em reais; não lê valor (isso é do formato.js).
const comoReais = (valor) => `R$ ${UM_DINHEIRO.exec(valor)[1]}`

// --- B2: o link da comanda vira botão "Abrir comanda N" ------------------------------------------
// O número vem da própria mensagem ("Comanda 16", "Comanda #16") ou, na falta dele, da conversa.
const NUMERO_NA_MENSAGEM = /\bcomanda\s*#?\s*(\d+)/i
export function rotuloDaComanda(texto, numeroComanda = null) {
  const numero = NUMERO_NA_MENSAGEM.exec(String(texto ?? ''))?.[1] ?? (numeroComanda ? String(numeroComanda) : null)
  return numero ? `Abrir comanda ${numero}` : 'Abrir comanda'
}

// --- C2: link de pagamento -----------------------------------------------------------------------
// Só o que a casa cobra: Mercado Pago e os provedores de link mais comuns. Outro host fica texto.
const HOSTS_DE_PAGAMENTO = [
  'mercadopago.com.br', 'mercadopago.com', 'mpago.la', 'mpago.li',
  'pagseguro.uol.com.br', 'pag.ae', 'pagar.me', 'checkout.stripe.com', 'buy.stripe.com',
  'asaas.com', 'infinitepay.io', 'picpay.me',
]

export function ehLinkDePagamento(url) {
  try {
    const u = new URL(url)
    if (u.protocol !== 'https:' || u.username || u.password) return false
    const host = u.hostname.toLowerCase()
    return HOSTS_DE_PAGAMENTO.some((h) => host === h || host.endsWith('.' + h))
  } catch {
    return false
  }
}

// Quanto o cliente vai pagar por esse link: o sinal, quando a encomenda pede; senão o total; senão o
// único valor que a mensagem cita. Com mais de um valor e nenhum deles marcado, não adivinha.
export function valorDoPagamento(texto) {
  const t = String(texto ?? '')
  const sinal = /Sinal agora por link:\s*(R\$\s*\d[\d.]*,\d{2})/i.exec(t)
  if (sinal) return comoReais(sinal[1])
  const total = /(?:^|\n)\s*Total(?: dos itens)?:\s*(R\$\s*\d[\d.]*,\d{2})/i.exec(t)
  if (total) return comoReais(total[1])
  const todos = [...new Set((t.match(DINHEIRO) ?? []).map(comoReais))]
  return todos.length === 1 ? todos[0] : null
}

export const rotuloDoLinkDePagamento = (valor) => (valor ? `Pagar ${valor}` : 'Abrir link de pagamento')

const URL_NO_TEXTO = /(https:\/\/[^\s<>]+)/
const PONTUACAO_NO_FIM = /[.,;:!?)\]]+$/

// Recebe as partes de `partesComLinkDoCardapio` e abre os trechos de texto em texto + link de pagamento.
// O `href` é a URL como veio; a pontuação colada no fim ("...abc123.") volta para o texto.
export function comLinksDePagamento(partes, textoCompleto) {
  const valor = valorDoPagamento(textoCompleto)
  const saida = []
  const empurrar = (parte) => {
    const ultima = saida.at(-1)
    if (parte.tipo === 'texto' && ultima?.tipo === 'texto') ultima.texto += parte.texto
    else saida.push(parte)
  }
  for (const parte of partes) {
    if (parte.tipo !== 'texto') { empurrar(parte); continue }
    for (const pedaco of parte.texto.split(URL_NO_TEXTO)) {
      if (pedaco === '') continue
      if (!pedaco.startsWith('https://')) { empurrar({ tipo: 'texto', texto: pedaco }); continue }
      const url = pedaco.replace(PONTUACAO_NO_FIM, '')
      if (!ehLinkDePagamento(url)) { empurrar({ tipo: 'texto', texto: pedaco }); continue }
      empurrar({ tipo: 'pagamento', texto: url, rotulo: rotuloDoLinkDePagamento(valor) })
      if (url.length < pedaco.length) empurrar({ tipo: 'texto', texto: pedaco.slice(url.length) })
    }
  }
  return saida
}

// --- C1: resumo do pedido em cartão --------------------------------------------------------------
// O texto do resumo é livre (vem do servidor e do console). Só vira cartão quando reconhecido: pelo
// menos uma linha "Nx Item" e uma linha "Total: R$ …". Qualquer outra coisa continua texto.
//   Resumo do seu pedido:
//   - 2x Lasanha (sem cebola): R$ 76,00
//   Frete: R$ 10,00
//   Total: R$ 86,00
//   Entrega: sex 09/10, das 12:00 às 14:00
const LINHA_DE_ITEM = /^\s*[-•*]\s*(\d+(?:[.,]\d+)?)\s*[x×]\s+(.+?)(?::\s*(R\$\s*\d[\d.]*,\d{2}))?\s*$/i
const LINHA_DE_VALOR = /^\s*([^:\n]{2,40}):\s*(R\$\s*\d[\d.]*,\d{2})\s*(\(.*\))?\s*$/
const LINHA_DE_DETALHE = /^\s*(Entrega|Pagamento)\b[^:\n]*:\s*(\S.*)$/i

export function resumoDoPedidoNoTexto(texto) {
  const linhas = String(texto ?? '').split('\n')
  const primeiro = linhas.findIndex((l) => LINHA_DE_ITEM.test(l))
  if (primeiro < 0) return null
  const cabecalho = linhas.slice(0, primeiro).join('\n').trim()
  const itens = []
  let i = primeiro
  for (; i < linhas.length; i += 1) {
    const achado = LINHA_DE_ITEM.exec(linhas[i])
    if (!achado) break
    itens.push({
      // A quantidade fica como o servidor escreveu ("1,5"): é texto de tela em pt-BR, não número.
      qtd: achado[1],
      descricao: achado[2].trim(),
      valor: achado[3] ? comoReais(achado[3]) : null,
    })
  }
  const rodape = []
  for (; i < linhas.length; i += 1) {
    const linha = linhas[i]
    if (linha.trim() === '') break
    const valor = LINHA_DE_VALOR.exec(linha)
    if (valor) {
      rodape.push({
        tipo: 'valor', rotulo: valor[1].trim(), valor: comoReais(valor[2]), nota: valor[3]?.trim() ?? null,
        forte: /^total\b/i.test(valor[1].trim()),
      })
      continue
    }
    const detalhe = LINHA_DE_DETALHE.exec(linha)
    if (detalhe) { rodape.push({ tipo: 'detalhe', rotulo: detalhe[1], texto: detalhe[2].trim() }); continue }
    break
  }
  if (!rodape.some((l) => l.tipo === 'valor' && l.forte)) return null
  return { cabecalho, itens, rodape, depois: linhas.slice(i).join('\n').trim() }
}
