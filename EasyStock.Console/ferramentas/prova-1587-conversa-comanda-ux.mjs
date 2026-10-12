/* eslint-disable no-console */
// Prova da #1587 (banca de navegação e UX de 11/10): conversa, comanda e cardápio no Balcão.
//   A. pedido já criado no modo API trava os itens da comanda, sem botão que só avisa;
//   B. o painel do cardápio diz de quem é a comanda, no cabeçalho e no rodapé;
//   C. anexo que o EasyStok não envia é recusado ao escolher; foto que falha fica escrita;
//   D. o fio só rola sozinho ao trocar de conversa, em mensagem da casa ou com a leitura no fim;
//   E. depois de inserir a resposta pronta o seletor avisa quem desenha, que devolve o foco.
// As regras puras (A, C, D) são provadas pelo domínio e conferidas contra a ação da API que elas
// espelham. O que é só ligação de tela (B e o resto) a prova lê no JSX, como a prova-1551.
// Foco de verdade, rolagem de verdade e pixels ficam para o navegador.
//
//   node ferramentas/prova-1587-conversa-comanda-ux.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

// React e os contextos de mentira só servem ao hook do seletor (E): estado por posição, sem tela.
const hooks = { valores: [], indice: 0 }
globalThis.__prova1587 = {
  useState(inicial) {
    const i = hooks.indice++
    if (!(i in hooks.valores)) hooks.valores[i] = inicial
    return [hooks.valores[i], (v) => { hooks.valores[i] = typeof v === 'function' ? v(hooks.valores[i]) : v }]
  },
  useMemo(fn) { hooks.indice++; return fn() },
  catalogo: { respostasProntas: [] },
}
const modulo = (fonte) => 'data:text/javascript,' + encodeURIComponent(fonte)

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador === 'react') {
      return { url: modulo('const p = globalThis.__prova1587; export const useState = (i) => p.useState(i); export const useMemo = (f) => p.useMemo(f)'), shortCircuit: true }
    }
    if (especificador === './contextos') {
      return { url: modulo('export const useCatalogo = () => globalThis.__prova1587.catalogo'), shortCircuit: true }
    }
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = {
  getItem: () => JSON.stringify({ token: 't', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa' } }),
  setItem: () => {}, removeItem: () => {},
}
globalThis.window = globalThis.window ?? { dispatchEvent: () => {} }
const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url: String(url), metodo: opcoes.method ?? 'GET' })
  const data = { id: 'm-api', direcao: 'Saida', autor: 'Atendente', texto: '', enviadaEm: '2026-10-11T15:00:00Z', status: 'Enviada' }
  return new Response(JSON.stringify({ data }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const acao = await import('../src/aplicacao/acoes.js')
const { comandaTravada } = await import('../src/dominio/pedido.js')
const { LINHA_COMANDA_TRAVADA } = await import('../src/dominio/resumoPedido.js')
const { FOTO_NAO_ENVIADA, SO_FOTO_NO_EASYSTOK, tipoDeArquivo, validarArquivo } = await import('../src/dominio/anexos.js')
const { canalPorNome, motivoDeAnexo } = await import('../src/dominio/canal.js')
const { FOLGA_DO_FIM, deveRolarParaOFim, pertoDoFim, retratoDoFio } = await import('../src/dominio/rolagemDoFio.js')
const { CANAIS } = await import('../src/infra/catalogo.js')
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const { criarAcoesEncerramentoEMidiaApi } = await import('../src/aplicacao/api/encerramentoEMidia.js')
const { useAtalhoBarra } = await import('../src/aplicacao/useAtalhoBarra.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

// No Windows o checkout vem com CRLF; a prova lê como o CI (LF). Comentário não conta como ligação.
const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8').replace(/\r\n/g, '\n')
const semComentarios = (fonte) => fonte
  .replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/^\s*\/\/.*$/gm, '')
const fonte = (caminho) => semComentarios(ler(caminho))

// ── A. Comanda travada ───────────────────────────────────────────────────────────────────
const EDITA_COMANDA = ['adicionarItem', 'removerItem', 'ajustarQuantidade', 'ajustarObservacao', 'forcarEncaixe']
const montarComanda = (pedido) => {
  const despachos = []
  const locais = []
  const estadoRef = { current: { conversas: [{ id: 'c1', nome: 'Ana Souza', pedido }] } }
  const acoesLocais = new Proxy({}, { get: (_, nome) => () => { locais.push(nome) } })
  return { api: criarAcoesComandaApi(acoesLocais, { despachar: (a) => despachos.push(a), estadoRef }), despachos, locais }
}

await confere('A a comanda só trava com pedido já criado e só no modo API', () => {
  const criado = { pedidoId: 'p-1', estado: 'aguardando', itens: [{ sku: 'a', qtd: 1 }] }
  const rascunho = { estado: 'aguardando', itens: [{ sku: 'a', qtd: 1 }] }
  assert.equal(comandaTravada(criado, { fonteApi: true }), true)
  assert.equal(comandaTravada(rascunho, { fonteApi: true }), false, 'rascunho continua editável')
  assert.equal(comandaTravada(criado, { fonteApi: false }), false, 'demonstração não trava')
  assert.equal(comandaTravada(criado), false)
  assert.equal(comandaTravada(null, { fonteApi: true }), false)
})

await confere('A a trava é a mesma regra da ação: com pedido criado ela só avisava, sem mexer em nada', () => {
  const criado = montarComanda({ pedidoId: 'p-1', estado: 'aguardando', itens: [] })
  for (const nome of EDITA_COMANDA) criado.api[nome]('c1', 'sku', 1)
  assert.deepEqual(criado.locais, [], 'nenhuma ação local roda com pedido criado')
  assert.equal(criado.despachos.length, EDITA_COMANDA.length)
  assert.ok(criado.despachos.every((d) => d.tipo === acao.AVISO_API))
  const rascunho = montarComanda({ estado: 'aguardando', itens: [] })
  for (const nome of EDITA_COMANDA) rascunho.api[nome]('c1', 'sku', 1)
  assert.deepEqual(rascunho.locais, EDITA_COMANDA, 'rascunho segue editando local')
  assert.deepEqual(rascunho.despachos, [])
})

await confere('A a frase diz só o que existe: o console não troca item de pedido criado', () => {
  assert.match(LINHA_COMANDA_TRAVADA, /já está no EasyStok/)
  assert.match(LINHA_COMANDA_TRAVADA, /itens não mudam/)
  const comandaApi = fonte('infra/api/comandaApi.js')
  assert.ok(!/\/itens/.test(comandaApi), 'se o console ganhar rota de itens do pedido, a trava e a frase mudam')
})

const blocoPedido = fonte('features/ficha-cliente/BlocoPedido.jsx')
await confere('A na Ficha, contador, anotação, arrasto e "Adicionar itens" seguem a trava', () => {
  assert.match(blocoPedido, /const itensTravados = fechado \|\| comandaTravada\(pedido, \{ fonteApi \}\)/)
  assert.match(blocoPedido, /fechado=\{itensTravados\}/, 'contador de quantidade')
  assert.match(blocoPedido, /const observacaoTravada = itensTravados \|\| Boolean\(pedido\.cobranca\)/)
  assert.match(blocoPedido, /id: 'comanda-solta', data: \{ type: 'comanda' \}, disabled: itensTravados/)
  assert.match(blocoPedido, /\{itensTravados && <p className=\{css\.situacaoEnvio\}>\{LINHA_COMANDA_TRAVADA\}<\/p>\}/)
  // O cardápio segue abrindo (ingredientes, foto do prato), só que sem prometer que adiciona.
  assert.match(blocoPedido, /<Botao largo onClick=\{aoAbrirCardapio\}>\{itensTravados \? 'Ver cardápio' : 'Adicionar itens'\}<\/Botao>/)
  assert.match(blocoPedido, /!itensTravados && <p className=\{css\.situacaoEnvio\}>\{LINHA_COMANDA_VAI_JUNTO\}<\/p>/)
})
await confere('A trocar a janela de pedido criado continua de pé (não depende da trava dos itens)', () => {
  assert.match(blocoPedido, /\{!fechado && fonteApi && \(\s*<SeletorJanelaApi /)
})

const painelCardapio = fonte('features/cardapio/PainelCardapio.jsx')
await confere('A no cardápio, somar e tirar prato ficam desligados com o motivo escrito', () => {
  assert.match(painelCardapio, /const travada = comandaTravada\(pedido, \{ fonteApi \}\)/)
  assert.match(painelCardapio, /disabled=\{!situacao\.vendavel \|\| !doToque \|\| travada\}/, 'toque no prato')
  assert.match(painelCardapio, /disabled: !situacao\.vendavel \|\| !doToque \|\| travada/, 'arrasto do prato')
  assert.match(painelCardapio, /disabled=\{!situacao\.vendavel \|\| !p\.disponivel \|\| travada\}/, 'porção')
  assert.equal(painelCardapio.split('{!travada && (').length - 1, 3, 'o "+" do prato e os dois "−" somem')
  assert.match(painelCardapio, /papel === 'escolher' && travada && <output className=\{css\.comandaTravada\}>\{LINHA_COMANDA_TRAVADA\}<\/output>/)
})

// ── B. De quem é a comanda ───────────────────────────────────────────────────────────────
await confere('B o painel do cardápio mostra o primeiro nome do cliente no cabeçalho e no rodapé', () => {
  assert.match(painelCardapio, /const nomeCliente = selecionada\?\.nome \? primeiroNome\(selecionada\.nome\) : ''/)
  assert.match(painelCardapio, /<h2>Cardápio de hoje<\/h2>\s*\{nomeCliente && <p className=\{css\.donoDaComanda\} aria-live="polite">Comanda de <b>\{nomeCliente\}<\/b><\/p>\}/)
  assert.match(painelCardapio, /<RodapeComanda pedido=\{pedido\} cardapio=\{cardapio\} linhas=\{linhas\} nomeCliente=\{nomeCliente\} \/>/)
  assert.match(painelCardapio, /Comanda\{nomeCliente \? ` de \$\{nomeCliente\}` : ''\} · /)
})

// ── C. Mídia ─────────────────────────────────────────────────────────────────────────────
const whatsapp = canalPorNome(CANAIS, 'WhatsApp')
const instagram = canalPorNome(CANAIS, 'Instagram')
const montarMidia = (canal) => {
  const despachos = []
  const estadoRef = { current: { conversas: [{ id: 'c1', canal }] } }
  const api = criarAcoesEncerramentoEMidiaApi({
    despachar: (a) => despachos.push(a), agoraRef: { current: 0 }, estadoRef,
    falhaDoEnvio: (id, mensagemId, erro) => ({ tipo: 'falha', id, mensagemId, erro }),
  })
  return { api, despachos }
}
const FOTO = { formato: 'imagem', arte: 'data:image/png;base64,AAAA', texto: 'foto.png', nomeArquivo: 'foto.png', legenda: '' }
const PDF = { formato: 'arquivo', arte: 'data:application/pdf;base64,AAAA', texto: 'cardapio.pdf', nomeArquivo: 'cardapio.pdf' }

await confere('C na demonstração o anexo segue só a regra do canal', () => {
  assert.equal(motivoDeAnexo(whatsapp, 'imagem'), null)
  assert.equal(motivoDeAnexo(whatsapp, 'pdf'), null)
  assert.equal(motivoDeAnexo(instagram, 'imagem'), null)
  assert.equal(motivoDeAnexo(instagram, 'pdf'), 'Instagram não aceita arquivo.')
  assert.deepEqual(validarArquivo({ tipo: 'application/pdf', tamanho: 10 }), { aceito: true, motivo: null })
})

await confere('C no modo API só a foto no WhatsApp passa; o resto é recusado com a frase', () => {
  const api = { fonteApi: true }
  assert.equal(motivoDeAnexo(whatsapp, 'imagem', api), null)
  assert.equal(motivoDeAnexo(instagram, 'imagem', api), 'Instagram ainda não recebe foto pelo EasyStok.')
  assert.equal(motivoDeAnexo(whatsapp, 'pdf', api), 'WhatsApp ainda não recebe arquivo pelo EasyStok.')
  assert.equal(motivoDeAnexo(instagram, 'pdf', api), 'Instagram não aceita arquivo.', 'a regra do canal vem antes')
  assert.deepEqual(validarArquivo({ tipo: 'application/pdf', tamanho: 10, soImagem: true }), { aceito: false, motivo: SO_FOTO_NO_EASYSTOK })
  assert.deepEqual(validarArquivo({ tipo: 'text/plain', tamanho: 10, soImagem: true }), { aceito: false, motivo: SO_FOTO_NO_EASYSTOK })
  assert.deepEqual(validarArquivo({ tipo: 'image/png', tamanho: 10, soImagem: true }), { aceito: true, motivo: null })
  assert.ok(!/PDF/.test(SO_FOTO_NO_EASYSTOK.split('.')[0]), 'a frase não oferece PDF')
})

await confere('C a recusa na escolha espelha o envio da API: o que ela recusa só avisaria, o que aceita sai', async () => {
  for (const [canal, mensagem] of [['WhatsApp', PDF], ['Instagram', FOTO]]) {
    const tipo = tipoDeArquivo(mensagem.arte.slice(5, mensagem.arte.indexOf(';')))
    assert.ok(motivoDeAnexo(canalPorNome(CANAIS, canal), tipo, { fonteApi: true }), `${canal} ${tipo} deveria ser recusado`)
    const { api, despachos } = montarMidia(canal)
    assert.equal(await api.enviarMidia('c1', mensagem), false)
    assert.deepEqual(despachos.map((d) => d.tipo), [acao.AVISO_API], `${canal} ${tipo}: só aviso, sem balão`)
  }
  chamadas.length = 0
  const { api, despachos } = montarMidia('WhatsApp')
  assert.equal(await api.enviarMidia('c1', FOTO), true)
  assert.deepEqual(despachos.map((d) => d.tipo), [acao.ENVIAR_MIDIA, acao.CONFIRMAR_ENVIO_API])
  assert.equal(chamadas.length, 1)
})

const composer = fonte('features/atendimento/Composer.jsx')
await confere('C o composer recusa ao escolher e não promete PDF no modo API', () => {
  assert.match(composer, /validarArquivo\(\{ tipo: arquivo\.type, tamanho: arquivo\.size, soImagem: fonteApi \}\)/)
  assert.match(composer, /const bloqueioCanal = motivoDeAnexo\(canal, tipo, \{ fonteApi \}\)/)
  assert.match(composer, /soImagem=\{fonteApi\}/)
  assert.match(composer, /titulo=\{fonteApi \? 'Anexar foto do computador \(imagem, até 8 MB\)' : /)
  const anexos = fonte('features/atendimento/ComposerAnexos.jsx')
  assert.match(anexos, /accept=\{`image\/jpeg,image\/png,image\/webp,image\/gif\$\{soImagem \? '' : ',application\/pdf'\}`\}/)
})

await confere('C foto que não saiu fica escrita ao lado do botão, na galeria e no cardápio', () => {
  assert.equal(FOTO_NAO_ENVIADA, 'A foto não foi enviada. O motivo está no balão da conversa.')
  const galeria = fonte('features/anexos/PainelGaleria.jsx')
  assert.match(galeria, /if \(enviado === false\) \{ setFalhouId\(peca\.id\); return \}/)
  assert.match(galeria, /\{falhouId === peca\.id && <p className=\{css\.cartaoFalha\} role="alert">\{FOTO_NAO_ENVIADA\}<\/p>\}/)
  assert.match(painelCardapio, /if \(enviado === false\) \{ setFalhaFoto\(\{ sku: item\.sku, conversaId: selecionada\.id \}\); return \}/)
  assert.match(painelCardapio, /falhou: falhaFoto\?\.conversaId === selecionada\.id \? falhaFoto\.sku : null/, 'a falha não segue para outra conversa')
  assert.match(painelCardapio, /\{envioDeFoto\?\.falhou === item\.sku && <p className=\{css\.falhaFoto\} role="alert">\{FOTO_NAO_ENVIADA\}<\/p>\}/)
})

// ── D. Rolagem do fio ────────────────────────────────────────────────────────────────────
const doCliente = (id) => ({ id, dir: 'in' })
const daCasa = (id) => ({ id, dir: 'out', status: 'enviando' })
const automatica = (id) => ({ id, dir: 'out', automatica: true })
const LENDO_ACIMA = false
const NO_FIM = true

await confere('D o retrato do fio separa a mensagem da casa da automática e da do cliente', () => {
  assert.deepEqual(retratoDoFio('c1', []), { conversaId: 'c1', total: 0, ultimaId: null, ultimaDaCasa: false })
  assert.equal(retratoDoFio('c1', [doCliente('a'), daCasa('b')]).ultimaDaCasa, true)
  assert.equal(retratoDoFio('c1', [doCliente('a'), automatica('b')]).ultimaDaCasa, false)
  assert.equal(retratoDoFio('c1', [daCasa('a'), doCliente('b')]).ultimaDaCasa, false)
  assert.equal(retratoDoFio('c1', [{ id: 's', dir: 'sistema' }]).ultimaDaCasa, false)
})

await confere('D abrir ou trocar de conversa leva ao fim, mesmo com a leitura lá em cima', () => {
  const fio = retratoDoFio('c1', [doCliente('a')])
  assert.equal(deveRolarParaOFim(null, fio, LENDO_ACIMA), true)
  assert.equal(deveRolarParaOFim(fio, retratoDoFio('c2', [doCliente('x')]), LENDO_ACIMA), true)
})

await confere('D lendo mais acima, mensagem do cliente e automática não puxam o fio', () => {
  const antes = retratoDoFio('c1', [doCliente('a')])
  assert.equal(deveRolarParaOFim(antes, retratoDoFio('c1', [doCliente('a'), doCliente('b')]), LENDO_ACIMA), false)
  assert.equal(deveRolarParaOFim(antes, retratoDoFio('c1', [doCliente('a'), automatica('b')]), LENDO_ACIMA), false)
  assert.equal(deveRolarParaOFim(antes, antes, LENDO_ACIMA), false, '"digitando" ou nota nova, sem mensagem')
})

await confere('D mensagem enviada daqui leva ao fim; com a leitura no fim, qualquer novidade leva', () => {
  const antes = retratoDoFio('c1', [doCliente('a')])
  assert.equal(deveRolarParaOFim(antes, retratoDoFio('c1', [doCliente('a'), daCasa('b')]), LENDO_ACIMA), true)
  assert.equal(deveRolarParaOFim(antes, retratoDoFio('c1', [doCliente('a'), doCliente('b')]), NO_FIM), true)
  assert.equal(deveRolarParaOFim(antes, retratoDoFio('c1', [doCliente('a'), automatica('b')]), NO_FIM), true)
  assert.equal(deveRolarParaOFim(antes, antes, NO_FIM), true)
})

await confere('D histórico que entra por cima e confirmação que só troca o id não contam como mensagem nova', () => {
  const antes = retratoDoFio('c1', [doCliente('a'), daCasa('local-1')])
  const comHistorico = retratoDoFio('c1', [doCliente('antiga'), doCliente('a'), daCasa('local-1')])
  assert.equal(deveRolarParaOFim(antes, comHistorico, LENDO_ACIMA), false)
  const confirmada = retratoDoFio('c1', [doCliente('a'), { id: 'm-api', dir: 'out', status: 'enviada' }])
  assert.equal(deveRolarParaOFim(antes, confirmada, LENDO_ACIMA), false)
})

await confere('D "no fim" tem folga de um balão curto', () => {
  const medida = (topo) => ({ alturaTotal: 2000, topo, alturaVisivel: 600 })
  assert.equal(pertoDoFim(medida(1400)), true)
  assert.equal(pertoDoFim(medida(1400 - FOLGA_DO_FIM)), true)
  assert.equal(pertoDoFim(medida(1400 - FOLGA_DO_FIM - 1)), false)
  assert.equal(pertoDoFim({ alturaTotal: 300, topo: 0, alturaVisivel: 600 }), true, 'conversa que cabe inteira está no fim')
})

await confere('D o fio usa a regra: nada de rolar a cada mensagem', () => {
  const thread = fonte('features/atendimento/Thread.jsx')
  assert.ok(!thread.includes('scrollIntoView'), 'Thread ainda rola direto')
  assert.match(thread, /useRolagemDoFio\(fim, \{ mensagens, chaveRolagem, digitando, totalDeNotas: notas\.length \}\)/)
  const hook = fonte('features/atendimento/useRolagemDoFio.js')
  assert.match(hook, /if \(deveRolarParaOFim\(anterior\.current, depois, noFim\.current\)\) \{\s*fimRef\.current\?\.scrollIntoView\(\{ block: 'end' \}\)/)
  assert.equal(hook.split('scrollIntoView').length - 1, 1)
  assert.match(hook, /area\.addEventListener\('scroll', medir, \{ passive: true \}\)/)
})

// ── E. Foco depois da resposta pronta ────────────────────────────────────────────────────
const RESPOSTAS = [
  { id: 'r1', titulo: 'Boas-vindas', atalho: '/oi', texto: 'Olá, {nome}! Como posso ajudar?' },
  { id: 'r2', titulo: 'Horário', atalho: '/hora', texto: 'Abrimos às 11h.' },
]
const montarSeletor = (rascunhoInicial) => {
  hooks.valores = []
  globalThis.__prova1587.catalogo = { respostasProntas: RESPOSTAS }
  const estado = { rascunho: rascunhoInicial, ordem: [] }
  const desenhar = () => {
    hooks.indice = 0
    return useAtalhoBarra({
      rascunho: estado.rascunho, conversa: { id: 'c1', nome: 'Ana Souza' },
      aoInserir: (texto) => { estado.rascunho = texto; estado.ordem.push('texto') },
    })
  }
  return { estado, desenhar, aoInserido: () => estado.ordem.push(desenhar().aberto ? 'foco-com-lista-aberta' : 'foco') }
}
const tecla = (key) => ({ key, shiftKey: false, preventDefault() {} })

await confere('E pelo botão, o clique insere, fecha a lista e só então chama quem devolve o foco', () => {
  const { estado, desenhar, aoInserido } = montarSeletor('Oi')
  desenhar().abrir()
  const aberto = desenhar()
  assert.equal(aberto.aberto, true)
  assert.equal(aberto.pelaBarra, false)
  aberto.inserir(aberto.itens[0], aoInserido)
  assert.deepEqual(estado.ordem, ['texto', 'foco'])
  assert.equal(estado.rascunho, 'Oi Olá, Ana! Como posso ajudar?')
  assert.equal(desenhar().aberto, false)
})

await confere('E o Enter da busca faz o mesmo; sem resposta na lista não chama ninguém', () => {
  const { estado, desenhar, aoInserido } = montarSeletor('')
  desenhar().abrir()
  assert.equal(desenhar().aoTeclar(tecla('Enter'), aoInserido), true)
  assert.deepEqual(estado.ordem, ['texto', 'foco'])
  const vazio = montarSeletor('')
  vazio.desenhar().abrir()
  vazio.desenhar().mudarBusca('termo que não existe')
  assert.equal(vazio.desenhar().itens.length, 0)
  vazio.desenhar().aoTeclar(tecla('Enter'), vazio.aoInserido)
  assert.deepEqual(vazio.estado.ordem, [])
  vazio.desenhar().aoTeclar(tecla('Escape'), vazio.aoInserido)
  assert.deepEqual(vazio.estado.ordem, [], 'Esc fecha sem levar o foco para o campo')
})

await confere('E pelo atalho "/" nada muda: o texto troca o termo e o Enter do campo não exige foco novo', () => {
  const { estado, desenhar } = montarSeletor('/hora')
  const aberto = desenhar()
  assert.equal(aberto.pelaBarra, true)
  assert.equal(aberto.aoTeclar(tecla('Enter')), true)
  assert.equal(estado.rascunho, 'Abrimos às 11h.')
})

await confere('E o seletor repassa o aviso no clique e no Enter, e o composer foca o campo de texto', () => {
  const seletor = fonte('features/atendimento/SeletorRespostas.jsx')
  assert.match(seletor, /onClick=\{\(\) => seletor\.inserir\(r, aoInserido\)\}/)
  assert.match(seletor, /onKeyDown=\{\(e\) => \{ seletor\.aoTeclar\(e, aoInserido\) \}\}/)
  assert.match(composer, /<SeletorRespostas seletor=\{barra\} aoInserido=\{\(\) => campoRef\.current\?\.querySelector\('textarea'\)\?\.focus\(\)\}/)
  assert.match(composer, /<div ref=\{campoRef\}>\s*<CampoArea/, 'o campo de texto mora dentro de campoRef')
})

await confere('E composição IME e seleção de texto não inserem respostas, fecham a lista ou mudam o destaque', () => {
  const m = montarSeletor('/')
  for (const key of ['Enter', 'ArrowDown', 'ArrowUp', 'Escape']) {
    for (const extra of [{ isComposing: true }, { nativeEvent: { isComposing: true } }, { keyCode: 229 },
      { altKey: true }, { ctrlKey: true }, { metaKey: true }, { shiftKey: true }]) {
      const evento = { ...tecla(key), ...extra, preventDefault() { assert.fail('a edição nativa não pode ser capturada') } }
      assert.equal(m.desenhar().aoTeclar(evento), false)
      assert.equal(m.desenhar().destaque, 0)
      assert.equal(m.desenhar().aberto, true)
      assert.equal(m.estado.rascunho, '/')
    }
  }
  assert.deepEqual(m.estado.ordem, [])
  m.desenhar().aoTeclar(tecla('ArrowDown'))
  assert.equal(m.desenhar().destaque, 1)
  m.desenhar().aoTeclar(tecla('Enter'))
  assert.equal(m.estado.rascunho, 'Abrimos às 11h.')
})

await confere('E texto multilinha mantém semântica nativa e aponta para a opção da lista no portal', () => {
  const seletor = fonte('features/atendimento/SeletorRespostas.jsx')
  const campo = composer.slice(composer.indexOf('<CampoArea'), composer.indexOf('/>', composer.indexOf('<CampoArea')))
  assert.doesNotMatch(campo, /role=/, 'textarea continua sendo caixa de texto multilinha')
  assert.match(campo, /aria-controls=\{sugestoesNoCampo \? idListaRespostas : undefined\}/)
  assert.match(campo, /aria-activedescendant=\{sugestoesNoCampo && barra\.itens\[barra\.destaque\]/)
  assert.match(campo, /if \(e\.isComposing \|\| e\.nativeEvent\?\.isComposing \|\| e\.keyCode === 229/)
  assert.match(composer, /idLista=\{idListaRespostas\}/)
  assert.match(seletor, /const idLista = idInformado \?\? idGerado/)
  assert.match(seletor, /id=\{idLista\} role="listbox"/)
  assert.match(seletor, /id=\{idDe\(i\)\}/)
  assert.match(seletor, /role="combobox"/)
  assert.match(seletor, /aria-expanded=\{itens.length > 0\}/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
