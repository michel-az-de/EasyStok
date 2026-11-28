// Prova da #3393: padrão visual do Financeiro (crítica impeccable r3, 27/11). Domínio puro e regras de JSX e CSS;
// o desenho e o clique ficam em prova-financeiro-navegador.mjs.
//
// Roda com: node ferramentas/prova-3393-financeiro.mjs
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

const ler = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')
const regra = (texto, seletor) => {
  const inicio = texto.indexOf(`${seletor} {`)
  assert.notEqual(inicio, -1, `regra ${seletor} não encontrada`)
  return texto.slice(inicio, texto.indexOf('}', inicio))
}
let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const modulos = await import('../src/dominio/modulos.js')
const financeiro = await import('../src/dominio/financeiro.js')
const painel = ler('features/gestao/financeiro/PainelFinanceiro.jsx')
const estilo = ler('features/gestao/financeiro/financeiro.module.css')
const menu = ler('features/hall/MenuDosModulos.jsx')
const caixaJsx = ler('features/gestao/caixa/AbaCaixaApi.jsx')
const caixaCss = ler('features/gestao/caixa/abaCaixa.module.css')
const moduloFinanceiro = modulos.MODULOS.find((m) => m.id === 'financeiro')

caso('#3393 menu: Categorias, Centros de custo e Fechamentos formam o grupo Ajustes, com id e aba de antes', () => {
  const doGrupo = moduloFinanceiro.telas.filter((t) => t.grupo === 'ajustes').map((t) => `${t.id}:${t.aba}`)
  assert.deepEqual(doGrupo, ['categorias:financeiro-categorias', 'centros-custo:financeiro-centros', 'fechamentos-legados:fechamentos-legados'])
})

caso('#3393 menu: secoesDoMenu põe o grupo depois das telas soltas e some quando fica vazio', () => {
  const telas = moduloFinanceiro.telas
  const todas = modulos.secoesDoMenu(telas)
  assert.deepEqual(todas.map((s) => s.rotulo), [null, 'Ajustes'])
  assert.deepEqual(todas[1].telas.map((t) => t.id), ['categorias', 'centros-custo', 'fechamentos-legados'])
  assert.equal(todas.flatMap((s) => s.telas).length, telas.length, 'nenhuma tela some nem repete')
  const soCaixa = modulos.secoesDoMenu(telas.filter((t) => t.id === 'caixa'))
  assert.deepEqual(soCaixa.map((s) => s.rotulo), [null], 'o Operador que só vê o Caixa não vê um Ajustes vazio')
})

caso('#3393 menu: o título do grupo não é link (as provas contam os links do menu)', () => {
  assert.match(menu, /secoesDoMenu\(/)
  assert.match(menu, /<h3 key=\{`grupo-\$\{secao\.rotulo\}`\} className=\{css\.grupoMenu\}>\{secao\.rotulo\}<\/h3>/)
})

caso('#3393 visão geral: saldo previsto de 30 dias = a receber − a pagar, em centavos, com as vencidas (r5)', () => {
  assert.equal(financeiro.saldoPrevisto30d({ totalAVencer30dReceber: 2620, totalVencidoReceber: 0, totalAVencer30dPagar: 3607.5, totalVencidoPagar: 799.35 }), -1786.85)
  assert.equal(financeiro.aPagarAte30d({ totalAVencer30dPagar: 0.1, totalVencidoPagar: 0.2 }), 0.3)
  assert.equal(financeiro.saldoPrevisto30d({ totalAVencer30dReceber: 100.1, totalAVencer30dPagar: 40.2 }), 59.9)
  assert.equal(financeiro.saldoPrevisto30d({ totalAVencer30dReceber: 0, totalAVencer30dPagar: 70 }), -70)
  assert.equal(financeiro.saldoPrevisto30d({}), 0)
})

caso('#3393 barras: escala relativa ao maior valor, sem dividir por zero', () => {
  assert.deepEqual(financeiro.escalaDasBarras([200, 50]), [100, 25])
  assert.deepEqual(financeiro.escalaDasBarras([0, 0]), [0, 0])
  assert.deepEqual(financeiro.escalaDasBarras([3, 1]).map(Math.round), [100, 33])
})

caso('#3393 contas: falta da lista soma só o que ainda falta (sem cancelada nem rascunho)', () => {
  const contas = [{ status: 'Aberta', pendente: 30.1 }, { status: 'ParcialmentePaga', pendente: 9.9 }, { status: 'Cancelada', pendente: 50 }, { status: 'Rascunho', pendente: 70 }, { status: 'Paga', pendente: 0 }]
  assert.equal(financeiro.faltaDaLista(contas), 40)
})

caso('#3393 visão geral: herói do saldo só com os dois lados e diz o que fica de fora', () => {
  assert.match(painel, /verPagar && verReceber && <SaldoPrevisto/)
  assert.match(painel, /Sem o dinheiro em caixa e sem os pedidos sem conta/)
  assert.match(painel, /<span>A receber menos a pagar · vencidas e próximos 30 dias<\/span>/, 'o número não é saldo: o nome diz o que ele é (r4)')
  assert.match(painel, /className=\{css\.barrasSaldo\}/)
})

caso('#3393 visão geral: Vencidas só aparece quando há algo vencido', () => {
  assert.match(painel, /\{vencido > 0 && <a className=\{`\$\{css\.cartaoVencidas\} \$\{css\.temVencida\}`\}/)
})

caso('#3393 visão geral: atalhos só no celular (o menu lateral já mostra as telas)', () => {
  assert.match(estilo, /@media \(min-width: 760px\) \{ \.atalhos \{ display: none; \} \}/)
})

caso('#3393 extrato: sinal explícito e entrada em verde, não só cor', () => {
  assert.match(estilo, /\.indicadoresTres \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\); gap: var\(--e-2\); \}/, 'no celular os 3 totais ficam numa faixa e a lista sobe (r4)')
  assert.match(painel, /item\.lado === 'pagar' \? '−' : '\+'/)
  assert.match(regra(estilo, '.valorEntrada'), /color: var\(--sucesso\)/)
  assert.match(regra(estilo, '.estornado'), /text-decoration: line-through/, 'estornado riscado, não confundido com zero')
})

caso('#3393 contas: Pagar da linha não é primário e a lista mostra a falta somada', () => {
  assert.doesNotMatch(painel, /podeRegistrarNaConta\(conta, podeGerenciar\) && <Botao variante="primario"/)
  assert.match(painel, /const faltaNaLista = faltaDaLista\(consulta\.dados\?\.itens \?\? \[\]\)/)
  assert.match(painel, /\{faltaNaLista > 0 && <p className=\{css\.somaLista\}>/)
  assert.match(painel, /className=\{`\$\{css\.vence\} /)
})

caso('#3393 relatórios: gráfico Recebido x Pago com texto equivalente', () => {
  assert.match(painel, /<figure className=\{css\.grafico\}/)
  assert.match(painel, /<figcaption/)
  assert.match(regra(estilo, '.barraRecebido'), /background: var\(--sucesso\)/)
  assert.match(regra(estilo, '.parBarras > .barraPago, .legendaGrafico .barraPago'), /repeating-linear-gradient/, 'Pago se distingue por forma, não só por cor')
})

caso('#3393 números: indicador e valor com algarismos tabulares; zero esmaecido', () => {
  assert.match(regra(estilo, '.indicador > strong'), /font-variant-numeric: tabular-nums/)
  assert.match(estilo, /^\.valor \{[^}]*font-variant-numeric: tabular-nums/m)
  assert.match(regra(estilo, '.zero'), /color: var\(--tinta-3\)/)
})

caso('#3393 caixa: sem zebra, divisor de 1px e zero esmaecido nos 7 dias', () => {
  assert.doesNotMatch(caixaCss, /\.fluxo tbody tr:nth-child\(even\) \{ background/)
  assert.match(caixaCss, /\.fluxo tbody tr \+ tr > \* \{ border-top: var\(--b-1\) solid var\(--linha\); \}/)
  assert.match(regra(caixaCss, '.zero'), /color: var\(--tinta-3\)/)
  assert.match(caixaJsx, /className=\{valorDoFluxo\(l\.entrou\)\}/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
