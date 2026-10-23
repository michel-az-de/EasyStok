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
  assert.match(fonte('features/atendimento/PainelAtendimento.jsx'), /\{fonteApi && !encerrada && !bloqueada && <PassarConversa key=\{'passar-' \+ selecionada\.id\} conversa=\{selecionada\} \/>\}/)
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

// #1966 (frente D da #1889): o GET de estornos manuais devolve `aDevolver` (pedido cancelado com dinheiro recebido e ainda
// não devolvido, PR #1967). Devoluções mostra o valor onde a equipe decide o estorno, aberta ou recolhida.
await confere('#1966 Devoluções mostra o valor a devolver do pedido cancelado', async () => {
  const { aDevolverDe, resumoDasDevolucoes, textoADevolver } = await import('../src/dominio/cobranca.js')
  const { moeda } = await import('../src/dominio/formato.js')
  assert.equal(aDevolverDe({ aDevolver: 25 }), 25)
  for (const sem of [{ aDevolver: 0 }, { aDevolver: -3 }, {}, null, { aDevolver: 'x' }]) assert.equal(aDevolverDe(sem), 0, JSON.stringify(sem))
  assert.equal(resumoDasDevolucoes({ aDevolver: 25, totalDevolvido: 10, conferirAcerto: true }), `${moeda(25)} a devolver`)
  assert.equal(resumoDasDevolucoes({ aDevolver: 0, totalDevolvido: 10, conferirAcerto: true }), `${moeda(10)} devolvidos`)
  assert.equal(resumoDasDevolucoes({ aDevolver: 0, totalDevolvido: 0, conferirAcerto: true }), 'Recebimento registrado')
  assert.equal(resumoDasDevolucoes({ aDevolver: 0, totalDevolvido: 0, conferirAcerto: false }), 'Recebimentos e estornos')
  const bloco = fonte('features/ficha-cliente/BlocoDevolucoes.jsx')
  assert.match(bloco, /const aDevolver = aDevolverDe\(dados\)/)
  assert.match(bloco, /resumo=\{erro \? RESUMO_COM_ERRO : resumoDasDevolucoes\(\{ aDevolver, totalDevolvido, conferirAcerto \}\)\}/)
  // O caminho citado é o que a tela oferece: o estorno online só aparece com pagamento online.
  assert.match(textoADevolver({ comOnline: true }), /estorno online/)
  assert.doesNotMatch(textoADevolver({ comOnline: false }), /online/, 'mandava ao estorno online que esta tela não mostra')
  assert.match(textoADevolver({ comOnline: false }), /devolução manual/)
  assert.match(bloco, /const comOnline = dados\?\.pagamentos\.some\(\(p\) => !p\.manual\) \?\? false/)
  assert.match(bloco, /\{comOnline && <EstornosOnline pedidoId=\{pedidoId\} \/>\}/)
  assert.match(bloco, /\{aDevolver > 0 && <output className=\{css\.corpoBloco\}><strong>A devolver: \{moeda\(aDevolver\)\}<\/strong>\. \{textoADevolver\(\{ comOnline \}\)\}<\/output>\}/)
  const caso = readFileSync(new URL('../../EasyStock.Application/UseCases/Pedidos/Cobranca/ConsultarEstornosManuaisUseCase.cs', import.meta.url), 'utf8')
  assert.match(caso, /IReadOnlyList<PedidoEstornoManual> Estornos, decimal ADevolver = 0m\)/, 'o contrato da API mudou')
})

// ── Frente J1 da #1889 (#1989, laboratório de 23/10) ─────────────────────────────────────────────

await confere('#1989-7 respondeu no meio da venda: a faixa não sugere encerrar', async () => {
  const { resumoDoModo } = await import('../src/dominio/resumoDoModo.js')
  const { PAUSA_POR_ASSUMIR } = await import('../src/dominio/automatico.js')
  const em = (min) => new Date(AGORA - min * 60000).toISOString()
  const venda = (extra = {}) => ({ id: 'v1', nome: 'Rita', estado: 'Em atendimento', canal: 'WhatsApp', pedido: null, mensagens: [
    { id: 'a', dir: 'in', texto: 'Oi, quero dois raviólis de abóbora para hoje', em: em(6) },
    { id: 'b', dir: 'in', texto: 'Rua Harmonia, 300, Vila Madalena, CEP 05435-000', em: em(5) },
    { id: 'c', dir: 'out', texto: 'Oi, Rita! Já anoto aqui.', status: 'entregue', em: em(1) },
  ], ...extra })
  for (const [caso, conversa] of [
    ['sem itens na comanda ainda', venda()],
    ['comanda em montagem', venda({ pedido: { numero: '7', estado: 'aguardando', itens: [] } })],
  ]) {
    const r = resumoDoModo(conversa, PAUSA_POR_ASSUMIR, false)
    assert.equal(r.sugereEncerrar, false, caso)
    assert.doesNotMatch(r.linha, /encerre/i, `${caso}: ${r.linha}`)
  }
  // Conversa que acabou (agradecimento, sem pedido) continua sugerindo encerrar.
  const fim = venda({ mensagens: [
    { id: 'a', dir: 'in', texto: 'Obrigada, estava uma delícia!', em: em(3) },
    { id: 'b', dir: 'out', texto: 'Que bom, Rita!', status: 'entregue', em: em(1) },
  ] })
  assert.equal(resumoDoModo(fim, PAUSA_POR_ASSUMIR, false).sugereEncerrar, true)
  // Revisão da PR #1997: agradecimento que cita "pedido" ou "marmita" é despedida, não venda.
  for (const texto of ['Recebi o pedido, obrigada', 'Obrigada, adorei a marmita!', 'Valeu, chegou certinho o pedido']) {
    const despedida = venda({ mensagens: [
      { id: 'a', dir: 'in', texto, em: em(3) },
      { id: 'b', dir: 'out', texto: 'Nós que agradecemos!', status: 'entregue', em: em(1) },
    ] })
    assert.equal(resumoDoModo(despedida, PAUSA_POR_ASSUMIR, false).sugereEncerrar, true, texto)
  }
})

await confere('#1989-4 cabeçalho da conversa: um "Link de compra" e nenhuma chave repetida entre irmãos', () => {
  const painel = fonte('features/atendimento/PainelAtendimento.jsx')
  const de = painel.indexOf('<div className={css.direita} data-ancora-assistente>')
  const direita = painel.slice(de, painel.indexOf('</header>', de))
  assert.ok(de >= 0 && direita.length > 0, 'cabeçalho não encontrado')
  const chaves = [...direita.matchAll(/key=\{([^}]+)\}/g)].map((m) => m[1].trim())
  assert.equal(new Set(chaves).size, chaves.length, `chaves repetidas entre irmãos: ${chaves.join(' | ')}`)
  assert.equal((direita.match(/<CompartilharComanda\b/g) ?? []).length, 1, 'um botão só no cabeçalho')
  // O botão que a Moldura passa à Ficha também não usa o id puro como chave.
  assert.doesNotMatch(fonte('app/Moldura.jsx'), /<CompartilharComanda key=\{selecionada\.id\}/)
})

await confere('#1989-1 (#1765-3) forma já escolhida: "Gerar cobrança e enviar" abre o resumo antes de gerar', async () => {
  const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
  assert.doesNotMatch(barra, /aoGerarPix: \(\) => \(pedido\.meio \? executar/, 'o clique ainda gera direto quando a forma já está escolhida')
  assert.doesNotMatch(barra, /pedindoMeio && situacaoCob\.chave === 'nenhuma' && !pedido\.meio/, 'o resumo só aparecia sem forma escolhida')
  assert.match(barra, /data-resumo-itens/)
  assert.match(barra, /resumoDaCobrancaEscolhida\(/)
  // Gerar só no botão de confirmação, com a forma que está marcada nele.
  assert.equal((barra.match(/executar\(\(\) => aoGerarPix\(/g) ?? []).length, 1)
  const { itensDoResumo } = await import('../src/dominio/resumoPedido.js')
  const cardapio = [{ sku: 'R', nome: 'Ravióli de abóbora', porcao: '400 g', preco: 59 }, { sku: 'L', nome: 'Lasanha', porcao: '500 g', preco: 62 }]
  assert.equal(itensDoResumo({ itens: [{ sku: 'R', qtd: 2 }, { sku: 'L', qtd: 1 }] }, cardapio), '2× Ravióli de abóbora, 1× Lasanha')
  assert.equal(itensDoResumo({ itens: [{ sku: 'R', qtd: 1, nome: 'Ravióli' }] }, []), '1× Ravióli')
})

if (falhasC.length > 0) {
  console.log(`\n${falhasC.length} falha(s) da frente C.`)
  process.exit(1)
}
console.log(`\n${passos} passos verdes.`)
