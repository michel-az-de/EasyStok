/* eslint-disable no-console */
// Prova da issue #1870 (rodada visual 2: navegação do console no desktop).
// Medido antes no laboratório, em 1280/1440/1920: rodapé fixo de 35 px sempre visível; "Sair" em
// três lugares conforme a tela; Entregas com menu lateral de 216 px para 3 itens e "Entregas"
// escrito 5 vezes; trilho de 84/68 px no Balcão e 100/84 px nos módulos, com "Balcão" num e
// "Atendimento" no outro; título de área com 19, 20 ou 22 px; ficha com pedido rolando 66 px em
// 1280x720; faixa de modo com 102 px em 2 linhas; aviso do composer em 2 linhas em coluna estreita.
//
//   1  rodapé só com aviso e Sair atrás do avatar (trilho do Balcão, trilho dos módulos, hall);
//   2  módulo de trabalho: telas na barra, sem menu lateral nem título repetido;
//   3  um trilho só (largura e nome) e uma escala compacta só, registrada em tokens.css;
//   4  ficha compacta com Pós-venda visível; faixa de modo e aviso do composer em uma linha.
//
// Leitura do fonte onde só existe composição. As medidas de antes e depois na tela real saem
// dos roteiros de captura da rodada.
//
//   node ferramentas/prova-1870-visual.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// --- 1. Rodapé e Sair --------------------------------------------------------------------------

confere('1  a faixa ocupa espaço só com aviso, falha, integração parada ou sessão vencendo em qualquer tela', () => {
  const faixa = fonte('app/FaixaApi.jsx').split('export function FaixaApi()')[1]
  assert.match(faixa, /if \(!fonteApi\) return null/)
  assert.match(faixa, /if \(!falhou && !aviso && !integracaoParada && !vencimento\) return null/)
  assert.match(faixa, /sincronizacao\?\.estado === 'erro'/)
  assert.match(faixa, /avisoDeAcao \?\? sincronizacao\?\.avisoFixo/)
  assert.match(faixa, /\$\{falhou \? css\.falha : aviso \? css\.aviso : ''\}/)
  assert.match(faixa, /\{\(falhou \|\| aviso\) && <span className=\{css\.texto\}>\s*\{texto\}/)
  assert.match(faixa, /fecharAvisoApi\(\{ fixo: !avisoDeAcao \}\)/, 'fechar aviso de ação não apaga o persistente')
  assert.match(faixa, /\{integracaoParada && <span className=\{css\.texto\}>\{integracaoParada\}/)
  assert.match(faixa, /Ver integrações<\/a>/)
  assert.match(faixa, /\{vencimento && <strong className=\{css\.vence\}>\{vencimento\}<\/strong>\}/)
  assert.doesNotMatch(faixa, /rotuloDaSessao|>Sair<|className="sr"/, 'sessão sem problema não vira uma faixa permanente nem esconde problemas')
})
confere('1  o Sair mora no menu do avatar: trilho do Balcão, trilho dos módulos e canto do hall', () => {
  const conta = fonte('componentes/MenuDaConta.jsx')
  assert.match(conta, /aria-expanded=\{aberto\} aria-controls=\{id\}/)
  assert.match(conta, /role="group" aria-label="Conta"/)
  assert.match(conta, /const fecharComEsc = useCallback\(\(evento\) => \{ evento\.preventDefault\(\); fechar\(\) \}/, 'Esc não propaga o fechamento para outra camada')
  assert.match(conta, /useEscape\(aberto, fecharComEsc\)/, 'Esc fecha e devolve o foco ao avatar')
  assert.match(conta, /botaoRef\.current\?\.focus\(\)/)
  assert.match(conta, /const nome = nomeDaSessao \|\| \(aoSair \? 'Sua conta' : null\)/, 'sessão sem nome ainda sai')
  assert.match(fonte('app/Moldura.jsx'), /<MenuDaConta nome=\{atendente\} detalhe=\{conta\?\.casa\} aoSair=\{aoSair\}/)
  assert.match(fonte('features/hall/MenuDosModulos.jsx'), /<MenuDaConta nome=\{conta\?\.nome\} detalhe=\{conta\?\.casa\} aoSair=\{aoSair\}/)
  assert.match(fonte('features/hall/HallDeModulos.jsx'), /<MenuDaConta lado="abaixo"/)
  assert.doesNotMatch(fonte('features/hall/HallDeModulos.jsx'), /<Botao className=\{css\.sair\}/)
  assert.match(fonte('app/App.jsx'), /conta: \{ nome: sessao\.usuario\?\.nome \?\? null, casa: sessao\.empresa\?\.nome \?\? null \}/)
})
confere('1  com o trilho à vista a barra do módulo não repete Sair nem os links do trilho; sem ele, o Sair fica na barra', () => {
  const css = fonte('features/hall/moldura.module.css')
  assert.match(css, /@media \(min-width: 1101px\) \{\s*\.barra \.sair, \.contexto > a\.voltar \{ display: none; \}/)
  assert.match(css, /@media \(max-width: 1100px\) \{\s*\.lateral > \.trilho \{ display: none; \}/)
  assert.match(fonte('features/hall/MolduraDoModulo.jsx'), /\{aoSair && <Botao className=\{css\.sair\}/)
})
confere('1  o tema do trilho do Balcão vai para o menu da conta (em 1280x720 o trilho passava de 720 px)', () => {
  const moldura = fonte('app/Moldura.jsx')
  assert.doesNotMatch(moldura, /<InterruptorTema vertical \/>/)
  assert.match(moldura, /rotuloSair=\{sessaoPersistente \? 'Sair deste aparelho' : 'Sair'\}>\s*<InterruptorTema \/>\s*<\/MenuDaConta>/)
  assert.match(fonte('componentes/Trilho.module.css'), /width: var\(--largura-item-trilho\); min-height: max\(var\(--toque\), var\(--altura-item-trilho\)\);/)
  assert.match(fonte('componentes/MenuDaConta.module.css'), /position: fixed;/, 'a camada não é recortada pelo trilho')
})

// --- 2. Módulo de trabalho ---------------------------------------------------------------------

confere('2  módulo com tela de operação mostra as telas na barra e esconde o menu lateral', () => {
  const moldura = fonte('features/hall/MolduraDoModulo.jsx')
  assert.match(moldura, /const telasNaBarra = telas\.length > 1 && telas\.some\(\(t\) => t\.tipo === TELA_OPERACAO\)/)
  assert.match(moldura, /<nav className=\{css\.telasNaBarra\} aria-label=\{`Telas de \$\{modulo\.nome\}`\}>/)
  assert.equal((moldura.match(/semMenu=\{telasNaBarra\}/g) ?? []).length, 2, 'lateral e menu do tablet')
  assert.match(fonte('features/hall/MenuDosModulos.jsx'), /\{!semMenu && <nav className=\{css\.menu\}/)
})
confere('2  Entregas: o título fica para o leitor de tela e a aba não repete o nome do módulo', () => {
  assert.match(fonte('features/entregas/TelaEntregasApi.jsx'), /<h1 className="sr">\{agenda \? 'Agenda de encomendas' : 'Entregas'\}<\/h1>/)
  assert.match(fonte('features/entregas/ConteudoEntregasApi.jsx'), /const ABAS = \[\['hoje', 'Do dia'\],/)
})

// --- 3. Trilho e escala ------------------------------------------------------------------------

confere('3  Balcão e módulos usam o mesmo trilho, do token', () => {
  const tokens = fonte('estilos/tokens.css')
  // #1872: a largura nasce de `TRILHO` (paineis.js) e o item é o trilho menos o respiro: 76 e 68 px.
  assert.match(fonte('dominio/paineis.js'), /export const TRILHO = 76/, 'a geometria dos painéis é a fonte da largura')
  assert.match(tokens, /--respiro-trilho: 4px;/)
  assert.match(tokens, /--largura-item-trilho: calc\(var\(--largura-trilho\) - 2 \* var\(--respiro-trilho\)\);/)
  assert.match(tokens, /--altura-item-trilho: 54px;/)
  const trilho = fonte('componentes/Trilho.module.css')
  assert.match(trilho, /\.trilho \{\s*flex: 0 0 var\(--largura-trilho\); width: var\(--largura-trilho\);/)
  assert.match(trilho, /width: var\(--largura-item-trilho\); min-height: max\(var\(--toque\), var\(--altura-item-trilho\)\);/)
  for (const moldura of ['app/moldura.module.css', 'features/hall/moldura.module.css']) {
    assert.match(fonte(moldura), /\.trilho \{\s*composes: trilho from '(\.\.\/)+componentes\/Trilho\.module\.css'/, `${moldura} compõe o trilho único`)
  }
})
confere('3  o item do Balcão no trilho tem o nome e o ícone do módulo, como nos módulos', () => {
  const moldura = fonte('app/Moldura.jsx')
  assert.match(moldura, /const MODULO_ATENDIMENTO = moduloPorId\('atendimento'\)/)
  assert.match(moldura, /<IconeDoModulo modulo=\{MODULO_ATENDIMENTO\} \/>\s*<span>\{MODULO_ATENDIMENTO\.nome\}<\/span>/)
  assert.doesNotMatch(moldura, /<span>Balcão<\/span>/)
})
confere('3  a densidade compacta mora em tokens.css e é aplicada inteira, sem cópia parcial', () => {
  const tokens = fonte('estilos/tokens.css')
  const bloco = tokens.match(/\.densidade-compacta \{([^}]*)\}/)
  assert.ok(bloco, 'classe registrada')
  for (const t of ['--t-micro', '--t-apoio', '--t-corpo', '--t-forte', '--t-titulo', '--t-valor', '--t-display']) assert.match(bloco[1], new RegExp(`${t}:`))
  for (const [arquivo, classe] of [['app/moldura.module.css', 'areaAtendimento'], ['features/hall/moldura.module.css', 'moldura'],
    ['features/entregas/dashboard.module.css', 'paginaApi'], ['features/gestao/gestao.module.css', 'janelaGestao']]) {
    const css = fonte(arquivo)
    assert.match(css, new RegExp(`\\.${classe} \\{\\s*(?:/\\*[^*]*\\*/\\s*)?composes: densidade-compacta from global;`), arquivo)
    assert.doesNotMatch(css, /--t-(micro|apoio|corpo|forte|titulo|valor|display):/, `${arquivo} sem cópia da escala`)
  }
})

// --- 4. Ficha, faixa de modo e composer ---------------------------------------------------------

confere('4  ficha compacta mantém cabeçalho, cliente e acessos sem molduras, com rolagem só no conteúdo', () => {
  const css = fonte('features/ficha-cliente/ficha.module.css')
  const regra = (nome) => {
    const resultado = css.match(new RegExp(`\\.${nome} \\{([^}]*)\\}`))
    assert.ok(resultado, `regra ${nome} existe`)
    return resultado[1]
  }
  assert.match(regra('painel'), /display: flex; flex-direction: column;/)
  assert.match(regra('painel'), /min-height: 0;/)
  assert.match(regra('conteudo'), /overflow-y: auto; flex: 1; min-height: 0;/)
  assert.match(regra('conteudo'), /grid-template-columns: minmax\(0, 1fr\);/)
  assert.match(regra('conteudo'), /gap: var\(--e-3\); align-content: start;/)
  for (const nome of ['painel', 'conteudo', 'cartaoCliente']) {
    assert.doesNotMatch(regra(nome), /border(?:-\w+)?: (?!0(?:px)?[; ])|box-shadow:/, `${nome} sem contorno decorativo`)
  }
  assert.match(regra('cartaoCliente'), /padding: 0;/, 'cliente condensado em vez de outro cartão acolchoado')
  assert.match(regra('acessoFicha'), /min-height: var\(--toque\);/, 'alvo acompanha desktop e toque')
  assert.match(regra('cabecalhoFicha'), /flex-shrink: 0;/, 'cabeçalho não participa da rolagem')
  const painel = fonte('features/ficha-cliente/PainelFicha.jsx')
  assert.match(painel, /<header className=\{css\.cabecalhoFicha\}>/)
  assert.match(painel, /<div className=\{css\.conteudo\}>[\s\S]*?<CartaoDoCliente conversa=\{selecionada\}/)
  assert.match(painel, /<nav className=\{css\.acessosFicha\} aria-label="Detalhes do atendimento">/)
  assert.match(painel, /titulo="Pós-venda"/)
})
confere('4  faixa de modo: estado inteiro, Assumir à vista e demais ações no contexto', () => {
  const barra = fonte('features/atendimento/BarraModo.jsx')
  assert.match(barra, /const fraseRepetida = \(resumo\.chave === MODOS\.COM_VOCE \|\| \(resumo\.chave === MODOS\.LIGADO && !semAutomatico\)\) && linha === resumo\.linha/)
  assert.match(barra, /<p className=\{fraseRepetida \|\| !mostrarLinha \? 'sr' : css\.modoTexto\} title=/)
  assert.match(barra, /const mostrarLinha = semAutomatico \|\| bloqueada/)
  assert.match(barra, /\{podeAssumir && \([\s\S]*?onClick=\{aoAssumir\}[\s\S]*?Assumir/)
  assert.match(barra, /<details[^>]+className=\{css\.maisAcoesModo\}/)
  assert.match(barra, /<summary>Mais ações<\/summary>/)
  assert.match(barra, /evento\.key === 'Escape'/)
  const css = fonte('features/atendimento/atendimento.module.css')
  assert.doesNotMatch(css, /\.modoTexto\s*\{[^}]*text-overflow:\s*ellipsis/)
  assert.match(css, /\.maisAcoesModo > summary:focus-visible \{ outline: 3px solid var\(--halo\)/)
})
confere('4  restrição de formato permanece acessível e problemas de envio continuam visíveis junto do campo', () => {
  const composer = fonte('features/atendimento/Composer.jsx')
  assert.match(composer, /\{restricao && \(podeAnexar \|\| podeAudio\s*\? <p id=\{idRestricao\} className=\{css\.restricao\}>\{restricao\}<\/p>\s*: <p id=\{idRestricao\} className="sr">\{restricao\}<\/p>\)\}/)
  assert.match(composer, /aria-describedby=\{!podeFoto && restricao \? idRestricao : undefined\}/)
  assert.match(composer, /descritoPor=\{!podeAnexar && restricao \? idRestricao : undefined\}/)
  assert.match(composer, /\{motivo && \([\s\S]*?<p className=\{`\$\{css\.bloqueio\}[^`]*`\}>[\s\S]*?<strong>\{motivo\.titulo\}<\/strong>\s*<span>\{motivo\.detalhe\}<\/span>/, 'bloqueio e janela fora do prazo não ficam só no leitor de tela')
  for (const erro of ['erroAnexo', 'variavelSobrando']) assert.match(composer, new RegExp(`\\{${erro} && <p className=\\{css\\.restricao\\} role="alert">\\{${erro}\\}`))
  assert.match(composer, /\{erroAssumir && <p className=\{css\.erroAssumir\} role="alert">\{erroAssumir\}/)
  const css = fonte('features/atendimento/atendimento.module.css')
  const bloqueio = css.match(/\.bloqueio \{([^}]*)\}/)?.[1]
  assert.ok(bloqueio, 'estilo do bloqueio existe')
  assert.doesNotMatch(bloqueio, /display: none|visibility: hidden|opacity: 0/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
