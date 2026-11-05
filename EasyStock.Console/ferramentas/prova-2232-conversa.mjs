/* eslint-disable no-console */
// Prova da #2232 (banca UX/UI do Atendimento), Tela 2: a conversa, no celular (558x684).
// Uma verificação por achado, nomeada com o id da banca. Falha antes da mudança, passa depois.
//
//   A2  faixa "O automático passou para você · motivo", sem pílula com quadrado de 8 px
//   A3  uma ação primária ("Assumir conversa"); o resto no menu ⋮
//   A4  um menu só na conversa (o do cabeçalho)
//   A5  a conversa abre na última mensagem (também quando o painel estava escondido) e avisa "Novas mensagens"
//   A6  balão enviado a 75 % no máximo, cor própria e ponta na direção do remetente (geometria, sem raio)
//   A7  microfone vira "Enviar" ao digitar (já atendia)
//   A8  modo nota com fundo de nota e o aviso "Só a equipe vê"
//   B1  mensagem de sistema como pílula central neutra
//   B2  URL da comanda vira "Abrir comanda N"
//   B3  pílula de data fixa no topo ao rolar
//   C1  resumo do pedido vira cartão com números tabulares à direita
//   C2  link de pagamento vira "Pagar R$ 19,00" (ou "Abrir link de pagamento"), em nova aba
//   C3  tique de status com ícone e palavra, não só cor
//
// Regras puras do domínio, componentes renderizados no react-dom/server e leitura do fonte onde só há CSS.
//
//   node ferramentas/prova-2232-conversa.mjs

import { readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'

process.env.NODE_ENV = 'production'
globalThis.window = { location: { origin: 'http://127.0.0.1:5173' }, open() {} }

// --- Carga dos módulos do console no node (mesmo caminho da prova 1577) -----------------------------
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const arquivosDe = (dir) => readdirSync(dir, { withFileTypes: true })
  .flatMap((e) => (e.isDirectory() ? arquivosDe(join(dir, e.name)) : [join(dir, e.name)]))
const jsx = new Map()
await Promise.all(arquivosDe(raiz).filter((a) => a.endsWith('.jsx')).map(async (arquivo) => {
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env.DEV', 'false')
  jsx.set(pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code)
}))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const extensao of ['.js', '.jsx']) {
        try { return proximo(especificador + extensao, contexto) } catch { /* próximo formato */ }
      }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', shortCircuit: true, source: jsx.get(url) }
    if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default new Proxy({}, { get: (_, p) => String(p) })' }
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = false\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const { createElement: h } = await import('react')
const { renderToStaticMarkup } = await import('react-dom/server')
// Módulos novos desta entrega: antes dela não existem, e a verificação falha dizendo isso.
const novo = async (caminho) => import(caminho).catch(() => ({}))
const existe = (modulo, nome) => assert.equal(typeof modulo[nome], 'function', `${nome} ainda não existe`)
const conteudo = await novo('../src/dominio/conteudoDaMensagem.js')
const transferencia = await novo('../src/dominio/faixaDeTransferencia.js')
const modo = await novo('../src/dominio/acoesDoModo.js')
const rolagem = await import('../src/dominio/rolagemDoFio.js')
const { tiqueDaMensagem } = await import('../src/dominio/mensagem.js')
const { Balao } = await import('../src/features/atendimento/Balao.jsx')
const { BarraModo } = await import('../src/features/atendimento/BarraModo.jsx')
const { Thread } = await import('../src/features/atendimento/Thread.jsx')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}
const html = (elemento) => renderToStaticMarkup(elemento)
const tira = (marcado) => marcado.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim()
const css = fonte('features/atendimento/atendimento.module.css')
const regra = (seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...css.matchAll(new RegExp(`(?:^|\\n)${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}

const EM = '2026-10-29T13:02:00.000Z'
const mensagem = (extra) => ({ id: 'm1', dir: 'out', texto: 'oi', em: EM, status: 'entregue', ...extra })
const balao = (extra, props = {}) => html(h(Balao, { mensagem: mensagem(extra), ...props }))
const passagem = { motivo: 'Restrição alimentar', origem: 'agente', em: EM }
const conversaComVoce = { id: 'c1', nome: 'Thais Regina', estado: 'Aberto', passagem, mensagens: [], pedido: null }
const barra = (conversa, pausado = false, bloqueada = false) => html(h(BarraModo, {
  conversa, pausado, bloqueada, aoAssumir() {}, aoDevolver() {}, aoEncerrar() {},
}))

const SERVIDOR = 'Resumo do seu pedido:\n- 2x Lasanha 600 g (sem cebola): R$ 76,00\n- 1x Suco de uva: R$ 12,00\n'
  + 'Frete: R$ 10,00\nTotal: R$ 98,00\nEntrega: sex 09/10, das 12:00 às 14:00\nPagamento: link online (Pix ou cartão)'
const MERCADO_PAGO = 'https://link.mercadopago.com.br/casadababa/cdbcartao2026000101'

// --- A2 -------------------------------------------------------------------------------------------
await confere('A2  a faixa de transferência diz "O automático passou para você · motivo"', () => {
  existe(transferencia, 'faixaDaTransferencia')
  const faixa = transferencia.faixaDaTransferencia(passagem)
  assert.equal(faixa.texto, 'O automático passou para você · Restrição alimentar')
  assert.match(faixa.longo, /não soube responder e passou para você/, 'a frase longa continua no title')
  assert.equal(transferencia.faixaDaTransferencia(null), null)
})
await confere('A2  a barra da conversa renderiza a faixa, sem a pílula "Passou para você"', () => {
  const marcado = barra(conversaComVoce)
  assert.match(tira(marcado), /O automático passou para você · Restrição alimentar/)
  assert.doesNotMatch(marcado, /Passou para você/)
  assert.match(css, /\.faixaTransferencia\s*\{[^}]*border-left:/, 'a direção da faixa vem de uma barra lateral, sem raio')
})

// --- A3 e A4 --------------------------------------------------------------------------------------
await confere('A3  uma ação primária: "Assumir conversa"; Devolver e o resto não ficam na faixa', () => {
  existe(modo, 'acoesDoModo')
  const marcado = barra(conversaComVoce)
  assert.match(marcado, />Assumir conversa</)
  assert.equal((marcado.match(/<button/g) ?? []).length, 1, 'um botão só na faixa')
  assert.doesNotMatch(marcado, /Devolver ao Assistente|Concluir conversa|Mais ações/)
  const acoes = modo.acoesDoModo(conversaComVoce)
  assert.equal(acoes.podeAssumir, true)
  assert.equal(acoes.podeDevolver, true)
})
await confere('A4  um menu só: a faixa não abre menu e o ⋮ do cabeçalho leva Devolver ao Assistente', () => {
  assert.doesNotMatch(barra(conversaComVoce), /<details/)
  const painel = fonte('features/atendimento/PainelAtendimento.jsx')
  assert.equal((painel.match(/<details/g) ?? []).length, 1, 'um <details> só na conversa')
  assert.match(painel, /<DevolverAoAssistente[^>]*conversa=\{selecionada\}/)
  assert.doesNotMatch(fonte('features/atendimento/BarraModo.jsx'), /maisAcoesModo|Mais ações/)
  const bloqueada = modo.acoesDoModo?.({ ...conversaComVoce, estado: 'Aberto' }, false, true)
  assert.deepEqual([bloqueada?.podeAssumir, bloqueada?.podeDevolver], [false, false], 'bloqueada não oferece nada')
})

// --- A5 -------------------------------------------------------------------------------------------
await confere('A5  painel que ficou visível rola para a última mensagem; o já visível, não', () => {
  existe(rolagem, 'deveRolarAoFicarVisivel')
  assert.equal(rolagem.deveRolarAoFicarVisivel(false, true), true)
  assert.equal(rolagem.deveRolarAoFicarVisivel(true, true), false)
  assert.equal(rolagem.deveRolarAoFicarVisivel(true, false), false)
  assert.equal(rolagem.deveRolarAoFicarVisivel(undefined, true), false)
})
await confere('A5  fala nova fora da vista conta como "Novas mensagens"; rolou ou trocou de conversa, zero', () => {
  existe(rolagem, 'novasForaDaVista')
  const antes = { conversaId: 'c1', total: 10, ultimaId: 'm10', ultimaDaCasa: false }
  const depois = { conversaId: 'c1', total: 12, ultimaId: 'm12', ultimaDaCasa: false }
  assert.equal(rolagem.novasForaDaVista(antes, depois, false), 2)
  assert.equal(rolagem.novasForaDaVista(antes, depois, true), 0, 'o fio rolou: já está à vista')
  assert.equal(rolagem.novasForaDaVista(antes, { ...depois, conversaId: 'c2' }, false), 0)
  assert.equal(rolagem.novasForaDaVista(null, depois, false), 0)
  assert.equal(rolagem.novasForaDaVista(antes, { ...depois, total: 60, ultimaId: 'm10' }, false), 0, 'histórico antigo entrou por cima')
})
await confere('A5  a visibilidade desce do painel até o hook e o botão "Novas mensagens" existe', () => {
  assert.match(fonte('features/atendimento/PainelAtendimento.jsx'), /<Thread[\s\S]*?visivel=\{visivel\}/)
  assert.match(fonte('features/atendimento/Thread.jsx'), /useRolagemDoFio\(fim, \{[^}]*visivel/)
  assert.match(fonte('features/atendimento/Thread.jsx'), /Novas mensagens/)
  const hook = fonte('features/atendimento/useRolagemDoFio.js')
  assert.match(hook, /deveRolarAoFicarVisivel/)
  assert.match(hook, /novasForaDaVista/)
  assert.match(css, /\.novasMensagens\s*\{[^}]*position:\s*sticky/)
})

// --- A6 -------------------------------------------------------------------------------------------
await confere('A6  balão com no máximo 75 %, cor própria de saída e ponta por geometria (sem raio)', () => {
  assert.match(regra('.balao'), /max-width:\s*min\(75%,/)
  assert.match(regra('.saida'), /background:\s*var\(--sucesso-pastel\)/, 'o enviado tem cor própria')
  assert.match(regra('.saida.doAutomatico'), /background:\s*var\(--preenchimento-forte\)/, 'o automático segue neutro')
  for (const lado of ['saida', 'entrada']) {
    const ponta = regra(`.${lado}::after`)
    assert.match(ponta, /clip-path:\s*polygon\(/, `ponta do .${lado} por clip-path`)
    assert.match(ponta, /background:\s*inherit/, `ponta do .${lado} na cor do balão`)
  }
  assert.match(regra('.saida::after'), /left:\s*100%/, 'a ponta do enviado aponta para a direita')
  assert.match(regra('.entrada::after'), /right:\s*100%/, 'a ponta do recebido aponta para a esquerda')
  assert.doesNotMatch(regra('.saida::after') + regra('.entrada::after'), /border-radius:\s*[1-9]/)
})

// --- A7 -------------------------------------------------------------------------------------------
await confere('A7  já atende: com texto ou anexo o botão é "Enviar"; vazio, é o microfone', () => {
  const composer = fonte('features/atendimento/Composer.jsx')
  assert.match(composer, /\{temTexto \|\| anexo \|\| !acoes\.audio \? \(/)
  assert.match(composer, /aria-label="Enviar mensagem"[\s\S]*?>Enviar<\/Botao>/)
  assert.match(composer, /rotulo="Gravar áudio"[\s\S]*?icone="mic"/)
})

// --- A8 -------------------------------------------------------------------------------------------
await confere('A8  modo nota: fundo de nota e aviso "Só a equipe vê"', () => {
  const painel = fonte('features/atendimento/PainelAtendimento.jsx')
  assert.match(painel, /editorNotaVisivel[\s\S]{0,200}css\.modoNota/)
  assert.match(painel, /Só a equipe vê/)
  assert.match(regra('.modoNota'), /background:\s*var\(--alerta-pastel\)/)
  assert.match(regra('.avisoNota'), /color:\s*var\(--tinta\)/)
})

// --- B1 -------------------------------------------------------------------------------------------
await confere('B1  mensagem de sistema é uma pílula central neutra, definida uma vez', () => {
  const marcado = balao({ dir: 'sistema', texto: 'Conversa reaberta' })
  assert.match(marcado, /class="[^"]*\bsistema\b/)
  const pilula = regra('.sistema')
  assert.match(pilula, /align-self:\s*center/)
  assert.match(pilula, /background:\s*var\(--preenchimento\)/)
  assert.match(pilula, /border:\s*var\(--b-1\) solid var\(--linha\)/)
  assert.equal((css.match(/(?:^|\n)\.sistema\s*\{/g) ?? []).length, 1, 'uma regra só, sem sobrescrita')
})

// --- B2 -------------------------------------------------------------------------------------------
const URL_COMANDA = 'https://casadababa.com/comanda.html?c=Xa91'
await confere('B2  o rótulo da comanda leva o número da mensagem ou da conversa', () => {
  existe(conteudo, 'rotuloDaComanda')
  assert.equal(conteudo.rotuloDaComanda(`Aqui está sua comanda: ${URL_COMANDA}\nComanda 16.`), 'Abrir comanda 16')
  assert.equal(conteudo.rotuloDaComanda(`Comanda #7 ${URL_COMANDA}`), 'Abrir comanda 7')
  assert.equal(conteudo.rotuloDaComanda(`Aqui está sua comanda: ${URL_COMANDA}`, 12), 'Abrir comanda 12')
  assert.equal(conteudo.rotuloDaComanda(`Aqui está sua comanda: ${URL_COMANDA}`), 'Abrir comanda')
})
await confere('B2  a bolha mostra o botão "Abrir comanda 16", sem a URL crua', () => {
  const marcado = balao({ texto: `Aqui está sua comanda: ${URL_COMANDA}\nEscolha os pratos. Comanda 16.` })
  assert.match(tira(marcado), /Abrir comanda 16/)
  assert.doesNotMatch(tira(marcado), /casadababa\.com\/comanda\.html/, 'a URL cabe no href, não no texto')
  assert.match(marcado, new RegExp(`href="${URL_COMANDA.replace(/[.?]/g, '\\$&')}"`))
})

// --- B3 -------------------------------------------------------------------------------------------
await confere('B3  a pílula de data fica fixa no topo do seu dia', () => {
  assert.match(regra('.separadorDia'), /position:\s*sticky/)
  assert.match(regra('.separadorDia'), /top:/)
  const fio = html(h(Thread, {
    mensagens: [
      mensagem({ id: 'a', dir: 'in', em: '2026-10-28T13:00:00.000Z' }),
      mensagem({ id: 'b', dir: 'out', em: '2026-10-29T13:00:00.000Z' }),
    ],
    chaveRolagem: 'c1', agora: Date.parse('2026-10-30T13:00:00.000Z'),
  }))
  assert.equal((fio.match(/class="grupoDia"/g) ?? []).length, 2, 'cada dia é um grupo: a data só gruda no próprio dia')
  assert.ok(fio.indexOf('class="grupoDia"') < fio.indexOf('separadorDia'), 'a pílula está dentro do grupo')
})

// --- C1 -------------------------------------------------------------------------------------------
await confere('C1  o resumo do servidor é reconhecido: itens, valores e totais', () => {
  existe(conteudo, 'resumoDoPedidoNoTexto')
  const resumo = conteudo.resumoDoPedidoNoTexto(SERVIDOR)
  assert.equal(resumo.cabecalho, 'Resumo do seu pedido:')
  assert.deepEqual(resumo.itens, [
    { qtd: '2', descricao: 'Lasanha 600 g (sem cebola)', valor: 'R$ 76,00' },
    { qtd: '1', descricao: 'Suco de uva', valor: 'R$ 12,00' },
  ])
  assert.deepEqual(resumo.rodape.map((l) => [l.rotulo, l.valor ?? l.texto]),
    [['Frete', 'R$ 10,00'], ['Total', 'R$ 98,00'], ['Entrega', 'sex 09/10, das 12:00 às 14:00'], ['Pagamento', 'link online (Pix ou cartão)']])
  assert.equal(resumo.rodape.find((l) => l.rotulo === 'Total').forte, true)
  assert.equal(resumo.depois, '')
})
await confere('C1  texto que não é resumo continua texto', () => {
  existe(conteudo, 'resumoDoPedidoNoTexto')
  assert.equal(conteudo.resumoDoPedidoNoTexto('Oi! Total: R$ 10,00 fica bom?'), null)
  assert.equal(conteudo.resumoDoPedidoNoTexto('- 2x Lasanha: R$ 76,00\nobrigada!'), null, 'sem Total não vira cartão')
  assert.equal(conteudo.resumoDoPedidoNoTexto(''), null)
  assert.equal(conteudo.resumoDoPedidoNoTexto(null), null)
})
await confere('C1  a bolha desenha o cartão com valores tabulares à direita e a cobrança depois dele', () => {
  const marcado = balao({ texto: `${SERVIDOR}\n\nPague por este link, válido por 30 minutos: ${MERCADO_PAGO}` })
  assert.match(marcado, /<dl class="[^"]*cartaoPedido/)
  assert.match(marcado, /<dd[^>]*>R\$ 76,00<\/dd>/)
  assert.match(marcado, /Pagar R\$ 98,00/, 'a cobrança sai depois do cartão, com o valor do total')
  assert.match(regra('.valorPedido') + regra('.cartaoPedido dd'), /font-variant-numeric:\s*tabular-nums/)
  assert.match(regra('.valorPedido') + regra('.cartaoPedido dd'), /text-align:\s*right/)
})

// --- C2 -------------------------------------------------------------------------------------------
await confere('C2  só link de pagamento da lista vira botão de pagar; o resto é texto', () => {
  existe(conteudo, 'ehLinkDePagamento')
  assert.equal(conteudo.ehLinkDePagamento(MERCADO_PAGO), true)
  assert.equal(conteudo.ehLinkDePagamento('https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=abc'), true)
  assert.equal(conteudo.ehLinkDePagamento('http://link.mercadopago.com.br/x'), false, 'sem https não')
  assert.equal(conteudo.ehLinkDePagamento('https://mercadopago.com.br.golpe.example/x'), false)
  assert.equal(conteudo.ehLinkDePagamento('https://golpe.example/?u=mercadopago.com'), false)
  assert.equal(conteudo.ehLinkDePagamento('javascript:alert(1)'), false)
})
await confere('C2  o valor é o sinal, o total ou o único valor citado; na dúvida, nenhum', () => {
  existe(conteudo, 'valorDoPagamento')
  assert.equal(conteudo.valorDoPagamento('Total: R$ 19,00\nPix de 30 minutos: x'), 'R$ 19,00')
  assert.equal(conteudo.valorDoPagamento('Total: R$ 98,00\nSinal agora por link: R$ 49,00\nRestante na entrega: R$ 49,00'), 'R$ 49,00')
  assert.equal(conteudo.valorDoPagamento('São R$ 1.250,50 no cartão: x'), 'R$ 1.250,50')
  assert.equal(conteudo.valorDoPagamento('Aqui o link: x'), null)
  assert.equal(conteudo.valorDoPagamento('De R$ 10,00 por R$ 8,00: x'), null)
  existe(conteudo, 'rotuloDoLinkDePagamento')
  assert.equal(conteudo.rotuloDoLinkDePagamento('R$ 19,00'), 'Pagar R$ 19,00')
  assert.equal(conteudo.rotuloDoLinkDePagamento(null), 'Abrir link de pagamento')
})
await confere('C2  a bolha mostra "Pagar R$ 19,00" com o href intacto, em nova aba e sem a URL crua', () => {
  const marcado = balao({ texto: `Pedido 2026-0001 anotado!\nTotal: R$ 19,00\nPix de 30 minutos: ${MERCADO_PAGO}` })
  assert.match(tira(marcado), /Pagar R\$ 19,00/)
  assert.doesNotMatch(tira(marcado), /link\.mercadopago\.com\.br/)
  const ancora = marcado.match(/<a [^>]*href="https:\/\/link\.mercadopago[^>]*>/)?.[0] ?? ''
  assert.ok(ancora, 'âncora do link de pagamento')
  assert.match(ancora, new RegExp(`href="${MERCADO_PAGO}"`))
  assert.match(ancora, /target="_blank"/)
  assert.match(ancora, /rel="noopener noreferrer"/)
})
await confere('C2  sem valor na mensagem, o rótulo é "Abrir link de pagamento"; do cliente, nunca vira link', () => {
  assert.match(tira(balao({ texto: `Aqui está o link: ${MERCADO_PAGO}` })), /Abrir link de pagamento/)
  const doCliente = balao({ dir: 'in', texto: `olha ${MERCADO_PAGO}` })
  assert.doesNotMatch(doCliente, /<a /, 'mensagem que chega do cliente não vira link (#1510)')
})

// --- C3 -------------------------------------------------------------------------------------------
await confere('C3  todo tique de status traz ícone e palavra à vista', () => {
  for (const status of ['enviada', 'entregue', 'lida']) {
    const tique = tiqueDaMensagem(status)
    assert.ok(tique.marca && tique.rotulo, `${status}: marca e rótulo`)
    const marcado = balao({ status })
    assert.match(tira(marcado), new RegExp(`${tique.marca} ${tique.rotulo}`), `${status}: "${tique.marca} ${tique.rotulo}" à vista`)
    assert.doesNotMatch(marcado, new RegExp(`class="sr"[^>]*>\\s*${tique.rotulo}`), `${status}: palavra não fica só para leitor de tela`)
  }
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
