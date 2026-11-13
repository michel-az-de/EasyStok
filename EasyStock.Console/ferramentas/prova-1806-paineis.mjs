/* eslint-disable no-console */
// Prova da issue #1806, item 1 (revisão gravada de 14/10): o Cardápio de hoje e o Assistente
// trocam de lado, mudam de largura e ampliam, e nenhum dos dois cobre a conversa inteira.
//
//   1  lado e modo inválidos caem no padrão;
//   2  preferência corrompida, storage bloqueado ou ausente não quebram a tela;
//   3  em 1280x720 e 1440x900 a largura máxima de cada painel deixa a conversa com 320 px;
//   4  o modo flutuante fica dentro da tela;
//   5  a composição usa a regra (App, PainelCardapio, BalaoAssistente).
//
// A geometria vem do CSS da moldura (trilho de 100 px em --largura-trilho, vão de 12 px, conversa com 320 px no
// mínimo); a prova confere esses números no próprio CSS para não divergirem em silêncio.
//
//   node ferramentas/prova-1806-paineis.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

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
})

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try { await fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

let paineis
let preferencia
let larguras
await confere('0  a regra pura existe em dominio/paineis e a preferência em aplicacao/preferenciaDosPaineis', async () => {
  paineis = await import('../src/dominio/paineis.js')
  preferencia = await import('../src/aplicacao/preferenciaDosPaineis.js')
  larguras = await import('../src/hooks/larguras.js')
})

if (paineis && preferencia && larguras) {
  const {
    CONVERSA_MINIMA, TRILHO, VAO, CARDAPIO, ASSISTENTE, MARGEM_ASSISTENTE,
    ladoValido, normalizarCardapio, normalizarAssistente, larguraMaxima, conversaVisivel,
    larguraDoCardapio, posicaoDentroDaTela, LADOS_CARDAPIO, LADOS_ASSISTENTE,
  } = paineis

  // --- 1. Lado e modo ------------------------------------------------------------------------
  await confere('1  lado inválido cai no padrão; lado válido passa', () => {
    assert.equal(ladoValido('direita', LADOS_CARDAPIO, 'esquerda'), 'direita')
    assert.equal(ladoValido('cima', LADOS_CARDAPIO, 'esquerda'), 'esquerda')
    assert.equal(ladoValido(undefined, LADOS_CARDAPIO, 'esquerda'), 'esquerda')
    assert.equal(ladoValido('flutuante', LADOS_CARDAPIO, 'esquerda'), 'esquerda', 'cardápio não flutua')
    assert.equal(ladoValido('flutuante', LADOS_ASSISTENTE, 'direita'), 'flutuante')
  })

  await confere('1  cardápio: padrão do lado do Balcão e compacto; lixo cai no padrão', () => {
    const padrao = { lado: 'esquerda', modo: 'compacto' }
    assert.deepEqual(normalizarCardapio(null), padrao)
    assert.deepEqual(normalizarCardapio('direita'), padrao)
    assert.deepEqual(normalizarCardapio([1, 2]), padrao)
    assert.deepEqual(normalizarCardapio({ lado: 7, modo: 'gigante', extra: true }), padrao)
    assert.deepEqual(normalizarCardapio({ lado: 'direita', modo: 'amplo' }), { lado: 'direita', modo: 'amplo' })
  })

  await confere('1  assistente: padrão à direita, largura limitada, posição só número finito', () => {
    const padrao = { lado: 'direita', largura: ASSISTENTE.padrao, x: null, y: null }
    assert.deepEqual(normalizarAssistente(undefined), padrao)
    assert.deepEqual(normalizarAssistente({ lado: 'meio', largura: 'larga', x: 'a', y: Infinity }), padrao)
    assert.equal(normalizarAssistente({ largura: 9999 }).largura, ASSISTENTE.max)
    assert.equal(normalizarAssistente({ largura: 10 }).largura, ASSISTENTE.min)
    assert.deepEqual(normalizarAssistente({ lado: 'flutuante', largura: 400, x: 300, y: 120 }),
      { lado: 'flutuante', largura: 400, x: 300, y: 120 })
  })

  // --- 2. Preferência guardada -------------------------------------------------------------
  const memoria = () => {
    const dados = new Map()
    return { dados, getItem: (k) => (dados.has(k) ? dados.get(k) : null), setItem: (k, v) => dados.set(k, String(v)) }
  }
  const bloqueado = {
    getItem() { throw new Error('SecurityError') },
    setItem() { throw new Error('QuotaExceededError') },
  }
  const { lerPreferencia, gravarPreferencia, CHAVE_CARDAPIO, CHAVE_ASSISTENTE } = preferencia

  await confere('2  storage bloqueado: ler devolve o padrão e gravar devolve false sem lançar', () => {
    assert.deepEqual(lerPreferencia(CHAVE_CARDAPIO, normalizarCardapio, bloqueado), normalizarCardapio(null))
    assert.deepEqual(lerPreferencia(CHAVE_ASSISTENTE, normalizarAssistente, bloqueado), normalizarAssistente(null))
    assert.equal(gravarPreferencia(CHAVE_CARDAPIO, { lado: 'direita' }, normalizarCardapio, bloqueado), false)
  })

  await confere('2  JSON corrompido ou valor estranho no storage cai no padrão', () => {
    const s = memoria()
    s.setItem(CHAVE_CARDAPIO, '{lado:')
    assert.deepEqual(lerPreferencia(CHAVE_CARDAPIO, normalizarCardapio, s), normalizarCardapio(null))
    s.setItem(CHAVE_ASSISTENTE, '"flutuante"')
    assert.deepEqual(lerPreferencia(CHAVE_ASSISTENTE, normalizarAssistente, s), normalizarAssistente(null))
    s.setItem(CHAVE_ASSISTENTE, 'null')
    assert.deepEqual(lerPreferencia(CHAVE_ASSISTENTE, normalizarAssistente, s), normalizarAssistente(null))
  })

  await confere('2  sem localStorage nenhum (fora do navegador) a leitura não quebra', () => {
    assert.deepEqual(lerPreferencia(CHAVE_CARDAPIO, normalizarCardapio, null), normalizarCardapio(null))
    assert.doesNotThrow(() => gravarPreferencia(CHAVE_CARDAPIO, { lado: 'direita' }, normalizarCardapio, null))
  })

  await confere('2  gravar guarda só o normalizado e a leitura devolve a escolha', () => {
    const s = memoria()
    assert.equal(gravarPreferencia(CHAVE_CARDAPIO, { lado: 'direita', modo: 'amplo', lixo: 1 }, normalizarCardapio, s), true)
    assert.deepEqual(JSON.parse(s.dados.get(CHAVE_CARDAPIO)), { lado: 'direita', modo: 'amplo' })
    assert.deepEqual(lerPreferencia(CHAVE_CARDAPIO, normalizarCardapio, s), { lado: 'direita', modo: 'amplo' })
    assert.notEqual(CHAVE_CARDAPIO, CHAVE_ASSISTENTE, 'uma chave por painel: um não reseta o outro')
  })

  // --- 3. Conversa sempre legível ----------------------------------------------------------
  await confere('3  geometria da regra é a do CSS da moldura (trilho, vão, conversa mínima)', () => {
    const molduraCss = fonte('app/moldura.module.css')
    assert.match(molduraCss, /\.palcoTres > \.centro \{ min-width: (\d+)px; \}/)
    assert.equal(Number(molduraCss.match(/\.palcoTres > \.centro \{ min-width: (\d+)px; \}/)[1]), CONVERSA_MINIMA)
    // #1870/#1872: o trilho é o CSS e o token compartilhados com os módulos; o token nasce de TRILHO.
    assert.match(molduraCss, /\.trilho \{\s*composes: trilho from '\.\.\/componentes\/Trilho\.module\.css'/)
    assert.match(fonte('componentes/Trilho.module.css'), /\.trilho \{\s*flex: 0 0 var\(--largura-trilho\)/)
    assert.deepEqual(paineis.tokensDaGeometria(), { '--largura-trilho': `${TRILHO}px` }, 'trilho = token gerado da constante')
    assert.doesNotMatch(fonte('estilos/tokens.css'), /--largura-trilho:/, 'o token não tem segunda cópia em tokens.css')
    assert.match(fonte('main.jsx'), /tokensDaGeometria\(\)/, 'a partida aplica o token')
    assert.match(fonte('componentes/AlcaLargura.module.css'), new RegExp(`width: ${VAO}px`))
    assert.match(fonte('estilos/tokens.css'), new RegExp(`--e-3: ${VAO}px`), 'padding do palco = vão')
    assert.match(fonte('estilos/tokens.css'), new RegExp(`--e-4: ${MARGEM_ASSISTENTE}px`), 'margem do assistente')
    assert.equal(CONVERSA_MINIMA, 320)
  })

  // Trilho de 76 px e colunas aprovadas: a conversa mantém pelo menos 320 px ao abrir painéis.
  const telas = [{ tela: 1280, esperado: { cardEsq: 568, cardDir: CARDAPIO.max, assEsq: 552, assDir: ASSISTENTE.max } },
    { tela: 1440, esperado: { cardEsq: CARDAPIO.max, cardDir: CARDAPIO.max, assEsq: ASSISTENTE.max, assDir: ASSISTENTE.max } }]
  for (const { tela, esperado } of telas) {
    const { balcao, ficha } = larguras.padraoPorTela(tela)
    const area = { tela, balcao, ficha, colunas: true }
    const casos = [
      ['cardápio à esquerda', { lado: 'esquerda', empurra: true, faixa: CARDAPIO }, esperado.cardEsq],
      // #1828: à direita o cardápio alarga o Resumo, como à esquerda alarga o Balcão.
      ['cardápio à direita', { lado: 'direita', empurra: true, faixa: CARDAPIO }, esperado.cardDir],
      ['assistente à esquerda', { lado: 'esquerda', margem: MARGEM_ASSISTENTE, faixa: ASSISTENTE }, esperado.assEsq],
      ['assistente à direita', { lado: 'direita', margem: MARGEM_ASSISTENTE, faixa: ASSISTENTE }, esperado.assDir],
    ]
    for (const [nome, painel, maxEsperada] of casos) {
      await confere(`3  ${tela}: ${nome} vai até ${maxEsperada} px e a conversa fica com ${CONVERSA_MINIMA} px ou mais`, () => {
        const max = larguraMaxima({ ...area, ...painel })
        assert.equal(max, maxEsperada)
        assert.ok(conversaVisivel({ ...area, ...painel, largura: max }) >= CONVERSA_MINIMA, 'na máxima')
        assert.ok(conversaVisivel({ ...area, ...painel, largura: painel.faixa.min }) >= CONVERSA_MINIMA, 'na mínima')
        if (max < painel.faixa.max) {
          assert.ok(conversaVisivel({ ...area, ...painel, largura: max + 16 }) < CONVERSA_MINIMA, 'a máxima é justa, não folgada')
        }
      })
    }
  }

  // #1828: medido na tela, o amplo à direita cobria 156 px da conversa em 1440 (160 em 1280).
  await confere('3  cardápio à direita alarga o Resumo e não cobre a conversa, compacto ou amplo', () => {
    for (const tela of [1280, 1440, 1920]) {
      const padrao = larguras.padraoPorTela(tela)
      for (const cardapio of [CARDAPIO.min, CARDAPIO.padrao, CARDAPIO.max]) {
        const ficha = larguras.fichaComCardapio({ ...padrao, cardapio })
        assert.ok(ficha + VAO >= cardapio, `${tela}/${cardapio}: a conversa termina antes do painel`)
        assert.ok(ficha >= padrao.ficha, `${tela}/${cardapio}: Resumo já largo não encolhe`)
      }
    }
    assert.equal(larguras.fichaComCardapio({ ficha: 520, cardapio: 460 }), 520)
    assert.equal(larguras.fichaComCardapio({ ficha: 360, cardapio: 600 }), 588)
  })

  await confere('3  tela pequena: a máxima nunca fica abaixo da mínima do painel', () => {
    const area = { tela: 1180, balcao: 480, ficha: 520, colunas: true }
    assert.equal(larguraMaxima({ ...area, lado: 'esquerda', empurra: true, faixa: CARDAPIO }), CARDAPIO.min)
    assert.equal(larguraMaxima({ ...area, lado: 'direita', empurra: true, faixa: CARDAPIO }), CARDAPIO.min)
    assert.equal(larguraMaxima({ ...area, lado: 'direita', margem: MARGEM_ASSISTENTE, faixa: ASSISTENTE }), ASSISTENTE.min)
  })

  await confere('3  sem as três colunas (tablet) a máxima é a tela menos as margens', () => {
    assert.equal(larguraMaxima({ tela: 900, colunas: false, lado: 'direita', margem: 20, faixa: ASSISTENTE }), ASSISTENTE.max)
    assert.equal(larguraMaxima({ tela: 500, colunas: false, lado: 'direita', margem: 20, faixa: ASSISTENTE }), 460)
    assert.equal(larguraMaxima({ tela: 300, colunas: false, lado: 'direita', margem: 20, faixa: ASSISTENTE }), ASSISTENTE.min)
  })

  await confere('3  cardápio compacto usa a largura guardada dentro dos limites; amplo usa a máxima', () => {
    assert.equal(larguraDoCardapio({ modo: 'compacto', guardada: 500, maxima: 544 }), 500)
    assert.equal(larguraDoCardapio({ modo: 'compacto', guardada: 590, maxima: 492 }), 492, 'guardada maior que a tela permite')
    assert.equal(larguraDoCardapio({ modo: 'compacto', guardada: 300, maxima: 600 }), CARDAPIO.min)
    assert.equal(larguraDoCardapio({ modo: 'compacto', guardada: undefined, maxima: 600 }), CARDAPIO.padrao)
    assert.equal(larguraDoCardapio({ modo: 'amplo', guardada: 460, maxima: 592 }), 592)
    assert.equal(larguraDoCardapio({ modo: 'outro', guardada: 460, maxima: 592 }), 460)
  })

  // --- 4. Flutuante dentro da tela ---------------------------------------------------------
  await confere('4  flutuante: posição fora da tela volta para dentro; sem posição nasce no canto inferior direito', () => {
    const base = { largura: 432, altura: 500, tela: 1280, alturaTela: 720, margem: 20 }
    assert.deepEqual(posicaoDentroDaTela({ ...base, x: -300, y: -50 }), { x: 0, y: 0 })
    assert.deepEqual(posicaoDentroDaTela({ ...base, x: 5000, y: 5000 }), { x: 1280 - 432, y: 720 - 500 })
    assert.deepEqual(posicaoDentroDaTela({ ...base, x: 400, y: 100 }), { x: 400, y: 100 })
    assert.deepEqual(posicaoDentroDaTela({ ...base, x: null, y: null }), { x: 1280 - 432 - 20, y: 720 - 500 - 20 })
    assert.deepEqual(posicaoDentroDaTela({ ...base, altura: 900, x: 10, y: 10 }), { x: 10, y: 0 }, 'mais alto que a tela: cola no topo')
  })

  // --- 5. Composição -----------------------------------------------------------------------
  await confere('5  App guarda lado e modo do cardápio e alarga a coluna do lado em que ele abre', () => {
    const app = fonte('app/App.jsx')
    assert.match(app, /usePreferenciaDePainel\(CHAVE_CARDAPIO, normalizarCardapio\)/)
    assert.match(app, /ladoDoBalcao=\{tamanho === DESKTOP && prefCardapio\.lado === 'esquerda'\}/)
    assert.match(app, /cardapioAberto=\{modal === 'cardapio' && selecionada \? prefCardapio\.lado : null\}/)
    const moldura = fonte('app/Moldura.jsx')
    assert.match(moldura, /cardapioAberto === 'esquerda' \? balcaoComCardapio\(larguras\) : larguras\.balcao/)
    assert.match(moldura, /cardapioAberto === 'direita' \? fichaComCardapio\(larguras\) : larguras\.ficha/)
    assert.match(app, /larguraDoCardapio\(/)
  })

  await confere('5  Cardápio: alça com máxima da tela, Ampliar/Reduzir e troca de lado com rótulo', () => {
    const painel = fonte('features/cardapio/PainelCardapio.jsx')
    assert.match(painel, /max=\{larguraMaxima\}/)
    assert.match(painel, /amplo \? 'Reduzir' : 'Ampliar'/)
    assert.match(painel, /Mover o cardápio para a/)
    assert.match(painel, /aria-label=\{`\$\{amplo \? 'Reduzir' : 'Ampliar'\} o cardápio`\}/)
  })

  await confere('5  Assistente: três posições, alça de largura, barra de título arrastável e Escape mantido', () => {
    const balao = fonte('features/assistente/BalaoAssistente.jsx')
    assert.match(balao, /usePreferenciaDePainel\(CHAVE_ASSISTENTE, normalizarAssistente\)/)
    assert.match(balao, /<AlcaLargura/)
    assert.match(balao, /posicaoDentroDaTela\(/)
    assert.match(balao, /aria-label="Mover assistente"/)
    for (const rotulo of ['Encaixar à esquerda', 'Flutuante', 'Encaixar à direita']) assert.ok(balao.includes(rotulo), rotulo)
    assert.match(balao, /useEscape\(aberto,/)
  })
}

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
