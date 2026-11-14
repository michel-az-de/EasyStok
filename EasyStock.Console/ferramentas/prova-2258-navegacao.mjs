// Prova da #2258: navegação, cabeçalho, Ajuda, Avisos e nomes (auditoria de UX/UI da Casa da Baba, 05/11).
//
// Cobre: o nome "Casa da Baba" na aba e na marca (H9); "Respostas automáticas" no lugar de "Automáticas"
// solto (A4, H7, T9); Início no lugar de "Módulos"/"Ver módulos" (A3, T9); Ajuda conforme a tela aberta, sem
// o link para a própria home (A3); ícones distintos entre módulos e entre as telas de Configurações e do
// Financeiro (A1, G2, F7); ordem única dos módulos no Menu (A2); Tema e Sair só atrás do avatar (A9, G4);
// o botão Menu só onde o menu lateral some (G3, F6); módulo de uma tela só sem menu lateral (K2); avisos
// do aparelho com legenda e botões com cara de botão (A8, H11, A12); o título da loja sem prometer fechar
// com um toque (T10). O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-navegacao.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2258-navegacao.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const ler = (caminho) => readFileSync(new URL(caminho, base), 'utf8').replace(/\r\n/g, '\n')
const importar = (caminho) => import(new URL(caminho, base).href)
const entre = (texto, de, ate) => {
  const inicio = texto.indexOf(de)
  assert.notEqual(inicio, -1, `trecho "${de}" não encontrado`)
  const fim = texto.indexOf(ate, inicio + de.length)
  return texto.slice(inicio, fim === -1 ? undefined : fim)
}
// Tira o MenuDaConta (autofechado ou com filhos): o que está atrás do avatar não conta como solto.
const semAvatar = (texto) => texto.replace(/<MenuDaConta[^>]*\/>/g, '').replace(/<MenuDaConta[\s\S]*?<\/MenuDaConta>/g, '')

let ok = 0
const falhas = []
const caso = async (nome, fn) => {
  try { await fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${erro.message.split('\n')[0]}`) }
}

await caso('H9: a aba, a marca, o login e o convite dizem "Casa da Baba"', async () => {
  const { tituloDaAba } = await importar('dominio/tituloDaAba.js')
  assert.match(tituloDaAba({ tipo: 'hall' }), /Casa da Baba$/)
  assert.match(readFileSync(new URL('../index.html', base), 'utf8'), /<title>Casa da Baba<\/title>/)
  for (const arquivo of ['componentes/Marca.jsx', 'features/login/TelaLogin.jsx', 'features/login/TelaConvite.jsx', 'app/Moldura.jsx']) {
    assert.doesNotMatch(ler(arquivo), /Casa da Baba/, `${arquivo} ainda escreve "Baba"`)
  }
})

await caso('A4/H7/T9: nenhum "Automáticas" solto nos arquivos da moldura, do menu e da aba de respostas', () => {
  const solto = />\s*Automáticas\s*<|['"`]Automáticas['"`]|\{\s*['"]Automáticas['"]\s*\}/
  for (const arquivo of ['app/Moldura.jsx', 'features/hall/MenuDosModulos.jsx', 'features/gestao/respostas/AbaRespostas.jsx', 'app/menuMais.js']) {
    assert.doesNotMatch(ler(arquivo), solto, `${arquivo} tem "Automáticas" sozinho`)
  }
  assert.doesNotMatch(ler('features/gestao/respostas/AbaRespostas.jsx'), /<h3[^>]*>Mensagens automáticas<\/h3>/)
  assert.match(ler('features/hall/MenuDosModulos.jsx'), /NOME_RESPOSTAS_AUTOMATICAS/)
  assert.match(ler('app/Moldura.jsx'), /NOME_RESPOSTAS_AUTOMATICAS/)
})

await caso('T9/A3: "Início" no lugar de "Módulos" e "Ver módulos"; "Ajuda" no lugar de "Ajuda do sistema"', async () => {
  const { gruposDoMenuMais } = await importar('app/menuMais.js')
  const titulos = gruposDoMenuMais({ tema: 'light' })[0].itens.map((item) => item.titulo)
  assert.ok(titulos.includes('Início'), `grupo "Ir para": ${titulos.join(', ')}`)
  assert.ok(titulos.includes('Ajuda') && !titulos.includes('Ajuda do sistema') && !titulos.includes('Módulos'))
  for (const arquivo of ['features/hall/MolduraDoModulo.jsx', 'features/hall/MenuDosModulos.jsx', 'features/gestao/ModalGestao.jsx', 'features/ajuda/AjudaDoConsole.jsx']) {
    assert.doesNotMatch(ler(arquivo), /Ver módulos|<span>Módulos<\/span>|Voltar aos módulos|Guia do sistema/, `${arquivo} mantém o nome antigo`)
  }
  assert.doesNotMatch(ler('app/Moldura.jsx'), /Ajuda do sistema|<span>Módulos<\/span>/)
})

await caso('A3: a Ajuda da home não manda "voltar" e não tem link para a própria home', async () => {
  const { contextoDaAjuda } = await importar('features/ajuda/diagnosticoDoConsole.js')
  const home = contextoDaAjuda({ tipo: 'hall' }, false, { permite: () => true })
  assert.ok(home.passos.length > 0)
  assert.ok(!home.passos.some((passo) => /voltar ao atendimento/i.test(passo)), home.passos.join(' | '))
  const ajuda = ler('features/ajuda/AjudaDoConsole.jsx')
  assert.match(ajuda, /rota\.tipo !== ROTA_HALL && <a href=\{HASH_HALL\}/)
})

await caso('A3: a Ajuda de uma tela de módulo continua falando daquela tela', async () => {
  const { contextoDaAjuda } = await importar('features/ajuda/diagnosticoDoConsole.js')
  const financeiro = contextoDaAjuda({ tipo: 'modulo', modulo: 'financeiro', tela: 'caixa' }, false, { permite: () => true })
  assert.match(financeiro.titulo, /^Financeiro/)
  assert.notDeepEqual(financeiro.passos, contextoDaAjuda({ tipo: 'hall' }, false, { permite: () => true }).passos)
})

await caso('A1/G2/F7: o mapa único dá ícone distinto a cada módulo e a cada tela de Configurações e do Financeiro', async () => {
  const { iconeDoModulo, iconeDaTela } = await importar('componentes/iconesDoConsole.js')
  const { MODULOS } = await importar('dominio/modulos.js')
  const dosModulos = MODULOS.map((modulo) => iconeDoModulo(modulo.id))
  assert.ok(dosModulos.every(Boolean), `módulo sem ícone: ${dosModulos}`)
  assert.equal(new Set(dosModulos).size, MODULOS.length, 'dois módulos dividem o mesmo ícone')
  for (const id of ['configuracoes', 'financeiro']) {
    const telas = MODULOS.find((modulo) => modulo.id === id).telas
    const icones = telas.map((tela) => iconeDaTela(id, tela.id))
    assert.equal(new Set(icones).size, telas.length, `${id}: ${icones.join(', ')}`)
  }
})

await caso('A1/G2: o menu e a moldura desenham pelo mapa único, sem ícone próprio de Cozinha e de respostas', () => {
  const menu = ler('features/hall/MenuDosModulos.jsx')
  assert.match(menu, /IconeDoModulo/)
  assert.doesNotMatch(menu, /nome="raio"/)
  const moldura = ler('app/Moldura.jsx')
  assert.doesNotMatch(moldura, /cooking-pot/)
  assert.doesNotMatch(moldura, /icone="raio"|nome="raio"/)
})

await caso('A2: o menu lista os módulos na ordem única do Início, da Ajuda e da Busca', async () => {
  const { modulosDoHall, ORDEM_DOS_MODULOS } = await importar('dominio/modulos.js')
  const ids = modulosDoHall({ fonteApi: true }).map((modulo) => modulo.id)
  assert.deepEqual(ids, ORDEM_DOS_MODULOS.filter((id) => ids.includes(id)))
  assert.match(ler('features/hall/MenuDosModulos.jsx'), /emOrdemDosModulos\(MODULOS\)/)
})

await caso('A9/G4: Tema e Sair só atrás do avatar, nas barras dos módulos e do Balcão', () => {
  const barra = entre(ler('features/hall/MolduraDoModulo.jsx'), '<header className={css.barra}>', '</header>')
  assert.match(barra, /<MenuDaConta/)
  const solto = semAvatar(barra)
  assert.doesNotMatch(solto, /<InterruptorTema/, 'tema solto na barra do módulo')
  assert.doesNotMatch(solto, /css\.sair|>\s*\{?sessaoPersistente \? 'Sair|>Sair</, 'Sair solto na barra do módulo')
  const cabecalho = entre(ler('app/Moldura.jsx'), 'function Cabecalho(', 'function DisposicaoConversas')
  assert.doesNotMatch(semAvatar(cabecalho), /<InterruptorTema/, 'tema solto no topo do Balcão')
  assert.match(ler('features/hall/MenuDosModulos.jsx'), /<MenuDaConta[\s\S]*<InterruptorTema \/>[\s\S]*<\/MenuDaConta>/)
})

await caso('G3/F6: o botão Menu só aparece onde o menu lateral some (até 759 px)', () => {
  const css = ler('features/hall/moldura.module.css')
  const faixa1100 = entre(css, '@media (max-width: 1100px) {', '@media (max-width: 759px)')
  assert.doesNotMatch(faixa1100, /\.abrirMenu/, 'o Menu ainda abre um modal que repete o menu lateral visível')
  assert.match(entre(css, '@media (max-width: 759px) {', '\n}'), /\.abrirMenu \{ display: inline-flex/)
})

await caso('K2: módulo com uma tela só no menu filtrado não tem menu lateral', () => {
  const fonte = ler('features/hall/MolduraDoModulo.jsx')
  assert.match(fonte, /semMenu = telasNaBarra \|\| telas\.length <= 1/)
  assert.doesNotMatch(fonte, /semMenu=\{telasNaBarra\}/)
})

await caso('A8/H11/A12: botões do sino com cara de botão e ícone; aparelho desligado com legenda e "Ativar" antes da lista', () => {
  const sino = ler('features/notificacoes/SinoDaSessao.jsx')
  assert.doesNotMatch(sino, /variante="texto" onClick=\{\(evento\)/, '"Marcar como lido" ainda é botão de texto')
  assert.match(sino, /<Botao icone="check"/)
  assert.match(sino, /<Botao icone="bell" onClick=\{aparelho\.ativar\}/)
  assert.match(sino, /data-aparelho-desligado[^>]*title="Avisos desligados neste aparelho"/)
  assert.ok(sino.indexOf('{semAvisoNoAparelho && secaoDoAparelho}') < sino.indexOf('<ul className={css.lista}'))
})

await caso('T9: o relógio do topo largo tem o rótulo "Lembretes" e o botão de Ajuda segue visível', () => {
  const cabecalho = entre(ler('app/Moldura.jsx'), 'function Cabecalho(', 'function DisposicaoConversas')
  assert.match(cabecalho, /<Sininho comRotulo=\{!compacto\} \/>/)
  assert.match(cabecalho, /icone="ajuda"[^>]*onClick=\{aoAbrirAjuda\}>Ajuda</)
})

await caso('T10: o título do controle da loja não promete fechar com um toque', () => {
  const moldura = ler('app/Moldura.jsx')
  assert.doesNotMatch(moldura, /toque para fechar/)
  assert.match(moldura, /Fechar a loja \(pede confirmação\)/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exitCode = 1
