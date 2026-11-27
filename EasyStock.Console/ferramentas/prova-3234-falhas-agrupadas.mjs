/* eslint-disable no-console */
// Prova da #3234 (C1, A2): envios seguidos que falharam com o mesmo texto e o mesmo tipo de falha viram um balão só
// no fio, com o aviso uma vez. Na produção eram 4 balões iguais com "O WhatsApp demorou a responder" repetido.
// Só regra pura, sem navegador (a prova-2185 E32 mede a tela).
//   a  4 falhas seguidas iguais viram 1 linha com 3 anteriores; a mais recente fica à vista;
//   b  texto diferente, tipo diferente, mensagem do cliente, dia, nota ou divisória quebram o grupo;
//   c  o aviso do grupo sai do tipo: tempo esgotado diz "pode ter chegado", nunca "não saíram";
//   d, e, f  foto/modelo/formato/arte, recusa de outro motivo e mensagem reenviada não agrupam;
//   g, h  o grupo aberto continua aberto quando cresce; abertas e soltas são de cada conversa;
//   j  (A2b) o mesmo modelo com o mesmo texto agrupa, como na produção; nome diferente, texto livre igual ou
//      modelo sem texto não. Entra pelo formato da API (mensagemDaApi), o mesmo caminho da tela.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// O src importa sem extensão (Vite); aqui o Node pede o .js (como a prova-1761).
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { agruparFalhasSeguidas, avisoDasTentativas, marcarNaConversa, marcasDaConversa } = await import('../src/dominio/falhasSeguidas.js')
const { TIPOS_DE_FALHA, falhaDeEnvio, semOReenvio } = await import('../src/dominio/falhaDeEnvio.js')
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const TIMEOUT = 'The operation was canceled. Envio incerto.'
const msg = (id, extra = {}) => ({ id, dir: 'out', texto: 'Oi, tudo bem?', status: 'falhou', erro: TIMEOUT, ...extra })
const linha = (m) => ({ tipo: 'mensagem', chave: m.id, mensagem: m, trocaDeVoz: false })
const ids = (linhas) => linhas.map((l) => (l.tipo === 'mensagem' ? `${l.mensagem.id}${l.anteriores?.length ? `+${l.anteriores.map((a) => a.id).join('+')}` : ''}` : l.tipo))

confere('a  4 falhas seguidas iguais viram 1 linha; a mais recente à vista, as 3 de antes guardadas em ordem', () => {
  const saida = agruparFalhasSeguidas([{ ...linha(msg('m1')), trocaDeVoz: true }, linha(msg('m2')), linha(msg('m3')), linha(msg('m4'))])
  assert.deepEqual(ids(saida), ['m4+m1+m2+m3'])
  assert.equal(saida[0].chave, 'm4')
  // A linha do grupo começa onde a primeira tentativa começava: a troca de voz (espaço antes) é dela.
  assert.equal(saida[0].trocaDeVoz, true, 'o grupo perdeu a troca de voz da primeira tentativa')
})

confere('b  só agrupa o que é igual e seguido no fio', () => {
  const entregue = msg('ok', { status: 'entregue', erro: null })
  const cliente = msg('in', { dir: 'in', status: null, erro: null })
  const outroTexto = msg('t2', { texto: 'Outra coisa' })
  const outroTipo = msg('n1', { erro: 'Message undeliverable' })
  assert.deepEqual(ids(agruparFalhasSeguidas([linha(msg('m1')), linha(outroTexto), linha(msg('m2'))])), ['m1', 't2', 'm2'])
  assert.deepEqual(ids(agruparFalhasSeguidas([linha(msg('m1')), linha(outroTipo)])), ['m1', 'n1'])
  assert.deepEqual(ids(agruparFalhasSeguidas([linha(msg('m1')), linha(cliente), linha(msg('m2'))])), ['m1', 'in', 'm2'])
  assert.deepEqual(ids(agruparFalhasSeguidas([linha(msg('m1')), linha(entregue), linha(msg('m2'))])), ['m1', 'ok', 'm2'])
  for (const tipo of ['dia', 'nota', 'divisoria', 'assunto']) {
    assert.deepEqual(ids(agruparFalhasSeguidas([linha(msg('m1')), { tipo, chave: tipo }, linha(msg('m2'))])), ['m1', tipo, 'm2'], `${tipo} não quebrou o grupo`)
  }
})

// O modelo daqui não tem nome (a demonstração cria assim); o modelo da API está no caso j.
confere('d  foto, modelo sem nome e anexo não agrupam: o texto deles é rótulo ("Foto"), não o conteúdo', () => {
  for (const extra of [{ midia: { conversaId: 'c1', mensagemId: 'x' }, texto: 'Foto' }, { modelo: true, texto: 'Mensagem de modelo sem texto' }, { formato: 'imagem' }, { arte: 'a.png' }]) {
    const saida = agruparFalhasSeguidas([linha(msg('m1', extra)), linha(msg('m2', extra))])
    assert.deepEqual(ids(saida), ['m1', 'm2'], JSON.stringify(extra))
  }
})

confere('e  recusa com motivo diferente não agrupa: o motivo de cada uma se perderia', () => {
  const saida = agruparFalhasSeguidas([linha(msg('r1', { erro: 'Não enviada: Sem conexão' })), linha(msg('r2', { erro: 'Não enviada: Número inválido' }))])
  assert.deepEqual(ids(saida), ['r1', 'r2'])
})

confere('f  a mensagem que a atendente reenviou fica solta: não some dentro de um grupo fechado', () => {
  const linhas = [linha(msg('m1')), linha(msg('m2')), linha(msg('m3')), linha(msg('m4'))]
  assert.deepEqual(ids(agruparFalhasSeguidas(linhas, { soltas: new Set(['m2']) })), ['m1', 'm2', 'm4+m3'])
})

confere('i  o que saiu por SMS, espera o cliente, não tem id no servidor ou foi apagado não agrupa: cada um tem o seu estado', () => {
  for (const extra of [{ porSms: true }, { aguardaCliente: true }, { semIdServidor: true }, { apagadaEm: '2026-11-23T15:00:00Z' }]) {
    const chave = Object.keys(extra)[0]
    const saida = agruparFalhasSeguidas([linha(msg('m1', extra)), linha(msg('m2')), linha(msg('m3', extra))])
    assert.deepEqual(saida.map((l) => l.mensagem.id + (l.anteriores?.length ? '+' + l.anteriores.length : '')), ['m1', 'm2', 'm3'], `${chave} entrou num grupo`)
  }
})

confere('g  o grupo é guardado pela tentativa mais antiga: aberto continua aberto quando chega outra falha igual', () => {
  const dois = agruparFalhasSeguidas([linha(msg('m1')), linha(msg('m2'))])
  const tres = agruparFalhasSeguidas([linha(msg('m1')), linha(msg('m2')), linha(msg('m3'))])
  assert.equal(dois[0].chaveDoGrupo, 'm1')
  assert.equal(tres[0].chaveDoGrupo, 'm1', 'a chave do grupo mudou quando ele cresceu')
  assert.equal(agruparFalhasSeguidas([linha(msg('m1'))])[0].chaveDoGrupo, undefined, 'mensagem sozinha não é grupo')
})

confere('h  abertas e soltas são de cada conversa: ir a outra e voltar não perde nem mistura', () => {
  let marcas = marcarNaConversa({}, 'A', 'abertas', 'm1', true)
  marcas = marcarNaConversa(marcas, 'A', 'soltas', 'm9', false)
  marcas = marcarNaConversa(marcas, 'B', 'abertas', 'x1', true)
  assert.deepEqual([...marcasDaConversa(marcas, 'A').abertas], ['m1'], 'clicar em B apagou o que estava aberto em A')
  assert.deepEqual([...marcasDaConversa(marcas, 'A').soltas], ['m9'])
  assert.deepEqual([...marcasDaConversa(marcas, 'B').abertas], ['x1'])
  assert.equal(marcasDaConversa(marcas, 'B').soltas.size, 0, 'a solta de A vazou para B')
  assert.equal(marcasDaConversa(marcas, 'C').abertas.size, 0)
  const fechado = marcarNaConversa(marcas, 'A', 'abertas', 'm1', true)
  assert.equal(marcasDaConversa(fechado, 'A').abertas.size, 0, 'alternar de novo não fechou')
  assert.deepEqual([...marcasDaConversa(marcarNaConversa(marcas, 'A', 'soltas', 'm9', false), 'A').soltas], ['m9'], 'soltar de novo não pode desfazer')
  assert.equal(marcasDaConversa(marcas, 'A').abertas.has('m1'), true, 'o estado anterior foi mutado')
})

confere('j  o mesmo modelo com o mesmo texto agrupa; nome diferente, texto livre igual ou modelo sem texto não', () => {
  // O formato da API (MensagemAtendimentoResult), traduzido como a tela traduz: a produção tinha 4 destes seguidos.
  // Como EnviarModeloConversaUseCase grava: tipo Modelo, cabeçalho, corpo e rodapé numa string só, por quebra de linha.
  const PRONTO = 'Pedido 21 pronto\nOi Ana, seu pedido 21 está pronto para retirar.\nEasyStok'
  const daApi = (id, extra = {}) => linha(mensagemDaApi({
    id, direcao: 'Saida', autor: 'Usuario', tipoConteudo: 'Modelo', texto: PRONTO, status: 'Falhou', erro: TIMEOUT,
    enviadaEm: '2026-11-23T15:00:00', modelo: { nome: 'pedido_pronto', idioma: 'pt_BR' }, ...extra,
  }, 'c1'))
  assert.deepEqual(ids(agruparFalhasSeguidas([daApi('p1'), daApi('p2'), daApi('p3'), daApi('p4')])), ['p4+p1+p2+p3'], 'o mesmo modelo repetido não agrupou')
  const outroModelo = daApi('o1', { modelo: { nome: 'retomada', idioma: 'pt_BR' } })
  assert.deepEqual(ids(agruparFalhasSeguidas([daApi('p1'), outroModelo])), ['p1', 'o1'], 'modelos diferentes com o mesmo texto agruparam')
  const textoLivre = daApi('t1', { modelo: null, tipoConteudo: 'Texto' })
  assert.deepEqual(ids(agruparFalhasSeguidas([textoLivre, daApi('p1')])), ['t1', 'p1'], 'texto livre e modelo com o mesmo texto agruparam')
  assert.deepEqual(ids(agruparFalhasSeguidas([daApi('p1'), textoLivre])), ['p1', 't1'], 'modelo e texto livre com o mesmo texto agruparam')
  // Sem texto, a tradução põe o rótulo no lugar: dois envios do mesmo modelo podem ter parâmetros diferentes.
  const semTexto = (id) => daApi(id, { tipoConteudo: 'Modelo', texto: '' })
  assert.equal(semTexto('s1').mensagem.texto, 'Mensagem de modelo sem texto')
  assert.deepEqual(ids(agruparFalhasSeguidas([semTexto('s1'), semTexto('s2')])), ['s1', 's2'], 'modelo sem texto agrupou pelo rótulo')
  const emBranco = (id) => daApi(id, { texto: '   ' })
  assert.deepEqual(ids(agruparFalhasSeguidas([emBranco('b1'), emBranco('b2')])), ['b1', 'b2'], 'modelo com texto em branco agrupou pelo rótulo')
  // Banca A2b: a programada tem selo próprio e não se junta ao envio feito na hora. O disparo
  // (DisparoMensagensProgramadas) a grava como Texto, sem modelo; por isso o par é contra o texto livre igual.
  const programada = daApi('g1', { modelo: null, tipoConteudo: 'Texto', programada: true })
  assert.deepEqual(ids(agruparFalhasSeguidas([programada, textoLivre])), ['g1', 't1'], 'programada e texto igual feito na hora agruparam')
  assert.deepEqual(ids(agruparFalhasSeguidas([textoLivre, programada])), ['t1', 'g1'], 'texto feito na hora e programada igual agruparam')
  // Retomada (espera o cliente), saída por SMS, cabeçalho com mídia e outra falha têm estado próprio.
  for (const [extra, motivo] of [[{ aguardaClienteDesde: '2026-11-23T15:00:00' }, 'retomada'], [{ reservaSmsEm: '2026-11-23T15:00:00' }, 'SMS'],
    [{ midiaChave: 'midia/x.jpg', midiaMime: 'image/jpeg' }, 'cabeçalho com mídia'], [{ erro: 'Não enviada: Sem conexão' }, 'outra falha']]) {
    assert.deepEqual(ids(agruparFalhasSeguidas([daApi('p1'), daApi('x1', extra), daApi('p2')])), ['p1', 'x1', 'p2'], `${motivo} entrou num grupo de modelo`)
  }
  // O texto livre continua agrupando pelo mesmo caminho da API.
  assert.deepEqual(ids(agruparFalhasSeguidas([daApi('a1', { modelo: null, tipoConteudo: 'Texto' }), daApi('a2', { modelo: null, tipoConteudo: 'Texto' })])), ['a2+a1'])
})

// #3374 (C12): modelo não tem Reenviar, então o aviso não manda "reenviar".
confere('k  a frase de modelo não fala em reenviar; a de texto continua igual', () => {
  const grupo = avisoDasTentativas(falhaDeEnvio('timeout'), 4)
  assert.match(grupo, /antes de reenviar/)
  assert.equal(semOReenvio(grupo), '4 tentativas sem resposta do WhatsApp. Pode ter chegado: confira com o cliente.')
  assert.doesNotMatch(semOReenvio(falhaDeEnvio('timeout').texto), /reenviar/)
  assert.equal(semOReenvio('Não foi enviada. Tente de novo.'), 'Não foi enviada. Tente de novo.')
})

confere('c  o aviso do grupo sai do tipo: tempo esgotado pode ter chegado; os outros não saíram', () => {
  const tempo = falhaDeEnvio(TIMEOUT)
  assert.equal(tempo.tipo, TIPOS_DE_FALHA.TEMPO_ESGOTADO)
  assert.equal(avisoDasTentativas(tempo, 1), tempo.texto)
  const quatro = avisoDasTentativas(tempo, 4)
  assert.match(quatro, /^4 tentativas sem resposta do WhatsApp\./)
  assert.match(quatro, /Pode ter chegado/)
  assert.doesNotMatch(quatro, /não saíram/)
  const naoEntregue = falhaDeEnvio('Message undeliverable')
  assert.equal(avisoDasTentativas(naoEntregue, 3), `3 tentativas não saíram. ${naoEntregue.texto}`)
  assert.equal(avisoDasTentativas(falhaDeEnvio(''), 2), '2 tentativas não saíram. Tente de novo.')
})

console.log(`\nprova-3234-falhas-agrupadas: ${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
