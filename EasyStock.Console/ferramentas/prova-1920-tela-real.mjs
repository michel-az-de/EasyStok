// Prova da #1920 (caça de bugs do console na tela real da 1.11.0, refs #1889). Medido antes no modo
// API, contra o laboratório do master, em 1280, 1440, 1920 e 390:
//
//   1  Entregas rolava de lado (44 px em 1440, 100 px em 1280): `.paginaApi > *` esticava o h1 `.sr`;
//   2  o Caixa e o Abrir a loja diziam "deixou R$ 288,00 na gaveta" com a sugestão em R$ 100,00, e
//      Últimos fechamentos punha R$ 373,00 ao lado de "Contado na gaveta: R$ 173,00" (`saldoFinal`);
//   3  pedido na entrega, sem nada pago, aparecia "Sinal pago" no Resumo da Ficha;
//   4  o cartão do cliente repetia "Ainda não comprou com a casa" (a IA devolve com ponto final);
//   5  cardápio e assistente cobriam 16 px do trilho (`left: 84px`; o trilho tem 100 px);
//   6  telefone cru "+5511987650101" na Ficha, e editar corrompia para "(55) 11987-6501";
//   7  o hall dizia "Saldo no caixa" para o total com Pix e cartão.
//
// Regras puras e leitura do fonte onde só existe composição; as medidas de antes e depois estão na
// issue e no comentário da #1889.
//
//   node ferramentas/prova-1920-tela-real.mjs

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
const { rotuloDoResumo } = await import('../src/dominio/pedido.js')
const { gavetaDoFechamento } = await import('../src/dominio/aberturaDaLoja.js')
const { mascaraTelefone, telefoneDoE164 } = await import('../src/dominio/formato.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) }
  catch (erro) { falhas.push(descricao); console.error('FALHOU  ' + descricao + '\n        ' + String(erro.message).split('\n')[0]) }
}

// --- 1. Título oculto não estica a página ------------------------------------------------------

confere('1  .sr resiste a regra de módulo com a mesma especificidade (largura, altura e posição)', () => {
  const regra = fonte('estilos/base.css').match(/\.sr \{([^}]+)\}/)?.[1] ?? ''
  for (const prop of ['position: absolute', 'width: 1px', 'height: 1px', 'overflow: hidden', 'margin: -1px']) {
    assert.match(regra, new RegExp(`${prop} !important`), `${prop} com !important`)
  }
})

// --- 2. Gaveta do fechamento ---------------------------------------------------------------------

confere('2  a gaveta do fechamento é o dinheiro, não o total com Pix e cartão', () => {
  assert.equal(gavetaDoFechamento({ saldoFinal: 288, dinheiroNaGaveta: 188 }), 188)
  assert.equal(gavetaDoFechamento({ saldoFinal: 120, dinheiroNaGaveta: null }), 120, 'fechamento antigo segue o saldo final')
})
confere('2  Caixa, Abrir a loja e Últimos fechamentos mostram a gaveta, o mesmo número da sugestão', () => {
  const caixa = fonte('features/gestao/caixa/AbaCaixaApi.jsx')
  assert.match(caixa, /deixou \$\{moeda\(gavetaDoFechamento\(ultimo\)\)\} na gaveta/)
  assert.match(caixa, /<span className=\{css\.linhaValor\}[^>]*>\{moeda\(gavetaDoFechamento\(f\)\)\}<\/span>/)
  assert.doesNotMatch(caixa, /\{moeda\((ultimo|f)\.saldoFinal\)\}<\/span>|deixou \$\{moeda\(ultimo\.saldoFinal\)\}/)
  const loja = fonte('features/loja/GestoLoja.jsx')
  assert.match(loja, /<dt>Na gaveta em \{diaMes\(ultimo\.data\)\}<\/dt><dd>\{moeda\(gavetaDoFechamento\(ultimo\)\)\}<\/dd>/)
  assert.doesNotMatch(loja, /moeda\(ultimo\.saldoFinal\)/)
})

// --- 3. Na entrega não é sinal ---------------------------------------------------------------------

confere('3  aprovado com saldo e nada pago (modo API) é "A receber na entrega"', () => {
  assert.equal(rotuloDoResumo('aprovado baba', 50, 0), 'A receber na entrega')
})
confere('3  com dinheiro já recebido, ou sem a informação (demonstração), segue "Sinal pago"', () => {
  assert.equal(rotuloDoResumo('aprovado baba', 44, 44), 'Sinal pago')
  assert.equal(rotuloDoResumo('aprovado baba', 44), 'Sinal pago')
  assert.equal(rotuloDoResumo('aprovado baba', 0, 0), 'Pago')
  assert.equal(rotuloDoResumo('aguardando pagamento', 88, 0), 'Aguardando pagamento')
})
confere('3  a Ficha passa o que entrou só quando a API trouxe os pagamentos', () => {
  assert.match(fonte('features/ficha-cliente/PainelFicha.jsx'),
    /rotuloDoResumo\(pedido\.estado, saldo, Array\.isArray\(pedido\.pagamentosApi\) \? totalPago\(pedido\) : null\)/)
})

// --- 4. Frase repetida -----------------------------------------------------------------------------

confere('4  o cartão compara o resumo da IA sem a pontuação do fim', () => {
  const jsx = fonte('features/ficha-cliente/CartaoDoCliente.jsx')
  assert.match(jsx, /\{resumo\.texto && !mesmaFrase\(resumo\.texto, numeros\) && /)
  const corpo = jsx.match(/const mesmaFrase = (\(a, b\) => .+)\r?\n/)?.[1]
  assert.ok(corpo, 'mesmaFrase declarada')
  const mesmaFrase = new Function(`return ${corpo}`)()
  assert.equal(mesmaFrase('Ainda não comprou com a casa.', 'Ainda não comprou com a casa'), true)
  assert.equal(mesmaFrase('Cliente fiel, pede toda semana.', 'Ainda não comprou com a casa'), false)
  assert.equal(mesmaFrase(null, 'Ainda não comprou com a casa'), false)
})

// --- 5. Trilho -------------------------------------------------------------------------------------

confere('5  cardápio, assistente e simulações começam depois do trilho pelo token, sem 84 px fixos', () => {
  assert.match(fonte('features/cardapio/cardapio.module.css'), /\.painelDoBalcao \{\s*left: var\(--largura-trilho\);/)
  assert.match(fonte('features/assistente/assistente.module.css'), /\.painelEsquerda \{ right: auto; left: calc\(var\(--largura-trilho\) \+ var\(--e-4\)\); \}/)
  assert.match(fonte('features/simulacoes/PainelSimulacoes.module.css'), /left: var\(--largura-trilho\);/)
  for (const css of ['features/cardapio/cardapio.module.css', 'features/assistente/assistente.module.css', 'features/simulacoes/PainelSimulacoes.module.css']) {
    assert.doesNotMatch(fonte(css), /left: (calc\()?84px/, `${css} sem left de 84 px`)
  }
})

// --- 6. Telefone -----------------------------------------------------------------------------------

confere('6  E.164 da API vira a máscara da tela e editar não troca o DDD', () => {
  assert.equal(telefoneDoE164('+5511987650101'), '(11) 98765-0101')
  assert.equal(telefoneDoE164('(11) 98765-0101'), '(11) 98765-0101', 'valor já mascarado (demonstração) fica igual')
  // Antes: a máscara era aplicada sobre o E.164 cru ao apagar o último dígito.
  assert.equal(mascaraTelefone('+551198765010'), '(55) 11987-6501', 'o defeito que a edição produzia')
  // Agora a edição parte da máscara: apagar o último dígito mantém o DDD 11.
  assert.equal(mascaraTelefone(telefoneDoE164('+5511987650101').slice(0, -1)), '(11) 9876-5010')
})
confere('6  o campo em linha mostra, abre e compara o telefone já na máscara', () => {
  const jsx = fonte('features/ficha-cliente/CampoEmLinha.jsx')
  assert.match(jsx, /const exibido = tipo === 'telefone' && valor \? telefoneDoE164\(valor\) : \(valor \?\? ''\)/)
  assert.match(jsx, /setRascunho\(exibido\)/)
  assert.match(jsx, /if \(rascunho === exibido\)/)
  assert.match(jsx, /\{exibido \|\| vazio\}/)
})

// --- 7. Hall ---------------------------------------------------------------------------------------

confere('7  o número do financeiro no hall se diz total, como no Caixa do dia', () => {
  const jsx = fonte('features/hall/PulsoDoHall.jsx')
  assert.match(jsx, /financeiro: \{ nome: 'Total do caixa',/)
  assert.doesNotMatch(jsx, /Saldo no caixa/)
})

if (falhas.length) {
  console.error(`\n${falhas.length} falha(s), ${passou} ok.`)
  process.exit(1)
}
console.log(`\n${passou} cenários da #1920 aprovados.`)
