/* eslint-disable no-console */
// Prova da issue #2232 (banca UX/UI do Atendimento, Tela 3: Ficha do cliente, celular 558x684).
//
//   F1  baixar e editar sem rótulo: um menu "Ações da ficha" com texto visível;
//   F2  endereço com ícone de local, "Copiar" (com retorno) e "Abrir no mapa" (nova aba, sem opener);
//   F3  o valor aparece uma vez: o destaque fica, a linha de apoio não o repete;
//   F4  a linha Entrega diz quando e onde, e nunca fica vazia;
//   F5  uma rolagem só na Ficha (o cabeçalho fica fora dela), sem lista rolando dentro da coluna;
//   F6  ajuda do "Marcar pago" em uma linha e confirmação com o valor;
//   F7  um menu só: as ações do pedido saem do ⋮ da barra e vão para o menu do cabeçalho.
//
// Componentes reais renderizados com os contextos (como a prova-1785); regras puras importadas;
// fonte e CSS lidos onde só existe composição.
//
//   node ferramentas/prova-2232-ficha.mjs

import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter(n => n.endsWith('.jsx')).map(async nome => {
  const arquivo = join(raiz, nome)
  const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
  return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
    if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
    return proximo(url, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const { PainelFicha } = await import('../src/features/ficha-cliente/PainelFicha.jsx')
const { BarraProximoPasso } = await import('../src/features/ficha-cliente/BarraProximoPasso.jsx')
const { BaixaAMao } = await import('../src/features/ficha-cliente/BlocoCobranca.jsx')
const ficha = await import('../src/features/ficha-cliente/apresentacaoDaFicha.js')
const { acaoDoPix } = await import('../src/features/ficha-cliente/acaoDoPix.js')
const { ContextoEstado, ContextoAcoes, ContextoCatalogo } = await import('../src/aplicacao/contextos.js')
const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { CANAIS, LINHAS_PRODUTO } = await import('../src/infra/catalogo.js')
const { moeda } = await import('../src/dominio/formato.js')

const AGORA = Date.parse('2026-11-04T12:00:00Z')
const JANELA = { janelaId: 'j1', data: '2026-11-05', horaInicio: '14:00:00', horaFim: '17:00:00', label: 'Tarde', vagasRestantes: 3, capacidade: 5 }
const pedidoApi = (alteracoes = {}) => pedidoDaApi({
  pedidoId: 'pedido-2232', numeroComanda: 7, status: 'aguardando_pagamento', total: 19, frete: 0,
  itens: [{ cardapioItemId: 'ravioli', nome: 'Ravióli', quantidade: 1, precoUnitario: 19 }],
  alocado: 0, totalPago: 0, pagamentos: [], janela: JANELA, ...alteracoes,
})
const conversa = (alteracoes = {}, cliente = {}) => ({
  id: 'bia', nome: 'Bianca Toledo', estado: 'Em atendimento', canal: 'WhatsApp', conta: 'cliente', clienteId: 'cliente-bia',
  cliente: { telefone: '11999990001', endereco: 'Rua Fernão Dias, 220, Pinheiros, 05427-010', pedidos: 1, tags: [], notas: [], ...cliente },
  mensagens: [], pedido: pedidoApi(), ...alteracoes,
})
const comContextos = (selecionada, filho, { fonteApi = true } = {}) =>
  h(ContextoAcessoModulos.Provider, { value: { acoes: {} } },
    h(ContextoEstado.Provider, { value: { selecionada, visiveis: [selecionada], conversas: [selecionada], alertasEstoque: [], automaticoPausado: {},
      agora: AGORA, fonteApi, fidelidade: { ativa: false }, historico: [] } },
    h(ContextoCatalogo.Provider, { value: { cardapio: [], canais: CANAIS, linhas: LINHAS_PRODUTO, janelas: [], entregadores: [], meiosDePagamento: [], restricoes: [] } },
    h(ContextoAcoes.Provider, { value: {} }, filho))))
const renderizarFicha = (alteracoes = {}, cliente = {}) =>
  renderToStaticMarkup(comContextos(conversa(alteracoes, cliente), h(PainelFicha, { aoAbrirCardapio() {} })))
const trecho = (html, abre, fecha) => html.match(new RegExp(`${abre}[\\s\\S]*?${fecha}`))?.[0] ?? ''

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}
const confereAsync = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// --- F1 ------------------------------------------------------------------------------------------

confere('F1  o cabeçalho tem um menu com texto "Ações", sem botão só de ícone para baixar e editar', () => {
  assert.equal(ficha.ROTULO_DO_MENU_DA_FICHA, 'Ações da ficha')
  const cabecalho = trecho(renderizarFicha(), '<header class="cabecalhoFicha"', '</header>')
  assert.match(cabecalho, /aria-haspopup="menu"/)
  assert.match(cabecalho, />Ações</, 'rótulo visível no gatilho')
  assert.doesNotMatch(cabecalho, /iconeFicha|aria-label="Editar cadastro"|aria-label="Baixar ficha em PDF"/)
})
confere('F1  o menu reúne cadastro e PDF; as ações do pedido entram só com pedido', () => {
  const sem = ficha.itensDoMenuDaFicha({ comPedido: false }).map((i) => i.rotulo)
  assert.deepEqual(sem, ['Editar cadastro', 'Baixar ficha em PDF'])
  const com = ficha.itensDoMenuDaFicha({ comPedido: true, acoes: { podeCancelar: true } }).map((i) => i.chave)
  assert.deepEqual(com, ['editar', 'baixar', 'cancelar', 'encerrar'])
})

// --- F2 ------------------------------------------------------------------------------------------

confere('F2  o endereço mostra ícone de local, "Copiar" e "Abrir no mapa" em nova aba, sem opener', () => {
  const html = renderizarFicha()
  const bloco = trecho(html, '<div class="enderecoCliente"', '</div></div>|</div>')
  assert.match(html, /enderecoCliente/, 'bloco do endereço')
  assert.match(bloco, /Rua Fernão Dias, 220, Pinheiros/)
  assert.match(bloco, /lucide-map-pin|map-pin/i, 'ícone de local')
  assert.match(bloco, />Copiar</)
  const link = bloco.match(/<a [^>]*>/)?.[0] ?? ''
  assert.match(link, /href="https:\/\/www\.google\.com\/maps\/search\/\?api=1&amp;query=Rua%20Fern%C3%A3o%20Dias%2C%20220%2C%20Pinheiros%2C%2005427-010"/)
  assert.match(link, /target="_blank"/)
  assert.match(link, /rel="noopener noreferrer"/)
  assert.match(bloco, />Abrir no mapa</)
})
await confereAsync('F2  copiar devolve se copiou; sem permissão ou sem API devolve falso e o link do mapa codifica o texto', async () => {
  const guardado = []
  assert.equal(await ficha.copiarTexto('Rua A, 1', { writeText: async (t) => { guardado.push(t) } }), true)
  assert.deepEqual(guardado, ['Rua A, 1'])
  assert.equal(await ficha.copiarTexto('x', { writeText: async () => { throw new Error('negado') } }), false)
  assert.equal(await ficha.copiarTexto('x', null), false)
  assert.equal(ficha.linkDoMapa('Rua A, 1 & 2'), 'https://www.google.com/maps/search/?api=1&query=Rua%20A%2C%201%20%26%202')
  assert.match(fonte('features/ficha-cliente/CartaoDoCliente.jsx'), /Copiado/, 'retorno visível')
})

// --- F3 ------------------------------------------------------------------------------------------

confere('F3  o valor do saldo aparece uma vez no Resumo (destaque), sem repetir na linha de apoio', () => {
  const resumo = trecho(renderizarFicha(), '<section class="resumoAtual"', '</section>')
  assert.match(resumo, /19,00 a receber/, 'destaque')
  assert.equal((resumo.match(/19,00/g) ?? []).length, 1, resumo)
})
confere('F3  linha de apoio: itens e, com sinal a receber, "sinal de R$ X"; o total some só quando repete o destaque', () => {
  assert.equal(ficha.linhaDoResumo({ estado: 'Cobrança pendente', itens: 1, total: 38, saldo: 38, destaque: true, sinal: 19 }),
    `Cobrança pendente · 1 item · sinal de ${moeda(19)}`)
  assert.equal(ficha.linhaDoResumo({ estado: 'Cobrança pendente', itens: 2, total: 38, saldo: 38, destaque: true }),
    'Cobrança pendente · 2 itens')
  assert.equal(ficha.linhaDoResumo({ estado: 'Pago', itens: 2, total: 81, saldo: 43, destaque: true }),
    `Pago · 2 itens · ${moeda(81)}`, 'total e saldo diferentes são dois números')
  assert.equal(ficha.linhaDoResumo({ estado: 'Pago', itens: 1, total: 19, saldo: 0, destaque: false }), `Pago · 1 item · ${moeda(19)}`)
})

// --- F4 ------------------------------------------------------------------------------------------

confere('F4  a linha Entrega diz a janela e o bairro, e nunca fica vazia', () => {
  assert.equal(ficha.resumoDaEntrega({ faixa: 'qui 05/11 · 14:00 às 17:00 · Tarde', endereco: 'x', bairro: 'Pinheiros' }), 'qui 05/11, 14h–17h · Pinheiros')
  assert.equal(ficha.resumoDaEntrega({ faixa: '19h30 às 20h30', endereco: 'x' }), '19h30–20h30')
  assert.equal(ficha.resumoDaEntrega({ endereco: 'x', bairro: 'Pinheiros' }), 'Pinheiros')
  assert.equal(ficha.resumoDaEntrega({ endereco: 'x' }), 'Entrega a combinar')
  assert.equal(ficha.resumoDaEntrega({}), 'Adicionar endereço')
  assert.equal(ficha.bairroDoEndereco('Rua Fernão Dias, 220, Pinheiros, 05427-010'), 'Pinheiros')
  assert.equal(ficha.bairroDoEndereco('Rua de teste, 10'), null, 'o número não é bairro')
})
confere('F4  na Ficha renderizada o acesso Entrega traz o dia, a janela e o bairro', () => {
  const html = renderizarFicha()
  const acesso = trecho(html, '<strong>Entrega</strong>', '</button>')
  assert.match(acesso, /qui 05\/11, 14h–17h · Pinheiros/, acesso)
  assert.doesNotMatch(html, /<small><\/small>/, 'sem linha de apoio vazia')
  const semNada = renderizarFicha({ pedido: pedidoApi({ janela: null }) }, { endereco: 'Rua de teste, 10' })
  assert.match(trecho(semNada, '<strong>Entrega</strong>', '</button>'), /Entrega a combinar/)
})

// --- F5 ------------------------------------------------------------------------------------------

confere('F5  uma rolagem só: .conteudo rola, o cabeçalho fica fora e nenhuma lista rola dentro da coluna', () => {
  const css = (arquivo) => fonte(`features/ficha-cliente/${arquivo}`)
  const regra = (texto, nome) => texto.match(new RegExp(`(?:^|\\n)\\.${nome}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
  const fichaCss = css('ficha.module.css')
  assert.match(regra(fichaCss, 'conteudo'), /overflow-y: auto;/)
  assert.match(regra(fichaCss, 'cabecalhoFicha'), /flex-shrink: 0;/)
  assert.match(regra(fichaCss, 'painel'), /flex-direction: column;/)
  for (const [arquivo, nome] of [['NotasDoCliente.module.css', 'lista'], ['AreaColaborativaIa.module.css', 'blocos']]) {
    assert.doesNotMatch(regra(css(arquivo), nome), /overflow-y: (auto|scroll)|max-height/, `${arquivo} .${nome} rola dentro da Ficha`)
  }
  const moldura = fonte('app/moldura.module.css')
  assert.match(regra(moldura, 'painelFicha'), /overflow: hidden;/, 'a coluna da Ficha não rola por cima')
  assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'), /<header className=\{css\.cabecalhoFicha\}[\s\S]*?<\/header>[\s\S]*?<div className=\{css\.conteudo\}>/)
})

// --- F6 ------------------------------------------------------------------------------------------

confere('F6  a ajuda do "Marcar pago" cabe em uma linha', () => {
  const info = acaoDoPix({ chave: 'aguardando' }, { linkPendenteApi: true, aoAbrirBaixa() {} })
  assert.match(info.nota, /Mercado Pago confirma sozinho/)
  assert.ok(info.nota.length <= 64, `${info.nota.length} caracteres: ${info.nota}`)
})
confere('F6  ao abrir a baixa a pergunta diz o valor: "Marcar R$ 19,00 como pago?"', () => {
  assert.equal(ficha.perguntaDaBaixa(19), `Marcar ${moeda(19)} como pago?`)
  assert.equal(ficha.perguntaDaBaixa(19, { sinal: true }), `Registrar sinal de ${moeda(19)}?`)
  const html = renderToStaticMarkup(h(BaixaAMao, { valorCobrado: 19, pedido: null, aoConfirmar() {}, aoCancelar() {} }))
  assert.match(html, new RegExp(`Marcar ${moeda(19).replace(/[$]/g, '\\$')} como pago\\?`))
})

// --- F7 ------------------------------------------------------------------------------------------

const pedidoDemo = (alteracoes = {}) => ({
  estado: 'pago', itens: [], pagamentos: [], meio: 'pix', numero: '1',
  cobranca: { id: 'cb', meio: 'pix', pagaEm: 1, valor: 100, valorPago: 100, criadaEm: 1, expiraEm: 2 }, ...alteracoes,
})
const renderizarBarra = (pedido, extra = {}) => renderToStaticMarkup(comContextos({ id: 'c1', nome: 'Ana', pedido }, h(BarraProximoPasso, {
  pedido, conversaId: 'c1', nomeCliente: 'Ana', agora: AGORA, emDesfazer: false, aoAvancar() {}, aoDesfazer() {}, ...extra,
}), { fonteApi: false }))
confere('F7  a barra do pedido não tem mais o ⋮ ao lado de "Marcar pago"', () => {
  const html = renderizarBarra(pedidoDemo())
  assert.doesNotMatch(html, /aria-haspopup="menu"|Mais ações do pedido/)
  assert.doesNotMatch(fonte('features/ficha-cliente/BarraProximoPasso.jsx'), /MenuDoPedido/)
  assert.equal(existsSync(new URL('../src/features/ficha-cliente/MenuDoPedido.jsx', import.meta.url)), false)
})
confere('F7  as ações do pedido moram no menu do cabeçalho e abrem a mesma confirmação da barra', () => {
  const acoes = ficha.acoesDoPedido({ pedido: pedidoDemo(), fonteApi: false })
  assert.equal(acoes.podeEstornar, true)
  assert.equal(acoes.podeVoltarEtapa, true)
  assert.equal(acoes.podeCancelar, false, 'pago na demonstração não cancela')
  assert.equal(ficha.acoesDoPedido({ pedido: pedidoDemo({ estado: 'cancelado' }) }).podeEstornar, false)
  assert.equal(ficha.acoesDoPedido({ pedido: null }).podeCancelar, false)
  // A barra abre a confirmação quando o menu pede (render-phase, como o `pedidoDeCancelar`): o `n` novo reabre.
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.match(barra, /pedidoDeAcao\.n !== acaoVista/)
  assert.match(barra, /iniciarConfirmacao\(pedidoDeAcao\.chave\)/)
  assert.doesNotMatch(renderizarBarra(pedidoDemo()), /Valor a devolver/, 'sem pedido do menu, nada abre sozinho')
  const painel = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(painel, /<MenuDaFicha/)
  assert.match(painel, /pedidoDeAcao=\{/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
