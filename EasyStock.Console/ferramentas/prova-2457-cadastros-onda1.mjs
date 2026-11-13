/* eslint-disable no-console */
// #2457 (onda 1 da revisão de Cadastros): tons de pílula válidos, nenhum maxLength, telefone com máscara sem
// perder o E.164, erro de validação por campo e limites avisados no próprio campo.
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { corpoCliente, corpoFornecedor, errosDoFormularioCadastro, telefoneNaTela, telefoneParaApi, TEXTOS_CADASTRO } from '../src/dominio/cadastros.js'
import { camposDoErro } from '../src/infra/api/errosDeCampo.js'

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const pasta = join(raiz, 'features/gestao/cadastros')
const telas = readdirSync(pasta).filter(f => f.endsWith('.jsx')).map(f => [f, readFileSync(join(pasta, f), 'utf8')])

// B4: a Pílula só pinta os tons que o CSS conhece; tom fora da lista vira texto sem marca.
const css = readFileSync(join(raiz, 'componentes/Pilula.module.css'), 'utf8')
const tonsValidos = new Set(['ok', 'aviso', 'perigo', 'marca', 'neutro', 'info'].filter(t => new RegExp(`\\.${t}\\b`).test(css)))
for (const [arquivo, fonte] of telas) {
  for (const [, expressao] of fonte.matchAll(/<Pilula[^>]*?\btom=(\{[^}]*\}|"[^"]*")/g)) {
    for (const [, tom] of expressao.matchAll(/['"]([a-z]+)['"]/g)) assert.ok(tonsValidos.has(tom), `${arquivo}: tom "${tom}" não existe na Pílula`)
  }
  // U3 (direção visual, seção 2): maxLength corta o texto colado sem avisar.
  assert.doesNotMatch(fonte, /maxLength/, `${arquivo}: limite vai como erro no campo, não como maxLength`)
  // U1: o título já está na barra do módulo e na aba.
  assert.doesNotMatch(fonte, />CADASTROS</, `${arquivo}: rótulo CADASTROS repete o título da barra`)
}

// U4: o * de obrigatório fica fora do nome acessível (o leitor de tela diz "obrigatório" pelo aria-required).
for (const [arquivo, fonte] of telas) assert.doesNotMatch(fonte, /rotulo\} \*`|['"] \*['"]/, `${arquivo}: * no texto do rótulo muda o nome do campo`)
for (const comp of ['componentes/Campo.jsx', 'componentes/Combobox.jsx']) {
  const fonte = readFileSync(join(raiz, comp), 'utf8')
  assert.match(fonte, /<span aria-hidden="true"> \*<\/span>/, `${comp}: * sem aria-hidden`)
  assert.match(fonte, /aria-required=\{obrigatorio/, `${comp}: obrigatório sem aria-required`)
}

// U2: a tela mostra a máscara; a API recebe o E.164 de sempre.
assert.equal(telefoneNaTela('+5511982254398'), '(11) 98225-4398')
assert.equal(telefoneNaTela('11982254398'), '(11) 98225-4398')
assert.equal(telefoneNaTela('(11) 3333-4444'), '(11) 3333-4444')
assert.equal(telefoneNaTela('+14155550100'), '+14155550100', 'Número de fora fica como foi digitado')
assert.equal(telefoneNaTela('+55'), '+55', 'Digitar o DDI não apaga o campo')
assert.equal(telefoneNaTela(''), '')
assert.equal(telefoneParaApi('(11) 98225-4398'), '+5511982254398')
assert.equal(telefoneParaApi('(11) 3333-4444'), '+551133334444')
assert.equal(telefoneParaApi('+5511982254398'), '+5511982254398')
assert.equal(telefoneParaApi('+1 (415) 555-0100'), '+14155550100')
assert.equal(telefoneParaApi('5511982254398'), '+5511982254398')
assert.equal(telefoneParaApi('  '), null)
assert.equal(telefoneParaApi('1234'), '1234', 'Telefone curto vai cru e a API recusa com o motivo')
assert.equal(corpoCliente({ nome: 'Ana', telefone: '(11) 98225-4398' }, 'e').telefone, '+5511982254398')
assert.equal(corpoFornecedor({ nome: 'Moinho', telefone: '(11) 3333-4444' }, 'e').telefone, '+551133334444')

// B5: ValidationProblemDetails vira erro no próprio campo, com a chave do formulário.
const campos = camposDoErro({ title: 'One or more validation errors occurred.', errors: { Email: ['E-mail inválido.'], '$.telefone': ['Telefone inválido.'], 'command.Nome': ['Informe o nome.', 'outra'] } })
assert.deepEqual(campos, { email: 'E-mail inválido.', telefone: 'Telefone inválido.', nome: 'Informe o nome.' })
assert.deepEqual(camposDoErro({ error: { code: 'X', message: 'm' } }), {})
assert.deepEqual(camposDoErro(null), {})

// U3: o limite aparece como erro do campo, com o tamanho atual.
const longo = 'a'.repeat(151)
assert.match(errosDoFormularioCadastro('clientes', { nome: longo }).nome, /150/)
assert.equal(errosDoFormularioCadastro('produtos', { nome: longo, categoriaId: 'c' }).nome, undefined, 'Produto aceita até 180')
assert.match(errosDoFormularioCadastro('fornecedores', { nome: 'F', email: `${'a'.repeat(140)}@exemplo.com.br` }).email, /150/, 'B2: e-mail do fornecedor cabe na coluna (150)')
assert.equal(errosDoFormularioCadastro('clientes', { nome: ' ' }).nome, 'Informe o nome.')
assert.equal(errosDoFormularioCadastro('produtos', { nome: 'Bolo' }).categoriaId, 'Escolha uma categoria.')
assert.match(errosDoFormularioCadastro('produtos', { nome: 'Bolo', categoriaId: 'c', id: 'p', status: 'Ativo', precoReferencia: 0 }).precoReferencia, /preço/)
assert.deepEqual(errosDoFormularioCadastro('bancos', { nome: 'Caixa' }), {})

// U5: contas bancárias com textos próprios.
assert.equal(TEXTOS_CADASTRO.bancos.busca, 'Nome ou instituição')
assert.equal(TEXTOS_CADASTRO.bancos.novo, 'Nova conta')
for (const tela of ['clientes', 'produtos', 'fornecedores', 'bancos']) assert.ok(TEXTOS_CADASTRO[tela]?.vazio, `${tela}: estado vazio ensina o próximo passo`)

console.log('prova-2457-cadastros-onda1: ok')
