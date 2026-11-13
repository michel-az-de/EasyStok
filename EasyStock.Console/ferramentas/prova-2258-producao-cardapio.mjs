// Prova da #2258: achados da auditoria de UX de 05/11 em Produção, Cardápio, Cozinha, Entregas e Canais.
//
// Cobre as funções puras (lote vencido fora do saldo, filtro "Com pendência" e o link da home,
// unidade e categoria na grafia de exibição, motivo do "Salvar contagem" parado, hora HH:MM) e as
// regras de texto de JSX e CSS que a auditoria apontou. O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-producao-cardapio.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2258-producao-cardapio.mjs

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
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    return proximo(url, contexto)
  },
})

const base = process.env.PROVA_SRC ? pathToFileURL(process.env.PROVA_SRC.replace(/[\\/]?$/, '/')) : new URL('../src/', import.meta.url)
const ler = (caminho) => readFileSync(new URL(caminho, base), 'utf8')
// Corpo da regra CSS que começa na linha (não pega o mesmo seletor dentro de outro).
const regra = (texto, seletor) => {
  const inicio = texto.search(new RegExp(`(^|\\n)\\s*${seletor.replace(/[.*+?^${}()|[\]\\>]/g, '\\$&')} \\{`))
  assert.notEqual(inicio, -1, `regra ${seletor} não encontrada`)
  return texto.slice(inicio, texto.indexOf('}', inicio))
}
let ok = 0
let falhas = 0
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (e) { falhas++; console.log(`FALHA  ${nome}: ${String(e.message).split(/\r?\n/)[0]}`) }
}

const estoque = await import(new URL('aplicacao/estoqueDoDia.js', base).href)
const filtros = await import(new URL('dominio/filtroDoCardapio.js', base).href)
const insumos = await import(new URL('aplicacao/insumos.js', base).href)
const { horaCurta } = await import(new URL('dominio/formato.js', base).href)
const { resumoDoWhatsApp } = await import(new URL('dominio/canais.js', base).href)

const cardapioJsx = ler('features/gestao/cardapio/AbaCardapioApi.jsx')
const cardapioCss = ler('features/gestao/cardapio/gestaoCardapio.module.css')
const abaCss = ler('features/gestao/cardapio/abaCardapio.module.css')
const insumosJsx = ler('features/gestao/producao/AbaInsumosApi.jsx')
const estoqueJsx = ler('features/gestao/producao/AbaEstoqueDoDiaApi.jsx')

// ---- Produção ---------------------------------------------------------------------------------

caso('P2: lote vencido diz que está fora do saldo', () => {
  assert.equal(estoque.quandoVence({ validade: '2026-10-17', vencido: true }), 'Venceu em 17/10 · fora do saldo')
  assert.equal(estoque.quandoVence({ validade: '2026-11-06', diasParaVencer: 1 }), 'Vence amanhã')
  assert.equal(estoque.quandoVence({ validade: '2026-11-20', diasParaVencer: 15 }), 'Vence em 20/11')
})

caso('P3: os três números do Estoque têm o mesmo fundo de cartão; o tom do estado continua por cima', () => {
  assert.match(regra(abaCss, '.resumoOperacao > div'), /background:\s*var\(--preenchimento\)/)
  assert.match(regra(abaCss, '.resumoOperacao > .resumoPerigo'), /background:\s*var\(--erro-pastel\)/)
  assert.match(regra(abaCss, '.resumoOperacao > .resumoAtencao'), /background:\s*var\(--alerta-pastel\)/)
})

caso('P4: "Ajustar" fica na linha do prato (os estados encolhem em vez de empurrar o botão)', () => {
  assert.match(regra(abaCss, '.estados'), /flex:\s*1 1 \d/)
})

caso('P5: Cadastrar insumo tem rótulos alinhados, "Salvar insumo" à direita e Cancelar à esquerda', () => {
  assert.match(regra(abaCss, '.cadastroInsumo'), /align-items:\s*start/)
  const cadastro = insumosJsx.slice(insumosJsx.indexOf('<Modal titulo="Cadastrar insumo"'))
  const rodape = cadastro.slice(cadastro.indexOf('<div className={css.rodapeForm}>'))
  assert.ok(rodape.indexOf('Cancelar') !== -1 && rodape.indexOf('Cancelar') < rodape.indexOf('Salvar insumo'), 'Cancelar vem antes de Salvar insumo')
  assert.match(rodape, /<Botao tipo="submit" variante="primario"[^>]*>\{criando \? 'Cadastrando…' : 'Salvar insumo'\}/)
  assert.doesNotMatch(rodape, /É embalagem/, 'a chave "É embalagem" sai do rodapé')
})

caso('P6: o cadastro de insumo valida no clique e mostra o erro sob o campo', () => {
  assert.doesNotMatch(insumosJsx, /disabled=\{criando \|\| !novo\.nome\.trim\(\)\}/)
  // #2363: o nome do insumo é um campo comum (as sugestões da Wikipédia saíram).
  assert.match(insumosJsx, /<CampoTexto ref=\{nomeRef\} erro=\{erroDoNome\}[^>]*rotulo="Nome do insumo"/)
  assert.match(insumosJsx, /Informe o nome do insumo/)
})

caso('P6: "Salvar contagem" parado diz por quê', () => {
  const motivo = estoque.motivoDaContagem
  assert.equal(motivo({ contagem: '', motivo: 'x' }), 'Informe quantas porções você contou.')
  assert.equal(motivo({ contagem: '3', motivo: ' ' }), 'Informe o motivo do ajuste.')
  assert.equal(motivo({ porPorcao: true, contado: false, contagem: '3', motivo: 'x' }), 'Escolha a porção que você contou.')
  assert.equal(motivo({ contagem: '3', motivo: 'x' }), null)
  assert.equal(motivo({ salvando: true, contagem: '', motivo: '' }), null, 'salvando não é falta de dado')
  assert.match(estoqueJsx, /<Botao tipo="submit"[^>]*motivo=\{parado/)
  assert.match(estoqueJsx, /parado = motivoDaContagem\(/)
})

caso('P6: nenhum override local em Produção/Cardápio/Entregas esconde o desabilitado de um Botao', () => {
  // Verificado à mão: as regras :disabled restantes são de botões nativos (.tocar, .menos, .linha, .linhaJanela,
  // .enviarFoto, .botaoPorcao, .saldo button) que escurecem o estado; as de `background: transparent` sobre Botao
  // (.acaoDiscreta, .painelAcoes > button) repetiam o que o próprio Botao já faz e saíram.
  assert.doesNotMatch(abaCss, /\.acaoDiscreta:disabled/)
  assert.doesNotMatch(cardapioCss, /\.painelAcoes > button:disabled/)
})

caso('P7: unidades de insumo aparecem como g, kg, ml, L, un e o custo cita a unidade escolhida', () => {
  assert.deepEqual(insumos.UNIDADES_INSUMO, ['G', 'Kg', 'Ml', 'L', 'Un'], 'o valor enviado à API não muda')
  assert.deepEqual(insumos.UNIDADES_INSUMO.map(insumos.rotuloDaUnidade), ['g', 'kg', 'ml', 'L', 'un'])
  assert.equal(insumos.rotuloDaUnidade('Xis'), 'Xis', 'unidade desconhecida passa como veio')
  assert.equal(insumos.rotuloDaUnidade(null), '')
  // #2363: o custo vem do preço de compra, na unidade da família do insumo.
  assert.match(insumosJsx, /unidadesDeCompra\(unidade\)\.map\(\(u\) => \(\{ valor: u, rotulo: rotuloDaUnidade\(u\) \}\)\)/)
  assert.match(insumosJsx, /rotulo: rotuloDaUnidade\(u\)/)
})

// ---- Cardápio ---------------------------------------------------------------------------------

caso('C1: filtro "Com pendência" filtra e conta, e o link da home abre nele', () => {
  const itens = [
    { sku: 'A', nome: 'A', pendencias: ['foto'], arquivado: false },
    { sku: 'B', nome: 'B', pendencias: [], arquivado: false },
    { sku: 'C', nome: 'C', pendencias: ['categoria'], arquivado: true },
    { sku: 'D', nome: 'D' },
  ]
  assert.ok(filtros.FILTROS_DO_CARDAPIO.some((f) => f.id === 'pendencia' && f.rotulo === 'Com pendência'))
  assert.deepEqual(filtros.filtrarCardapio(itens, { filtro: 'pendencia' }).map((i) => i.sku), ['A'])
  assert.equal(filtros.contagensDoCardapio(itens).filtros.pendencia, 1)
  assert.equal(filtros.hashDoFiltro('pendencia'), '#/m/cardapio/itens/pendencia')
  assert.equal(filtros.filtroDaHash('#/m/cardapio/itens/pendencia'), 'pendencia')
  assert.equal(filtros.filtroDaHash('#/m/cardapio/itens/sem-foto'), 'sem-foto')
  assert.equal(filtros.filtroDaHash('#/m/cardapio/itens/qualquer'), null)
  assert.equal(filtros.filtroDaHash('#/m/cardapio/itens'), null)
  assert.equal(filtros.filtroDaHash(undefined), null)
  assert.match(cardapioJsx, /useState\(\(\) => filtroDaHash\(globalThis\.location\?\.hash\) \?\? 'todos'\)/)
  assert.match(cardapioJsx, /addEventListener\('hashchange'/)
})

caso('C2: o interruptor "No site" está ao lado de "Hoje", fora do menu ⋮', () => {
  const noSite = cardapioJsx.indexOf('aria-label={`No site: ${item.nome}`}')
  assert.ok(noSite > cardapioJsx.indexOf('className={css.valor}'))
  // #2185: o menu ⋮ do prato é o MenuDeAcoes.
  assert.ok(noSite < cardapioJsx.indexOf('<MenuDeAcoes'), 'antes do menu de ações')
  assert.ok(noSite < cardapioJsx.indexOf('className={css.rodapePrato}'))
})

caso('C3: selo "Novidade até" só enquanto a novidade vale', () => {
  assert.match(cardapioJsx, /ehNovidade\(item, Date\.now\(\)\)/)
  assert.doesNotMatch(cardapioJsx, /item\.novidadeAte &&/)
})

caso('C4: categoria começa com maiúscula na exibição e no select (o filtro segue pelo valor)', () => {
  assert.equal(filtros.rotuloDaCategoria('massas recheadas'), 'Massas recheadas')
  assert.equal(filtros.rotuloDaCategoria('Sobremesas'), 'Sobremesas')
  assert.equal(filtros.rotuloDaCategoria('  pães '), 'Pães')
  assert.equal(filtros.rotuloDaCategoria(null), '')
  assert.match(cardapioJsx, /<span className=\{css\.categoria\}>\{rotuloDaCategoria\(item\.categoria\)\}/)
  assert.match(cardapioJsx, /\{ valor: c\.nome, rotulo: `\$\{rotuloDaCategoria\(c\.nome\)\} \(\$\{c\.total\}\)` \}/)
})

caso('C5: lápis e ⋮ ficam juntos à direita', () => {
  assert.match(regra(cardapioCss, '.rodapePrato'), /justify-content:\s*flex-end/)
  // #2185: o ⋮ é o MenuDeAcoes, logo depois do lápis no rodapé; o <details> e seu CSS saíram.
  assert.match(cardapioJsx, /<div className=\{css\.rodapePrato\}>[\s\S]*?icone="lapis"[\s\S]*?<\/div>\}\s*<MenuDeAcoes\b/)
  assert.doesNotMatch(cardapioCss, /\.outrasAcoes|\.painelAcoes/)
})

// ---- Entregas, Cozinha, Canais ----------------------------------------------------------------

caso('E1: nomes únicos em Entregas', () => {
  const fonte = ler('features/entregas/ConteudoEntregasApi.jsx')
  assert.match(fonte, /\['cadastro', 'Janelas de entrega'\]/)
  assert.match(fonte, /\['hoje', 'Painel do dia'\]/)
  assert.doesNotMatch(fonte, /Janelas e frete/)
})

caso('K1: a Cozinha vira grade de duas colunas até 1100 px, sem rolagem lateral', () => {
  const css = ler('features/cozinha/cozinha.module.css')
  const bloco = css.slice(css.indexOf('@media (min-width: 641px) and (max-width: 1100px)'))
  assert.ok(css.includes('@media (min-width: 641px) and (max-width: 1100px)'), 'faixa 641 a 1100 px')
  assert.match(bloco, /\.colunas \{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\)/)
  assert.match(bloco, /\.colunas \{[^}]*overflow-x:\s*hidden/)
  assert.match(bloco, /\.coluna \{[^}]*min-width:\s*0/)
})

caso('K2: os chips Hoje e Amanhã não reservam o vão do ✓', () => {
  const fonte = ler('features/cozinha/TelaCozinhaApi.jsx')
  assert.doesNotMatch(fonte, /<Chip papel="escolha" reservarIcone/)
})

caso('G2: hora sempre HH:MM, com dois pontos, no fuso da loja', () => {
  assert.equal(horaCurta('2026-11-05T19:23:00Z'), '16:23')
  assert.equal(horaCurta('2026-11-05T12:05:00Z'), '09:05')
  assert.equal(horaCurta('2026-11-05T03:00:00Z'), '00:00')
  const horaCurtaNoFonte = ler('dominio/formato.js').match(/export const horaCurta[\s\S]*?\r?\n\r?\n/)[0]
  assert.doesNotMatch(horaCurtaNoFonte, /toLocaleTimeString/, 'o formato de toLocaleTimeString muda com o navegador ("16.23")')
  const r = resumoDoWhatsApp({ provider: 'wppconnect', sessaoWeb: { conectado: true }, ultimaMensagemRecebidaEm: '2026-11-05T19:23:00Z' })
  assert.ok(r.linhas[0].includes('16:23 de 05/11'), r.linhas[0])
})

caso('G1: Canais com hierarquia e sem jargão', () => {
  const meta = ler('features/gestao/integracoes/SecaoWhatsApp.jsx')
  const wpp = ler('features/gestao/integracoes/SecaoWppConnect.jsx')
  assert.match(meta, /Migrar para conexão oficial/)
  assert.match(meta, /useWppConnectApi\(\)/)
  assert.match(meta, /conectada \? \(/, 'o botão principal some com a provisória conectada')
  assert.match(wpp, /<Botao variante="texto" onClick=\{atualizar\} disabled=\{ocupado\}>Atualizar conexão<\/Botao>/)
  assert.match(wpp, /<Botao ref=\{desconectarRef\} variante="perigo"[^>]*>Desconectar número<\/Botao>/)
  assert.match(wpp, /confirmando/)
  assert.match(wpp, /Confirmar desconexão/)
  assert.doesNotMatch(wpp, /piloto/i)
  assert.match(wpp, /conexão provisória recebe texto, imagem e áudio/)
})

console.log(`\n${ok} casos ok, ${falhas} com falha`)
if (falhas > 0) process.exit(1)
