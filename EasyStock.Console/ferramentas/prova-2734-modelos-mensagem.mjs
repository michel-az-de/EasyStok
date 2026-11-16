/* eslint-disable no-console */
// #2734 (B1): modelos de mensagem. O console segue a regra do servidor (ModeloTextoAtendimento.cs): só
// {nome}, {pedido} e {faixa}, em minúsculas, são preenchidas; o resto sai literal para o cliente.
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const automacao = await import('../src/dominio/automacao.js')
const respostas = await import('../src/dominio/respostas.js')
const { NOME_RESPOSTAS_AUTOMATICAS } = await import('../src/dominio/modulos.js')
const { GATILHOS } = automacao
const lerFonte = (caminho) => readFileSync(new URL(`../src/${caminho}`, import.meta.url), 'utf8')

// T8: o texto padrão de "Pagamento confirmado" é o do servidor (RegraAutomatica.TextoPadraoPagamentoConfirmado).
const TEXTO_DO_SERVIDOR = 'Oi, {nome}! Recebemos seu pagamento e seu pedido está confirmado para {faixa}. Obrigada pela preferência!'
assert.equal(automacao.REGRAS_PADRAO.find((r) => r.id === 'recibo').texto, TEXTO_DO_SERVIDOR, 'T8: texto padrão igual ao do servidor')

// T2: o servidor só troca {nome}, {pedido} e {faixa}. Grafia diferente sai literal, e a tela diz qual.
assert.deepEqual(
  automacao.variaveisForaDaApi('Oi {Nome}, {nome_cliente}, { nome } e {endereço}.'),
  ['Nome', 'nome_cliente', ' nome ', 'endereço'],
  'T2: variante de grafia é variável que o servidor não preenche',
)
assert.deepEqual(automacao.variaveisForaDaApi('Oi {nome}, pedido {pedido}, {faixa}.'), [])
assert.equal(
  automacao.bloqueioDaAutomatica('Oi {Nome}.'),
  'Escreva {nome} no lugar de {Nome}: o EasyStok só preenche {nome}, {pedido} e {faixa}.',
  'T2: o bloqueio diz a grafia certa',
)
assert.equal(
  automacao.bloqueioDaAutomatica('Oi {Nome}.', false),
  'Tem {Nome}, que o EasyStok não preenche: assim ela não liga.',
  'T2: quem não edita só recebe o porquê',
)
assert.equal(
  automacao.bloqueioDaAutomatica('Cardápio {linkCardapio} e {Nome}.'),
  'Troque ou apague {linkCardapio}, {Nome}: o EasyStok não preenche e o cliente leria assim.',
  'T2: sem sugestão para todas, vale a mensagem de sempre',
)
assert.equal(automacao.sugestaoDeVariavel('Nome'), 'nome')
assert.equal(automacao.sugestaoDeVariavel('nome_cliente'), 'nome')
assert.equal(automacao.sugestaoDeVariavel(' faixa '), 'faixa')
assert.equal(automacao.sugestaoDeVariavel('CUPOM10'), null)

// T2 nas respostas prontas: a grafia errada ganha o aviso da grafia certa; texto da dona entre chaves segue livre.
assert.deepEqual(respostas.variaveisNaoResolvidas('Oi {nome_cliente}, use {CUPOM10}.'), ['{nome_cliente}'])
assert.equal(
  respostas.avisoDeVariaveisNaoResolvidas('Oi {Nome}.'),
  '{Nome} não é preenchido pelo EasyStok: escreva {nome}.',
)
assert.equal(
  respostas.avisoDeVariaveisNaoResolvidas('Oi {nome_cliente}.'),
  '{nome_cliente} não é preenchido pelo EasyStok: escreva {nome}.',
)
assert.equal(
  respostas.avisoDeVariaveisNaoResolvidas('Pedido {pedido}.'),
  'Esta conversa não tem número do pedido: apague {pedido} ou escreva no lugar.',
  'a grafia certa sem dado continua com o aviso de sempre',
)
assert.deepEqual(respostas.resolverVariaveis('Oi {Nome}, {nome}.', { nome: 'Ana Souza' }), { texto: 'Oi {Nome}, Ana.', faltando: [] })
assert.deepEqual(respostas.resolverVariaveis('{constructor}', { nome: 'Ana' }), { texto: '{constructor}', faltando: [] })

// T3: {faixa} de resposta pronta é preenchida com a janela da conversa; sem janela, fica como falta (nunca some calada).
assert.deepEqual(
  respostas.resolverVariaveis('Entrego {faixa}.', { nome: 'Ana' }, { faixa: '11h às 12h' }),
  { texto: 'Entrego 11h às 12h.', faltando: [] },
)
assert.deepEqual(
  respostas.resolverVariaveis('Entrego {faixa}.', { nome: 'Ana' }),
  { texto: 'Entrego {faixa}.', faltando: ['faixa'] },
  'T3: sem janela, {faixa} é apontada como falta',
)

// T7: a prévia do modo API usa a regra do servidor. Variável conhecida vira o exemplo; o resto fica literal e marcado.
assert.deepEqual(
  automacao.previaDoServidor('Oi {nome}, {Nome}, {abre} e {pedido}.', { nome: ' Ana ', pedido: '' }),
  [
    { texto: 'Oi ', tipo: 'texto' },
    { texto: 'Ana', tipo: 'valor' },
    { texto: ', ', tipo: 'texto' },
    { texto: '{Nome}', tipo: 'erro' },
    { texto: ', ', tipo: 'texto' },
    { texto: '{abre}', tipo: 'erro' },
    { texto: ' e ', tipo: 'texto' },
    { texto: '{pedido}', tipo: 'erro' },
    { texto: '.', tipo: 'texto' },
  ],
  'T7: prévia marca o que o servidor deixaria literal ou recusaria',
)
assert.deepEqual(automacao.previaDoServidor('', {}), [])

// T5: a automática ligada não sai quando falta valor; a tela diz quando isso acontece.
assert.equal(automacao.avisoDeValorFaltando(GATILHOS.PRIMEIRO_CONTATO, 'Oi! Pedido {pedido}.'),
  'Com {pedido} esta mensagem não sai: a primeira mensagem de uma conversa nova ainda não tem pedido.')
assert.equal(automacao.avisoDeValorFaltando(GATILHOS.PAGAMENTO_CONFIRMADO, TEXTO_DO_SERVIDOR),
  'Com {nome} esta mensagem não sai se o contato não tiver nome, mesmo ligada. '
  + 'Com {faixa} esta mensagem não sai se o pedido não tiver horário agendado, mesmo ligada.')
assert.equal(automacao.avisoDeValorFaltando(GATILHOS.ENCERRAMENTO, 'Até logo, {nome}! Pedido {pedido}.'),
  'Com {nome} esta mensagem não sai se o contato não tiver nome, mesmo ligada. '
  + 'Com {pedido} esta mensagem não sai se a conversa não tiver pedido em andamento.')
assert.equal(automacao.avisoDeValorFaltando(GATILHOS.POS_ENTREGA, 'Obrigada! Pedido {pedido}.'), null)
assert.equal(automacao.avisoDeValorFaltando(GATILHOS.POS_ENTREGA, 'Obrigada!'), null)

// T4: {pedido} é o mesmo nos dois lados: a descrição da lista do servidor e a do console dizem a mesma coisa.
assert.equal(automacao.VARIAVEIS_DA_API.find((v) => v.chave === 'pedido').descricao, 'número do pedido em andamento, o mesmo que o cliente lê')
assert.equal(automacao.VARIAVEIS.find((v) => v.chave === 'pedido').descricao, 'número do pedido em andamento, o mesmo que o cliente lê')

// T9: o modal usa o nome único do recurso (dominio/modulos.js).
const modal = lerFonte('features/automacoes/ModalAutomacoes.jsx')
assert.match(modal, /titulo=\{NOME_RESPOSTAS_AUTOMATICAS\}/, 'T9: título vem da constante')
assert.doesNotMatch(modal, /Mensagens automáticas/, 'T9: sem o nome antigo')
assert.equal(NOME_RESPOSTAS_AUTOMATICAS, 'Respostas automáticas')

// T7/T5 na tela: o modal usa a prévia do servidor e o aviso de valor faltando só no modo API.
assert.match(modal, /previaDoServidor\(/, 'T7: o modal monta a prévia pela regra do servidor')
assert.match(modal, /avisoDeValorFaltando\(/, 'T5: o modal avisa quando falta valor')

// T3 no atalho "/": a janela do pedido entra no preenchimento.
const atalho = lerFonte('aplicacao/useAtalhoBarra.js')
assert.match(atalho, /faixaDoPedido\(/, 'T3: o atalho resolve {faixa} pela janela do pedido')

console.log('prova-2734-modelos-mensagem: ok')
