/* eslint-disable no-console */
// Prova da #3234 (C5, A3): o modo da conversa ("Assistente respondendo", "Você respondendo") ocupava uma faixa
// própria de 40 px abaixo do cabeçalho só para uma pílula. Sem transferência nem frase à vista, ele cabe no
// cabeçalho como selo. A faixa da transferência (quem passou e por quê) e "O Assistente está desligado" ficam
// na faixa. Só regra pura, sem navegador (a prova-2185 E33 mede a tela).
//   a  Assistente respondendo, Você respondendo, outro atendente, concluída e bloqueada vão para o cabeçalho;
//   b  transferência com motivo e Assistente desligado ficam na faixa, com a frase à vista.
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

const { apresentacaoDoModo } = await import('../src/dominio/resumoDoModo.js')
const { PAUSA_POR_ASSUMIR, PAUSA_POR_OUTRO } = await import('../src/dominio/automatico.js')
const { AUTOMATICO_DESLIGADO } = await import('../src/dominio/motivoDeEscalada.js')

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao); console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').slice(0, 4).join(' ')}`)
  }
}

const agora = Date.now()
const conversa = (extra = {}) => ({
  id: 'c1', canal: 'WhatsApp', estado: 'Aberto', clienteId: 'cl1', passagem: null,
  mensagens: [{ id: 'm0', dir: 'in', texto: 'Oi', em: new Date(agora - 60000).toISOString() }], ...extra,
})
const FILA = { motivo: 'Restrição alimentar: alergia a camarão', em: null, assumida: false }

confere('a  só a pílula (e o Assumir) vai para o cabeçalho como selo', () => {
  const casos = {
    'Assistente respondendo': [conversa(), false, false],
    'Você respondendo': [conversa(), PAUSA_POR_ASSUMIR, false],
    'Com Ana Lima': [conversa({ responsavel: 'Ana Lima' }), PAUSA_POR_OUTRO, false],
    Concluída: [conversa({ estado: 'Encerrado' }), false, false],
    Bloqueado: [conversa({ bloqueio: { motivo: 'Teste', em: new Date(agora).toISOString() } }), true, true],
  }
  for (const [rotulo, [c, pausado, bloqueada]] of Object.entries(casos)) {
    const modo = apresentacaoDoModo(c, pausado, bloqueada)
    assert.equal(modo.resumo.rotulo, rotulo)
    assert.equal(modo.noCabecalho, true, `${rotulo} ficou na faixa`)
    assert.equal(modo.faixa, null, rotulo)
    assert.equal(modo.mostrarLinha, false, rotulo)
  }
})

confere('b  transferência com motivo e Assistente desligado ficam na faixa, com a frase à vista', () => {
  const passou = apresentacaoDoModo(conversa({ passagem: FILA }), true, false)
  assert.equal(passou.noCabecalho, false, 'a transferência subiu para o cabeçalho')
  assert.match(passou.faixa?.motivo ?? '', /alergia a camarão/)
  const desligado = apresentacaoDoModo(conversa({ passagem: { ...FILA, motivo: AUTOMATICO_DESLIGADO } }), PAUSA_POR_ASSUMIR, false)
  assert.equal(desligado.noCabecalho, false, '"O Assistente está desligado" subiu para o cabeçalho')
  assert.equal(desligado.mostrarLinha, true)
  assert.equal(desligado.linha, 'O Assistente está desligado.')
})

console.log(`\nprova-3234-modo-selo: ${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
