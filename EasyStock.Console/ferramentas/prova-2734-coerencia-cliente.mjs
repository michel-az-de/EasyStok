/* eslint-disable no-console */
// #2734: o cliente é o mesmo em Cadastros e na Ficha do Atendimento. Regras puras do relacionamento
// (nota, tag, telefone principal, atalho direto ao cadastro, prévia das notas) e a tradução única do dossiê.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const cadastro = await import('../src/dominio/fichaCliente.js')
const { previaDasNotas } = await import('../src/dominio/ficha.js')
const { moeda } = await import('../src/dominio/formato.js')
const { clienteDoDossie, relacionamentoDoDossie } = await import('../src/infra/api/traducaoCliente.js')

const ID = '3f2b8c1e-5a47-4d09-9b6e-1c2d3e4f5a6b'

// Nota: o teto do servidor (500) vira mensagem no campo, sem cortar o texto colado.
assert.equal(cadastro.erroDaNota('   '), 'Escreva a nota.')
assert.equal(cadastro.erroDaNota('a'.repeat(500)), null)
assert.equal(cadastro.erroDaNota('a'.repeat(501)), 'Use até 500 caracteres. Agora tem 501.')

// Tag: teto do servidor (40) e repetida pela mesma chave da Ficha (sem caixa, acento nem sublinhado).
assert.equal(cadastro.erroDaTag('', []), 'Escreva a tag.')
assert.equal(cadastro.erroDaTag('x'.repeat(41), []), 'Use até 40 caracteres. Agora tem 41.')
assert.equal(cadastro.erroDaTag('Alérgico', ['alergico']), 'Esta tag já existe.')
assert.equal(cadastro.erroDaTag('sem gluten', ['sem_gluten']), 'Esta tag já existe.')
assert.equal(cadastro.erroDaTag('vip', ['alergico']), null)

// Telefones: o principal vem primeiro e não se remove pela lista (ele muda no campo Telefone).
assert.deepEqual(cadastro.telefonesDaFicha([
  { id: 'T1', numero: '+5511955554444', whatsapp: true, principal: false },
  { id: 'T0', numero: '+5511999998888', whatsapp: true, principal: true },
]).map(t => [t.id, t.removivel]), [['T0', false], ['T1', true]])
assert.deepEqual(cadastro.telefonesDaFicha(undefined), [])

// Atalho da Ficha para o cadastro completo: terceiro segmento da rota, só com id de cadastro.
assert.equal(cadastro.hashDoCadastroDoCliente(ID), `#/m/cadastros/clientes/${ID}`)
assert.equal(cadastro.clienteDaHash(`#/m/cadastros/clientes/${ID}`), ID)
assert.equal(cadastro.clienteDaHash('#/m/cadastros/clientes'), null)
assert.equal(cadastro.clienteDaHash(`#/m/cadastros/produtos/${ID}`), null)
assert.equal(cadastro.clienteDaHash('#/m/cadastros/clientes/novo'), null)

// Números do relacionamento, com as palavras da Ficha ("valor total", "costuma pedir").
assert.deepEqual(cadastro.numerosDoRelacionamento({ pedidos: 7, totalGasto: 1234.5, favorito: 'Lasanha' }),
  [`Valor total ${moeda(1234.5)}`, 'Costuma pedir Lasanha'])
assert.deepEqual(cadastro.numerosDoRelacionamento({ pedidos: 0, totalGasto: 0, favorito: null }), [])
assert.deepEqual(cadastro.numerosDoRelacionamento(null), [])

// O dossiê traz as 20 notas mais recentes: com 20 na tela, o cadastro diz onde estão as outras.
assert.equal(cadastro.avisoDasNotas(20), 'Aqui aparecem as 20 notas mais recentes. As anteriores estão na Ficha do Atendimento.')
assert.equal(cadastro.avisoDasNotas(19), null)

// Ficha do Atendimento: a nota aparece sem clique. Recolhida, mostra as 2 mais recentes.
assert.deepEqual(previaDasNotas(['a', 'b', 'c'], false), { visiveis: ['a', 'b'], escondidas: 1 })
assert.deepEqual(previaDasNotas(['a', 'b', 'c'], true), { visiveis: ['a', 'b', 'c'], escondidas: 0 })
assert.deepEqual(previaDasNotas(['a'], false), { visiveis: ['a'], escondidas: 0 })
assert.deepEqual(previaDasNotas(undefined, false), { visiveis: [], escondidas: 0 })

// Uma tradução só do dossiê para as duas telas.
const dossie = {
  cliente: { id: ID, nome: 'Marina Teste', telefone: '+5511999998888', email: null, observacoes: 'Portão azul', motivoBloqueio: 'Pedido não pago', fotoUrl: null },
  enderecos: [],
  tags: [{ tag: 'alergico', origem: 'Manual', criadoEm: '2026-11-01T10:00:00' }],
  notas: [{ id: 'N1', texto: 'Alergia a camarao.', autor: 'Felipe', pedidoId: null, mensagemId: null, criadoEm: '2026-11-12T21:00:00' }],
  ultimosPedidos: [], itemFavorito: { nome: 'Lasanha', pedidos: 3 }, ultimaCompraEm: '2026-11-10T15:30:00',
  totalPedidos: 7, bloqueado: true, totalGasto: 1234.5,
  conversasRecentes: [{ id: 'C1', canal: 'ChatSite', situacao: 'Automatica', iniciadaEm: '2026-11-12T20:00:00', ultimaMensagemEm: '2026-11-12T21:05:00' }],
}
const lido = relacionamentoDoDossie(dossie)
assert.deepEqual(lido.tags, ['alergico'])
assert.deepEqual(lido.notas.map(n => [n.id, n.texto, n.autor, n.em]), [['N1', 'Alergia a camarao.', 'Felipe', '12/11, 18:00']])
assert.deepEqual(lido.bloqueio, { motivo: 'Pedido não pago', em: null, por: null, alcance: 'todos os canais' })
assert.deepEqual([lido.pedidos, lido.totalGasto, lido.favorito], [7, 1234.5, 'Lasanha'])
assert.deepEqual(lido.conversas, [{ id: 'C1', canal: 'Chat do site', estado: 'Aberto', em: '12/11, 18:05' }])
assert.equal(relacionamentoDoDossie({}), null)
assert.equal(relacionamentoDoDossie({ ...dossie, bloqueado: false, conversasRecentes: undefined }).bloqueio, null)
assert.deepEqual(relacionamentoDoDossie({ ...dossie, conversasRecentes: undefined }).conversas, [])

// C-03: a observação do cadastro chega à Ficha do Atendimento (antes o campo era lido e descartado).
assert.equal(clienteDoDossie(dossie).cliente.observacoes, 'Portão azul')
assert.equal(clienteDoDossie({ ...dossie, cliente: { ...dossie.cliente, observacoes: '   ' } }).cliente.observacoes, null)
// #2780: cadastro desativado chega à Ficha marcado; sem o campo (API anterior), segue ativo.
assert.equal(clienteDoDossie({ ...dossie, cliente: { ...dossie.cliente, ativo: false } }).cliente.inativo, true)
assert.equal(clienteDoDossie(dossie).cliente.inativo, false)

// O editor de cliente também abre pela Buscar na Casa, fora do AtendimentoProvider: nada nele pode ler o contexto
// do Atendimento (useAtendimento, useAcoes e useCatalogo lançam sem o provider e derrubam o console).
const { readFileSync } = await import('node:fs')
for (const arquivo of ['EditorCadastro.jsx', 'FichaCliente.jsx', 'RelacionamentoCliente.jsx']) {
  const fonte = readFileSync(new URL(`../src/features/gestao/cadastros/${arquivo}`, import.meta.url), 'utf8')
  assert.equal(/aplicacao\/contextos'/.test(fonte), false, `${arquivo} não lê o contexto do Atendimento`)
}

// Por isso as restrições do catálogo chegam ao editor por propriedade, também pela Buscar na Casa.
const app = readFileSync(new URL('../src/app/App.jsx', import.meta.url), 'utf8')
// #2849: a mesma função desenha a ficha na Busca e no Atendimento.
assert.match(app, /const renderizarFichaUnica = props => <EditorCadastro \{\.\.\.props\} restricoes=\{RESTRICOES\} \/>/, 'a ficha única recebe as restrições')
assert.match(app, /renderizarCadastro=\{renderizarFichaUnica\}/, 'a Buscar na Casa entrega as restrições ao editor')
assert.match(app, /<ContextoFichaUnicaDoCliente\.Provider value=\{renderizarFichaUnica\}>/, 'o Atendimento recebe a mesma ficha')

// Telefone de DDD 55 (Rio Grande do Sul): o texto que já está na máscara "(55) …" não tem DDI. Um dígito a mais
// é erro que a pessoa vê, não outro número gravado em silêncio.
const { telefoneNaTela } = await import('../src/dominio/cadastros.js')
const { erroDoTelefone } = await import('../src/dominio/validacaoDoCadastro.js')
assert.equal(telefoneNaTela('(55) 99999-8888'), '(55) 99999-8888')
assert.equal(telefoneNaTela('(55) 99999-88887'), '(55) 99999-88887', 'o dígito a mais não troca o DDD')
const TELEFONE_LONGO = 'Telefone com mais de 11 dígitos. Use só o DDD e o número.'
assert.equal(erroDoTelefone(telefoneNaTela('(55) 99999-88887')), TELEFONE_LONGO)
assert.equal(telefoneNaTela('5511999998888'), '(11) 99999-8888', 'colado com DDI segue virando o número nacional')
assert.equal(telefoneNaTela('+55 11 99999-8888'), '(11) 99999-8888')
assert.equal(telefoneNaTela('+55 (55) 99999-8888'), '(55) 99999-8888')
assert.equal(telefoneNaTela('+44 7911 123456'), '+44 7911 123456', 'número de outro país fica como foi digitado, sem virar número do Brasil')

// A validação lê o telefone como a máscara e segue a regra da rota que grava.
// 1. Ficha do Atendimento (edição em linha e cadastro rápido): grava pela conversa, e essa rota só aceita telefone
//    do Brasil. A prova lê o servidor para a regra da tela não se separar da dele: na tela real, o número de outro
//    país que a tela aceitava voltava com 400.
const servidor = (caminho) => readFileSync(new URL(`../../${caminho}`, import.meta.url), 'utf8')
assert.match(servidor('EasyStock.Domain/ValueObjects/TelefoneE164.cs'), /\^\\\+55\[1-9\]\[0-9\]\\d\{8,9\}\$/, 'TelefoneE164: +55, DDD que não começa com zero, 8 ou 9 dígitos')
assert.match(servidor('EasyStock.Application/Services/Atendimento/NormalizadorTelefone.cs'), /NormalizarE164Br\(string telefone\) => TelefoneE164\.From\(telefone\)\.Value/)
assert.match(servidor('EasyStock.Application/UseCases/Atendimento/ClienteDaConversa/CadastrarClienteDaConversaUseCase.cs'), /NormalizadorTelefone\.NormalizarE164Br\(telefone\)/, 'a rota da conversa normaliza pelo E.164 do Brasil')
for (const doBrasil of ['+55 11 99999-8888', '5511999998888', '+55 (31) 3333-4444', '(55) 99999-8888', '(31) 3333-4444']) {
  assert.equal(erroDoTelefone(doBrasil), null, doBrasil)
  assert.equal(erroDoTelefone(telefoneNaTela(doBrasil)), null, `${doBrasil} depois da máscara`)
}
const SO_DO_BRASIL = 'Aqui só entra telefone do Brasil, com DDD. O de outro país entra em Cadastros.'
for (const deFora of ['+44 7911 123456', '+351 912 345 678', '+1 415 555 0100', '+44 79']) {
  assert.equal(erroDoTelefone(deFora), SO_DO_BRASIL, `${deFora} na Ficha: recusado com a saída, sem ir ao servidor`)
}
assert.equal(erroDoTelefone('+5'), 'Telefone incompleto', 'um dígito depois do + ainda pode ser o 55')
assert.equal(erroDoTelefone('+55 11 99999-88887'), TELEFONE_LONGO, 'DDI 55 com um dígito a mais segue recusado')
assert.equal(erroDoTelefone('447911123456'), TELEFONE_LONGO, 'na Ficha a mensagem não ensina o +, que ela não aceita')
assert.equal(erroDoTelefone('+55 (31) 99999-999'), 'Telefone incompleto', 'celular sem o último número, com DDI')
assert.equal(erroDoTelefone('+'), 'Informe o telefone com DDD')
for (const dddZero of ['(01) 99999-8888', '0800 123 4567', '+55 01 99999-8888']) {
  assert.equal(erroDoTelefone(dddZero), 'O DDD não começa com zero. Confira o número.', dddZero)
}

// 2. Telefone extra de Cadastros: `/api/clientes/{id}/telefones` aceita número de outro país com 7 a 15 dígitos.
assert.match(servidor('EasyStock.Domain/ValueObjects/Telefone.cs'), /\^\\\+\?\\d\{7,15\}\$/, 'Telefone: 7 a 15 dígitos, com ou sem +')
assert.match(servidor('EasyStock.Application/UseCases/AdicionarClienteTelefone/AdicionarClienteTelefoneUseCase.cs'), /Telefone\.TryFrom\(cmd\.Numero\)/, 'o telefone extra usa a regra frouxa')
const TELEFONE_LONGO_COM_SAIDA = `${TELEFONE_LONGO} Para outro país, comece com + e o código do país.`
for (const aceito of ['+44 7911 123456', '+351 912 345 678', '+1 415 555 0100', '+55 11 99999-8888', '5511999998888', '(31) 3333-4444', '0800 123 4567']) {
  assert.equal(cadastro.erroDoTelefoneExtra(aceito), null, `${aceito} como telefone extra`)
}
assert.equal(cadastro.erroDoTelefoneExtra('+44 79'), 'Telefone incompleto')
assert.equal(cadastro.erroDoTelefoneExtra('+44 7911 123456 7890 12'), 'Telefone com mais de 15 dígitos. Confira o número.')
assert.equal(cadastro.erroDoTelefoneExtra('447911123456'), TELEFONE_LONGO_COM_SAIDA, 'sem o + não dá para saber que é de outro país: a mensagem diz como escrever')
assert.equal(cadastro.erroDoTelefoneExtra('+55 11 99999-88887'), TELEFONE_LONGO_COM_SAIDA)

// Texto de tela sem travessão no histórico do cliente.
const historico = readFileSync(new URL('../src/features/ficha-cliente/ModalHistorico.jsx', import.meta.url), 'utf8')
assert.equal(historico.includes('—'), false, 'ModalHistorico sem travessão')
assert.match(historico, /'Sem venda'/)

// Quem mostra a pessoa: as conversas carregadas com o mesmo cadastro.
const { conversasDoCliente } = await import('../src/dominio/conversa.js')
assert.deepEqual(conversasDoCliente([{ id: 'C1', clienteId: ID }, { id: 'C2', clienteId: 'outra' }, { id: 'C3', clienteId: ID }], ID), ['C1', 'C3'])
assert.deepEqual(conversasDoCliente([{ id: 'C1', clienteId: null }], null), [], 'contato sem cadastro não casa com ninguém')
assert.deepEqual(conversasDoCliente(undefined, ID), [])

// O cadastro gravado fora do Atendimento avisa; quem assinou recebe, e um ouvinte com defeito não cala os outros.
const { avisarMudancaNoCliente, assinarMudancaNoCliente } = await import('../src/aplicacao/mudancasDoCliente.js')
const avisos = []
const largarDefeito = assinarMudancaNoCliente(() => { throw new Error('ouvinte com defeito') })
const largar = assinarMudancaNoCliente(id => avisos.push(id))
avisarMudancaNoCliente(ID); avisarMudancaNoCliente(null); avisarMudancaNoCliente('')
assert.deepEqual(avisos, [ID], 'o aviso chega a quem assinou; sem id não avisa')
largar(); largarDefeito()
avisarMudancaNoCliente(ID)
assert.deepEqual(avisos, [ID], 'quem cancelou a assinatura não ouve mais')

// A Ficha do Atendimento assina o aviso e relê; a Buscar na Casa avisa ao gravar; endereço e telefone também avisam.
const provedor = readFileSync(new URL('../src/aplicacao/AtendimentoProvider.jsx', import.meta.url), 'utf8')
assert.match(provedor, /assinarMudancaNoCliente\(/, 'o Atendimento ouve a mudança do cadastro')
assert.match(provedor, /relerClienteDaConversa/, 'e relê a conversa da pessoa')
const busca = readFileSync(new URL('../src/features/busca/BuscaNaCasa.jsx', import.meta.url), 'utf8')
assert.match(busca, /aoGravarRelacionamento: \(\) => avisarMudancaNoCliente\(/, 'nota e tag gravadas pela Buscar na Casa avisam')
assert.match(busca, /aoSalvo: \(\) => \{[^}]*avisarMudancaNoCliente\(/, 'o cadastro salvo pela Buscar na Casa avisa')
const editor = readFileSync(new URL('../src/features/gestao/cadastros/EditorCadastro.jsx', import.meta.url), 'utf8')
assert.match(editor, /<FichaCliente[\s\S]*?aoGravar=\{aoGravarRelacionamento\}/, 'endereço e telefone gravados avisam como nota e tag')

console.log('prova-2734-coerencia-cliente ok: nota, tag, telefone principal e de DDD 55, atalho, números, prévia, tradução do dossiê, editor fora do Atendimento e aviso de mudança')
