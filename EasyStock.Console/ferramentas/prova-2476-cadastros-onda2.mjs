/* eslint-disable no-console */
// #2476 (onda 2 da revisão de Cadastros): documento com máscara sem perder o que não é CPF/CNPJ, pílula só no
// estado que pede ação, automática sem variável que o EasyStok não preenche e um tamanho de página só.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

// O domínio importa sem extensão (padrão do Vite); aqui o Node resolve com `.js`, como na prova-1441.
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const {
  corpoCliente, corpoFornecedor, documentoNaTela, documentoParaApi, situacaoDaLinha, TAMANHO_PAGINA_CADASTRO, temProximaPagina,
} = await import('../src/dominio/cadastros.js')
const { bloqueioDaAutomatica } = await import('../src/dominio/automacao.js')

const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const ler = (arquivo) => readFileSync(join(raiz, arquivo), 'utf8')

// U2: CPF (11 dígitos) e CNPJ (14) na máscara; o resto fica como foi escrito, sem virar um CPF que não é.
assert.equal(documentoNaTela('12345678900'), '123.456.789-00')
assert.equal(documentoNaTela('123.456.789-00'), '123.456.789-00')
assert.equal(documentoNaTela('1234'), '1234', 'No meio da digitação fica como foi escrito')
assert.equal(documentoNaTela('12345678000199'), '12.345.678/0001-99')
assert.equal(documentoNaTela('12.345.678/0001-99'), '12.345.678/0001-99')
assert.equal(documentoNaTela('123456780001'), '123456780001')
assert.equal(documentoNaTela('12.345.678-9'), '12.345.678-9', 'RG numérico gravado não vira CPF na tela')
assert.equal(documentoNaTela('1234567890'), '1234567890', '10 dígitos não é CPF')
assert.equal(documentoNaTela('MG-12.345.678'), 'MG-12.345.678', 'RG com letra fica como foi digitado')
assert.equal(documentoNaTela('123456789012345'), '123456789012345', 'Mais de 14 dígitos não é CPF nem CNPJ')
assert.equal(documentoNaTela(''), '')
assert.equal(documentoNaTela(null), '')

// A API recebe só os dígitos do que é numérico (ela já normaliza 11 e 14); o resto vai cru.
assert.equal(documentoParaApi('123.456.789-00'), '12345678900')
assert.equal(documentoParaApi('12.345.678/0001-99'), '12345678000199')
assert.equal(documentoParaApi('MG-12.345.678'), 'MG-12.345.678')
assert.equal(documentoParaApi('   '), null)
assert.equal(documentoParaApi('-'), null, 'Só pontuação é vazio')
assert.equal(documentoParaApi('...'), null)
assert.equal(documentoParaApi('12.345.678-9'), '12.345.678-9', 'RG numérico vai como está: salvar outro campo não o regrava')
assert.equal(documentoParaApi('1234567890'), '1234567890')
assert.equal(corpoCliente({ nome: 'Ana', documento: '123.456.789-00' }, 'e').documento, '12345678900')
assert.equal(corpoFornecedor({ nome: 'Moinho', documento: '12.345.678/0001-99' }, 'e').documento, '12345678000199')

// U6: pílula só quando o estado pede ação; o resto é texto (Pilula.module.css).
assert.deepEqual(situacaoDaLinha('clientes', { orderCount: 0, ativo: true }, false), { texto: 'Lead', tom: 'marca' })
assert.deepEqual(situacaoDaLinha('clientes', { orderCount: 3, ativo: true }, false), { texto: 'Cliente', tom: null })
assert.deepEqual(situacaoDaLinha('clientes', { orderCount: 0, ativo: false }, false), { texto: 'Inativo', tom: null })
assert.deepEqual(situacaoDaLinha('clientes', {}, true), { texto: 'Sem cadastro', tom: 'aviso' })
assert.deepEqual(situacaoDaLinha('fornecedores', { ativo: true }, false), { texto: 'Ativo', tom: null })
assert.deepEqual(situacaoDaLinha('produtos', { status: 'Inativo' }, false), { texto: 'Inativo', tom: null })
const painel = ler('features/gestao/cadastros/PainelCadastros.jsx')
assert.match(painel, /situacaoDaLinha\(/, 'A linha usa a regra do domínio')
assert.doesNotMatch(painel, /tomDaSituacao/, 'Regra de tom não fica no componente')

// U8: Cadastros › Modelos mostra só as respostas prontas; as automáticas ficam no trilho.
assert.match(ler('features/gestao/ModalGestao.jsx'), /respostas=\{<AbaRespostas comAutomaticas=\{false\} \/>\}/)
const aba = ler('features/gestao/respostas/AbaRespostas.jsx')
assert.match(aba, /comAutomaticas \? <SecaoAutomaticas/, 'Seção de automáticas só quando pedida')

// U8: variável que o EasyStok não preenche não sai para o cliente: a automática não liga nem salva com ela.
assert.equal(bloqueioDaAutomatica('Oi {nome}, pedido {pedido} sai {faixa}.'), null)
assert.equal(bloqueioDaAutomatica(''), null)
assert.equal(bloqueioDaAutomatica('Cardápio: {linkCardapio}'), 'Troque ou apague {linkCardapio}: o EasyStok não preenche e o cliente leria assim.')
assert.equal(bloqueioDaAutomatica('Abrimos {abre}. {linkCardapio}'), 'Troque ou apague {abre}, {linkCardapio}: o EasyStok não preenche e o cliente leria assim.')
assert.equal(bloqueioDaAutomatica('Cardápio: {linkCardapio}', false), 'Tem {linkCardapio}, que o EasyStok não preenche: assim ela não liga.', 'Quem não edita não recebe ordem de editar')
// A trava no componente (o Chromium da prova-2476-automaticas-navegador cobre a Aba; o modal segue a mesma regra).
const modalAutomacoes = ler('features/automacoes/ModalAutomacoes.jsx')
assert.match(modalAutomacoes, /const naoLiga = !regra\.ativa && fonteApi && Boolean\(bloqueioDaAutomatica\(regra\.texto\)\)/, 'Só trava ligar; desligar segue livre')
assert.match(modalAutomacoes, /disabled=\{!editavel \|\| naoLiga\}/, 'Chave travada com variável solta')
assert.match(modalAutomacoes, /disabled=\{!editando \|\| gravacao === 'salvando' \|\| Boolean\(bloqueio\)\}/, 'Salvar travado com variável solta')
assert.match(modalAutomacoes, /aria-describedby=\{naoLiga \? idBloqueio : undefined\}/, 'A chave travada aponta o motivo')

// A4: uma constante de página para a API e para as listas.
assert.equal(TAMANHO_PAGINA_CADASTRO, 20)
assert.equal(temProximaPagina(1, 20), false)
assert.equal(temProximaPagina(1, 21), true)
assert.equal(temProximaPagina(2, 40), false)
for (const arquivo of ['infra/api/cadastrosApi.js', 'features/gestao/cadastros/PainelCadastros.jsx', 'features/gestao/cadastros/ModelosCadastro.jsx']) {
  assert.doesNotMatch(ler(arquivo), /pageSize: 20|\* 20\b/, `${arquivo}: página fixa em 20`)
}

console.log('#2476: documento com máscara, pílula de ação, automática sem variável solta e página única conferidos.')
