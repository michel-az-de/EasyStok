// Prova da #3389: achados da revisão Impeccable do Financeiro (27/11). Domínio puro e regras de JSX e CSS;
// o desenho e o clique ficam em prova-financeiro-navegador.mjs.
//
// Roda com: node ferramentas/prova-3389-financeiro.mjs
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

const caixa = await import('../src/dominio/caixa.js')
const financeiro = await import('../src/dominio/financeiro.js')
const painel = ler('features/gestao/financeiro/PainelFinanceiro.jsx')
const modais = ler('features/gestao/financeiro/ModaisFinanceiras.jsx')
const estilo = ler('features/gestao/financeiro/financeiro.module.css')
const estorno = ler('features/gestao/caixa/PromptEstorno.jsx')

caso('#3389 (6): dia aberto com entrou e saiu em zero não é dia com movimento', () => {
  const linhas = [
    { situacao: 'aberto', entrou: 0, saiu: 0 },
    { situacao: 'aberto', entrou: 50, saiu: 0 },
    { situacao: 'fechado', entrou: 0, saiu: 20 },
    { situacao: 'sem-movimento', entrou: 0, saiu: 0 },
    { situacao: 'sem-abertura', entrou: 10, saiu: 0 },
  ]
  assert.equal(caixa.diasComMovimento(linhas), 3)
  assert.equal(caixa.diasComMovimento([]), 0)
})

caso('#3389 (6): a situação do dia segue "aberto" com tudo zerado (alimenta a pílula)', () => {
  assert.equal(caixa.linhaDoFluxo({ data: '2026-11-26', aberto: true }).situacao, 'aberto')
})

caso('#3389 (14): "Ambas" vira "Pagar e receber" só no rótulo', () => {
  assert.equal(financeiro.nomeDoTipoDeCategoria('Ambas'), 'Pagar e receber')
  assert.equal(financeiro.nomeDoTipoDeCategoria('Despesa'), 'Despesa')
  assert.equal(financeiro.nomeDoTipoDeCategoria('Receita'), 'Receita')
  assert.equal(financeiro.nomeDoTipoDeCategoria('Outra'), 'Outra')
  assert.match(modais, /\['Despesa', 'Receita', 'Ambas'\]\.map\(\(valor\) => \(\{ valor, rotulo: nomeDoTipoDeCategoria\(valor\) \}\)\)/)
  assert.match(painel, /nomeDoTipoDeCategoria\(item\.tipo\)/)
})

caso('#3389 (14): inativar tem título e botão próprios', () => {
  assert.match(painel, /titulo=\{categoria \? 'Inativar categoria' : 'Inativar centro de custo'\}/)
  assert.match(painel, /salvar=\{categoria \? 'Inativar categoria' : 'Inativar centro de custo'\}/)
})

caso('#3389 (2): o chip ativo não usa o fundo cheio da marca', () => {
  const ativo = regra(estilo, ".chip[aria-pressed='true']")
  assert.doesNotMatch(ativo, /background:\s*var\(--marca\)/)
  assert.match(ativo, /border-color:\s*var\(--marca\)/)
  assert.doesNotMatch(regra(estilo, '.chipAtivo, .chipAtivo:hover'), /background:\s*var\(--marca\)/)
})

caso('#3389 (3): Pagar e Receber da visão geral são secundários', () => {
  const linha = painel.split('\n').find((texto) => texto.includes('aoPagar({ lado, conta, parcela })'))
  assert.ok(linha, 'botão da lista do dia não encontrado')
  assert.doesNotMatch(linha, /variante="primario"/)
})

caso('#3389 (4): confirmações destrutivas usam a variante perigo, por chamada', () => {
  assert.match(estorno, /variante="perigo"/)
  assert.match(modais, /varianteSalvar = 'primario'/)
  assert.match(modais, /variante=\{varianteSalvar\}/)
  assert.match(modais, /fechar = 'Cancelar', variante = 'primario' \}/)
  assert.match(painel, /salvar: 'Cancelar a conta', fechar: 'Voltar', variante: 'perigo'/)
  assert.match(painel, /confirmar\('Estornar registro'[^\n]*\{ variante: 'perigo' \}\)/)
  assert.match(painel, /variante="perigo"/)
  assert.match(painel, /confirmar\('Emitir conta'(?:(?!variante: 'perigo')[^\n])*\)\)\}>Emitir conta/)
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
