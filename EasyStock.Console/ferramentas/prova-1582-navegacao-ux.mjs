// Prova da #1582 (banca de navegação e UX de 11/10): o shell do console.
//
// Cobre as regras puras (título da aba, releitura da matriz de acesso, textos da sessão)
// e, por leitura do fonte, os pontos de composição que só existem no JSX e no CSS: a volta
// ao hall no topo do Balcão, Ajuda e Sair em colunas próprias, o gesto da loja fora do
// Balcão e a saída da aba que ainda não grava. As medidas na tela (390, 1024 e 1366 px)
// estão na PR.
//
// Roda com: node ferramentas/prova-1582-navegacao-ux.mjs

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

const { TITULO_BASE, nomeDaTela, tituloDaAba } = await import('../src/dominio/tituloDaAba.js')
const { MODULOS, telasDoMenu, hashDoModulo } = await import('../src/dominio/modulos.js')
const { rotaDaHash, HASH_MODULO_ENTREGAS } = await import('../src/dominio/rota.js')
const { aposFalhaDeLeitura } = await import('../src/dominio/acessoModulos.js')
const { AVISO_SESSAO_VENCIDA, avisoDeVencimento } = await import('../src/dominio/sessao.js')

const fonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const rota = (hash, fonteApi = true) => rotaDaHash(hash, { fonteApi })

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// ---------------------------------------------------------------- título da aba

confere('cada tela do catálogo tem título próprio, com o mesmo nome do menu', () => {
  const titulos = new Set()
  for (const modulo of MODULOS) {
    for (const tela of telasDoMenu(modulo, { fonteApi: true })) {
      const titulo = tituloDaAba(rota(hashDoModulo(modulo.id, tela.id)))
      assert.equal(titulo, `${tela.rotulo} · ${TITULO_BASE}`)
      titulos.add(titulo)
    }
  }
  assert.equal(titulos.size, MODULOS.flatMap((m) => telasDoMenu(m, { fonteApi: true })).length)
})

confere('o hall fica só com o nome da casa e não herda contagem de outra tela', () => {
  assert.equal(tituloDaAba(rota('#/')), TITULO_BASE)
  assert.equal(tituloDaAba(rota('')), TITULO_BASE)
  assert.equal(tituloDaAba(rota('#/qualquer-coisa')), TITULO_BASE)
})

confere('o Balcão leva a contagem na frente e some com ela quando zera', () => {
  const balcao = rota('#/m/atendimento/balcao')
  assert.equal(tituloDaAba(balcao, 3), `(3) Balcão · ${TITULO_BASE}`)
  assert.equal(tituloDaAba(balcao, 0), `Balcão · ${TITULO_BASE}`)
  assert.equal(tituloDaAba(balcao), `Balcão · ${TITULO_BASE}`)
})

confere('janela avulsa pelo apelido também diz o que é', () => {
  assert.equal(tituloDaAba(rota('#/cozinha', false)), `Cozinha · ${TITULO_BASE}`)
  assert.equal(tituloDaAba(rota('#/entregas', false)), `Entregas · ${TITULO_BASE}`)
  assert.equal(nomeDaTela(rota('#/cardapio-link/abc', false)), 'Cardápio')
})

confere('Cozinha e Balcão em janelas separadas não têm o mesmo título', () => {
  assert.notEqual(tituloDaAba(rota('#/m/cozinha')), tituloDaAba(rota('#/m/atendimento'), 2))
  assert.equal(tituloDaAba(rota('#/m/cozinha')), `Fila de preparo · ${TITULO_BASE}`)
  assert.equal(tituloDaAba(rota(HASH_MODULO_ENTREGAS)), `Painel de entregas · ${TITULO_BASE}`)
})

confere('quem escreve o título: o App pela rota e o Balcão com a contagem', () => {
  const app = fonte('app/App.jsx')
  assert.match(app, /useTituloDaAba\(tituloDaAba\(rota, contarPrecisaDeVoce\(/)
  assert.match(app, /rota\.tipo === ROTA_PRINCIPAL \? null : tituloDaAba\(rota\)/)
  assert.equal(fonte('aplicacao/useTituloDaAba.js').match(/document\.title =/g).length, 1)
})

// ---------------------------------------------------------------- volta ao hall

confere('o topo do Balcão tem link para os módulos (tablet de 1024 a 1179 px, sem Mais e sem trilho)', () => {
  const moldura = fonte('app/Moldura.jsx')
  const cabecalho = moldura.slice(moldura.indexOf('function Cabecalho('), moldura.indexOf('function Trilho('))
  assert.match(cabecalho, /<a className=\{css\.marca\} href=\{HASH_HALL\} aria-label="Ir para os módulos"/)
  assert.match(cabecalho, /<h1 className="sr">Casa da Baba<\/h1>/)
})

// ---------------------------------------------------------------- Ajuda e Sair no celular

for (const [arquivo, grade] of [['features/hall/hall.module.css', '.topo'], ['features/hall/moldura.module.css', '.barra']]) {
  confere(`${arquivo}: Ajuda e Sair em colunas próprias no celular`, () => {
    const css = fonte(arquivo)
    const celular = css.slice(css.indexOf('@media (max-width: 640px)'))
    assert.match(celular, new RegExp(`\\${grade} \\{ display: grid; grid-template-columns: minmax\\(0, 1fr\\) auto auto;`))
    assert.match(celular, /\[data-ajuda-console\]\) \{ grid-row: 1; grid-column: 2; \}/)
    assert.match(celular, /\.sair \{ grid-row: 1; grid-column: 3;/)
    // O deslocamento fixo era o defeito: supunha um Ajuda de 84 px, e ele mede 104 px.
    assert.doesNotMatch(css, /\.sair \{[^}]*margin-left/)
  })
}

// ---------------------------------------------------------------- loja fora do Balcão

confere('o gesto de abrir e fechar a loja também é montado nas telas de módulo', () => {
  const app = fonte('app/App.jsx')
  const telaDaRota = app.slice(app.indexOf('function TelaDaRota('), app.indexOf('function RestaurarConversa('))
  assert.match(telaDaRota, /\{FONTE_API && <GestoLoja \/>\}/)
})

// ---------------------------------------------------------------- matriz de acesso

confere('releitura da matriz que falha mantém a matriz já carregada', () => {
  const carregada = { dados: { modulos: [{ id: 'cozinha', liberado: true }] }, erro: null }
  assert.equal(aposFalhaDeLeitura(carregada, 'O EasyStok demorou a responder.'), carregada)
})

confere('primeira carga da matriz que falha mostra o erro', () => {
  assert.deepEqual(aposFalhaDeLeitura({ dados: null, erro: null }, 'Sem conexão com o EasyStok.'), { dados: null, erro: 'Sem conexão com o EasyStok.' })
})

// ---------------------------------------------------------------- sessão

confere('o aviso de sessão perto de vencer não manda sair (Sair apaga o rascunho)', () => {
  const agora = Date.UTC(2026, 9, 11, 12, 0, 0)
  const aviso = avisoDeVencimento(agora + 5 * 60000, agora)
  assert.match(aviso, /^Sessão vence em 5 min\./)
  assert.match(aviso, /Quando vencer, entre de novo: o rascunho fica guardado\./)
  assert.equal(avisoDeVencimento(agora + 11 * 60000, agora), null)
})

confere('o login explica quando a sessão venceu sozinha', () => {
  assert.match(AVISO_SESSAO_VENCIDA, /sessão venceu/)
  const sessao = fonte('aplicacao/useSessaoApi.js')
  assert.match(sessao, /const expirou = \(\) => \{ setAvisoSaida\(AVISO_SESSAO_VENCIDA\); setSessao\(null\) \}/)
  assert.match(sessao, /if \(havia\) setAvisoSaida\(AVISO_SESSAO_VENCIDA\)/)
})

// ---------------------------------------------------------------- telas sem saída

confere('a aba de produção em preparação aponta um próximo passo real conforme o acesso', () => {
  const gestao = fonte('features/gestao/ModalGestao.jsx')
  assert.match(gestao, /saida: \{ modulo: 'producao', telas: \['producao'\] \}/)
  const producao = MODULOS.find((m) => m.id === 'producao')
  assert.ok(producao.telas.some((tela) => tela.id === 'producao'), 'destino Produção do dia existe no catálogo')
  assert.match(gestao, /permite\(saida\.modulo\)/)
})

confere('link para o Balcão dentro da Gestão fecha a janela em vez de ficar morto', () => {
  const gestao = fonte('features/gestao/ModalGestao.jsx')
  assert.match(gestao, /if \(destino\.tipo === ROTA_PRINCIPAL\) \{\s+evento\.preventDefault\(\)\s+aoFechar\(\)/)
})

confere('Entregas em outra janela abre a rota do módulo e não some com popup bloqueado', () => {
  const gaveta = fonte('features/entregas/GavetaEntregasApi.jsx')
  // #1616: o nome da janela virou constante (`JANELA_ENTREGAS`); a prova da #1616 confere o valor.
  assert.match(gaveta, /window\.open\(HASH_MODULO_ENTREGAS, JANELA_ENTREGAS,/)
  assert.match(gaveta, /if \(!janela\) window\.location\.hash = HASH_MODULO_ENTREGAS/)
  assert.equal(rota(HASH_MODULO_ENTREGAS).tela, 'painel')
})

console.log(`\n${passou} verificações passaram.`)
