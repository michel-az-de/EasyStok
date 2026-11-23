/* eslint-disable no-console */
// Prova da #3234 (C1, A2): envios seguidos que falharam com o mesmo texto e o mesmo tipo de falha viram um balão só
// no fio, com o aviso uma vez. Na produção eram 4 balões iguais com "O WhatsApp demorou a responder" repetido.
// Só regra pura, sem navegador (a prova-2185 E32 mede a tela).
//   a  4 falhas seguidas iguais viram 1 linha com 3 anteriores; a mais recente fica à vista;
//   b  texto diferente, tipo diferente, mensagem do cliente, dia, nota ou divisória quebram o grupo;
//   c  o aviso do grupo sai do tipo: tempo esgotado diz "pode ter chegado", nunca "não saíram".
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

const { agruparFalhasSeguidas, avisoDasTentativas } = await import('../src/dominio/falhasSeguidas.js')
const { TIPOS_DE_FALHA, falhaDeEnvio } = await import('../src/dominio/falhaDeEnvio.js')

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

confere('d  foto, modelo e anexo não agrupam: o texto deles é rótulo ("Foto"), não o conteúdo', () => {
  for (const extra of [{ midia: { conversaId: 'c1', mensagemId: 'x' }, texto: 'Foto' }, { modelo: true, texto: 'Mensagem de modelo sem texto' }, { formato: 'imagem', arte: 'a.png' }]) {
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
