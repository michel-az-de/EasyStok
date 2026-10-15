// Prova da #1616: a janela avulsa da Cozinha e das Entregas não carrega um segundo Balcão, e o
// anel do Pix vencido não oferece "Reenviar cobrança" no modo API.
//
// Cobre as regras puras (`dominio/janelaAvulsa.js`, `avisoDeLinkVencidoApi`) e, por leitura do
// fonte, a composição que só existe no JSX. A troca de janela em si é do navegador e foi
// medida na tela (ver a PR).
//
// Roda com: node ferramentas/prova-1616-janela-avulsa-pix.mjs

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
})

const { JANELA_COZINHA, JANELA_ENTREGAS, ehJanelaAvulsa, origemForaDoBalcao } = await import('../src/dominio/janelaAvulsa.js')
const { avisoDeLinkVencidoApi } = await import('../src/dominio/cobranca.js')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// ---------------------------------------------------------------- janela avulsa

confere('janela com o nome da Cozinha ou das Entregas e origem aberta é avulsa', () => {
  assert.equal(ehJanelaAvulsa({ nome: JANELA_COZINHA, origemAberta: true }), true)
  assert.equal(ehJanelaAvulsa({ nome: JANELA_ENTREGAS, origemAberta: true }), true)
})

confere('sem a janela de origem é uma janela comum: tablet da cozinha, hall, Balcão já fechado', () => {
  assert.equal(ehJanelaAvulsa({ nome: JANELA_COZINHA, origemAberta: false }), false)
  assert.equal(ehJanelaAvulsa({ nome: '', origemAberta: false }), false)
})

confere('janela aberta por outro caminho (sem os nomes do Balcão) não é avulsa, mesmo com origem', () => {
  assert.equal(ehJanelaAvulsa({ nome: '', origemAberta: true }), false)
  assert.equal(ehJanelaAvulsa({ nome: 'cdb-cardapio-cliente', origemAberta: true }), false)
})

confere('a volta só navega a janela de origem quando ela saiu do Balcão', () => {
  assert.equal(origemForaDoBalcao('#/m/atendimento/balcao', { fonteApi: true }), false)
  assert.equal(origemForaDoBalcao('#/m/atendimento', { fonteApi: true }), false)
  assert.equal(origemForaDoBalcao('#/m/producao/insumos', { fonteApi: true }), true)
  assert.equal(origemForaDoBalcao('#/', { fonteApi: true }), true)
  assert.equal(origemForaDoBalcao('', { fonteApi: true }), true)
})

confere('quem abre e quem reconhece a janela usam o mesmo nome', () => {
  assert.match(fonte('app/Moldura.jsx'), /window\.open\(destino, JANELA_COZINHA,/)
  assert.match(fonte('features/entregas/GavetaEntregasApi.jsx'), /window\.open\(HASH_MODULO_ENTREGAS, JANELA_ENTREGAS,/)
  assert.match(fonte('features/entregas/GavetaEntregas.jsx'), /window\.open\('#\/entregas', JANELA_ENTREGAS,/)
  for (const arquivo of ['app/Moldura.jsx', 'features/entregas/GavetaEntregasApi.jsx', 'features/entregas/GavetaEntregas.jsx']) {
    assert.doesNotMatch(fonte(arquivo), /'cdb-(cozinha|entregas)'/, arquivo)
  }
})

confere('na janela avulsa a barra do módulo troca os links por "Voltar ao Balcão"', () => {
  const moldura = fonte('features/hall/MolduraDoModulo.jsx')
  const contexto = moldura.slice(moldura.indexOf('<div className={css.contexto}>'), moldura.indexOf('<span className={css.modulo}>'))
  const [ramoAvulsa, ramoComum] = contexto.split(') : (')
  assert.match(ramoAvulsa, /\{avulsa \? \(/)
  assert.match(ramoAvulsa, /onClick=\{\(\) => voltarAoBalcao\(fonteApi\)\}/)
  assert.match(ramoAvulsa, /Voltar ao Balcão/)
  assert.doesNotMatch(ramoAvulsa, /href=/)
  assert.match(ramoComum, /href=\{HASH_HALL\}/)
  assert.match(ramoComum, /href=\{HASH_BALCAO\}/)
})

confere('a volta traz a janela de origem e só fecha esta se o foco não saiu daqui', () => {
  const moldura = fonte('features/hall/MolduraDoModulo.jsx')
  const volta = moldura.slice(moldura.indexOf('function voltarAoBalcao('), moldura.indexOf('export function MolduraDoModulo('))
  assert.match(volta, /origem\.focus\(\)/)
  assert.match(volta, /if \(document\.hasFocus\(\)\) window\.close\(\)/)
  assert.match(volta, /if \(!origem\) \{ window\.location\.hash = HASH_BALCAO; return \}/)
})

// ---------------------------------------------------------------- anel do Pix

confere('o texto do link vencido diz o que o EasyStok faz em cada tentativa', () => {
  // #1683: o job só reemite e cancela com o pedido esperando o pagamento; o status entra na pergunta.
  assert.match(avisoDeLinkVencidoApi({ tentativa: 1 }, 'aguardando_pagamento'), /manda um link novo ao cliente sozinho/)
  assert.match(avisoDeLinkVencidoApi({}, 'aguardando_pagamento'), /manda um link novo ao cliente sozinho/)
  assert.match(avisoDeLinkVencidoApi({ tentativa: 2 }, 'aguardando_pagamento'), /cancela o pedido sozinho/)
})

confere('no modo API o anel vencido mostra esse texto e não o botão "Reenviar cobrança"', () => {
  const relogio = fonte('features/ficha-cliente/RelogioPix.jsx')
  const zerado = relogio.slice(relogio.indexOf('if (zerado) {'), relogio.indexOf('// Contagem normal'))
  assert.match(zerado, /\{fonteApi && <p className=\{css\.legenda\}>\{avisoDeLinkVencidoApi\(cobranca, statusApi\)\}<\/p>\}/)
  assert.match(zerado, /\{!fonteApi && \(\s*<Botao largo variante="primario" icone="refresh-cw" onClick=\{aoReenviar\}>Reenviar cobrança<\/Botao>\s*\)\}/)
  assert.equal(zerado.match(/Reenviar cobrança/g).length, 1)
})

console.log(`\n${passou} verificações passaram.`)
