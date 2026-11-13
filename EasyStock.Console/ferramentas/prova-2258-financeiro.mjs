// Prova da #2258: achados de UX do Financeiro (auditoria de 05/11).
//
// Cobre as funções puras (aviso ao fechar o caixa com a data real, leitura das parcelas, corpo da
// categoria criada na hora) e as regras de JSX e CSS que a auditoria apontou: grade 2x2, chip ativo,
// resumo só com seleção, "+ Nova categoria" na escolha da categoria. O desenho é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-financeiro.mjs

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
// Cada caso roda até o fim: a prova lista tudo o que falta em vez de parar no primeiro.
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const abertura = await import('../src/dominio/aberturaDaLoja.js')
const caixa = await import('../src/dominio/caixa.js')
const financeiro = await import('../src/dominio/financeiro.js')
const iconesDoConsole = await import('../src/componentes/iconesDoConsole.js')

const painel = ler('features/gestao/financeiro/PainelFinanceiro.jsx')
const modais = ler('features/gestao/financeiro/ModaisFinanceiras.jsx')
const estilo = ler('features/gestao/financeiro/financeiro.module.css')
const modalFechar = ler('features/gestao/caixa/ModalFecharCaixa.jsx')

// ---- F2: o aviso do fechamento fala do dia que está sendo fechado
caso('F2: caixa de outro dia diz a data real, não "hoje"', () => {
  const aviso = abertura.avisoAoFecharCaixa('2026-10-28T09:00:00-03:00', '2026-11-05')
  assert.match(aviso, /O caixa de 28\/10 não reabre/)
  assert.match(aviso, /lançamentos de 28\/10 não podem mais ser estornados/)
  assert.doesNotMatch(aviso, /hoje/)
  assert.doesNotMatch(aviso, /—/)
})

caso('F2: caixa de hoje (ou sem data) mantém o texto de hoje', () => {
  const deHoje = abertura.avisoAoFecharCaixa('2026-11-05T08:00:00-03:00', '2026-11-05')
  assert.match(deHoje, /o caixa de hoje não reabre/)
  assert.match(deHoje, /Sem caixa aberto, a loja não abre mais hoje/)
  assert.match(deHoje, /lançamentos de hoje não podem mais ser estornados/)
  assert.equal(abertura.avisoAoFecharCaixa(null, '2026-11-05'), deHoje)
  assert.equal(deHoje, caixa.AVISO_AO_FECHAR_CAIXA)
})

caso('F2: o modal usa o aviso calculado pela data da abertura', () => {
  assert.match(modalFechar, /descricao=\{avisoAoFecharCaixa\(resumo\.abertoDesde, /)
  assert.doesNotMatch(modalFechar, /descricao=\{AVISO_AO_FECHAR_CAIXA\}/)
})

// ---- F13: parcelas
caso('F13: parcelas só em dígitos, de 1 a 60, com erro colado ao campo', () => {
  const lido = (texto) => financeiro.lerParcelas(texto)
  assert.deepEqual(lido('0'), { texto: '0', quantidade: 0, erro: 'Mínimo 1 parcela' })
  assert.deepEqual(lido('-3'), { texto: '3', quantidade: 3, erro: null })
  assert.deepEqual(lido(''), { texto: '', quantidade: null, erro: 'Mínimo 1 parcela' })
  assert.deepEqual(lido('1'), { texto: '1', quantidade: 1, erro: null })
  assert.deepEqual(lido('60'), { texto: '60', quantidade: 60, erro: null })
  assert.deepEqual(lido('61'), { texto: '61', quantidade: 61, erro: 'Máximo 60 parcelas' })
  assert.equal(lido('03').texto, '3', 'zero à esquerda não fica')
  assert.equal(lido('1e5').texto, '15', 'só dígitos')
})

caso('F13: o campo é texto numérico em string e a caixa "Já foi paga" não some por valor inválido', () => {
  assert.doesNotMatch(modais, /tipo="number"/)
  assert.doesNotMatch(modais, /Number\(e\.target\.value\)/)
  assert.match(modais, /inputMode="numeric"/)
  assert.match(modais, /erro=\{parcelasLidas\.erro\}/)
  assert.doesNotMatch(modais, /\{quantidade === 1 && emitir &&/)
})

// ---- F12: categoria criada ali mesmo
caso('F12: corpo e motivo da categoria criada na escolha', () => {
  assert.deepEqual(financeiro.corpoDaNovaCategoria('  Aluguel ', 'pagar'), { nome: 'Aluguel', tipo: 'Despesa' })
  assert.deepEqual(financeiro.corpoDaNovaCategoria('Vendas', 'receber'), { nome: 'Vendas', tipo: 'Receita' })
  assert.equal(financeiro.motivoDaNovaCategoria('   '), 'Escreva o nome da categoria.')
  assert.equal(financeiro.motivoDaNovaCategoria('Aluguel'), null)
})

caso('F12: "+ Nova categoria" existe na escolha, só para quem gerencia categorias, e seleciona a criada', () => {
  assert.match(modais, /\+ Nova categoria/)
  assert.match(modais, /gerenciarCategoriasFinanceiras === true/)
  assert.match(modais, /corpoDaNovaCategoria\(nomeNovaCategoria, lado\)/)
  assert.match(modais, /salvarCadastroFinanceiro\('categorias', corpo\)/)
  assert.match(modais, /setCategoria\(/)
  assert.doesNotMatch(modais, /em Categorias financeiras para criar esta conta/)
})

// ---- F15 / F14: opcionais marcados, erro de cada campo embaixo dele
caso('F15: opcionais marcados e erros no próprio campo', () => {
  assert.match(modais, /rotulo="Centro de custo \(opcional\)"/)
  assert.match(modais, /rotulo="Observações \(opcional\)"/)
  assert.match(modais, /rotulo="Descrição" required maxLength=\{200\}[^>]*erro=/)
})

caso('F14: erro do vencimento fica colado ao campo de vencimento', () => {
  assert.doesNotMatch(modais, /<ErroFinanceiro erro="O primeiro vencimento/)
  assert.match(modais, /rotulo="Primeiro vencimento"[^>]*erro=\{/)
})

caso('descarte: Nova conta pergunta antes de perder o que foi digitado', () => {
  assert.match(modais, /confirmarDescarte=\{/)
  assert.match(modais, /segurarFechamento=\{operacao\.ocupado\}/)
})

// ---- F7: um ícone por atalho
caso('F7: cada atalho da Visão geral usa o ícone da própria tela, sem repetir', () => {
  const linha = painel.split(/\r?\n/).find((l) => l.startsWith('const ATALHOS')) ?? ''
  const ids = [...linha.matchAll(/\['([a-z-]+)', '[^']+'\]/g)].map((m) => m[1])
  assert.equal(ids.length, 5)
  assert.match(painel, /<IconeDoModulo modulo=\{MODULO_FINANCEIRO\} telaId=\{id\}/)
  const icones = ids.map((id) => iconesDoConsole.iconeDaTela('financeiro', id))
  assert.equal(new Set(icones).size, 5, `ícones repetidos: ${icones}`)
})

// ---- F8: grade 2x2 e mesmo formato
caso('F8: indicadores em grade 2x2 fixa e os quatro com detalhe', () => {
  const grade = regra(estilo, '.indicadores')
  assert.match(grade, /grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\)/)
  assert.doesNotMatch(grade, /auto-fit/)
  assert.match(painel, /nome="Pago no mês"[^>]*detalhe=/)
  assert.match(painel, /nome="Recebido no mês"[^>]*detalhe=/)
})

// ---- F9: um caminho só
caso('F9: bloco inteiro leva à lista por "Ver lista ›", sem "toque" nem segundo link', () => {
  assert.doesNotMatch(painel, /Toque num bloco/)
  assert.doesNotMatch(painel, /Abrir a lista/)
  assert.doesNotMatch(painel, /Ver na lista/)
  assert.match(painel, /Ver lista ›/)
  assert.match(regra(estilo, '.verLista::after'), /position:\s*absolute;\s*inset:\s*0/)
  assert.match(regra(estilo, '.blocoDia'), /position:\s*relative/)
})

// ---- F10: chips
caso('F10: chips quebram linha, sem barra de rolagem, e o ativo é de marca com aria-pressed', () => {
  const chips = regra(estilo, '.chips')
  assert.match(chips, /flex-wrap:\s*wrap/)
  assert.doesNotMatch(chips, /overflow-x/)
  assert.doesNotMatch(chips, /scrollbar/)
  assert.match(painel, /aria-pressed=\{chip === item\.id && !comAvancado\}/)
  const ativo = regra(estilo, ".chip[aria-pressed='true']")
  assert.match(ativo, /background:\s*var\(--marca\)/)
  assert.match(ativo, /color:\s*var\(--marca-contraste\)/)
})

// ---- F11: resumo só com seleção
caso('F11: o painel "Conta selecionada" só existe com uma conta selecionada', () => {
  assert.doesNotMatch(painel, /<h2>Conta selecionada<\/h2>/)
  assert.doesNotMatch(painel, /Selecione uma conta para consultar/)
  assert.match(painel, /\{selecionada && <aside className=\{css\.contexto\}/)
  assert.match(painel, /className=\{selecionada \? css\.comContexto : css\.painel\}/)
})

console.log(`\n${ok} casos ok, ${falhas.length} com falha`)
if (falhas.length) process.exitCode = 1
