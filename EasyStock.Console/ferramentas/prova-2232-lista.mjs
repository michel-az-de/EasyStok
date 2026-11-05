/* eslint-disable no-console */
// Prova da issue #2232 (banca UX/UI do Atendimento, Tela 1: lista de conversas no celular).
// Uma verificação por achado da spec `docs/plan/banca-ux-2232/README.md`. Regras puras e
// leitura do fonte onde só existe composição.
//
//   node ferramentas/prova-2232-lista.mjs

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
const tentar = async (caminho) => { try { return await import(caminho) } catch { return {} } }

const { estadoInicial } = await import('../src/aplicacao/reducer/estadoInicial.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const cartao = await import('../src/features/caixa-de-entrada/apresentacaoDoCartao.js')
const { horaDaLista } = await tentar('../src/dominio/horaDaLista.js')
const { nomeDoContato } = await tentar('../src/dominio/nomeDoContato.js')
const dispensa = await tentar('../src/features/caixa-de-entrada/dispensaNaoEntregues.js')

const css = fonte('features/caixa-de-entrada/caixa.module.css')
const jsxCartao = fonte('features/caixa-de-entrada/CartaoConversa.jsx')
const jsxBarra = fonte('features/caixa-de-entrada/BarraBalcao.jsx')
const jsxNaoEntregues = fonte('features/caixa-de-entrada/NaoEntregues.jsx')
const jsxAvatar = fonte('componentes/Avatar.jsx')
const regra = (seletor) => {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return css.match(new RegExp(`(?:^|\\n|\\})\\s*${escapado}\\s*\\{([^}]*)\\}`))?.[1] ?? ''
}

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log(`  ok  ${descricao}`) } catch (erro) { falhas.push(descricao); console.log(`  XX  ${descricao}\n      ${erro.message}`) }
}

// Relógio da loja: 04/11/2026 22:00 em Brasília (01:00 UTC do dia 05).
const AGORA = Date.parse('2026-11-05T01:00:00Z')

confere('L5  o Balcão abre em "Precisa de você"', () => {
  assert.equal(estadoInicial({ conversas: [] }).filtros.aba, 'precisa')
})
confere('L5  segmento ligado em alto contraste; parado com contorno de botão', () => {
  assert.match(regra('.chipLigado'), /background:\s*var\(--acao\)/)
  assert.match(regra('.chipLigado'), /color:\s*var\(--sobre-acao\)/)
  assert.match(regra('.chipFiltro'), /border:\s*var\(--b-1\) solid var\(--linha-media\)/)
})

confere('L6  faixa de não entregues compacta, com "Revisar" e dispensável', () => {
  assert.match(jsxNaoEntregues, />Revisar</)
  assert.match(jsxNaoEntregues, /aria-label="Dispensar aviso de não entregues"/)
  assert.doesNotMatch(regra('.faixaNaoEntregues'), /font-size:\s*1rem/)
  assert.equal(typeof dispensa.faixaDispensada, 'function')
  const itens = [{ mensagem: { id: 'm1' } }, { mensagem: { id: 'm2' } }]
  const vistas = dispensa.assinaturaDaDispensa(itens)
  assert.equal(dispensa.faixaDispensada(itens, vistas), true, 'dispensada fica oculta')
  assert.equal(dispensa.faixaDispensada([itens[0]], vistas), true, 'uma a menos continua oculta')
  assert.equal(dispensa.faixaDispensada([...itens, { mensagem: { id: 'm3' } }], vistas), false, 'falha nova volta')
  assert.equal(dispensa.faixaDispensada(itens, null), false)
})

confere('L7  avatar sem "!" e iniciais só de letras', () => {
  assert.doesNotMatch(css, /avatarCartao::after/)
  assert.match(jsxAvatar, /\\p\{L\}/u, 'iniciais filtram letra')
  assert.match(regra('.cartao.precisa::before'), /background:\s*var\(--alerta\)/)
  assert.match(regra('.cartao.atrasada::before, .cartao.slaEstourado::before'), /background:\s*var\(--erro\)/)
})

confere('L8  hora da lista: "21:48" hoje, "ontem", "26 out"', () => {
  assert.equal(typeof horaDaLista, 'function')
  assert.equal(horaDaLista('2026-11-05T00:48:00Z', AGORA), '21:48')
  assert.equal(horaDaLista('2026-11-04T00:48:00Z', AGORA), 'ontem')
  assert.equal(horaDaLista('2026-10-26T15:00:00Z', AGORA), '26 out')
  assert.match(jsxCartao, /horaDaLista\(/)
  assert.doesNotMatch(jsxCartao, /duracao\(/)
})
confere('L8  hora neutra; alerta só com o prazo estourado', () => {
  assert.doesNotMatch(css, /horaNova/)
  assert.match(regra('.topo time.horaAtrasada'), /color:\s*var\(--erro\)/)
})

confere('L9  chip curto do motivo, sem a frase do automático', () => {
  const origem = origemDaPassagem({ origem: 'agente', motivo: 'Restrição alimentar, o automático não responde isso', em: '2026-11-04T14:05:00Z' })
  assert.equal(origem.chip, 'Restrição alimentar')
  assert.match(origem.longo, /não soube responder/, 'a frase inteira continua para a conversa')
  assert.equal(origemDaPassagem({ origem: 'agente', motivo: 'Endereço pendente' }).chip, 'Endereço pendente')
  assert.match(jsxCartao, /origem\?\.chip/)
})

confere('L10 telefone cru vira máscara', () => {
  assert.equal(typeof nomeDoContato, 'function')
  assert.equal(nomeDoContato('555434619500'), '+55 (54) 3461-9500')
  assert.equal(nomeDoContato('5511987654321'), '+55 (11) 98765-4321')
  assert.equal(nomeDoContato('Thais Regina'), 'Thais Regina')
  // Revisão 5: com "+" e país que não é o 55, o número fica como veio; a máscara do Brasil não se aplica.
  assert.equal(nomeDoContato('+1 415 555 2671'), '+1 415 555 2671')
  assert.equal(nomeDoContato('+14155552671'), '+14155552671')
  assert.equal(nomeDoContato('+55 54 3461-9500'), '+55 (54) 3461-9500')
  assert.equal(cartao.nomeDoCartao({ nome: '555434619500' }).principal, '+55 (54) 3461-9500')
})

confere('L11 cabeçalho sem número repetido; o contador mora no segmento', () => {
  assert.doesNotMatch(jsxBarra, /<h2>Conversas <span>/)
})

confere('X1  nome acessível composto do item da lista', () => {
  assert.equal(typeof cartao.rotuloDoCartao, 'function')
  assert.equal(
    cartao.rotuloDoCartao({ nome: 'Thais Regina', pendentes: 13, motivo: 'Endereço pendente', hora: 'ontem' }),
    'Thais Regina, 13 mensagens sem resposta, Endereço pendente, ontem',
  )
  assert.equal(
    cartao.rotuloDoCartao({ nome: 'Ana', pendentes: 1, hora: '21:48', selecionada: true, atrasada: true }),
    'Ana, 1 mensagem sem resposta, prazo de resposta estourado, 21:48, conversa aberta',
  )
  assert.match(jsxCartao, /aria-label=\{rotuloDoCartao\(/)
})

confere('T1  nome a 16 px e prévia a 15 px', () => {
  assert.match(regra('.topo strong'), /font-size:\s*1rem/)
  assert.match(regra('.previa'), /font-size:\s*var\(--t-corpo\)/)
})
confere('T3  contador sem a cor da marca', () => {
  assert.doesNotMatch(regra('.naoLidas'), /var\(--acao\)|var\(--marca\)/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
