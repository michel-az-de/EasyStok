/* eslint-disable no-console */
// Prova da #2687 (homologação #1769, Financeiro):
//   F1: centro de custo sem código deixa o Salvar desligado sem dizer por quê;
//   F2: o lançamento do pagamento de uma conta não se estorna pelo Caixa (a conta seguiria paga);
//   F3: a categoria desse lançamento aparece com o código ("contas-a-pagar");
//   F4: com o caixa de outro dia esquecido aberto, o aviso de confirmar a abertura disputava a tela.
//
// Roda com: node ferramentas/prova-2687-financeiro.mjs
// Contra outra árvore (ex.: o master, para ver o Red): PROVA_SRC=<pasta src> node ferramentas/prova-2687-financeiro.mjs
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { pathToFileURL } from 'node:url'

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
const { caixaDiaDaApi } = await import(new URL('infra/api/caixaApi.js', base).href)
const fonte = (arquivo) => readFileSync(new URL(arquivo, base), 'utf8')

let falhas = 0
const caso = (nome, fn) => { try { fn(); console.log(`ok   ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n     ${e.message}`) } }

const ID = '0123456789abcdef0123456789abcdef'
const dia = caixaDiaDaApi({ data: '2026-11-15', movimentos: [
  { id: 'm1', tipo: 'saida', valor: 150, categoria: 'contas-a-pagar', referencia: `cp:${ID}:parcela:${ID}:pag:${ID}` },
  { id: 'm2', tipo: 'entrada', valor: 80, categoria: 'contas-a-receber', referencia: `cr:${ID}:parcela:${ID}:txid:abc123` },
  { id: 'm3', tipo: 'entrada', valor: 20, categoria: 'Venda avulsa', referencia: 'pedido:42' },
] })
const [pagar, receber, avulsa] = dia.movimentos

caso('F2: lançamento de pagamento de conta vem marcado; o comum não', () => {
  assert.equal(pagar.pagamentoDeConta, true)
  assert.equal(receber.pagamentoDeConta, true)
  assert.equal(avulsa.pagamentoDeConta, false)
})

caso('F2: o Caixa não oferece Estornar para o pagamento de conta', () => {
  const src = fonte('features/gestao/caixa/AbaCaixaApi.jsx')
  const linha = src.split('\n').find((l) => l.includes('const podeEstornar'))
  assert.ok(linha?.includes('!m.pagamentoDeConta'), 'podeEstornar ignora pagamentoDeConta')
})

caso('F3: a categoria do pagamento de conta aparece como se lê', () => {
  assert.equal(pagar.categoria, 'Conta a pagar')
  assert.equal(receber.categoria, 'Conta a receber')
  assert.equal(avulsa.categoria, 'Venda avulsa')
})

caso('F4: com caixa esquecido aberto, o Confirmar abertura não aparece junto', () => {
  const src = fonte('features/gestao/caixa/AbaCaixaApi.jsx')
  assert.ok(/dia\.aberturaAConfirmar && !dia\.fechado && !dia\.esquecidoAberto/.test(src), 'ConfirmarAbertura aparece com o caixa esquecido aberto')
})

caso('F1: cadastro financeiro diz por que o Salvar está desligado', () => {
  const src = fonte('features/gestao/financeiro/ModaisFinanceiras.jsx')
  const bloco = src.slice(src.indexOf('export function ModalCadastroFinanceiro'), src.indexOf('export function ModalParcelaFinanceira'))
  assert.match(bloco, /motivo=\{/, 'sem motivo no FormularioFinanceiro')
  assert.match(bloco, /código do centro de custo/i, 'motivo não fala do código')
})

console.log(falhas ? `\n${falhas} falha(s)` : '\nprova-2687 ok')
process.exit(falhas ? 1 : 0)
