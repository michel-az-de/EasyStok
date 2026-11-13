// Prova da #2258: achados de UX do Atendimento (auditoria de 05/11, T1 a T21 e A4).
//
// Cobre as funções puras (cápsula de nome, iniciais, validação do cadastro), o desenho real do
// avatar e do nome (renderizados no Node) e as regras de texto de JSX e CSS que a auditoria apontou.
// O desenho em si é conferido no navegador.
//
// Roda com: node ferramentas/prova-2258-atendimento.mjs

import { registerHooks } from 'node:module'
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { join } from 'node:path'
import assert from 'node:assert/strict'
import { transformWithOxc } from 'vite'
import { createElement as h } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

// JSX real: o oxc transforma na carga, o CSS Module vira o nome da própria classe.
const raiz = fileURLToPath(new URL('../src/', import.meta.url))
const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
  const arquivo = join(raiz, nome)
  return [pathToFileURL(arquivo).href, (await transformWithOxc(readFileSync(arquivo, 'utf8'), arquivo, { jsx: { runtime: 'automatic' } })).code]
})))
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      for (const ext of ['.js', '.jsx']) { try { return proximo(especificador + ext, contexto) } catch { /* próximo formato */ } }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
    if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
    return proximo(url, contexto)
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
  try { fn(); ok++; console.log(`ok    ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}
process.on('beforeExit', () => {
  if (process.exitCode === undefined) { console.log(`\n${ok} ok, ${falhas.length} falha(s)`); process.exitCode = falhas.length ? 1 : 0 }
})

const { apresentacaoDoNome } = await import('../src/dominio/nomeDoContato.js')
const { iniciaisDoNome } = await import('../src/componentes/iniciaisDoNome.js')
const validacao = await import('../src/dominio/validacaoDoCadastro.js')
const cartao = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const { Avatar } = await import('../src/componentes/Avatar.jsx')
const { NomeDoContato } = await import('../src/componentes/NomeDoContato.jsx')

// --- T4, T5, T8: cápsula de nome -------------------------------------------------------------------

const AMBAR = { texto: 'Completar nome', tom: 'ambar', acao: 'completar-nome' }

caso('T5  nome completo: forma curta é o primeiro nome e a inicial do último sobrenome', () => {
  const a = apresentacaoDoNome('Thais Regina')
  assert.equal(a.completo, 'Thais Regina')
  assert.equal(a.curto, 'Thais R.')
  assert.equal(a.capsula, null)
  assert.equal(apresentacaoDoNome('Maria da Silva Santos').curto, 'Maria S.')
  assert.equal(apresentacaoDoNome('Maria da Silva').curto, 'Maria S.', 'partícula não vira inicial')
  assert.equal(apresentacaoDoNome('Ana B').curto, 'Ana B', 'a forma curta nunca é maior que o nome')
})

caso('T5  empresa: abrevia a última palavra, sem a forma legal', () => {
  assert.equal(apresentacaoDoNome('Notelet Eletrônicos').curto, 'Notelet Eletr.')
  assert.equal(apresentacaoDoNome('Notelet Eletrônicos Ltda').curto, 'Notelet Eletr.')
  assert.equal(apresentacaoDoNome('Notelet Eletrônicos Ltda').completo, 'Notelet Eletrônicos Ltda')
  assert.equal(apresentacaoDoNome('Casa Verde Ltda').curto, 'Casa Verde', 'palavra curta não abrevia')
  assert.equal(apresentacaoDoNome('Notelet Eletrônicos').capsula, null)
})

caso('T5  uma palavra só: cápsula âmbar que abre a edição do nome', () => {
  const a = apresentacaoDoNome('Thais')
  assert.equal(a.completo, 'Thais')
  assert.equal(a.curto, 'Thais')
  assert.deepEqual(a.capsula, AMBAR)
})

caso('T5  só telefone: sem +55 e cápsula cinza "Sem nome"', () => {
  for (const cru of ['553199999999', '+55 31 9999-9999', '3199999999']) {
    const a = apresentacaoDoNome(cru)
    assert.equal(a.completo, '(31) 9999-9999', cru)
    assert.equal(a.curto, '(31) 9999-9999', cru)
    assert.deepEqual(a.capsula, { texto: 'Sem nome', tom: 'neutro' }, cru)
  }
  assert.equal(apresentacaoDoNome('5511987654321').completo, '(11) 98765-4321')
  assert.equal(apresentacaoDoNome('+1 415 555 2671').completo, '+1 415 555 2671', 'país fora do 55 fica como veio')
  assert.equal(apresentacaoDoNome('553199999999').titulo, '+55 (31) 9999-9999', 'o title guarda o número inteiro')
})

caso('T4  visitante do site e "Comanda 6": "Visitante" e a cápsula "#6"', () => {
  for (const [nome, opcoes] of [['Visitante do site', { comanda: 6 }], ['Comanda 6', {}], ['Visitante', { comanda: '6' }], ['', { comanda: 6, origem: 'chat-site' }]]) {
    const a = apresentacaoDoNome(nome, opcoes)
    assert.equal(a.completo, 'Visitante', nome)
    assert.deepEqual(a.capsula, { texto: '#6', tom: 'neutro' }, nome)
    assert.equal(a.rotulo, 'Visitante #6', nome)
  }
  assert.equal(apresentacaoDoNome('Visitante do site', { comanda: 6 }).titulo, 'Visitante do site · Comanda 6')
  const semNumero = apresentacaoDoNome('Visitante do site')
  assert.equal(semNumero.completo, 'Visitante')
  assert.equal(semNumero.capsula, null, 'não inventa número')
  assert.equal(semNumero.rotulo, 'Visitante')
})

caso('T5  emoji no fim fica no fim e não conta na abreviação', () => {
  const a = apresentacaoDoNome('Thais Regina 🦋')
  assert.equal(a.completo, 'Thais Regina 🦋')
  assert.equal(a.curto, 'Thais R. 🦋')
  assert.equal(a.capsula, null)
  assert.equal(apresentacaoDoNome('Thais ❤️').capsula?.texto, 'Completar nome', 'o emoji não faz segunda palavra')
  assert.equal(apresentacaoDoNome('Notelet Eletrônicos 🏪').curto, 'Notelet Eletr. 🏪')
})

caso('T5  sem nenhum nome: "Contato" e cápsula "Sem nome"', () => {
  for (const vazio of ['', '   ', null, undefined]) {
    const a = apresentacaoDoNome(vazio)
    assert.equal(a.completo, 'Contato')
    assert.deepEqual(a.capsula, { texto: 'Sem nome', tom: 'neutro' })
  }
})

caso('T8  iniciais saem do mesmo nome e nunca são "?"', () => {
  assert.equal(iniciaisDoNome('Thais Regina'), 'TR')
  assert.equal(iniciaisDoNome('Visitante do site'), 'VS')
  assert.equal(iniciaisDoNome('Thais'), 'T')
  assert.equal(iniciaisDoNome('Thais Regina 🦋'), 'TR')
  assert.equal(iniciaisDoNome('Notelet Eletrônicos Ltda'), 'NL')
  assert.equal(iniciaisDoNome('Ana B'), 'AB', 'sobrenome de uma letra também conta')
  assert.equal(iniciaisDoNome('(31) 9999-9999'), '', 'telefone não tem letra')
  assert.equal(iniciaisDoNome(''), '')
  for (const nome of ['Thais Regina', 'Visitante do site', '553199999999', '', 'a b']) assert.notEqual(iniciaisDoNome(nome), '?')
})

caso('T8  avatar sem letra desenha o ícone de pessoa, nunca "?"', () => {
  const semLetra = renderToStaticMarkup(h(Avatar, { nome: '(31) 9999-9999' }))
  assert.doesNotMatch(semLetra, /\?/)
  assert.match(semLetra, /<svg/, 'ícone de pessoa')
  const comLetra = renderToStaticMarkup(h(Avatar, { nome: 'Thais Regina' }))
  assert.match(comLetra, />TR</)
  assert.doesNotMatch(comLetra, /<svg/)
  const fonte = ler('componentes/Avatar.jsx')
  assert.doesNotMatch(fonte, /'\?'/)
  assert.doesNotMatch(fonte, /length\s*>\s*2/, 'a palavra de duas letras ("Jo", "Ed") também vira inicial')
  assert.match(fonte, /\\p\{L\}|iniciaisDoNome/u)
})

caso('T5  o desenho do nome leva as duas formas, o nome inteiro no title e a cápsula', () => {
  const apresentacao = apresentacaoDoNome('Thais Regina')
  const html = renderToStaticMarkup(h(NomeDoContato, { ...apresentacao }))
  assert.match(html, /title="Thais Regina"/)
  assert.match(html, />Thais Regina</)
  assert.match(html, /aria-hidden="true"[^>]*>Thais R\.</, 'a forma curta é só visual')
  const ambar = renderToStaticMarkup(h(NomeDoContato, { ...apresentacaoDoNome('Thais') }))
  assert.match(ambar, />Completar nome</)
  const botao = renderToStaticMarkup(h(NomeDoContato, { ...apresentacaoDoNome('Thais'), aoCompletar: () => {} }))
  assert.match(botao, /<button[^>]*>Completar nome</, 'com ação, a cápsula âmbar é botão')
  const cinza = renderToStaticMarkup(h(NomeDoContato, { ...apresentacaoDoNome('553199999999'), aoCompletar: () => {} }))
  assert.doesNotMatch(cinza, /<button/, 'só a cápsula âmbar tem ação')
  const css = ler('componentes/NomeDoContato.module.css')
  assert.match(css, /\.capsula_ambar[^{]*\{[^}]*var\(--alerta-pastel\)/)
})

caso('T4  cartão, cabeçalho, ficha e botões "Abrir" falam o mesmo nome', () => {
  const nome = cartao.nomeDoCartao({ nome: 'Visitante do site', numeroComanda: 16 })
  assert.equal(nome.principal, 'Visitante')
  assert.equal(nome.complemento, null)
  assert.deepEqual(nome.capsula, { texto: '#16', tom: 'neutro' })
  assert.equal(nome.titulo, 'Visitante do site · Comanda 16')
  assert.equal(cartao.nomeDoCartao({ nome: 'Felipel Azevedo', numeroComanda: 3 }).complemento, 'Comanda 3', 'quem tem nome mantém a comanda ao lado')
  assert.equal(cartao.nomeDoCartao({ nome: '555434619500' }).principal, '(54) 3461-9500')
  assert.equal(cartao.nomeDoCartao({ nome: 'Thais Regina' }).curto, 'Thais R.')
  const painel = ler('features/atendimento/PainelAtendimento.jsx')
  assert.doesNotMatch(painel, /primeira\.nome\.split/, 'o botão Abrir usa o nome apresentado')
  assert.match(painel, /apresentacaoDoNome\(/)
  assert.match(ler('features/ficha-cliente/ModalCliente.jsx'), /apresentacaoDoNome\(/)
  assert.match(ler('features/ficha-cliente/CartaoDoCliente.jsx'), /apresentacaoDoNome\(/)
  assert.match(ler('features/ficha-cliente/BlocoCliente.jsx'), /apresentacaoDoNome\(/)
  assert.match(ler('features/caixa-de-entrada/CartaoConversa.jsx'), /<NomeDoContato/)
})

// --- T12, T13, T14, T16: validação do cadastro -----------------------------------------------------

caso('T14  telefone: vazio pede o DDD, 1 a 9 dígitos é incompleto, 10 ou 11 passam', () => {
  assert.equal(validacao.erroDoTelefone(''), 'Informe o telefone com DDD')
  assert.equal(validacao.erroDoTelefone('   '), 'Informe o telefone com DDD')
  for (const parcial of ['(12', '1', '(31) 9999', '(31) 99999-999']) assert.equal(validacao.erroDoTelefone(parcial), 'Telefone incompleto', parcial)
  assert.equal(validacao.erroDoTelefone('(31) 9999-9999'), null)
  assert.equal(validacao.erroDoTelefone('(31) 99999-9999'), null)
})

caso('T14  CEP é opcional; de 1 a 7 dígitos diz que precisa de 8', () => {
  assert.equal(validacao.erroDoCep(''), null)
  for (const parcial of ['1', '12', '12345', '12345-67']) assert.equal(validacao.erroDoCep(parcial), 'CEP precisa de 8 dígitos', parcial)
  assert.equal(validacao.erroDoCep('12345-678'), null)
})

caso('T12  nome: obrigatório e até 80 caracteres', () => {
  assert.equal(validacao.LIMITE_DO_NOME, 80)
  assert.equal(validacao.erroDoNome(''), 'Escreva o nome')
  assert.equal(validacao.erroDoNome('   '), 'Escreva o nome')
  assert.equal(validacao.erroDoNome('A'.repeat(80)), null)
  assert.equal(validacao.erroDoNome('A'.repeat(81)), 'Use até 80 caracteres')
  assert.equal(validacao.erroDoNome(`  ${'A'.repeat(80)}  `), null, 'espaço nas pontas não conta')
})

caso('T14  validarCadastro junta os erros e aponta o primeiro campo, na ordem da tela', () => {
  const ruim = validacao.validarCadastro({ telefone: '(12', cep: '12' })
  assert.deepEqual(ruim.erros, { telefone: 'Telefone incompleto', cep: 'CEP precisa de 8 dígitos' })
  assert.equal(ruim.primeiro, 'telefone')
  const soCep = validacao.validarCadastro({ telefone: '(31) 99999-9999', cep: '12' })
  assert.equal(soCep.primeiro, 'cep')
  const limpo = validacao.validarCadastro({ telefone: '(31) 99999-9999', cep: '' })
  assert.deepEqual(limpo.erros, {})
  assert.equal(limpo.primeiro, null)
  // Campo que não está na tela (cliente já tem telefone) não vira erro.
  assert.deepEqual(validacao.validarCadastro({ cep: '', campos: ['cep'] }).erros, {})
  assert.deepEqual(validacao.validarCadastro({ telefone: '', cep: '', campos: ['cep'] }).erros, {})
})

caso('T12 T13 T14 T16  o cadastro valida no blur e no clique, foca o erro e usa o exemplo claro', () => {
  const bloco = ler('features/ficha-cliente/BlocoCliente.jsx')
  assert.match(bloco, /dica="Nome e sobrenome" limite=\{LIMITE_DO_NOME\}/)
  assert.match(bloco, /erroDoNome\(/)
  assert.match(bloco, /erro=\{errosDoCadastro\.telefone\}/)
  assert.match(bloco, /erro=\{errosDoCadastro\.cep\}/)
  assert.match(bloco, /onBlur=/, 'valida ao sair do campo')
  assert.match(bloco, /\.focus\(\)/, 'foca o primeiro campo com erro')
  assert.match(bloco, /placeholder="\(00\) 00000-0000"/)
  assert.doesNotMatch(bloco, /98765-4321/)
  assert.doesNotMatch(bloco, /disabled=\{!podeSalvarCadastro/, 'o botão não fica mudo por telefone ou CEP')
})

// --- T1: filtro das conversas -------------------------------------------------------------------

caso('T1  o filtro abre ancorado à esquerda do botão, em portal, e cabe na tela', () => {
  const popover = ler('componentes/Popover.jsx')
  assert.match(popover, /alinhamento = 'direita'/)
  assert.match(popover, /alinhamento === 'esquerda'/)
  const balcao = ler('features/caixa-de-entrada/BarraBalcao.jsx')
  assert.match(balcao, /<Popover[^>]*rotulo="Filtros das conversas"[^>]*\bportal\b[^>]*alinhamento="esquerda"/)
  assert.match(regra(ler('componentes/Popover.module.css'), '.alinharEsquerda'), /left:\s*0;\s*right:\s*auto/)
  // O uso de sempre (menus à direita da tela) continua alinhado à direita.
  assert.doesNotMatch(ler('features/ficha-cliente/MenuDaFicha.jsx'), /alinhamento/)
})

// --- T2, T3: conversa carregando e separador de dia -------------------------------------------------

caso('T2  durante a carga o painel da conversa mostra esqueleto, não "Nenhuma conversa"', () => {
  const painel = ler('features/atendimento/PainelAtendimento.jsx')
  assert.match(painel, /cargaDaLista\(/)
  assert.match(painel, /carga === 'carregando'/)
  assert.match(painel, /css\.esqueleto/)
  const css = ler('features/atendimento/atendimento.module.css')
  assert.match(css, /\.esqueleto/)
  assert.match(css, /@media \(prefers-reduced-motion: reduce\)[^}]*\{[^}]*\.esqueleto/s)
})

caso('T3  o separador de dia tem fundo opaco, camada e margem; a faixa de modo fica fora da rolagem', () => {
  const css = ler('features/atendimento/atendimento.module.css')
  const separador = regra(css, '.separadorDia')
  assert.match(separador, /background:\s*var\(--chao-conversa\)/)
  assert.match(separador, /z-index:\s*[2-9]/)
  assert.match(separador, /padding:/)
  const painel = ler('features/atendimento/PainelAtendimento.jsx')
  assert.ok(painel.indexOf('<BarraModo') < painel.indexOf('css.rolagemCentro'), 'BarraModo antes da área que rola')
})

// --- T6, T7: etiqueta de motivo e prazo -----------------------------------------------------------

caso('T6  o motivo no cartão cabe em 18 caracteres e a frase inteira fica no title', () => {
  for (const chave of ['reclamacao', 'passagem', 'esperando', 'cobranca', 'aguardando-abertura', 'pedido-atrasado', 'atraso-entrega']) {
    const resumido = cartao.motivoResumido(chave, 'chat-site')
    assert.ok(resumido.texto.length <= 18, `${chave}: "${resumido.texto}"`)
    assert.ok(resumido.titulo.length >= resumido.texto.length)
  }
  assert.equal(cartao.motivoResumido('aguardando-abertura').texto, 'Loja fechada')
  assert.match(ler('features/caixa-de-entrada/CartaoConversa.jsx'), /motivoResumido\(/)
})

caso('T7  borda de prazo ganha ícone com texto acessível (cor não é o único sinal)', () => {
  const jsx = ler('features/caixa-de-entrada/CartaoConversa.jsx')
  assert.match(jsx, /Prazo de resposta estourado/)
  assert.match(jsx, /className=\{css\.sinalPrazo\}/)
})

// --- T11, T17, T18: ficha e cadastro ------------------------------------------------------------------

caso('T11  o modal é "Cadastro do cliente" e o painel lateral é "Cliente"', () => {
  assert.match(ler('features/ficha-cliente/ModalCliente.jsx'), /titulo="Cadastro do cliente"/)
  assert.match(ler('features/ficha-cliente/PainelFicha.jsx'), /<h2>Cliente<\/h2>/)
})

caso('T17  fechar com campo digitado pergunta antes de descartar', () => {
  const modal = ler('features/ficha-cliente/ModalCliente.jsx')
  assert.match(modal, /confirmarDescarte=\{/)
  assert.match(modal, /aoMudarSujo=/)
  assert.match(modal, /dispatchEvent\(new Event\('cancel'/, 'o Fechar do rodapé passa pela mesma pergunta do Esc')
  assert.match(ler('features/ficha-cliente/BlocoCliente.jsx'), /aoMudarSujo\?\.\(/)
})

caso('T18  "Salvar cadastro" é a ação principal do rodapé, à direita', () => {
  const modal = ler('features/ficha-cliente/ModalCliente.jsx')
  assert.match(modal, /ref=\{setRodapeDoCadastro\}/)
  assert.ok(modal.indexOf('Baixar PDF') < modal.indexOf('setRodapeDoCadastro}'), 'o botão principal fecha a fileira')
  const bloco = ler('features/ficha-cliente/BlocoCliente.jsx')
  assert.match(bloco, /createPortal\(/)
  assert.match(bloco, /rodapeDoCadastro/)
  assert.match(regra(ler('features/ficha-cliente/cliente.module.css'), '.acaoSalvar'), /justify-self:\s*end|margin-left:\s*auto/)
})

// --- T19, T20, A4, T21 -------------------------------------------------------------------------------------

caso('T19 T20 A4  textos do histórico', () => {
  const historico = ler('features/ficha-cliente/ModalHistorico.jsx')
  assert.doesNotMatch(historico, /Contato desde/)
  assert.match(historico, /Contato \{quandoRelativo/) // quandoRelativo já devolve "há 10 dias", "hoje" ou "ontem"
  // #2185: o ator tem um nome só no Atendimento, "Assistente" (guarda atorForaDoVocabulario).
  assert.match(historico, /\{ id: 'automaticas', rotulo: 'Assistente'/)
  assert.doesNotMatch(historico, /rotulo: '(?:Respostas )?[Aa]utom[áà]ticas'/)
  assert.match(historico, /comandaAberta\(/)
})

caso('T20  ficha sem pedido, mas com comanda aberta, diz "1 comanda aberta (R$ 495,00)"', () => {
  assert.equal(cartao.comandaAbertaNoHistorico, undefined, 'a regra mora no domínio, não no cartão')
})

caso('T21  o assistente tem uma entrada só, com rótulo', () => {
  const balao = ler('features/assistente/BalaoAssistente.jsx')
  assert.match(balao, /<span className=\{css\.rotuloGatilho\}>Assistente<\/span>/)
  assert.doesNotMatch(balao, /cabecalhoDoAtendimento && <span className=\{css\.rotuloGatilho\}/, 'o rótulo não depende do cabeçalho')
  const composer = ler('features/atendimento/Composer.jsx')
  assert.doesNotMatch(composer, /<Icone nome="sparkles" \/><span className=\{css\.rotuloAcao\}>Sugestões de IA/, 'a chave do completar não repete a faísca do assistente')
})

console.log(`\n${ok} casos ok`)
