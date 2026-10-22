/* eslint-disable no-console */
// Prova #1804 e #1805 (revisão gravada de 14/10): conversa reaberta de pedido já concluído não
// fica em "Precisa de você" sem o cliente ter escrito, e a cobrança não "gera e envia" com a
// janela de 24 h fechada.
//
// Frente C da #1889 (restos do atendimento, 22/10), no fim do arquivo:
//   #1765-10 "Não entregues" anda sozinha; #1940-2 cartão sem "Falta" na montagem; contenção de erro
//   por coluna e por tela; #1239 "Passar para…"; #1686-3/4 nova comanda e caminho para trocar item;
//   #1686-9 motivo da barra leva ao campo; #1686-10 devolução em dinheiro sem referência.
//
//   node ferramentas/prova-1804-balcao-fim.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
  // As ações do modo API puxam a infra: fonte de dados fixa e o JSON da massa como módulo.
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { motivoDePrecisar } = await import('../src/dominio/automatico.js')
const { motivoAntesDeCobrar } = await import('../src/dominio/resumoPedido.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }
const AGORA = Date.parse('2026-10-14T20:20:00Z')

const reaberta = (estadoDoPedido, ultimaFala) => ({
  id: 'c5', nome: 'Cliente Cinco', estado: 'Em atendimento', canal: 'WhatsApp',
  passagem: { motivo: 'Conversa reaberta para atendimento.', origem: 'reabertura', em: null, assumida: false },
  pedido: { numero: '5', estado: estadoDoPedido, itens: [{ sku: 'X', qtd: 1 }] },
  mensagens: [
    { id: 'm1', dir: 'in', texto: 'Oi', em: '2026-10-14T13:00:00Z' },
    { id: 'm2', dir: 'out', texto: 'Que bom que você gostou!', em: '2026-10-14T14:05:00Z', automatica: true },
    ...(ultimaFala === 'in' ? [{ id: 'm3', dir: 'in', texto: 'Mais uma coisa', em: '2026-10-14T20:00:00Z' }] : []),
  ],
})

assert.equal(motivoDePrecisar(reaberta('entregue', 'out'), AGORA)?.chave ?? null, null,
  'reaberta, pedido entregue e o cliente não escreveu: nada pendente')
ok('reaberta com pedido entregue e sem fala nova do cliente sai de "Precisa de você"')

assert.equal(motivoDePrecisar(reaberta('entregue', 'in'), AGORA)?.chave, 'passagem',
  'o cliente escreveu depois: volta a precisar de você')
ok('se o cliente escreve, a reaberta volta a "Precisa de você"')

// Revisão: no modo API a conversa fora da tela vem sem mensagens; aí vale o `naoLidas`.
const semMensagens = (naoLidas) => ({ ...reaberta('entregue', 'out'), mensagens: [], naoLidas })
assert.equal(motivoDePrecisar(semMensagens(1), AGORA)?.chave, 'passagem', 'sem mensagens e com não lida: pendente')
assert.equal(motivoDePrecisar(semMensagens(0), AGORA)?.chave ?? null, null, 'sem mensagens e sem não lida: nada pendente')
ok('conversa sem mensagens carregadas decide pelo número de não lidas')

assert.equal(motivoDePrecisar(reaberta('preparo', 'out'), AGORA)?.chave, 'passagem',
  'pedido em andamento segue pendente')
ok('reaberta com pedido em andamento continua em "Precisa de você"')

const pedidoPronto = { numero: '4', estado: 'aguardando', janela: 'j1|2026-10-15', itens: [{ sku: 'X', qtd: 2 }] }
const pronto = { pedido: pedidoPronto, clienteId: 'cli', endereco: 'Rua A, 1' }
const { permissaoDeEscrita } = await import('../src/dominio/janela.js')
const WHATSAPP = { nome: 'WhatsApp', temJanela: true, horasJanela: 24, aceitaModelo: true }
const conversaVelha = { estado: 'Em atendimento', canal: 'WhatsApp', mensagens: [{ dir: 'in', em: '2026-10-12T10:00:00Z' }] }
const fechada = permissaoDeEscrita(conversaVelha, AGORA, WHATSAPP)
assert.equal(fechada.pode, false, 'pré-condição: janela fechada')
const motivo = motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: fechada })
assert.equal(motivo, null, '#1847: janela fechada permite gerar sem envio')
ok('janela fechada permite criar a cobrança para copiar, sem alterar a permissão de envio')

assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: { pode: true } }), null, 'janela aberta não trava')
assert.equal(motivoAntesDeCobrar({ ...pronto, pedido: { ...pedidoPronto, pedidoId: 'p1' } }, { fonteApi: true, escrita: fechada }), null,
  'pedido já criado não volta a ser travado pela janela')
assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: false, escrita: fechada }), null, 'demonstração segue igual')
ok('janela aberta, pedido já criado e demonstração não mudam')

// Revisão: canal sem regra no catálogo e cliente bloqueado não são janela; a trava não vale.
const semRegra = permissaoDeEscrita({ ...conversaVelha, canal: 'Messenger' }, AGORA, undefined)
assert.equal(semRegra.pode, false, 'pré-condição: canal sem regra não escreve')
assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: semRegra }), null, 'canal sem regra não trava a cobrança')
const bloqueada = permissaoDeEscrita({ ...conversaVelha, bloqueio: { motivo: 'teste', em: '2026-10-12T10:00:00Z' } }, AGORA, WHATSAPP)
assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: bloqueada }), null, 'bloqueio tem trava própria, não a da janela')
ok('bloqueio e canal sem regra preservam suas proteções próprias')

// ── Frente C da #1889 ─────────────────────────────────────────────────────────────────────────
// Cada item roda à parte: um vermelho não esconde os outros.
const falhasC = []
const confere = async (descricao, fn) => {
  try { await fn(); ok(descricao) } catch (erro) { falhasC.push(descricao); console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`) }
}
const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')
globalThis.sessionStorage ??= { getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, usuario: { id: 'eu', nome: 'Eu' }, empresa: { id: 'empresa' } }), setItem: () => {}, removeItem: () => {} }
globalThis.window ??= { dispatchEvent: () => {} }

await confere('#1765-10 Não entregues relê a cada minuto e quando uma falha nova aparece na tela', async () => {
  const f = await import('../src/dominio/falhaDeEnvio.js')
  assert.equal(f.RECARGA_NAO_ENTREGUES_MS, 60000)
  const conversas = [{ mensagens: [{ id: 'b', status: 'falhou' }, { id: 'x', status: 'entregue' }] }, { mensagens: [{ id: 'a', status: 'falhou' }] }, {}]
  assert.equal(f.assinaturaDeFalhas(conversas), 'a,b')
  assert.equal(f.falhaNovaNaTela('a', 'a,b'), true, 'falha nova')
  assert.equal(f.falhaNovaNaTela('a,b', 'a'), false, 'falha que saiu (reenvio) não pede releitura')
  assert.equal(f.falhaNovaNaTela('a,b', 'a,b'), false)
  const tela = fonte('features/caixa-de-entrada/NaoEntregues.jsx')
  assert.match(tela, /setInterval\(\(\) => \{\s+if \(globalThis\.document\?\.visibilityState !== 'hidden'\) carregar\(\{ silencioso: true \}\)\s+\}, RECARGA_NAO_ENTREGUES_MS\)/)
  assert.match(tela, /if \(!aberto && falhaNovaNaTela\(antes, falhas\)\) carregar\(\{ silencioso: true \}\)/)
  assert.match(tela, /if \(!silencioso\) setCarregando\(true\)/, 'a releitura de fundo não pisca "Conferindo mensagens…"')
})

await confere('#1940-2 cartão da conversa: comanda em montagem no modo API não mostra "Falta" sem o frete', async () => {
  const { marcaDoPedido, MARCA_EM_MONTAGEM } = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
  const cardapio = [{ sku: 'LAS', preco: 38, nome: 'Lasanha' }]
  const montagem = { id: 'c', pedido: { numero: '7', estado: 'aguardando', itens: [{ sku: 'LAS', qtd: 1 }] } }
  assert.deepEqual(marcaDoPedido(montagem, cardapio, { fonteApi: true }), MARCA_EM_MONTAGEM)
  assert.doesNotMatch(marcaDoPedido(montagem, cardapio, { fonteApi: true }).texto, /Falta/)
  const criado = { id: 'c', pedido: { ...montagem.pedido, pedidoId: 'p1', totalApi: 50, alocadoApi: 0 } }
  assert.match(marcaDoPedido(criado, cardapio, { fonteApi: true }).texto, /^Falta R\$\s50,00$/, 'pedido criado: o total do EasyStok, com o frete')
  assert.match(marcaDoPedido(montagem, cardapio, { fonteApi: false }).texto, /^Falta/, 'a demonstração segue igual')
  assert.match(fonte('features/caixa-de-entrada/PainelConversas.jsx'), /aoAbrir=\{abrir\}\s+fonteApi=\{fonteApi\}/)
  assert.match(fonte('features/caixa-de-entrada/CartaoConversa.jsx'), /marcaDoPedido\(conversa, cardapio, \{ fonteApi \}\)/)
})

await confere('contenção de erro: cada coluna do Balcão e cada tela de módulo tem o seu limite, com "Recarregar esta parte"', () => {
  const limite = fonte('componentes/LimiteDeErro.jsx')
  assert.match(limite, /static getDerivedStateFromError\(erro\)/)
  assert.match(limite, /Recarregar esta parte/)
  assert.match(limite, /montagem: atual\.montagem \+ 1/, 'recarregar monta a parte de novo')
  const moldura = fonte('app/Moldura.jsx')
  assert.equal(moldura.match(/<LimiteDeErro parte="Lista de conversas">/g)?.length, 3, 'lista: desktop, tablet e celular')
  assert.equal(moldura.match(/<LimiteDeErro parte="Resumo">/g)?.length, 3, 'Resumo: desktop, gaveta e celular')
  assert.match(moldura, /<LimiteDeErro parte="Conversa">\s+<PainelAtendimento/)
  const modulo = fonte('features/hall/MolduraDoModulo.jsx')
  assert.match(modulo, /<LimiteDeErro parte=\{tela\?\.rotulo \?\? modulo\.nome\}>\{children\}<\/LimiteDeErro>/)
  assert.match(modulo, /<LimiteDeErro parte=\{tela\.rotulo\}>\{children\}<\/LimiteDeErro>/)
})

await confere('#1239 "Passar para…": a conversa vai ao atendente escolhido e a tela recebe o novo dono', async () => {
  const acao = await import('../src/aplicacao/acoes.js')
  const { comApi } = await import('../src/aplicacao/acoesApi.js')
  const { atendentesParaPassar } = await import('../src/features/atendimento/apresentacaoDaConversa.js')
  const chamadas = []
  let resposta = () => new Response(JSON.stringify({ data: { situacao: 'Assumida', assumidaPorUsuarioId: 'ana', assumidaPorNome: 'Ana' } }), { status: 200 })
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => { chamadas.push({ url: String(url), metodo: method, corpo: body ? JSON.parse(body) : null }); return resposta() }
  const despachos = []
  const api = comApi({}, { despachar: (d) => despachos.push(d), agoraRef: { current: 0 }, estadoRef: { current: { conversas: [{ id: 'c1' }] } } })
  assert.deepEqual(await api.transferirConversa('c1', 'ana'), { ok: true })
  const post = chamadas.find((c) => c.metodo === 'POST')
  assert.match(post.url, /\/api\/atendimento\/conversas\/c1\/transferir$/)
  assert.deepEqual(post.corpo, { paraUsuarioId: 'ana' })
  const confirmado = despachos.find((d) => d.tipo === acao.CONFIRMAR_RESPONSAVEL_API)
  assert.equal(confirmado?.situacao.responsavel, 'Ana')
  resposta = () => new Response(JSON.stringify({ error: { message: 'O usuário não atende conversas nesta empresa.' } }), { status: 422 })
  assert.deepEqual(await api.transferirConversa('c1', 'ze'), { erro: 'O usuário não atende conversas nesta empresa.' })
  assert.deepEqual(atendentesParaPassar([{ usuarioId: 'eu', nome: 'Eu' }, { usuarioId: 'ana', nome: 'Ana' }, { usuarioId: 'bia', nome: 'Bia' }], { usuarioId: 'eu', responsavelId: 'bia' }).map((a) => a.nome), ['Ana'])
  assert.match(fonte('features/atendimento/PainelAtendimento.jsx'), /\{fonteApi && !encerrada && !bloqueada && <PassarConversa key=\{selecionada\.id\} conversa=\{selecionada\} \/>\}/)
  assert.match(fonte('features/atendimento/PassarConversa.jsx'), /Passar para…/)
})

await confere('#1686-3 nova comanda na mesma conversa depois do pedido entregue ou cancelado', async () => {
  const acao = await import('../src/aplicacao/acoes.js')
  const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
  const base = (pedido) => ({ ...estadoInicial, conversas: [{ id: 'c1', nome: 'Ana', mensagens: [], pedido }] })
  const entregue = { pedidoId: 'p1', numero: 'P1', estado: 'entregue', itens: [{ sku: 'X', qtd: 1 }] }
  let estado = reducer(base(entregue), { tipo: acao.NOVA_COMANDA, id: 'c1', numeroPedido: '9' })
  const novo = estado.conversas[0].pedido
  assert.equal(novo.pedidoId, undefined, 'rascunho novo, sem o pedido antigo')
  assert.deepEqual([novo.estado, novo.itens.length], ['aguardando', 0])
  // A leitura do pedido antigo (entregue) não apaga o rascunho que ela começou.
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'c1', pedido: { ...entregue, leitura: 1 } })
  assert.equal(estado.conversas[0].pedido.pedidoId, undefined)
  const emAndamento = { ...entregue, estado: 'preparo' }
  assert.equal(reducer(base(emAndamento), { tipo: acao.NOVA_COMANDA, id: 'c1', numeroPedido: '9' }).conversas[0].pedido, emAndamento, 'pedido em andamento não é trocado')
  const ficha = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(ficha, /\{editavel && fonteApi && posVenda && novaComanda && \(/)
  assert.match(ficha, /onClick=\{\(\) => novaComanda\(selecionada\.id\)\}>Nova comanda<\/Botao>/)
})

await confere('#1686-4 pedido criado: a Seleção diz como trocar item e dá os botões (link da comanda, cancelar e refazer)', async () => {
  const r = await import('../src/dominio/resumoPedido.js')
  assert.match(r.caminhoParaTrocarItem({ estado: 'aguardando' }), /mande o link da comanda: o cliente altera e você aprova aqui\. Ou cancele este pedido e monte outro\.$/)
  assert.doesNotMatch(r.caminhoParaTrocarItem({ estado: 'preparo' }), /cancele/, 'pedido pago não sugere cancelar')
  assert.equal(r.podeCancelarParaRefazer({ pedidoId: 'p', estado: 'aguardando' }, { fonteApi: true }), true)
  assert.equal(r.podeCancelarParaRefazer({ pedidoId: 'p', estado: 'aguardando', cobranca: { pagaEm: 1 } }, { fonteApi: true }), false)
  assert.equal(r.podeCancelarParaRefazer({ pedidoId: 'p', estado: 'preparo' }, { fonteApi: true }), false)
  const bloco = fonte('features/ficha-cliente/BlocoPedido.jsx')
  assert.match(bloco, /<p className=\{css\.situacaoEnvio\}>\{caminhoParaTrocarItem\(pedido\)\}<\/p>\s+\{acaoAlterarPeloLink\}/)
  assert.match(bloco, />Cancelar e refazer<\/Botao>/)
  const ficha = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(ficha, /aoCancelarParaRefazer=\{\(\) => \{ setSecao\(null\); setPedidoDeCancelar\(\(n\) => n \+ 1\) \}\}/)
  assert.match(fonte('features/ficha-cliente/BarraProximoPasso.jsx'), /if \(pedidoDeCancelar\) \{ setMotivoCancelar\(''\); setErroCancelar\(null\); setConfirmando\('cancelar'\) \}/)
})

await confere('#1686-9 o motivo que segura a barra é link: abre a seção e foca o campo que resolve', async () => {
  const r = await import('../src/dominio/resumoPedido.js')
  const pedido = { itens: [{ sku: 'X', qtd: 1 }], janela: null }
  assert.deepEqual(r.pendenciaAntesDeCobrar({ pedido: { itens: [] } }, { fonteApi: true }).alvo, 'cardapio')
  assert.deepEqual(r.pendenciaAntesDeCobrar({ pedido, clienteId: null }, { fonteApi: true }).alvo, 'cadastro')
  assert.deepEqual(r.pendenciaAntesDeCobrar({ pedido, clienteId: 'c', endereco: null }, { fonteApi: true }).alvo, 'endereco')
  assert.deepEqual(r.pendenciaAntesDeCobrar({ pedido, clienteId: 'c', endereco: 'Rua A' }, { fonteApi: true }).alvo, 'janela')
  assert.equal(r.motivoAntesDeCobrar({ pedido, clienteId: 'c', endereco: 'Rua A' }, { fonteApi: true }), 'Escolha a janela de entrega em Seleção do atendimento.', 'o texto não muda')
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.match(barra, /<button type="button" className=\{css\.linkMotivo\} data-alvo-motivo=\{alvoDoPrerequisito\}\s+onClick=\{\(\) => aoIrParaMotivo\(alvoDoPrerequisito\)\}>\{info\.motivo\}<\/button>/)
  const ficha = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(ficha, /abrirSecao\('contato'\)\s+setSecao\('cliente'\)\s+focarCampoDaFicha\(alvo\)/)
  assert.match(ficha, /setSecao\('pedido'\); focarCampoDaFicha\('janela'\)/)
  const cliente = fonte('features/ficha-cliente/BlocoCliente.jsx')
  for (const marca of ['data-campo-ficha="cadastro"', 'data-campo-ficha="endereco"', 'data-campo-ficha="telefone cadastro"']) assert.ok(cliente.includes(marca), marca)
  assert.match(fonte('features/ficha-cliente/SeletorJanelaApi.jsx'), /data-campo-ficha="janela"/)
  // O foco escolhe o campo marcado, preferindo a vaga de janela à data.
  const { campoDaFicha } = await import('../src/features/ficha-cliente/focarCampoDaFicha.js')
  const vaga = { nome: 'vaga' }
  const area = { querySelector: (sel) => (sel.includes('[data-foco-preferido]') ? vaga : { nome: 'data' }), matches: () => false }
  assert.equal(campoDaFicha({ querySelector: (sel) => (sel === '[data-campo-ficha~="janela"]' ? area : null) }, 'janela'), vaga)
  assert.equal(campoDaFicha({ querySelector: () => null }, 'janela'), null)
})

await confere('#1686-10 devolução em dinheiro sem referência; nos outros meios ela segue obrigatória', async () => {
  const { referenciaObrigatoriaNaDevolucao } = await import('../src/dominio/cobranca.js')
  assert.equal(referenciaObrigatoriaNaDevolucao('dinheiro'), false)
  for (const meio of ['pix', 'credito', 'debito', 'transferencia', 'outro']) assert.equal(referenciaObrigatoriaNaDevolucao(meio), true, meio)
  const bloco = fonte('features/ficha-cliente/BlocoDevolucoes.jsx')
  assert.match(bloco, /\(!referenciaObrigatoria \|\| referencia\.trim\(\)\)/)
  assert.match(bloco, /required=\{referenciaObrigatoria\}/)
  const caso = readFileSync(new URL('../../EasyStock.Application/UseCases/Pedidos/Cobranca/RegistrarEstornoManualUseCase.cs', import.meta.url), 'utf8')
  assert.match(caso, /referencia\.Length == 0 && input\.Metodo != "dinheiro"/, 'a regra do console é a mesma da API')
})

await confere('#1940-3 topo do Balcão abaixo de 400 px aperta vãos e recuos, sem tirar a área de toque', () => {
  // Medido no laboratório: a 375 px o "Mais" passava 16 px da borda (399 px de conteúdo); depois, 6 px de folga a 375 e a 390.
  const css = fonte('app/moldura.module.css')
  const bloco = css.match(/@media \(max-width: 399px\) \{([\s\S]*?)\n\}/)?.[1] ?? ''
  assert.match(bloco, /\.topo \{ gap: 2px; padding-inline: 6px; \}/)
  assert.match(bloco, /\.controles \{ gap: 2px; \}/)
  assert.match(bloco, /\.controles > button \{ padding: 0 8px; \}/)
  assert.doesNotMatch(bloco, /width|min-width|--toque/, 'a área de toque (44 px) não muda')
})

if (falhasC.length > 0) {
  console.log(`\n${falhasC.length} falha(s) da frente C.`)
  process.exit(1)
}
console.log(`\n${passos} passos verdes.`)
