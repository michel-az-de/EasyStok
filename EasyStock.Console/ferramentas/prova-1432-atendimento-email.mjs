// Prova da #1432: atendimento por e-mail no console.
//  1. O catálogo tem o canal E-mail (chip do filtro), sem janela, aceitando foto.
//  2. A conversa e a mensagem da API trazem o assunto; o fio mostra o assunto só quando muda.
//  3. Integrações > E-mail: formulário com o padrão da Hostinger, validação, senha vazia não vai
//     no corpo (a API mantém a gravada) e as três chamadas certas à API.
//
//   node ferramentas/prova-1432-atendimento-email.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { CANAIS, NOMES_DE_CANAL } = await import('../src/infra/catalogo.js')
const { canalPorNome, restricoesDoCanal } = await import('../src/dominio/canal.js')
const { conversaDaApi, mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const email = await import('../src/dominio/email.js')
const caixaApi = await import('../src/infra/api/caixaEmailApi.js')

// 1. Canal no catálogo, com ícone desenhado.
assert.ok(NOMES_DE_CANAL.includes('E-mail'), 'o filtro de canal ganha o chip E-mail')
const canal = canalPorNome(CANAIS, 'E-mail')
assert.equal(canal.desconhecido, undefined, 'E-mail é canal declarado, não o padrão silencioso')
assert.equal(canal.temJanela, false, 'e-mail não tem janela de 24 h')
assert.equal(canal.aceitaModelo, false)
assert.equal(canal.aceitaMidia.foto, true, 'foto sai como imagem no corpo')
assert.match(restricoesDoCanal(canal), /arquivo/, 'o composer avisa que arquivo não vai')
const icones = readFileSync(new URL('../src/componentes/Icone.jsx', import.meta.url), 'utf8')
assert.match(icones, new RegExp(`\\n\\s*${canal.icone}: '`), 'o ícone do canal existe no conjunto')

// 2. Assunto da API até o cartão e o fio.
const resumo = {
  id: 'c1', canal: 'Email', contatoIdExterno: 'maria@exemplo.com', contatoNome: 'Maria Souza', clienteId: null,
  situacao: 'Assumida', naoLidas: 1, ultimaMensagemEm: '2026-10-07T13:00:00', ultimaMensagemTexto: 'Quero 2 lasanhas.',
  pedidoEmAndamentoId: null, assumidaPorUsuarioId: null, dentroDaJanela: true, motivoEscalada: null,
  assunto: 'Encomenda de bolo',
}
const msgs = [
  { id: 'm1', direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: 'Quero 2 lasanhas.', status: 'Entregue', enviadaEm: '2026-10-07T12:00:00', assunto: 'Encomenda de bolo' },
  { id: 'm2', direcao: 'Saida', autor: 'Dona', tipoConteudo: 'Texto', texto: 'Temos!', status: 'Enviada', enviadaEm: '2026-10-07T12:05:00' },
  { id: 'm3', direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: 'Para sábado.', status: 'Entregue', enviadaEm: '2026-10-07T12:10:00', assunto: 'Re: Encomenda de bolo' },
  { id: 'm4', direcao: 'Entrada', autor: 'Cliente', tipoConteudo: 'Texto', texto: 'Outra coisa.', status: 'Entregue', enviadaEm: '2026-10-07T12:20:00', assunto: 'RES: Nota fiscal' },
]
const conversa = conversaDaApi(resumo, msgs, { id: 'u1', nome: 'Thati' })
assert.equal(conversa.canal, 'E-mail', 'Email da API vira o nome do catálogo')
assert.equal(conversa.assunto, 'Encomenda de bolo', 'o cartão lê o assunto da conversa')
assert.equal(conversa.mensagens[0].assunto, 'Encomenda de bolo')
assert.equal(mensagemDaApi({ ...msgs[1] }).assunto, undefined, 'mensagem sem assunto não ganha o campo')
assert.equal(conversaDaApi({ ...resumo, canal: 'WhatsApp', assunto: undefined }, [], null).assunto, null)
assert.deepEqual(
  email.marcarMudancasDeAssunto(conversa.mensagens),
  ['Encomenda de bolo', null, null, 'RES: Nota fiscal'],
  '"Re:" do mesmo assunto não repete; assunto novo aparece',
)
assert.equal(email.semPrefixos('RES: Re: Fwd: Bolo'), 'Bolo')
for (const arquivo of ['../src/features/caixa-de-entrada/CartaoConversa.jsx', '../src/features/atendimento/Thread.jsx']) {
  assert.match(readFileSync(new URL(arquivo, import.meta.url), 'utf8'), /<AssuntoEmail /, `${arquivo} mostra o assunto`)
}

// 3. Formulário da caixa.
const vazio = email.formularioDaCaixa(null)
assert.equal(vazio.imapHost, 'imap.hostinger.com')
assert.equal(vazio.imapPorta, 993)
assert.equal(vazio.smtpHost, 'smtp.hostinger.com')
assert.equal(vazio.smtpPorta, 465)
assert.match(email.problemaDaCaixa(vazio, false), /e-mail completo/)
const preenchido = { ...vazio, endereco: ' contato@casadababa.com ', nomeExibicao: 'Casa da Baba', usuario: 'contato@casadababa.com' }
assert.match(email.problemaDaCaixa(preenchido, false), /senha/, 'primeira vez exige senha')
assert.equal(email.problemaDaCaixa(preenchido, true), null, 'com senha gravada, campo vazio mantém')
assert.match(email.problemaDaCaixa({ ...preenchido, imapHost: 'imap.hostinger.com:993' }, true), /IMAP/)
assert.match(email.problemaDaCaixa({ ...preenchido, smtpPorta: '0' }, true), /Porta SMTP/)
const corpoSemSenha = email.corpoDaCaixa({ ...preenchido, smtpPorta: '587' })
assert.equal('senha' in corpoSemSenha, false, 'senha vazia não vai no corpo')
assert.equal(corpoSemSenha.endereco, 'contato@casadababa.com')
assert.equal(corpoSemSenha.smtpPorta, 587)
const daApi = email.formularioDaCaixa({ endereco: 'contato@casadababa.com', imapHost: 'imap.x', imapPorta: 993, smtpHost: 'smtp.x', smtpPorta: 465, usuario: 'u', senhaDefinida: true })
assert.equal(daApi.senha, '', 'a senha nunca chega ao formulário')
assert.deepEqual(email.textoDoTeste({ imapOk: true, smtpOk: false, smtpErro: 'SMTP: usuário ou senha recusados' }),
  ['Leitura (IMAP): conectou e entrou.', 'Envio (SMTP): SMTP: usuário ou senha recusados'])

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }
const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body ? JSON.parse(opcoes.body) : undefined })
  return new Response(JSON.stringify({ data: { ok: true } }))
}
await caixaApi.obterCaixaEmail()
await caixaApi.salvarCaixaEmail(corpoSemSenha)
await caixaApi.testarCaixaEmail(email.corpoDaCaixa({ ...preenchido, senha: 'digitada' }))
assert.deepEqual(chamadas.map((c) => `${c.metodo} ${c.url}`), [
  'GET /api/integracoes/email/caixa',
  'PUT /api/integracoes/email/caixa',
  'POST /api/integracoes/email/caixa/teste',
])
assert.equal(chamadas[1].corpo.senha, undefined)
assert.equal(chamadas[2].corpo.senha, 'digitada', 'o teste usa a senha digitada antes de salvar')

console.log('prova-1432: canal E-mail, assunto e caixa de suporte OK')
