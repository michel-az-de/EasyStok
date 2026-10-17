// Prova da #1683 (design sprint do Console, cobrança da Ficha no modo API): a tela só afirma o
// que o EasyStok fez e só oferece o que ele executa.
//
//   1  link vencido mantém "Marcar pago" na barra;
//   2  o aviso de "link novo / cancelamento" só vale com o pedido em `aguardando_pagamento`;
//   3  "Gerar cobrança e enviar" devolve `{ erro }` para a barra mostrar ao lado do botão;
//   4  cobrança criada sem a mensagem sair não diz "Enviada ao cliente";
//   5  "Alterar forma" no modo API só troca entre link e na entrega, que é o que o back executa;
//   6  "Desfazer pagamento" não aparece para o sinal pago no Mercado Pago;
//   7  estorno e "Voltar etapa" esperam a resposta e só fecham no sucesso;
//   8  ação destrutiva usa a variante `perigo` do botão;
//   9  confirmação em linha é formulário: Enter confirma, Esc volta, foco no motivo;
//   10 "Marcar pago" no recebimento comum e "Registrar sinal" na entrada manual de encomenda.
//
// Regras puras e ações reais (HTTP controlado no `fetch`); a composição que só existe no JSX é
// conferida por leitura do fonte. Não prova pixels nem foco nativo.
//
// Roda com: node ferramentas/prova-1683-cobranca.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const cobranca = await import('../src/dominio/cobranca.js')
const pagamento = await import('../src/dominio/pagamento.js')
const { situacaoDoEnvio } = await import('../src/dominio/resumoPedido.js')
const { pedidoDaApi } = await import('../src/infra/api/comandaApi.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replaceAll(String.fromCharCode(13), '')
const trecho = (texto, inicio, fim) => {
  const de = texto.indexOf(inicio)
  assert.ok(de >= 0, `trecho não encontrado: ${inicio}`)
  const ate = texto.indexOf(fim, de + inicio.length)
  assert.ok(ate > de, `fim do trecho não encontrado: ${fim}`)
  return texto.slice(de, ate)
}

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

const AGORA = '2026-10-12T15:00:00Z'
const barra = fonte('features/ficha-cliente/BarraProximoPasso.jsx')
// #1875: `acaoDoPix` saiu da barra para o próprio módulo; as leituras de `case 'expirada'` vão para lá.
const acaoDoPix = fonte('features/ficha-cliente/acaoDoPix.js')
const bloco = fonte('features/ficha-cliente/BlocoCobranca.jsx')
const relogio = fonte('features/ficha-cliente/RelogioPix.jsx')

// ------------------------------------------------------------------ ações reais, HTTP na fronteira

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const PEDIDO_ID = '11111111-1111-1111-1111-111111111111'
const doServidor = (extra = {}) => ({
  pedidoId: PEDIDO_ID, status: 'aguardando_pagamento', total: 50, frete: 5, itens: [], totalPago: 0, pagamentos: [],
  cobranca: { cobrancaId: 'cb', provedor: 'mercadopago', status: 'Pendente', valor: 50, tentativa: 1,
    linkPagamento: 'https://mp/x', criadaEm: AGORA, expiraEm: '2026-10-12T15:30:00Z' },
  ...extra,
})
const rascunho = (extra = {}) => ({ estado: 'aguardando', itens: [{ sku: 'massa', qtd: 1, obs: '' }], janela: 'j1|2026-10-13', meio: 'pix', ...extra })

function montar(pedido, responder) {
  const chamadas = []
  const despachados = []
  globalThis.fetch = async (url, opcoes = {}) => {
    chamadas.push({ url: String(url), metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : null })
    return responder(String(url), opcoes.method ?? 'GET')
  }
  const estadoRef = { current: { conversas: [{ id: 'conversa', pedido }] } }
  const acoes = criarAcoesComandaApi({}, { despachar: (a) => despachados.push(a), estadoRef })
  const avisos = () => despachados.filter((d) => d.mensagem).map((d) => d.mensagem)
  const posts = () => chamadas.filter((c) => c.metodo === 'POST')
  const ultimoPedido = () => despachados.filter((d) => d.pedido).at(-1)?.pedido
  return { acoes, avisos, posts, ultimoPedido, estadoRef }
}
const ok = (data) => new Response(JSON.stringify({ data }))
const falha = (mensagem) => new Response(JSON.stringify({ error: { message: mensagem } }), { status: 500 })

// ------------------------------------------------------------------ 1

await confere('1  link vencido no modo API: a barra segue oferecendo "Marcar pago"', () => {
  const vencida = trecho(acaoDoPix, "case 'expirada':", "case 'em-conferencia':")
  const ramoApi = trecho(vencida, 'if (linkVencidoApi)', 'return {\n        rotuloEstado')
  assert.match(ramoApi, /botoes: \[\{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa \}\]/)
  assert.doesNotMatch(ramoApi, /botoes: \[\]/)
})

// ------------------------------------------------------------------ 2

await confere('2  aviso de link novo e cancelamento só com o pedido em aguardando_pagamento', () => {
  const cancelado = cobranca.avisoDeLinkVencidoApi({ tentativa: 2 }, 'cancelado')
  assert.match(cancelado, /Pedido cancelado/)
  assert.doesNotMatch(cancelado, /continua valendo|Marcar pago|link novo/)
  const pedido = { estado: 'cancelado', totalApi: 65, alocadoApi: 32.5, cobranca: { pagaEm: 1, valor: 32.5 } }
  assert.equal(pagamento.diferencaAposPagamento(pedido, []), 0)
  assert.equal(pagamento.totalPago(pedido), 32.5)
  assert.equal(pagamento.diferencaAposPagamento({ ...pedido, estado: 'pago' }, []), 32.5)
  assert.match(cobranca.avisoDeLinkVencidoApi({ tentativa: 1 }, 'aguardando_pagamento'), /manda um link novo ao cliente sozinho/)
  assert.match(cobranca.avisoDeLinkVencidoApi({ tentativa: 2 }, 'aguardando_pagamento'), /cancela o pedido sozinho/)
  for (const status of ['aguardando', 'aprovado_baba', 'preparando', 'pronto', 'saiu_para_entrega', undefined]) {
    for (const tentativa of [1, 2]) {
      const texto = cobranca.avisoDeLinkVencidoApi({ tentativa }, status)
      assert.doesNotMatch(texto, /link novo|cancela/, `${status}: promete o que o job não faz`)
      assert.match(texto, /venceu/, status)
      assert.match(texto, /pedido continua valendo/, status)
      assert.match(texto, /Marcar pago/, `${status}: o texto usa o nome do botão`)
      assert.doesNotMatch(texto, /—/)
    }
  }
})

await confere('2  anel e barra passam o status do pedido ao aviso', () => {
  assert.match(relogio, /\{fonteApi && <p className=\{css\.legenda\}>\{avisoDeLinkVencidoApi\(cobranca, statusApi\)\}<\/p>\}/)
  assert.match(bloco, /<RelogioPix[\s\S]*?statusApi=\{pedido\.statusApi\}/)
  assert.match(barra, /avisoDeLinkVencidoApi\(pedido\.cobranca, pedido\.statusApi\)/)
})

// ------------------------------------------------------------------ 3

await confere('3  sem janela: "Gerar cobrança e enviar" devolve { erro } e mantém o aviso', async () => {
  const t = montar(rascunho({ janela: null }), () => ok(doServidor()))
  const resultado = await t.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(resultado?.erro, 'Escolha a janela de entrega antes de enviar ao cliente.')
  assert.deepEqual(t.avisos(), ['Escolha a janela de entrega antes de enviar ao cliente.'])
  assert.equal(t.posts().length, 0)
})

await confere('3  POST recusado: devolve { erro } com o motivo e mantém o aviso', async () => {
  const t = montar(rascunho(), (url, metodo) => (metodo === 'POST' ? falha('Sem vaga na janela') : ok(null)))
  const resultado = await t.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(resultado?.erro, 'Pedido não criado: Sem vaga na janela')
  assert.deepEqual(t.avisos(), ['Pedido não criado: Sem vaga na janela'])
})

await confere('3  a ação seguinte na mesma conversa não fica presa depois do erro', async () => {
  const t = montar(rascunho({ janela: null }), () => ok(doServidor()))
  await t.acoes.gerarCobranca('conversa', null, 'pix')
  const de_novo = await t.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(de_novo?.erro, 'Escolha a janela de entrega antes de enviar ao cliente.')
})

// ------------------------------------------------------------------ 4

const comCobranca = (extra = {}) => ({ estado: 'aguardando', itens: [{ sku: 'massa', qtd: 1 }], cobranca: { criadaEm: Date.parse(AGORA), link: 'https://mp/x' }, ...extra })

await confere('4  mensagem que não saiu: a comanda não diz "Enviada ao cliente"', () => {
  const s = situacaoDoEnvio(comCobranca({ resumoNaoEnviado: true }))
  assert.equal(s.pode, false)
  assert.equal(s.chave, 'nao-enviada')
  assert.equal(s.texto, 'Cobrança criada, mas a mensagem não saiu. Abra Pagamento no resumo, copie o link e envie pela conversa.')
  const semLink = situacaoDoEnvio(comCobranca({ resumoNaoEnviado: true, cobranca: { criadaEm: Date.parse(AGORA), link: null } }))
  assert.equal(semLink.texto, 'Cobrança criada, mas a mensagem não saiu. Avise o cliente pela conversa.')
  assert.match(situacaoDoEnvio(comCobranca()).texto, /^Enviada ao cliente às \d{2}:\d{2}/)
})

await confere('4  o pedido criado guarda que o resumo não saiu; quando saiu, não guarda', async () => {
  const naoSaiu = montar(rascunho(), (url, metodo) => ok(metodo === 'POST' ? { ...doServidor(), enviadoAoCliente: false } : doServidor()))
  await naoSaiu.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(naoSaiu.ultimoPedido().resumoNaoEnviado, true)
  assert.equal(situacaoDoEnvio(naoSaiu.ultimoPedido()).chave, 'nao-enviada')
  const saiu = montar(rascunho(), (url, metodo) => ok(metodo === 'POST' ? { ...doServidor(), enviadoAoCliente: true } : doServidor()))
  await saiu.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(saiu.ultimoPedido().resumoNaoEnviado, undefined)
  assert.equal(situacaoDoEnvio(saiu.ultimoPedido()).chave, 'enviada')
})

await confere('4  a releitura do mesmo pedido não apaga a marca de que o resumo não saiu', async () => {
  const acao = await import('../src/aplicacao/acoes.js')
  const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
  const conversa = (pedido) => ({ id: 'conv-1', nome: 'Maria', conta: 'cliente', estado: 'Aberto', mensagens: [], cliente: {}, pedido })
  const lido = (pedidoId, leitura) => ({ ...comCobranca(), pedidoId, leitura })
  const base = { ...estadoInicial, conversas: [conversa({ ...lido('p1', 1), resumoNaoEnviado: true })], selecionadaId: 'conv-1' }
  const daPolling = reducer(base, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [conversa(lido('p1', 2))] })
  assert.equal(daPolling.conversas[0].pedido.resumoNaoEnviado, true, 'polling')
  const daAcao = reducer(base, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'conv-1', pedido: lido('p1', 2) })
  assert.equal(daAcao.conversas[0].pedido.resumoNaoEnviado, true, 'recarga da ação')
  const outro = reducer(base, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'conv-1', pedido: lido('p2', 2) })
  assert.equal(outro.conversas[0].pedido.resumoNaoEnviado, undefined, 'pedido novo não herda a marca')
  const limpo = reducer(base, { tipo: acao.SINCRONIZAR_PEDIDO, id: 'conv-1', pedido: { ...lido('p1', 2), resumoNaoEnviado: false } })
  assert.notEqual(limpo.conversas[0].pedido.resumoNaoEnviado, true, 'a ação que mandou link novo apaga a marca')
})

await confere('4  trocar a forma manda link novo: a marca de que o resumo não saiu se apaga', async () => {
  const t = montar(rascunho(), (url, metodo) => ok(metodo === 'POST' && /\/pedido$/.test(url) ? { ...doServidor(), enviadoAoCliente: false } : doServidor()))
  await t.acoes.gerarCobranca('conversa', null, 'pix')
  assert.equal(t.ultimoPedido().resumoNaoEnviado, true)
  t.estadoRef.current.conversas[0].pedido = t.ultimoPedido()
  await t.acoes.alterarMeioPagamento('conversa', 'maquininha')
  assert.equal(t.ultimoPedido().resumoNaoEnviado, false, 'a recarga depois da troca diz que a marca caiu')
})

await confere('4  com link em aberto e sem copia e cola, a Cobrança deixa copiar o link', () => {
  assert.match(bloco, /\{situacao\.chave === 'aguardando' && !cobranca\.copiaECola && cobranca\.link && <CopiaECola codigo=\{cobranca\.link\} rotulo="Copiar link" \/>\}/)
})

// ------------------------------------------------------------------ 5

await confere('5  no EasyStok a troca é só entre link e na entrega, com o texto do que acontece', () => {
  const doLink = cobranca.trocaDeFormaNoEasyStok({ meio: 'cartao-link', link: 'https://mp/x', manual: false }, 'Ana')
  assert.equal(doLink.meio, 'maquininha')
  assert.equal(doLink.texto, 'O link de pagamento é cancelado e o pedido passa a receber na entrega. Ana não recebe mensagem: avise pela conversa.')
  const daEntrega = cobranca.trocaDeFormaNoEasyStok({ meio: 'maquininha', link: null, manual: true }, 'Ana')
  assert.equal(daEntrega.meio, 'cartao-link')
  assert.equal(daEntrega.texto, 'O EasyStok gera um link de pagamento (Pix ou cartão) e manda para Ana se a conversa estiver aberta.')
  for (const troca of [doLink, daEntrega]) assert.doesNotMatch(troca.texto, /recebe uma nova|—/)
})

await confere('5  a tela usa essa troca no modo API e não pergunta meio que o back ignora', () => {
  const alterar = trecho(bloco, 'function AlterarForma(', '// Bloco Cobrança da ficha.')
  assert.match(alterar, /const trocaApi = fonteApi \? trocaDeFormaNoEasyStok\(cobranca, nomeCliente\) : null/)
  assert.match(alterar, /\{trocaApi \? <p>\{trocaApi\.texto\}<\/p> : \(/)
  assert.match(bloco, /<AlterarForma cobranca=\{cobranca\} nomeCliente=\{nomeCliente\} fonteApi=\{fonteApi\} aoAlterar=\{troca\.emitir\} \/>/)
})

await confere('5  trocar entre formas online não vai ao EasyStok nem muda o meio na tela', async () => {
  const pedido = pedidoDaApi(doServidor())
  const t = montar(pedido, () => ok(doServidor()))
  const resultado = await t.acoes.alterarMeioPagamento('conversa', 'pix')
  assert.equal(t.posts().length, 0, 'foi ao EasyStok para uma troca que ele não executa')
  assert.equal(resultado?.erro, 'A cobrança já está nessa forma de pagamento. Nada mudou.')
  assert.deepEqual(t.avisos(), [resultado.erro])
  assert.equal(t.ultimoPedido(), undefined)
})

await confere('5  trocar de link para na entrega continua indo ao EasyStok', async () => {
  const t = montar(pedidoDaApi(doServidor()), () => ok(doServidor()))
  await t.acoes.alterarMeioPagamento('conversa', 'maquininha')
  assert.deepEqual(t.posts().map((c) => c.corpo), [{ forma: 'na_entrega' }])
})

// ------------------------------------------------------------------ 6

await confere('6  "Desfazer pagamento" só para baixa manual: sinal pago no Mercado Pago não oferece', () => {
  const naEntrega = { cobrancaId: 'cb2', provedor: 'na_entrega', status: 'Pendente', valor: 25, criadaEm: AGORA }
  const sinal = { id: 'pg-sinal', valor: 25, metodo: 'pix', pagoEm: AGORA }
  const soSinal = pedidoDaApi(doServidor({ status: 'aguardando', totalPago: 25, sinalNaReserva: true, pagamentos: [sinal], cobranca: naEntrega }))
  assert.equal(cobranca.podeDesfazerPagamento(soSinal), false, 'oferece desfazer o sinal online (o back responde 409)')
  const sinalEBaixa = pedidoDaApi(doServidor({ status: 'aguardando', totalPago: 50, sinalNaReserva: true, cobranca: naEntrega,
    pagamentos: [sinal, { id: 'pg-mao', valor: 25, metodo: 'dinheiro', pagoEm: '2026-10-12T16:00:00Z' }] }))
  assert.equal(cobranca.podeDesfazerPagamento(sinalEBaixa), true)
  const manual = pedidoDaApi(doServidor({ status: 'aguardando', totalPago: 25, cobranca: naEntrega, pagamentos: [{ ...sinal, metodo: 'dinheiro' }] }))
  assert.equal(cobranca.podeDesfazerPagamento(manual), true)
  assert.equal(cobranca.podeDesfazerPagamento({ ...manual, statusApi: 'preparando' }), false)
})

// ------------------------------------------------------------------ 7

await confere('7  estorno e "Voltar etapa" esperam a resposta, mostram o erro e só fecham no sucesso', () => {
  const confirmar = trecho(barra, 'const confirmarCorrecao = async', '// "Marcar pago": o formulário')
  assert.match(confirmar, /if \(corrigindo\.current\) return/)
  assert.match(confirmar, /setMarcoEmVoo\(marco\)/)
  assert.match(confirmar, /const resultado = await fazer\(\)/)
  assert.match(confirmar, /if \(resultado\?\.erro\) setErroCorrecao\(resultado\.erro\)\s+else setConfirmando\(null\)/)
  assert.match(confirmar, /finally \{ corrigindo\.current = false; setMarcoEmVoo\(null\) \}/)
  const estorno = trecho(barra, "{confirmando === 'estornar' && (", "{confirmando === 'desfazer' && (")
  const voltar = trecho(barra, "{confirmando === 'voltar' && (", '</div>\n  )\n}')
  for (const [nome, form] of [['estorno', estorno], ['voltar', voltar]]) {
    assert.match(form, /confirmarCorrecao\(\(\) => ao(MarcarEstorno|VoltarEtapa)\(/, nome)
    assert.match(form, /\{erroCorrecao && <p className=\{css\.motivoDesabilitado\} role="alert">\{erroCorrecao\}<\/p>\}/, nome)
    assert.doesNotMatch(form, /\); setConfirmando\(null\) \}|\)\s+setConfirmando\(null\)\s+\}\}/, `${nome}: fecha sem esperar`)
  }
  assert.match(estorno, /\{emVoo \? 'Registrando…' : 'Marcar estorno'\}/)
  assert.match(voltar, /\{emVoo \? 'Voltando…' : 'Voltar'\}/)
})

// ------------------------------------------------------------------ 8

await confere('8  o botão tem a variante perigo, com os tokens de erro', () => {
  const css = fonte('componentes/Botao.module.css')
  assert.match(css, /\.perigo \{[^}]*background: var\(--erro\);[^}]*color: var\(--sobre-erro\);/)
  assert.match(fonte('../ferramentas/verificar-contraste.mjs'), /conferir\('sobre-erro', 'erro'\)/)
})

await confere('8  cancelar, estornar e desfazer confirmam com a variante perigo', () => {
  for (const rotulo of ["{emVoo ? 'Cancelando…' : 'Cancelar pedido'}", "{emVoo ? 'Registrando…' : 'Marcar estorno'}", "{emVoo ? 'Desfazendo…' : 'Desfazer pagamento'}"]) {
    const ate = barra.indexOf(rotulo)
    assert.ok(ate > 0, rotulo)
    const abertura = barra.slice(barra.lastIndexOf('<Botao', ate), ate)
    assert.match(abertura, /variante="perigo"/, rotulo)
  }
  const anel = trecho(relogio, "{confirmando === 'cancelar' && (", '// Contagem normal')
  assert.match(anel, /<Botao variante="perigo"/)
})

// ------------------------------------------------------------------ 9

await confere('9  as confirmações em linha são formulário: Enter confirma, Esc volta, foco no motivo', () => {
  const blocos = {
    cancelar: trecho(barra, "{confirmando === 'cancelar' && (", "{confirmando === 'estornar' && ("),
    estornar: trecho(barra, "{confirmando === 'estornar' && (", "{confirmando === 'desfazer' && ("),
    desfazer: trecho(barra, "{confirmando === 'desfazer' && (", "{confirmando === 'voltar' && ("),
  }
  for (const [nome, form] of Object.entries(blocos)) {
    assert.match(form, /<form className=\{css\.confirmarCorrecao\} onKeyDown=\{teclaDaConfirmacao\} onSubmit=\{/, nome)
    assert.match(form, /<\/form>/, nome)
    assert.match(form, /tipo="submit"/, nome)
    assert.match(form, /autoFocus/, nome)
    assert.doesNotMatch(form, /<div className=\{css\.confirmarCorrecao\}>/, nome)
  }
  const tecla = trecho(barra, 'const teclaDaConfirmacao = ', '\n\n')
  assert.match(tecla, /evento\.key === 'Escape'/)
  assert.match(tecla, /setConfirmando\(null\)/)
  assert.match(tecla, /evento\.key === 'Enter' && \(evento\.ctrlKey \|\| evento\.metaKey\)/, 'Enter sozinho no motivo não confirma ação destrutiva')
  assert.doesNotMatch(tecla, /!evento\.shiftKey/)
  assert.match(tecla, /requestSubmit\(\)/)
})

await confere('9  a baixa à mão também é formulário: Enter registra, Esc volta', () => {
  const baixa = trecho(bloco, 'export function BaixaAMao(', '\n}\n')
  assert.match(baixa, /<form className=\{css\.baixaDivergente\} onKeyDown=\{teclaDaBaixa\} onSubmit=\{/)
  assert.match(baixa, /tipo="submit"/)
  assert.match(baixa, /evento\.key === 'Escape' && !enviando/)
  assert.match(baixa, /aoCancelar\(\)/)
})

// ------------------------------------------------------------------ 10

await confere('10  receber distingue sinal manual de quitação, com ação secundária no bloco', () => {
  assert.doesNotMatch(bloco, />\s*Recebi\s*</)
  const naEntrega = trecho(bloco, '{!cancelado && editavel && !recebendo && (', "{disponivel('marcarRecebidoEntrega') && (")
  assert.match(naEntrega, /variante="secundario"[\s\S]*sinalPendente \? 'Registrar sinal' : 'Marcar pago'/)
  assert.doesNotMatch(naEntrega, /variante="primario"/)
  const baixa = trecho(bloco, 'export function BaixaAMao(', '// "Alterar forma de pagamento"')
  assert.match(baixa, /enviando \? 'Registrando…' : aguardaSinalManual\(pedido\) \? 'Registrar sinal' : 'Marcar pago'/)
  assert.doesNotMatch(baixa, /'Confirmar'/)
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} verificações passaram.`)
