/* eslint-disable no-console */
// Prova da issue #1697 (crítica de layout na tela real, computador). Medido antes, em 1440x900
// no modo API com pedido aguardando pagamento: Ficha com 2.781 px para 595 visíveis (4,7 telas),
// comanda 1,5 tela abaixo e cobrança 3,3 telas abaixo; cardápio aberto cobrindo a conversa
// inteira; em 1280 a conversa com 380 px, menor que o Balcão (360) e a Ficha (420).
//
//   1  seção da Ficha pode nascer recolhida, e a escolha de quem atende continua valendo;
//   2  com pedido em andamento, cadastro, tags e histórico nascem recolhidos (o pedido sobe);
//   3  larguras padrão acompanham a tela, sem passar por cima do que a pessoa arrastou;
//   4  o cardápio abre do lado do Balcão e deixa a conversa à vista.
//
// Regras puras e leitura do fonte onde só existe composição. Não prova pixels: as medidas de
// antes e depois na tela estão em docs/plan/erp-casa-da-baba/13-critica-ux-desktop-2026-10-12.md.
//
//   node ferramentas/prova-1697-ux-desktop.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const recolhidos = await import('../src/hooks/recolhidos.js')
const larguras = await import('../src/hooks/larguras.js')
const ficha = await import('../src/dominio/ficha.js')
const paineis = await import('../src/dominio/paineis.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// --- 1. Recolhido de padrão --------------------------------------------------------------------

confere('1  sem escolha gravada vale o padrão da seção; com escolha, vale a escolha', () => {
  assert.equal(recolhidos.estaRecolhido({}, 'tags'), false)
  assert.equal(recolhidos.estaRecolhido({}, 'tags', true), true)
  assert.equal(recolhidos.estaRecolhido({ tags: false }, 'tags', true), false, 'ela abriu: fica aberta')
  assert.equal(recolhidos.estaRecolhido({ tags: true }, 'tags', false), true, 'ela recolheu: fica recolhida')
})
confere('1  alternar parte do que está na tela, não do que está gravado', () => {
  assert.deepEqual(recolhidos.alternarRecolhido({}, 'tags', true), { tags: false })
  assert.deepEqual(recolhidos.alternarRecolhido({}, 'tags'), { tags: true })
  assert.deepEqual(recolhidos.alternarRecolhido({ tags: false }, 'tags', true), { tags: true })
})
confere('1  o Bloco aceita o padrão e abrir de fora abre também a que nasce recolhida', () => {
  assert.match(fonte('componentes/Bloco.jsx'), /useRecolhido\(chave, recolhidoDePadrao\)/)
  assert.match(fonte('hooks/useRecolhido.js'), /if \(atual\(\)\[chave\] !== false\) gravar\(/)
})

// --- 2. Pedido primeiro ------------------------------------------------------------------------

const cadastrado = { telefone: '+5511987650001', endereco: 'Rua Teste, 100' }
confere('2  com pedido em andamento, cadastro completo, tags e histórico nascem recolhidos', () => {
  assert.deepEqual(ficha.fichaRecolhidaDePadrao({ pedido: { estado: 'aguardando', itens: [] }, cliente: cadastrado }),
    { contato: true, tags: true, historico: true })
})
confere('2  cadastro incompleto fica aberto: é o que falta para cobrar', () => {
  const semEndereco = ficha.fichaRecolhidaDePadrao({ pedido: { estado: 'aguardando', itens: [] }, cliente: { telefone: '+55', endereco: null } })
  assert.equal(semEndereco.contato, false)
  assert.equal(semEndereco.tags, true)
})
confere('2  sem pedido, ou com pedido encerrado, a Ficha é a do cliente: tudo aberto', () => {
  const aberto = { contato: false, tags: false, historico: false }
  assert.deepEqual(ficha.fichaRecolhidaDePadrao({ pedido: null, cliente: cadastrado }), aberto)
  assert.deepEqual(ficha.fichaRecolhidaDePadrao({ pedido: { estado: 'entregue', itens: [] }, cliente: cadastrado }), aberto)
  assert.deepEqual(ficha.fichaRecolhidaDePadrao({ pedido: { estado: 'cancelado', itens: [] }, cliente: cadastrado }), aberto)
})
// #1785: o cliente agora abre em modal; a renderização da ficha é coberta por prova-1785-ficha.
confere('2  cliente aceita recolhimento e blocos vazios do pós-venda nascem recolhidos', () => {
  const cliente = fonte('features/ficha-cliente/BlocoCliente.jsx')
  for (const chave of ['contato', 'tags', 'historico']) {
    assert.match(cliente, new RegExp(`chave="${chave}" recolhidoDePadrao=\\{recolhidas\\.${chave}`), chave)
  }
  assert.match(fonte('features/ficha-cliente/BlocoDevolucoes.jsx'), /chave="devolucoes" recolhidoDePadrao=\{!erro\}/)
  assert.match(fonte('features/ficha-cliente/BlocoOcorrenciasApi.jsx'), /chave="ocorrencias" recolhidoDePadrao=\{!erro\}/)
  assert.match(fonte('features/ficha-cliente/SeletorJanelaApi.jsx'), /chave="janela-pedido" recolhidoDePadrao=\{janelaRecolhidaDePadrao\(campos\)\}/)
})
confere('2  erro e o que pede ação não nascem escondidos', () => {
  // Troca de janela: recolhe com vaga já marcada ou ainda carregando; abre com erro ou sem vaga.
  assert.equal(ficha.janelaRecolhidaDePadrao({ leitura: { atual: { rotulo: 'ter 13/10' } }, erro: null }), true)
  assert.equal(ficha.janelaRecolhidaDePadrao({ leitura: null, erro: null }), true)
  assert.equal(ficha.janelaRecolhidaDePadrao({ leitura: null, erro: 'Janelas não carregaram' }), false)
  assert.equal(ficha.janelaRecolhidaDePadrao({ leitura: { atual: null }, erro: null }), false)
  assert.equal(ficha.resumoDaJanela({ leitura: { atual: { rotulo: 'ter 13/10' } }, erro: null }), 'ter 13/10')
  assert.equal(ficha.resumoDaJanela({ leitura: { atual: null }, erro: null }), 'Sem vaga de entrega')
  assert.equal(ficha.resumoDaJanela({ leitura: null, erro: 'x' }), ficha.RESUMO_COM_ERRO)
  // Recolhida por escolha dela, a seção ainda diz no título que não carregou.
  assert.match(fonte('features/ficha-cliente/BlocoDevolucoes.jsx'), /resumo=\{erro \? RESUMO_COM_ERRO : /)
  assert.match(fonte('features/ficha-cliente/BlocoOcorrenciasApi.jsx'), /resumo=\{erro \? RESUMO_COM_ERRO : /)
  // Endereço salvo com pedido em andamento abre uma pergunta dentro do bloco: ele não recolhe por cima dela.
  assert.match(fonte('features/ficha-cliente/BlocoCliente.jsx'), /chave="contato" recolhidoDePadrao=\{recolhidas\.contato && !avisoEndereco\}/)
})
confere('3  Enter na alça restaura o padrão da tela, não o padrão fixo antigo', () => {
  assert.match(fonte('app/Moldura.jsx'), /padrao=\{largurasPadrao\.balcao\}/)
  assert.match(fonte('app/Moldura.jsx'), /padrao=\{largurasPadrao\.ficha\}/)
  assert.match(fonte('app/App.jsx'), /largurasPadrao=\{largurasPadrao\}/)
  assert.match(fonte('hooks/useLarguras.js'), /padrao: padraoPorTela\(tela\)/)
})
confere('2  com pedido criado, o convite do link fica só com o botão, sem o parágrafo de explicação', () => {
  assert.match(fonte('features/ficha-cliente/ComandaDoCliente.jsx'), /\{!visiveis\.length && !erro && !pedido\?\.pedidoId && <p>Compartilhe com o cliente/)
})

// --- 3. Larguras -------------------------------------------------------------------------------

const TRILHO_E_FOLGAS = 120
const conversa = (vw) => { const p = larguras.padraoPorTela(vw); return vw - TRILHO_E_FOLGAS - p.balcao - p.ficha }
confere('3  em notebook a conversa não fica menor que as colunas dos lados', () => {
  for (const vw of [1280, 1366]) {
    const p = larguras.padraoPorTela(vw)
    assert.ok(conversa(vw) >= 460, `${vw}: conversa com ${conversa(vw)} px`)
    assert.ok(conversa(vw) > p.balcao && conversa(vw) > p.ficha, `${vw}: a conversa é a coluna mais larga`)
    assert.ok(p.balcao >= 300 && p.ficha >= 320, 'respeita os mínimos das alças')
  }
})
confere('3  em tela média nada muda; em tela larga Balcão e Ficha ganham largura', () => {
  assert.deepEqual(larguras.padraoPorTela(1440), { balcao: 360, ficha: 420, cardapio: 460 })
  // #1868: em 1920 a Ficha de 520 sobrava 200 px vazios; 60 deles foram para a prévia do Balcão.
  assert.equal(larguras.padraoPorTela(1920).ficha, 480)
  assert.equal(larguras.padraoPorTela(1920).balcao, 420)
  assert.ok(1920 - TRILHO_E_FOLGAS - 420 - 480 >= 880, 'a conversa segue com cerca de 900 px')
})
confere('3  o que a pessoa arrastou vale em qualquer tela; padrão antigo gravado sozinho não conta', () => {
  assert.deepEqual(larguras.largurasDaTela({ ficha: 480 }, 1280), { ...larguras.padraoPorTela(1280), ficha: 480 })
  // Antes desta rodada o padrão era gravado no primeiro uso, sem ninguém arrastar nada.
  assert.deepEqual(larguras.escolhidasDoSalvo({ balcao: 360, ficha: 420, cardapio: 460 }), {})
  assert.deepEqual(larguras.escolhidasDoSalvo({ balcao: 360, ficha: 500, cardapio: 460 }), { ficha: 500 })
  assert.deepEqual(larguras.escolhidasDoSalvo(null), {})
  assert.deepEqual(larguras.escolhidasDoSalvo({ ficha: 'larga', balcao: 400 }), { balcao: 400 })
})

// --- 4. Cardápio -------------------------------------------------------------------------------

confere('4  no computador o cardápio abre do lado do Balcão, com a alça do lado da conversa', () => {
  // #1806: a pessoa pode mandar o cardápio para o lado do Resumo; o padrão segue o do Balcão.
  assert.match(fonte('app/App.jsx'), /ladoDoBalcao=\{tamanho === DESKTOP && prefCardapio\.lado === 'esquerda'\}/)
  assert.equal(paineis.normalizarCardapio(null).lado, 'esquerda')
  const painel = fonte('features/cardapio/PainelCardapio.jsx')
  assert.match(painel, /ladoDoBalcao \? css\.painelDoBalcao : ''/)
  assert.match(painel, /invertida=\{!ladoDoBalcao\}/)
  assert.doesNotMatch(fonte('app/App.jsx'), /deslocamentoDireita=\{tamanho === DESKTOP \? larguras\.ficha : 0\}/)
  assert.match(fonte('features/cardapio/cardapio.module.css'), /\.painelDoBalcao \{[^}]*flex-direction: row-reverse/)
})

confere('4  com o cardápio aberto o Balcão abre espaço: o painel não corta o começo das mensagens', () => {
  // Painel de 84 a 84 + cardápio; a conversa começa em 84 + balcão + 12 (alça).
  for (const vw of [1280, 1440, 1920]) {
    const l = larguras.largurasDaTela({}, vw)
    assert.ok(larguras.balcaoComCardapio(l) + 12 >= l.cardapio, `${vw}: a conversa começa depois do painel`)
  }
  assert.equal(larguras.balcaoComCardapio({ balcao: 480, cardapio: 460 }), 480, 'Balcão já largo não encolhe')
  assert.equal(larguras.balcaoComCardapio({ balcao: 360, cardapio: 400 }), 428, 'vale o mínimo de 440 do painel')
  assert.match(fonte('app/App.jsx'), /cardapioAberto=\{modal === 'cardapio' && selecionada \? prefCardapio\.lado : null\}/)
  assert.match(fonte('app/Moldura.jsx'), /cardapioAberto === 'esquerda' \? balcaoComCardapio\(larguras\) : larguras\.balcao/)
})
confere('1  título de seção recolhida não quebra em duas linhas; quem encurta é o resumo', () => {
  assert.match(fonte('componentes/Bloco.module.css'), /\.gatilho > span:first-child \{ flex-shrink: 0; white-space: nowrap; \}/)
})

if (falhas.length > 0) {
  console.error(`\n${passou} ok, ${falhas.length} falha(s)`)
  process.exit(1)
}
console.log(`\n${passou} ok, 0 falha(s)`)
