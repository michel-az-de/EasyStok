/* eslint-disable no-console */
// Prova da issue #2232 (banca UX/UI do Atendimento, topo, abas inferiores e menu Mais no celular).
// Uma verificação por achado da spec `docs/plan/banca-ux-2232/README.md`: L1, L2, L3, L4, L11, X3 e
// M1 a M4. Regras puras (`app/rotulosDoTopo.js`, `app/menuMais.js`) e leitura do fonte e do CSS onde só
// existe composição. A medida na tela (390 e 558 px) fica nos roteiros de captura da banca visual.
//
//   node ferramentas/prova-2232-topo.mjs

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
const tentar = async (caminho) => { try { return await import(caminho) } catch { return {} } }
const lerOuVazio = (caminho) => { try { return fonte(caminho) } catch { return '' } }

const rotulos = await tentar('../src/app/rotulosDoTopo.js')
const menu = await tentar('../src/app/menuMais.js')
const { FUNCIONAMENTO_PADRAO } = await import('../src/dominio/funcionamento.js')

const moldura = fonte('app/Moldura.jsx')
const molduraCss = fonte('app/moldura.module.css')
const abasJsx = fonte('componentes/BarraDeAbas.jsx')
const abasCss = fonte('componentes/BarraDeAbas.module.css')
const popoverJsx = fonte('componentes/Popover.jsx')
const popoverCss = fonte('componentes/Popover.module.css')
const balaoJsx = fonte('features/assistente/BalaoAssistente.jsx')
const balaoCss = fonte('features/assistente/assistente.module.css')
// #2185 (R04): o contador único do app é a `Pilula contagem`; o `Contador` próprio da #2232 foi
// fundido nela para não haver dois desenhos do mesmo número.
const pilulaJsx = fonte('componentes/Pilula.jsx')
const pilulaCss = fonte('componentes/Pilula.module.css')
const contadorProprio = lerOuVazio('componentes/Contador.jsx')

const regraDe = (css, seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...css.matchAll(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`, 'g'))].map((m) => m[1]).join(' ')
}

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${String(erro.message).split('\n')[0]}`) }
}

// Relógio da loja: 04/11/2026 15:00 em Brasília (dentro das 8h às 22h) e 23:30 (fora).
const DENTRO = Date.parse('2026-11-04T18:00:00Z')
const FORA = Date.parse('2026-11-05T02:30:00Z')

console.log('Topo, abas e menu Mais (#2232)')

confere('L1  fechar a loja pede confirmação; abrir não; dentro do horário a justificativa já é o diálogo', () => {
  const pede = rotulos.fechamentoPedeConfirmacao
  assert.equal(typeof pede, 'function', 'falta fechamentoPedeConfirmacao')
  const base = { agora: DENTRO, funcionamento: FUNCIONAMENTO_PADRAO }
  assert.equal(pede({ ...base, aberta: false, fonteApi: false }), false, 'abrir não pede')
  assert.equal(pede({ ...base, aberta: true, fonteApi: false }), true, 'demonstração: fechar pede')
  assert.equal(pede({ ...base, aberta: true, fonteApi: true }), false, 'API dentro do horário: o gesto com justificativa já pede')
  assert.equal(pede({ agora: FORA, funcionamento: FUNCIONAMENTO_PADRAO, aberta: true, fonteApi: true }), true, 'API fora do horário, aberta na mão: fecha direto hoje, agora confirma')
  assert.match(moldura, /Fechar loja/)
  assert.match(moldura, /Cancelar/)
  assert.match(moldura, /fechamentoPedeConfirmacao\(/)
})
confere('L1  o controle diz "Loja aberta" / "Loja fechada" por escrito, com ponto de estado, também no celular', () => {
  const controle = moldura.slice(moldura.indexOf('function ControleLoja'), moldura.indexOf('const MODULO_ATENDIMENTO'))
  assert.match(controle, /Loja/)
  assert.match(controle, /aberta/)
  assert.match(controle, /fechada/)
  assert.ok(!/'Aberta'|'Fechada'/.test(controle), 'a palavra solta "Aberta"/"Fechada" sem a loja continua')
  assert.match(controle, /css\.lojaPonto/, 'falta o ponto de estado')
  assert.ok(!/lojaTextoCurto|lojaTextoLongo/.test(molduraCss + moldura), 'o texto curto sem contexto do celular continua')
})

// #2246: o contador passou de quadrado a pílula, com os cantos arredondados da banca.
confere('L2  um estilo só de contador: Pilula contagem em pílula, neutro ou de perigo, usado no topo e nas abas', () => {
  assert.equal(contadorProprio, '', 'voltou um componente Contador paralelo à Pilula contagem')
  assert.match(pilulaJsx, /contagem/)
  assert.match(pilulaJsx, /perigo/)
  const raios = [...regraDe(pilulaCss, '.contagem').matchAll(/border-radius:\s*([^;}]+)/g)].map((m) => m[1].trim())
  assert.deepEqual(raios, ['var(--r-pilula)'], 'o contador tem um raio só, o da pílula')
  assert.match(moldura, /<Pilula contagem=/)
  assert.match(abasJsx, /<Pilula[^>]*contagem=/)
  assert.ok(!/\.conta\b[^{]*\{[^}]*background/.test(molduraCss), 'o topo ainda desenha o próprio contador')
  assert.ok(!/\.contador\s*\{[^}]*background/.test(abasCss), 'as abas ainda desenham o próprio contador')
})
confere('L2  o nome acessível leva o número: "Lembretes, 3", "Avisos, 2 não lidas", "Cozinha"', () => {
  assert.equal(rotulos.nomeComContador?.('Lembretes', 3), 'Lembretes, 3')
  assert.equal(rotulos.nomeComContador?.('Lembretes', 0), 'Lembretes')
  assert.equal(rotulos.nomeComContador?.('Avisos', 2, 'não lidas'), 'Avisos, 2 não lidas')
  // Sininho e SinoDaSessao já nomeiam com o número; só o desenho do contador mora lá.
  assert.match(fonte('features/lembretes/Sininho.jsx'), /Lembretes, \$\{contagem\}/)
  assert.match(fonte('features/notificacoes/SinoDaSessao.jsx'), /Avisos\$\{.*não lidas/)
})
confere('L2  Cozinha, que no celular era só ícone, vira item com nome no menu Mais', () => {
  const grupos = menu.gruposDoMenuMais?.({ tema: 'light', permite: () => true, celular: true })
  const itens = (grupos ?? []).flatMap((g) => g.itens)
  assert.ok(itens.some((i) => i.chave === '__cozinha' && i.titulo === 'Cozinha'), 'falta Cozinha no menu')
  assert.match(moldura, /!celular && permite\('cozinha'\)|celular \? null/, 'o botão só de ícone da Cozinha continua no topo do celular')
})

confere('L3  "Auto" diz o estado por escrito: ligadas ou desligadas; sem regras (API), sem estado nem número', () => {
  assert.equal(rotulos.estadoDasAutomaticas?.([{ ativa: true }, { ativa: false }]), 'ligadas')
  assert.equal(rotulos.estadoDasAutomaticas?.([{ ativa: false }]), 'desligadas')
  assert.equal(rotulos.estadoDasAutomaticas?.(null), null)
  assert.match(moldura, /estadoDasAutomaticas\(/)
  assert.match(moldura, /rotuloCurto[^>]*>Auto</)
})
confere('L3  nome acessível: "Respostas automáticas, N ligadas"', () => {
  assert.equal(rotulos.nomeDasAutomaticas?.([{ ativa: true }, { ativa: true }, { ativa: false }]), 'Respostas automáticas, 2 ligadas')
  assert.equal(rotulos.nomeDasAutomaticas?.([{ ativa: false }]), 'Respostas automáticas, 0 ligadas')
  assert.equal(rotulos.nomeDasAutomaticas?.(null), 'Respostas automáticas')
  assert.match(moldura, /nomeDasAutomaticas\(/)
  assert.ok(!/Automáticas, \$\{/.test(moldura), 'o nome antigo "Automáticas, N ligadas" continua')
})

confere('L4  "Abrir assistente" usa ícone de assistente com rótulo, não a ilustração de uma pessoa', () => {
  const gatilho = balaoJsx.slice(balaoJsx.indexOf('const gatilho = ('), balaoJsx.indexOf('return (\n    <>\n      {cabecalhoDoAtendimento ?'))
  assert.ok(gatilho.length > 100, 'gatilho não encontrado')
  assert.ok(!/AvatarAssistente/.test(gatilho), 'o gatilho ainda usa a ilustração de pessoa')
  assert.match(gatilho, /<Icone nome="agente"/)
  assert.match(gatilho, /rotuloGatilho/)
  assert.ok(!/min-width:\s*1180px\)\s*and\s*\(pointer:\s*fine\)\s*\{\s*\.gatilhoNoCabecalho/.test(balaoCss), 'o rótulo só aparece em desktop com mouse')
})

confere('L11 a aba Conversas mostra só o que pede ação; sem pendência, sem número', () => {
  const abas = rotulos.abasDoCelular
  assert.equal(typeof abas, 'function', 'falta abasDoCelular')
  assert.equal(abas(11)[0].contador, 11)
  assert.equal(abas(0)[0].contador, undefined)
  assert.ok(!/contador:\s*visiveis\.length/.test(moldura), 'a aba ainda mostra o total de conversas visíveis')
  assert.match(moldura, /contarPrecisaDeVoce\(/)
  assert.match(moldura, /from '\.\.\/dominio\/automatico'/)
  assert.ok(!/features\/caixa-de-entrada\/BarraBalcao/.test(moldura))
})

confere('X3  abas "Conversas · Mensagens · Cliente" com os mesmos ids, e nome acessível com o número', () => {
  const abas = rotulos.abasDoCelular?.(11) ?? []
  assert.deepEqual(abas.map((a) => a.rotulo), ['Conversas', 'Mensagens', 'Cliente'])
  assert.deepEqual(abas.map((a) => a.id), ['balcao', 'atendimento', 'ficha'])
  assert.equal(abas[0].nome, 'Conversas, 11 precisam de você')
  assert.equal(rotulos.abasDoCelular?.(1)[0].nome, 'Conversas, 1 precisa de você')
  assert.equal(rotulos.abasDoCelular?.(0)[0].nome, 'Conversas')
  assert.match(abasJsx, /aria-label=\{aba\.nome\}/)
})

confere('M1  o tema do menu Mais marca a opção ativa (menuitemradio, aria-checked, marca visível)', () => {
  const grupos = menu.gruposDoMenuMais?.({ tema: 'dark', permite: () => true }) ?? []
  const tema = grupos.find((g) => g.titulo === 'Tema')
  assert.deepEqual(tema?.itens.map((i) => [i.chave, i.marcado]), [['__claro', false], ['__escuro', true]])
  assert.match(popoverJsx, /menuitemradio/)
  assert.match(popoverJsx, /aria-checked=/)
  assert.match(popoverJsx, /item\.marcado/)
  assert.match(moldura, /gruposDoMenuMais\(\{[^}]*tema/, 'a moldura não passa o tema ao menu')
})

confere('M2  Módulos e Entregas abrem o menu como navegação, antes e separados das preferências', () => {
  const grupos = menu.gruposDoMenuMais?.({ tema: 'light', permite: () => true }) ?? []
  assert.equal(grupos[0]?.titulo, 'Ir para')
  const chaves = grupos[0]?.itens.map((i) => i.chave) ?? []
  assert.ok(chaves.includes('__modulos') && chaves.includes('__entregas'), `navegação sem Módulos e Entregas: ${chaves}`)
  assert.ok(chaves.indexOf('__modulos') < chaves.indexOf('__entregas'))
  const posTema = grupos.findIndex((g) => g.titulo === 'Tema')
  assert.ok(posTema > 0, 'o tema precisa vir depois da navegação')
  assert.ok(!(grupos[posTema].itens.some((i) => ['__modulos', '__entregas', '__gestao'].includes(i.chave))), 'preferência misturada com navegação')
  assert.deepEqual((menu.gruposDoMenuMais?.({ tema: 'light', permite: (m) => m !== 'entregas' }) ?? [])[0].itens.map((i) => i.chave).includes('__entregas'), false)
})

confere('M3  "Hoje · N em andamento · N na esteira" sai do menu', () => {
  const grupos = menu.gruposDoMenuMais?.({ tema: 'light', permite: () => true, aoSair: () => {} }) ?? []
  assert.ok(grupos.length > 0, 'menu vazio')
  assert.ok(!grupos.some((g) => g.titulo === 'Hoje' || g.itens.some((i) => i.informativo)), 'o resumo do dia continua no menu')
  assert.ok(!/grupoDoDia|na esteira|em andamento/.test(moldura), 'o resumo do dia continua na moldura')
})

confere('M4  no celular o menu abre como folha inferior, sem rolagem interna para o conteúdo atual', () => {
  // #2185: o Mais do topo é o MenuDeAcoes com grupos, que abre o Popover como folha.
  assert.match(moldura, /<MenuDeAcoes[^>]*\bgrupos=/)
  assert.match(fonte('componentes/MenuDeAcoes.jsx'), /<Popover[^>]*\bfolha=\{Boolean\(grupos\)\}/)
  assert.match(popoverJsx, /css\.folha/)
  const folha = regraDe(popoverCss, '.folha')
  assert.match(folha, /bottom:\s*0/)
  assert.match(folha, /left:\s*0/)
  assert.match(folha, /right:\s*0/)
  // Conta do pior caso: 48 px por linha, 32 px por título de grupo e 16 px de recuo; cabe em 667 de altura.
  const grupos = menu.gruposDoMenuMais?.({ tema: 'light', permite: () => true, aoSair: () => {}, celular: true, atendente: 'Thatiane', casa: 'Casa da Baba' }) ?? []
  const altura = menu.alturaDaFolha?.(grupos)
  assert.ok(Number.isFinite(altura), 'falta alturaDaFolha')
  assert.ok(altura <= 667 - 96, `folha de ${altura} px não cabe em 667 px com a margem do topo`)
  assert.ok(!grupos.flatMap((g) => g.itens).some((i) => (i.detalhe ?? '').length > 60), 'detalhe longo empurra a folha para a rolagem')
})

console.log(`\n${passou} verificações passaram${falhas.length ? `, ${falhas.length} falharam` : ''}.`)
if (falhas.length) {
  falhas.forEach((f) => console.log('  - ' + f))
  process.exit(1)
}
