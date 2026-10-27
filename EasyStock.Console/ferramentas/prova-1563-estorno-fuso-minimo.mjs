/* eslint-disable no-console */
// Prova da #1563 (itens da #1544 no console):
//   1. estorno online pendente que nunca foi ao Mercado Pago ganha "Descartar": rota própria, sem
//      corpo, com confirmação, só para quem pode estornar e só enquanto `enviadoEm` é nulo;
//   2. mensagem programada e lembrete leem a hora digitada como hora DA LOJA: o mesmo valor dá o
//      mesmo instante com o aparelho em UTC, em São Paulo ou em Tóquio;
//   3. apagar o mínimo do insumo manda `limparMinimo: true` (null é "não mexe"; zero venceria a
//      herança da categoria); com valor, o corpo é o de antes;
//   4. `obterItemCardapio` (morto, devolvia null no erro) saiu.
// Cada catraca foi provada por mutação (desfazer a correção reprova a linha correspondente).
//
//   node ferramentas/prova-1563-estorno-fuso-minimo.mjs

import { registerHooks } from 'node:module'
import { readFileSync, readdirSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

globalThis.sessionStorage = { getItem: () => JSON.stringify({ token: 'teste', expiraEm: Date.now() + 60000, empresa: { id: 'empresa' } }) }

const { descartarEstornoOnline } = await import('../src/infra/api/estornosOnlineApi.js')
const { criarGestaoInsumos } = await import('../src/aplicacao/insumos.js')
const programada = await import('../src/dominio/mensagemProgramada.js')
const lembrete = await import('../src/dominio/lembrete.js')
const formato = await import('../src/dominio/formato.js')

const RAIZ = new URL('../src/', import.meta.url)
// No Windows o checkout vem com CRLF; a prova lê como o CI (LF).
const ler = (caminho) => readFileSync(new URL(caminho, RAIZ), 'utf8').replace(/\r\n/g, '\n')
// Comentário não conta como ligação: sai antes de procurar.
const semComentarios = (fonte) => fonte
  .replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
  .replace(/\/\*[\s\S]*?\*\//g, '')
  .replace(/^\s*\/\/.*$/gm, '')
const vezes = (fonte, trecho) => fonte.split(trecho).length - 1

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

function montarFetch(resposta) {
  const chamadas = []
  globalThis.fetch = async (url, { method = 'GET', body } = {}) => {
    chamadas.push({ metodo: method, url, corpo: body ?? null })
    const { status = 200, data = null, error } = resposta
    return new Response(status === 204 ? null : JSON.stringify(error ? { error } : { data }), { status })
  }
  return chamadas
}

// --- 1. Descartar estorno online nunca enviado --------------------------------------------
const PEDIDO = '11111111-2222-3333-4444-555555555555'
const OPERACAO = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

await confere('descartar chama a rota própria por POST, sem corpo, e devolve o estorno recusado', async () => {
  const chamadas = montarFetch({ data: { id: OPERACAO, situacao: 'recusado', detalhe: 'Descartada antes do envio.' } })
  const estorno = await descartarEstornoOnline(PEDIDO, OPERACAO)
  assert.equal(chamadas.length, 1)
  assert.equal(chamadas[0].metodo, 'POST')
  assert.ok(chamadas[0].url.endsWith(`/api/pedidos/${PEDIDO}/estornos-online/${OPERACAO}/descartar`), chamadas[0].url)
  assert.equal(chamadas[0].corpo, null)
  assert.equal(estorno.situacao, 'recusado')
})

await confere('o 409 da API chega com o status e a mensagem dela (é o que a tela mostra)', async () => {
  const mensagem = 'A solicitação já foi enviada ao Mercado Pago e não pode ser descartada. Confira o estorno pendente.'
  montarFetch({ status: 409, error: { code: 'estorno_ja_enviado', message: mensagem } })
  await assert.rejects(descartarEstornoOnline(PEDIDO, OPERACAO), (erro) => {
    assert.equal(erro.status, 409)
    assert.equal(erro.codigo, 'estorno_ja_enviado')
    assert.equal(erro.message, mensagem)
    return true
  })
})

const tela = semComentarios(ler('features/ficha-cliente/EstornosOnline.jsx'))
const gancho = semComentarios(ler('aplicacao/useEstornosOnline.js'))

await confere('só o pendente NÃO enviado pode ser descartado (enviadoEm nulo)', () => {
  assert.ok(tela.includes("const podeDescartar = (estorno) => estorno.situacao === 'pendente' && estorno.enviadoEm == null\n"))
})
await confere('o botão exige a mesma permissão de "Conferir" e fica travado durante o envio', () => {
  assert.equal(vezes(tela, 'podeDescartar(e) && podeSolicitar && '), 2, 'descartar sem a guarda de permissão')
  assert.ok(tela.includes('descartando !== e.id && <Botao disabled={ocupado} onClick={() => setDescartando(e.id)}>Descartar solicitação não enviada</Botao>'))
  assert.ok(tela.includes("<Botao variante=\"primario\" disabled={ocupado} onClick={async () => { await descartar(e.id); setDescartando(null) }}>{ocupado ? 'Descartando…' : 'Confirmar descarte'}</Botao>"))
})
await confere('descartar só dispara depois da confirmação (um clique não descarta)', () => {
  assert.equal(vezes(tela, 'descartar(e.id)'), 1)
  const confirmacao = tela.indexOf('descartando === e.id && <>')
  const disparo = tela.indexOf('descartar(e.id)')
  assert.ok(confirmacao !== -1 && disparo > confirmacao, 'descartar fora do bloco de confirmação')
  assert.ok(tela.slice(confirmacao, disparo).includes('Nada foi enviado ao Mercado Pago'), 'confirmação sem dizer o que acontece')
})
await confere('"Conferir estorno pendente" continua igual: descartar entrou ao lado, não no lugar', () => {
  assert.ok(tela.includes("{e.situacao === 'pendente' && podeSolicitar && <Botao disabled={ocupado} onClick={() => retomar(e.id)}>Conferir estorno pendente</Botao>}"))
})
await confere('o gancho não deixa duas ações de dinheiro em voo, mostra o erro da API e relê no 409', () => {
  const inicio = gancho.indexOf('async function descartar(operacaoId) {')
  assert.notEqual(inicio, -1, 'descartar não encontrado no gancho')
  const corpo = gancho.slice(inicio, gancho.indexOf('\n  }\n', inicio))
  assert.ok(corpo.includes('if (emVoo.current) return false'))
  assert.ok(corpo.includes('await descartarEstornoOnline(pedidoId, operacaoId)'))
  assert.ok(corpo.includes('if (!recebido && e.status === 409) await carregar()'))
  assert.ok(corpo.includes(': e.message)'))
  assert.ok(corpo.includes('finally { emVoo.current = false; if (ativo.current) setOcupado(false) }'))
})

// #2079 (C4): depois do estorno online, "A devolver" da modal e o "a devolver" da Ficha ficavam com o valor antigo:
// o EstornosOnline só relia a si mesmo. Agora ele avisa o bloco Devoluções, que relê o seu `aDevolver` e pede à
// Ficha a releitura do pedido (o alocado muda e o `useADevolver` relê).
const devolucoes = semComentarios(ler('features/ficha-cliente/BlocoDevolucoes.jsx'))
const painel = semComentarios(ler('features/ficha-cliente/PainelFicha.jsx'))
const acoesComanda = semComentarios(ler('aplicacao/api/comanda.js'))
await confere('#2079-C4 estorno online feito relê o "A devolver" da modal e o pedido da Ficha', () => {
  assert.ok(tela.includes('export function EstornosOnline({ pedidoId, aoAlterado = null })'), 'EstornosOnline sem aviso de mudança')
  assert.ok(tela.includes('const ok = await fazer(...args)\n    if (ok) aoAlterado?.()'), 'o aviso só sai quando a API respondeu')
  for (const nome of ['solicitar', 'retomar', 'descartar']) {
    assert.ok(tela.includes(`const ${nome} = depoisAvisar(${nome}NoGancho)`), `${nome} não avisa o bloco`)
  }
  assert.ok(devolucoes.includes('export function BlocoDevolucoes({ pedidoId, cancelado = false, aoAlterado = null })'))
  assert.ok(devolucoes.includes('<EstornosOnline pedidoId={pedidoId} aoAlterado={mudou} />'), 'o bloco não ouve o estorno online')
  assert.ok(devolucoes.includes('const mudou = () => { atualizar(); aoAlterado?.() }'))
  assert.ok(devolucoes.includes('if (await registrar('), 'devolução manual')
  assert.match(devolucoes, /setAberto\(false\); setConfirmado\(false\); setPagamentoId\(''\); setMotivo\(''\); setReferencia\(''\)\s+aoAlterado\?\.\(\)/)
  assert.ok(painel.includes('aoAlterado={() => recarregarPedido?.(selecionada.id)}'), 'a Ficha não relê o pedido')
  assert.ok(acoesComanda.includes('recarregarPedido: (id) => recarregar(id).catch(() => null),'))
})

// --- 2. Hora digitada é hora da loja ------------------------------------------------------
// O `process.env.TZ` troca o fuso do processo na hora. A primeira verificação prova que a troca
// vale: sem ela, as seguintes passariam sem olhar nada.
const FUSOS = ['UTC', 'America/Sao_Paulo', 'Asia/Tokyo']
const emCadaFuso = (fn) => FUSOS.map((fuso) => { process.env.TZ = fuso; return fn() })
const fusoOriginal = process.env.TZ
const iso = (ms) => new Date(ms).toISOString()

await confere('a troca de fuso do aparelho vale nesta prova (senão ela não mede nada)', () => {
  const doAparelho = emCadaFuso(() => new Date(2026, 9, 8, 9, 30).toISOString())
  assert.equal(new Set(doAparelho).size, FUSOS.length, `fuso do aparelho não mudou: ${doAparelho.join(' ')}`)
})
await confere('mensagem programada: 09:30 digitado é 09:30 da loja em qualquer aparelho', () => {
  assert.deepEqual(emCadaFuso(() => programada.horarioLocalParaUtc('2026-10-08T09:30')),
    FUSOS.map(() => '2026-10-08T12:30:00.000Z'))
  assert.equal(programada.horarioLocalParaUtc(''), null)
  assert.equal(programada.horarioLocalParaUtc('inválido'), null)
})
await confere('mensagem programada: o horário sugerido é o relógio da loja, e ida e volta fecham', () => {
  const agora = Date.parse('2026-10-09T01:30:30Z') // 22:30 do dia 8 na loja; no UTC já é dia 9
  assert.deepEqual(emCadaFuso(() => programada.horarioLocalPadrao(agora)), FUSOS.map(() => '2026-10-08T23:31'))
  assert.deepEqual(emCadaFuso(() => programada.horarioLocalParaUtc(programada.horarioLocalPadrao(agora))),
    FUSOS.map(() => '2026-10-09T02:31:00.000Z'))
})
await confere('a lista mostra a mesma hora que foi digitada', () => {
  assert.deepEqual(emCadaFuso(() => formato.dataHora(Date.parse(programada.horarioLocalParaUtc('2026-10-08T09:30')))),
    emCadaFuso(() => formato.dataHora(Date.parse('2026-10-08T12:30:00Z'))))
  assert.match(formato.dataHora(Date.parse(programada.horarioLocalParaUtc('2026-10-08T09:30'))), /08\/10\/2026.*09:30/)
})

const MANHA = Date.parse('2026-10-08T11:00:00Z') // 08:00 na loja
const NOITE = Date.parse('2026-10-09T01:30:00Z') // 22:30 do dia 8 na loja
await confere('lembrete "outro horário": hora da loja, hoje se ainda não passou, amanhã se passou', () => {
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDoHorario('09:30', MANHA))), FUSOS.map(() => '2026-10-08T12:30:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDoHorario('07:00', MANHA))), FUSOS.map(() => '2026-10-09T10:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDoHorario('23:00', NOITE))), FUSOS.map(() => '2026-10-09T02:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDoHorario('22:30', NOITE))), FUSOS.map(() => '2026-10-10T01:30:00.000Z'))
})
await confere('lembrete "amanhã": 9 h do dia seguinte DA LOJA', () => {
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDaOpcao('amanha', NOITE))), FUSOS.map(() => '2026-10-09T12:00:00.000Z'))
  assert.deepEqual(emCadaFuso(() => iso(lembrete.quandoDaOpcao('30min', NOITE))), FUSOS.map(() => '2026-10-09T02:00:00.000Z'))
})
await confere('os rótulos da modal e o prazo do sininho falam a hora da loja', () => {
  assert.deepEqual(emCadaFuso(() => lembrete.rotuloDoHorario('23:00', NOITE)), FUSOS.map(() => 'Hoje · 23h00'))
  assert.deepEqual(emCadaFuso(() => lembrete.rotuloDoHorario('07:00', NOITE)), FUSOS.map(() => 'Amanhã · 7h00'))
  assert.deepEqual(emCadaFuso(() => lembrete.rotuloDaOpcao('amanha', NOITE)), FUSOS.map(() => 'Amanhã · 9h00'))
  assert.deepEqual(emCadaFuso(() => lembrete.rotuloDaOpcao('10min', NOITE)), FUSOS.map(() => 'Em 10 min · 22h40'))
  const depoisDeAmanha = Date.parse('2026-10-10T12:00:00Z') // dia 10, 09:00 na loja
  const prazos = emCadaFuso(() => lembrete.descreverPrazo(depoisDeAmanha, NOITE))
  assert.equal(new Set(prazos).size, 1, prazos.join(' | '))
  assert.match(prazos[0], /10\/10.*09:00/)
})
await confere('relógio da loja -> instante: ida e volta com partesNoFuso, e dia que transborda o mês', () => {
  const ms = formato.instanteNoFuso({ ano: 2026, mes: 10, dia: 8, horas: 9, minutos: 30 })
  assert.equal(iso(ms), '2026-10-08T12:30:00.000Z')
  assert.deepEqual(formato.partesNoFuso(ms), { ano: 2026, mes: 10, dia: 8, horas: 9, minutos: 30, diaDaSemana: 4 })
  assert.equal(iso(formato.instanteNoFuso({ ano: 2026, mes: 10, dia: 32, horas: 9 })), '2026-11-01T12:00:00.000Z')
  assert.equal(iso(formato.instanteNoFuso({ ano: 2026, mes: 12, dia: 32 })), '2027-01-01T03:00:00.000Z')
})
if (fusoOriginal === undefined) delete process.env.TZ
else process.env.TZ = fusoOriginal

// --- 3. Apagar o mínimo do insumo ---------------------------------------------------------
function montarGestao() {
  const chamadas = montarFetch({ status: 204 })
  const erros = []
  const gestao = criarGestaoInsumos({ recarregar: async () => {}, aoErro: (m) => erros.push(m) })
  const corpoDoPut = () => JSON.parse(chamadas.find((c) => c.metodo === 'PUT').corpo)
  return { gestao, corpoDoPut, erros }
}

await confere('mínimo apagado manda limparMinimo, nunca zero', async () => {
  const m = montarGestao()
  assert.equal(await m.gestao.atualizar('p1', { minimo: '', custo: '0,05' }), true)
  assert.deepEqual(m.corpoDoPut(), { minimo: null, custo: 0.05, limparMinimo: true })
  const soEspacos = montarGestao()
  // #1600: custo apagado passou a limpar também (antes era "não mexe", o defeito do custo).
  await soEspacos.gestao.atualizar('p1', { minimo: '  ', custo: '' })
  assert.deepEqual(soEspacos.corpoDoPut(), { minimo: null, custo: null, limparMinimo: true, limparCusto: true })
})
await confere('mínimo com valor (inclusive zero digitado) vai como antes, sem o campo novo', async () => {
  const m = montarGestao()
  // #1600: com o custo preenchido, para o corpo continuar sem campo de limpar nenhum.
  await m.gestao.atualizar('p1', { minimo: '2500', custo: '0,05' })
  assert.deepEqual(m.corpoDoPut(), { minimo: 2500, custo: 0.05 })
  const zero = montarGestao()
  await zero.gestao.atualizar('p1', { minimo: '0', custo: '0,05' })
  assert.deepEqual(zero.corpoDoPut(), { minimo: 0, custo: 0.05 })
})
await confere('marcar embalagem não limpa o mínimo', async () => {
  const m = montarGestao()
  await m.gestao.marcarEmbalagem('p1', true)
  assert.deepEqual(m.corpoDoPut(), { minimo: null, custo: null, embalagem: true })
})

// --- 4. obterItemCardapio saiu ------------------------------------------------------------
function arquivosDe(pasta) {
  return readdirSync(pasta, { withFileTypes: true }).flatMap((e) => {
    const caminho = new URL(e.name + (e.isDirectory() ? '/' : ''), pasta)
    return e.isDirectory() ? arquivosDe(caminho) : /\.(js|jsx|mjs)$/.test(e.name) ? [caminho] : []
  })
}
await confere('ninguém define nem chama obterItemCardapio; a ficha do prato vem de lerFichaDoPrato', () => {
  const fontes = arquivosDe(RAIZ)
  assert.ok(fontes.length > 100, `varredura curta demais: ${fontes.length} arquivos`)
  const comONome = fontes.filter((f) => readFileSync(f, 'utf8').includes('obterItemCardapio')).map((f) => f.pathname.split('/src/')[1])
  assert.deepEqual(comONome, [])
  assert.ok(ler('aplicacao/edicaoCardapio.js').includes('export const lerFichaDoPrato = async (id) => detalheDaApi(await obterItem(id))'))
})

console.log(`\n${passou} verificações passaram, ${falhas.length} falharam.`)
if (falhas.length) process.exit(1)
