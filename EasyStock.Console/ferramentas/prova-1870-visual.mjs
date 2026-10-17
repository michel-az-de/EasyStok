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

confere('1  no desktop e nos módulos a faixa some sem aviso, falha, integração parada ou sessão vencendo', () => {
  const faixa = fonte('app/FaixaApi.jsx')
  assert.match(faixa, /const enxuta = useTamanhoTela\(\) === DESKTOP \|\| somenteAvisos/)
  assert.match(faixa, /if \(enxuta && !falhou && !aviso && !integracaoParada && !vencimento\) return null/)
  assert.match(faixa, /\{!enxuta && <button type="button" className=\{css\.sair\} onClick=\{aoSair\}>Sair<\/button>\}/)
  assert.match(faixa, /\{!enxuta && rotuloDaSessao\(sessao\)\}/)
})
confere('1  o Sair mora no menu do avatar: trilho do Balcão, trilho dos módulos e canto do hall', () => {
  const conta = fonte('componentes/MenuDaConta.jsx')
  assert.match(conta, /aria-expanded=\{aberto\} aria-controls=\{id\}/)
  assert.match(conta, /role="group" aria-label="Conta"/)
  assert.match(conta, /useEscape\(aberto, fechar\)/, 'Esc fecha e devolve o foco ao avatar')
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
  assert.match(fonte('componentes/Trilho.module.css'), /width: var\(--largura-item-trilho\); min-height: var\(--altura-item-trilho\);/)
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
  // #1872: a largura nasce de `TRILHO` (paineis.js) e o item é o trilho menos o respiro: 100 e 84 px.
  assert.match(fonte('dominio/paineis.js'), /export const TRILHO = 100/, 'a geometria dos painéis é a fonte da largura')
  assert.match(tokens, /--respiro-trilho: 8px;/)
  assert.match(tokens, /--largura-item-trilho: calc\(var\(--largura-trilho\) - 2 \* var\(--respiro-trilho\)\);/)
  assert.match(tokens, /--altura-item-trilho: 54px;/)
  const trilho = fonte('componentes/Trilho.module.css')
  assert.match(trilho, /\.trilho \{\s*flex: 0 0 var\(--largura-trilho\); width: var\(--largura-trilho\);/)
  assert.match(trilho, /width: var\(--largura-item-trilho\); min-height: var\(--altura-item-trilho\);/)
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

confere('4  ficha compacta (blocos, acessos e barra Andamento) e Pós-venda continua entre os acessos', () => {
  const css = fonte('features/ficha-cliente/ficha.module.css')
  assert.match(css, /grid-template-columns: minmax\(0, 1fr\); gap: var\(--e-3\); align-content: start;\s*padding: var\(--e-3\) 0;/)
  assert.match(css, /min-height: 48px; padding: 6px 0;/)
  assert.match(css, /\.contagemEtapa \{ margin-left: var\(--e-2\);/, 'etapa na linha do Andamento')
  assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'), /Pós-venda/)
})
confere('4  faixa de modo: "Passou para você" não repete a frase; no desktop é uma linha só', () => {
  const barra = fonte('features/atendimento/BarraModo.jsx')
  assert.match(barra, /const fraseRepetida = resumo\.chave === MODOS\.COM_VOCE && linha === resumo\.linha/)
  assert.match(barra, /<p className=\{fraseRepetida \? 'sr' : css\.modoTexto\} title=/)
  assert.match(fonte('features/atendimento/atendimento.module.css'), /@media \(min-width: 760px\) \{\s*\.modo \{ padding-inline: var\(--e-3\); \}\s*\.modoLinha \{ flex-wrap: nowrap; \}/)
})
confere('4  composer de canal só de texto mostra o curto e mantém o motivo no title e no leitor de tela', () => {
  const composer = fonte('features/atendimento/Composer.jsx')
  assert.match(composer, /title=\{restricao\}><span aria-hidden="true">Só texto neste canal<\/span><span className="sr">\{restricao\}<\/span><\/p>/)
  assert.match(composer, /aria-describedby=\{!podeFoto && restricao \? idRestricao : undefined\}/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
