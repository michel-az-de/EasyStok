/* eslint-disable no-console */
// Prova da issue #2246: cantos arredondados no Console, pela regra da banca UX/UI de 04/11.
// Raio por papel (#2864: 6 px controle, 10 px cartão, 14 px painel), 999 px em chips, avatares e contadores, e canto de direção no balão.
//
//   node ferramentas/prova-2246-cantos.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const regra = (css, seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...css.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}
const token = (css, nome) => (css.match(new RegExp(`--${nome}:\\s*([^;]+);`)) || [])[1]?.trim()

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n')[0]}`) }
}

const tokens = fonte('estilos/tokens.css')
const atendimento = fonte('features/atendimento/atendimento.module.css')

confere('tokens: raio por papel (#2864: 6, 10 e 14 px); 999 px na pílula; 2 px na ponta', () => {
  for (const [nome, valor] of [['r-1', '6px'], ['r-2', '10px'], ['r-3', '14px']]) assert.equal(token(tokens, nome), valor, `--${nome}`)
  assert.equal(token(tokens, 'r-pilula'), '999px')
  assert.equal(token(tokens, 'r-ponta'), '2px')
})

confere('balão: arredondado, com o canto da ponta em --r-ponta só na troca de voz', () => {
  assert.match(regra(atendimento, '.balao'), /border-radius:\s*var\(--r-2\)/)
  for (const lado of ['.entrada', '.saida']) {
    assert.doesNotMatch(regra(atendimento, lado), /border-radius:\s*var\(--r-1\)/, `${lado} sem o raio de controle`)
  }
  assert.match(regra(atendimento, '.trocaDeVoz.entrada'), /border-top-left-radius:\s*var\(--r-ponta\)/, 'recebido: ponta à esquerda')
  assert.match(regra(atendimento, '.trocaDeVoz.saida'), /border-top-right-radius:\s*var\(--r-ponta\)/, 'enviado: ponta à direita')
})

confere('contador numérico em pílula', () => {
  // #2185 (R04): o contador único do app é a Pilula contagem, e o "não lidas" do cartão é ela.
  assert.match(regra(fonte('componentes/Pilula.module.css'), '.contagem'), /border-radius:\s*var\(--r-pilula\)/)
  assert.match(fonte('features/caixa-de-entrada/CartaoConversa.jsx'), /<Pilula\s+contagem=\{pendentes\}[^>]*className=\{css\.naoLidas\}/)
  assert.doesNotMatch(regra(fonte('features/caixa-de-entrada/caixa.module.css'), '.naoLidas'), /border-radius/, '.naoLidas não redesenha o contador')
})

confere('avatar e chip seguem em pílula', () => {
  assert.match(fonte('componentes/Avatar.module.css'), /border-radius:\s*var\(--r-pilula\)/)
  assert.match(fonte('componentes/Chip.module.css'), /border-radius:\s*var\(--r-pilula\)/)
})

confere('contadores, pontos, chips, selos e tags dos módulos em pílula', () => {
  const pilulas = [
    ['features/caixa-de-entrada/caixa.module.css', ['.pontoOrdem', '.chipFiltro', '.pontoPrecisa']],
    ['features/cozinha/cozinha.module.css', ['.pontoVivo']],
    ['features/ficha-cliente/cliente.module.css', ['.seloCaptado', '.seloPontos', '.seloInformado']],
    ['features/ficha-cliente/ficha.module.css', ['.tagsCliente li']],
    ['features/lembretes/lembretes.module.css', ['.ponto', 'button.pilula']],
  ]
  for (const [arquivo, seletores] of pilulas) {
    const css = fonte(arquivo)
    for (const seletor of seletores) {
      assert.match(regra(css, seletor), /border-radius:\s*var\(--r-pilula\)/, `${arquivo} ${seletor}`)
    }
  }
})

confere('painel móvel flutuante usa o raio de painel', () => {
  assert.match(regra(fonte('componentes/PainelMovel.module.css'), '.painel'), /border-radius:\s*var\(--r-3\)/)
})

confere('ponto de aviso do aparelho é redondo', () => {
  assert.match(regra(fonte('features/notificacoes/notificacoes.module.css'), '.pontoAparelho'), /border-radius:\s*var\(--r-pilula\)/)
})

confere('bolinhas e botão do interruptor são redondos', () => {
  assert.match(regra(fonte('features/automacoes/automacoes.module.css'), ".chave input[type='checkbox']::after"), /border-radius:\s*var\(--r-pilula\)/, 'botão do interruptor')
  assert.match(regra(fonte('features/cozinha/cozinha.module.css'), '.marcaEtapa'), /border-radius:\s*var\(--r-pilula\)/, 'marca da etapa')
  assert.match(regra(atendimento, '.sistema'), /border-radius:\s*var\(--r-pilula\)/, 'aviso do sistema é pílula central')
})

confere('cartão da comanda e gaveta da ficha arredondados', () => {
  assert.match(regra(fonte('features/ficha-cliente/comanda.module.css'), '.comanda, .papelSelecao'), /border-radius:\s*var\(--r-2\)/, 'comanda')
  assert.match(regra(fonte('app/moldura.module.css'), '.gaveta.gaveta'), /border-radius:\s*var\(--r-3\)/, 'gaveta')
})

// A ponta (2 px) fica no balão e na tecla de atalho da dica; faixas já não têm barra lateral (#2734, regra do dono
// de 12/11/2026), então não há canto de ponta nelas. Miniatura e anel de foco seguem o raio de controle.
confere('--r-ponta só no balão e na tecla de atalho', () => {
  for (const caminho of ['features/atendimento/composerAnexos.module.css', 'componentes/Bloco.module.css']) {
    assert.doesNotMatch(fonte(caminho), /border-radius:\s*var\(--r-ponta\)\s*;/, caminho)
  }
})

confere('nenhum comentário do Console promete cantos retos', () => {
  for (const caminho of ['componentes/Pilula.jsx', 'componentes/Pilula.module.css', 'features/atendimento/atendimento.module.css']) {
    assert.doesNotMatch(fonte(caminho), /cantos? (seguem )?retos|Quadrado: raio 0/i, caminho)
  }
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
