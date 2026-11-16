/* eslint-disable no-console */
// Prova da issue #2762: polimento visual do console.
// Fixa a fundação comum (tempos, keyframes, esqueleto, vazio centrado) e a guarda que nasceu do achado
// principal: em módulo CSS o nome de keyframe global só vale com `global(nome)`. Sem isso o nome ganha
// hash, não acha o keyframe e a animação morre em silêncio (havia 11 cópias locais por causa disso).
//
//   node ferramentas/prova-2762-polimento.mjs

import { readFileSync, readdirSync } from 'node:fs'
import assert from 'node:assert/strict'

const SRC = new URL('../src/', import.meta.url)
const fonte = (caminho) => readFileSync(new URL(caminho, SRC), 'utf8')
const regra = (css, seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...css.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}
const token = (css, nome) => (css.match(new RegExp(`--${nome}:\\s*([^;]+);`)) || [])[1]?.trim()
const semComentario = (css) => css.replace(/\/\*[\s\S]*?\*\//g, '')
const keyframesDe = (css) => new Set([...semComentario(css).matchAll(/@keyframes\s+([\w-]+)/g)].map((m) => m[1]))

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n').slice(0, 12).join('\n      ')}`) }
}

const tokens = fonte('estilos/tokens.css')
const base = fonte('estilos/base.css')
const globais = keyframesDe(base)

// ---------- fundação ----------

confere('tokens: três tempos que não são de transição (realce, giro, respiro)', () => {
  assert.equal(token(tokens, 'dur-realce'), '2000ms')
  assert.equal(token(tokens, 'dur-giro'), '800ms')
  assert.equal(token(tokens, 'dur-respiro'), '1280ms')
})

confere('base: keyframes globais de saída, espera e realce', () => {
  for (const nome of ['surgir', 'surgirDeLado', 'esmaecer', 'sumir', 'girar', 'respirar', 'realcar']) assert.ok(globais.has(nome), `falta @keyframes ${nome}`)
  assert.match(base, /@keyframes realcar\s*\{\s*from\s*\{\s*background-color:\s*var\(--realce,\s*var\(--marca-claro\)\)/, 'o realce parte de --realce e some sozinho')
})

confere('base: o link responde no tempo do botão, só por cor', () => {
  const link = regra(base, 'a')
  assert.match(link, /-webkit-tap-highlight-color:\s*transparent/)
  assert.match(link, /transition:[^;]*color var\(--dur-curta\) var\(--ease-padrao\)/)
  assert.doesNotMatch(link, /transform/, 'estado de interação é cor, não transformação')
  assert.match(base, /:where\(a:any-link\):active\s*\{\s*color:\s*var\(--marca-forte\)/)
})

confere('base: movimento reduzido continua derrubando animação e transição para um quadro', () => {
  const bloco = base.slice(base.indexOf('@media (prefers-reduced-motion: reduce)'))
  assert.match(bloco, /animation-duration:\s*\.001ms\s*!important/)
  assert.match(bloco, /animation-iteration-count:\s*1\s*!important/)
  assert.match(bloco, /transition-duration:\s*\.001ms\s*!important/)
})

confere('Esqueleto: enfeite que respira, com o texto da espera ainda na página', () => {
  const css = fonte('componentes/Esqueleto.module.css')
  const jsx = fonte('componentes/Esqueleto.jsx')
  assert.match(regra(css, '.esqueleto'), /animation:\s*global\(respirar\)\s+calc\(var\(--dur-respiro\) \* 2\)/)
  assert.match(regra(css, '.esqueleto'), /background:\s*var\(--preenchimento-forte\)/)
  assert.match(regra(css, '.lista'), /animation:\s*global\(esmaecer\)[^;]*var\(--dur-media\)\s+both/, 'a lista só aparece depois de --dur-media: resposta rápida não pisca esqueleto')
  assert.match(jsx, /aria-hidden="true"/, 'a peça é enfeite')
  assert.match(jsx, /<output[^>]*aria-busy="true"/, 'quem anuncia a espera é a lista')
  assert.match(jsx, /<span className="sr">\{children\}<\/span>/, 'o texto de espera segue no DOM')
})

confere('Vazio: `centro` põe o vazio de painel inteiro no meio, sem mudar o padrão', () => {
  const css = fonte('componentes/Vazio.module.css')
  const jsx = fonte('componentes/Vazio.jsx')
  assert.match(regra(css, '.vazio'), /align-content:\s*start/, 'o padrão segue colado no topo (#2135)')
  assert.match(regra(css, '.centro'), /align-content:\s*center/)
  assert.match(jsx, /centro = false/)
})

// ---------- guarda: nome de animação em módulo CSS ----------

const RESERVADAS = new Set(['none', 'infinite', 'alternate', 'alternate-reverse', 'reverse', 'normal', 'both', 'forwards', 'backwards', 'running', 'paused',
  'linear', 'ease', 'ease-in', 'ease-out', 'ease-in-out', 'step-start', 'step-end', 'inherit', 'initial', 'unset', 'revert'])
const listar = (pasta) => readdirSync(new URL(pasta, SRC), { withFileTypes: true, recursive: true })
  .filter((e) => e.isFile() && e.name.endsWith('.module.css'))
  .map((e) => `${(e.parentPath ?? e.path).replaceAll('\\', '/').split('/src/')[1] ?? ''}/${e.name}`.replace(/^\//, ''))

// Devolve os nomes de animação de uma folha: { locais: [nome], globais: [nome] }.
const nomesDeAnimacao = (css) => {
  const limpo = semComentario(css)
  const locais = []
  const dosGlobais = []
  for (const [, prop, valor] of limpo.matchAll(/(?:^|[;{\s])(animation|animation-name)\s*:\s*([^;}]+)/g)) {
    for (const parte of valor.split(/,(?![^()]*\))/)) {
      for (const [, nome] of parte.matchAll(/\bglobal\(\s*([\w-]+)\s*\)/g)) dosGlobais.push(nome)
      const resto = parte.replace(/\b[\w-]+\([^()]*(?:\([^()]*\)[^()]*)*\)/g, ' ').replace(/!important/g, ' ')
      for (const palavra of resto.split(/\s+/).filter(Boolean)) {
        if (RESERVADAS.has(palavra) || /^-?\d*\.?\d+(ms|s)?$/.test(palavra)) continue
        if (prop === 'animation-name' || /^[A-Za-z_][\w-]*$/.test(palavra)) locais.push(palavra)
      }
    }
  }
  return { locais, globais: dosGlobais }
}

confere('módulo CSS: toda animação aponta para keyframe que existe (local na folha, ou global com `global()`)', () => {
  const mortas = []
  for (const arquivo of listar('.')) {
    const css = fonte(arquivo)
    const definidos = keyframesDe(css)
    const { locais, globais: usados } = nomesDeAnimacao(css)
    for (const nome of locais) if (!definidos.has(nome)) mortas.push(`${arquivo}: \`${nome}\` não é keyframe desta folha${globais.has(nome) ? ' (é global: escreva global(' + nome + '))' : ''}`)
    for (const nome of usados) if (!globais.has(nome)) mortas.push(`${arquivo}: global(${nome}) não existe em estilos/base.css`)
  }
  assert.deepEqual(mortas, [], `animação morta em silêncio:\n${mortas.join('\n')}`)
})

confere('a guarda enxerga os três casos (controle da própria prova)', () => {
  const folha = '@keyframes meu { to { opacity: 0 } }\n.a { animation: meu var(--dur-curta) var(--ease-saida) both; }\n.b { animation: surgir var(--dur-media) both, global(esmaecer) 1s; }\n.c { animation-name: global(girar); animation-duration: calc(var(--dur-giro) * 2); }\n.d { animation: none; }'
  const { locais, globais: usados } = nomesDeAnimacao(folha)
  assert.deepEqual(locais, ['meu', 'surgir'])
  assert.deepEqual(usados, ['esmaecer', 'girar'])
  const folhas = listar('.')
  assert.ok(folhas.length >= 80, `a varredura achou só ${folhas.length} folhas: caminho errado deixaria a guarda verde à toa`)
  assert.ok(folhas.includes('componentes/Esqueleto.module.css') && folhas.includes('app/moldura.module.css'), 'a varredura lê componentes e casca')
})

// ---------- peças base ----------

confere('Botao: o indicador de espera entra em fluxo, com a tinta do botão, e o texto segue à vista', () => {
  const css = fonte('componentes/Botao.module.css')
  const giro = regra(css, ".botao[aria-busy='true']::before")
  assert.match(giro, /animation-name:\s*global\(girar\)/)
  assert.match(giro, /animation-duration:\s*var\(--dur-giro\)/)
  assert.match(giro, /border:\s*var\(--b-2\) solid currentColor/, 'a tinta do giro é a do botão em qualquer variante')
  assert.doesNotMatch(giro, /position:\s*absolute/, 'giro sobreposto escondia o texto e pedia cor por variante')
  assert.match(regra(css, ".botao[aria-busy='true'] > svg"), /display:\s*none/, 'o giro ocupa o lugar do ícone: a largura do botão não muda')
  assert.doesNotMatch(css, /aria-busy='true'\]\s*\{[^}]*color:\s*transparent/, 'o texto "Salvando…" não some')
})

confere('Campo: o select tem seta própria, igual em todo navegador e nos dois temas', () => {
  const css = fonte('componentes/Campo.module.css')
  const seletor = regra(css, 'select.controle')
  assert.match(seletor, /appearance:\s*none/)
  assert.match(seletor, /background-image:[^;]*var\(--tinta-2\)/, 'a seta usa a tinta do tema')
  assert.match(seletor, /padding-right:\s*var\(--e-6\)/, 'o texto não passa por baixo da seta')
  // O estado mexe só em background-color: `background:` apagaria a seta no hover e no foco.
  for (const estado of ['.controle:hover:not(:disabled)', '.controle:focus', '.controle:disabled']) {
    assert.doesNotMatch(regra(css, estado), /(?:^|[;\s])background:/, `${estado} não pode usar o atalho background`)
  }
})

confere('interruptor: um desenho só, na base, sem cópia por tela', () => {
  assert.match(base, /input\[type='checkbox'\]\[role='switch'\]\s*\{[^}]*appearance:\s*none/)
  assert.match(base, /input\[type='checkbox'\]\[role='switch'\]:checked\s*\{[^}]*background:\s*var\(--acao\)/)
  assert.match(base, /input\[type='checkbox'\]\[role='switch'\]:disabled\s*\{[^}]*border-style:\s*dashed/, 'desabilitado segue tracejado, como o botão')
  for (const folha of ['features/gestao/avisos/abaAvisos.module.css', 'features/gestao/impressao/abaImpressao.module.css']) {
    assert.doesNotMatch(semComentario(fonte(folha)), /role='switch'\]/, `${folha}: o interruptor mora em estilos/base.css`)
  }
})

confere('primeira carga: a raiz não fica vazia enquanto o pacote não executa', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8')
  assert.match(html, /<div id="root"><div class="carga-inicial" role="status" aria-label="Carregando"><\/div><\/div>/)
  assert.match(html, /\.carga-inicial \{[^}]*animation: carga-surgir 150ms 400ms/, 'só aparece depois de 400 ms: carga rápida não pisca')
  assert.match(html, /border: 3px solid currentColor/, 'a tinta vem do tema')
})

// ---------- achados da revisão por captura ----------

confere('sino: os pontos de estado ficam nos cantos do ícone nas três barras, sem alargar o botão', () => {
  const trilho = semComentario(fonte('componentes/Trilho.module.css'))
  const balcao = semComentario(fonte('app/moldura.module.css'))
  const modulo = semComentario(fonte('features/hall/moldura.module.css'))
  assert.match(trilho, /\.filhos \[data-aviso-erro\],[^{]*\{[^}]*position:\s*absolute/)
  assert.match(balcao, /\.controles > button > \[data-aviso-erro\]\s*\{[^}]*position:\s*absolute/)
  assert.match(balcao, /\.controles > button:has\(\[data-aviso-erro\]\) > \[data-aparelho-desligado\]\s*\{[^}]*left:/, 'com os dois, cada ponto tem o seu canto')
  for (const marca of ['data-aparelho-desligado', 'data-aviso-erro']) {
    assert.match(modulo, new RegExp(`\\.avisos > button > :global\\(\\[${marca}\\]\\)\\s*\\{[^}]*position:\\s*absolute`), `cabeçalho do módulo no celular: ${marca}`)
  }
  const sino = fonte('features/notificacoes/SinoDaSessao.jsx')
  for (const marca of ['data-aviso-erro', 'data-contador-sino', 'data-aparelho-desligado']) assert.ok(sino.includes(marca), `o sino perdeu a marca ${marca}: a regra de posição ficaria solta`)
})

confere('login: a altura do botão do Google fica reservada, e sai junto quando não há Google configurado', () => {
  const css = semComentario(fonte('features/login/login.module.css'))
  assert.match(regra(css, '.google'), /min-height:\s*44px/, 'o cartão não salta quando o script do Google chega')
  assert.match(regra(css, '.google:empty'), /display:\s*none/, 'sem Google a reserva viraria um vão no cartão')
  assert.match(fonte('features/login/BotaoGoogle.jsx'), /return null/, 'é o `null` do botão que deixa a caixa vazia')
})

confere('busca: o X nativo só aparece com texto digitado, e o placeholder cabe inteiro', () => {
  assert.match(regra(fonte('componentes/Campo.module.css'), '.controle:placeholder-shown::-webkit-search-cancel-button'), /display:\s*none/)
})

confere('Impressão: os dois interruptores se declaram como interruptor', () => {
  const jsx = fonte('features/gestao/impressao/AbaImpressao.jsx')
  const chaves = [...jsx.matchAll(/<label className=\{css\.chave\}>\s*(<input[\s\S]*?\/>)/g)].map((m) => m[1])
  assert.equal(chaves.length, 2, 'a aba tem dois interruptores')
  for (const chave of chaves) {
    assert.match(chave, /role="switch"/)
    assert.match(chave, /aria-checked=/)
  }
})

// ---------- achados da revisão sobre pares de build ----------

confere('Modal: uma marca só de cada lado do miolo, filete sob o título ao rolar e divisória do rodapé', () => {
  const css = semComentario(fonte('componentes/Modal.module.css'))
  const miolo = regra(css, '.conteudo')
  assert.doesNotMatch(miolo, /radial-gradient/, 'a sombra em lente somava com a divisória do rodapé e virava mancha')
  assert.match(miolo, /background-image:\s*linear-gradient\(var\(--papel\), var\(--papel\)\), linear-gradient\(var\(--linha\), var\(--linha\)\)/)
  assert.match(miolo, /background-attachment:\s*local, scroll/, 'a faixa do papel rola com o miolo e descobre o filete')
  assert.match(regra(css, '.rodape'), /border-top:\s*var\(--b-1\) solid var\(--linha\)/)
})

confere('Modal: a dica do botão parado fica à esquerda na mesma linha, e na folha nenhum rótulo parte em dois', () => {
  const css = semComentario(fonte('componentes/Modal.module.css'))
  const [largo, folha] = css.split('@media (max-width: 759px)')
  const dica = regra(largo, '.rodape > span > span[id]')
  assert.match(dica, /order:\s*-1/)
  assert.match(dica, /flex:\s*1 1 0/, 'linha própria aumentava o rodapé e fazia o miolo rolar')
  assert.match(folha, /\.rodape > span > span\[id\]\s*\{\s*flex-basis:\s*100%/, 'no celular a dica segue com a linha inteira')
  assert.match(folha, /\.rodape > button, \.rodape > span > button\s*\{[^}]*min-width:\s*fit-content/, 'o botão que não cabe desce inteiro')
})

confere('ListaDados: a coluna do rótulo é da lista e mede a palavra mais larga, sem tirar largura do valor', () => {
  const css = semComentario(fonte('componentes/ListaDados.module.css'))
  assert.match(regra(css, '.lista'), /grid-template-columns:\s*minmax\(80px, min-content\) minmax\(0, 1fr\)/)
  assert.match(regra(css, '.fileira'), /grid-template-columns:\s*subgrid/, 'todas as fileiras na mesma coluna')
  assert.doesNotMatch(regra(css, '.fileira dt'), /overflow-wrap:\s*anywhere/, 'rótulo não parte no meio da palavra')
})

// ---------- regressões que a segunda revisão achou no próprio polimento ----------

confere('módulo: a folha só ganha borda no escuro, e o ícone de 32 px não sai da margem no título nem na barra', () => {
  const css = semComentario(fonte('features/hall/moldura.module.css'))
  const celular = css.slice(css.indexOf('@media (max-width: 759px)'), css.indexOf('@media (max-width: 640px)'))
  assert.match(celular, /\.folha\s*\{\s*border:\s*var\(--b-1\) solid light-dark\(transparent, var\(--linha\)\)/, 'no claro o papel já contrasta: borda ali é linha a mais')
  assert.match(regra(css, ':is(.item, .nomeMenu) > svg:is([width="32"], [width="32px"])'), /margin:\s*calc\(-1 \* var\(--e-1\)\)/, 'no menu o ícone de marca ocupa 24 px, como os vizinhos')
  const fora = regra(css, ':is(.modulo, .titulo) > svg:is([width="32"], [width="32px"])')
  assert.match(fora, /margin-block:\s*calc\(-1 \* var\(--e-1\)\)/, 'no título e na barra a compensação é só na altura')
  assert.doesNotMatch(fora, /margin:|margin-inline|margin-left/, 'margem negativa de lado põe o ícone para fora da margem do conteúdo')
})

confere('listas da produção: o botão secundário não some no escuro e a coluna de ações mede o mesmo em toda linha', () => {
  const css = semComentario(fonte('features/gestao/cardapio/abaCardapio.module.css'))
  assert.match(regra(css, '.lista'), /background:\s*var\(--papel\)/, 'lista em tom elevado apaga o botão secundário no escuro')
  assert.doesNotMatch(css, /\.linha:hover/, 'a linha não é clicável inteira: hover nela apaga o botão e promete um clique que não existe')
  assert.match(regra(css, '.acoesFixas'), /flex:\s*none/, 'sem base fixa a sobra fica com o nome do insumo')
  assert.match(regra(css, '.acoesFixas > button:last-child'), /min-width:\s*9\.5rem/)
  assert.match(regra(css, '.detalhe'), /text-wrap:\s*pretty/, 'o detalhe é `span`: o `pretty` global só pega `p`')
  assert.match(fonte('features/gestao/producao/AbaEstoqueDoDiaApi.jsx'), /<Botao variante="secundario" icone="lapis" aria-label=\{`Ajustar contagem de \$\{p\.nome\}`\}/, 'o Ajustar da tabela tem fundo: sem ele a ação some na linha')
})

confere('alcance por bairro: a variação é um trecho próprio, sem ponto médio que fique órfão ao quebrar', () => {
  const css = semComentario(fonte('features/entregas/dashboard.module.css'))
  const numeros = regra(css, '.alcanceNumeros')
  assert.match(numeros, /display:\s*flex;\s*flex-wrap:\s*wrap/)
  assert.match(numeros, /gap:\s*0 var\(--e-3\)/, 'o respiro separa a variação dos números')
  assert.match(regra(css, '.alcanceNumeros .alcanceVariacao, .alcanceNumeros .alcanceCaiu'), /white-space:\s*nowrap/)
  assert.match(fonte('features/entregas/PainelAlcance.jsx'), /'clientes'\)\}<\/span>\s*\{' '\}<Variacao/, 'os números fecham o trecho antes da variação')
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
