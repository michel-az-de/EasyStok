// Ação e adaptador reais; apenas o transporte HTTP e a sessão ficam controlados.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

const g = globalThis.__expediente1653 = {}
const inline = (source) => ({ format: 'module', shortCircuit: true, source })
registerHooks({
  resolve(spec, ctx, next) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return next(`${spec}.js`, ctx)
    return next(spec, ctx)
  },
  load(url, ctx, next) {
    if (url.endsWith('/infra/api/cliente.js')) return inline('export const chamarApi=(...args)=>globalThis.__expediente1653.chamarApi(...args)')
    if (url.endsWith('/infra/api/sessao.js')) return inline('export const lerSessao=()=>globalThis.__expediente1653.sessao;export const identidadeDaSessao=(s)=>s.empresa.id+":"+s.usuario.id')
    return next(url, ctx)
  },
})
const { criarAcoesExpedienteApi } = await import('../src/aplicacao/api/expediente.js')
const { expedienteDaApi } = await import('../src/infra/api/expedienteApi.js')
const acao = await import('../src/aplicacao/acoes.js')
const BASE = '/api/atendimento/expediente'
const resposta = (abre = '08:00', campos = {}) => ({
  horarios: [{ diaDaSemana: 1, abre, fecha: '18:00' }], controleManual: 'Automatico',
  mensagemForaDoHorario: 'Voltamos amanhã', mensagemLojaFechada: 'Fechada por hoje', ...campos,
})
const adiado = () => {
  let resolver, rejeitar
  return { promessa: new Promise((ok, erro) => { resolver = ok; rejeitar = erro }), resolver: (v) => resolver(v), rejeitar: (e) => rejeitar(e) }
}
let numero = 0, provas = 0
function preparar() {
  g.sessao = { empresa: { id: `empresa-${++numero}` }, usuario: { id: 'dona' } }
  const chamadas = []
  g.chamarApi = (url, opcoes = {}) => {
    const chamada = { url, opcoes, ...adiado() }
    chamadas.push(chamada)
    return chamada.promessa
  }
  const criarProvider = () => {
    const eventos = []
    const estadoRef = { current: expedienteDaApi(resposta()) }
    const acoes = criarAcoesExpedienteApi({ despachar: (e) => eventos.push(e), estadoRef })
    return { acoes, eventos, sincronizacoes: () => eventos.filter((e) => e.tipo === acao.SINCRONIZAR_EXPEDIENTE) }
  }
  return { ...criarProvider(), criarProvider, chamadas }
}
const prova = async (nome, executar) => { await executar(); provas++; console.log(`OK ${nome}`) }

await prova('leitura fresca devolve o mesmo snapshot confirmado que despacha', async () => {
  const h = preparar(), leitura = h.acoes.recarregarExpediente()
  assert.equal(h.chamadas[0].url, BASE)
  h.chamadas[0].resolver(resposta('09:00'))
  const confirmado = await leitura
  assert.deepEqual(confirmado, expedienteDaApi(resposta('09:00')))
  assert.deepEqual(h.sincronizacoes(), [{ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...confirmado }])
})

for (const antigaPrimeiro of [false, true]) {
  await prova(`GETs fora de ordem compartilham confirmação recente (antiga primeiro: ${antigaPrimeiro})`, async () => {
    const h = preparar(), antiga = h.acoes.recarregarExpediente(), recente = h.acoes.recarregarExpediente()
    if (antigaPrimeiro) {
      h.chamadas[0].resolver(resposta())
      await Promise.resolve()
      assert.equal(h.sincronizacoes().length, 0)
    }
    h.chamadas[1].resolver(resposta('10:00'))
    const confirmado = await recente
    if (!antigaPrimeiro) h.chamadas[0].resolver(resposta())
    assert.equal(await antiga, confirmado, 'o modal nunca recebe a confirmação antiga')
    assert.equal(h.sincronizacoes().length, 1)
  })
}

await prova('falha de GET substituído acompanha confirmação recente sem aviso obsoleto', async () => {
  const h = preparar(), antiga = h.acoes.recarregarExpediente(), recente = h.acoes.recarregarExpediente()
  h.chamadas[1].resolver(resposta('10:00'))
  const confirmado = await recente
  h.chamadas[0].rejeitar(new Error('falha antiga'))
  assert.equal(await antiga, confirmado)
  assert.equal(h.eventos.some((e) => e.tipo === acao.AVISO_API), false)
})

const mutacoes = [
  { nome: 'horários', executar: (a) => a.salvarHorariosExpediente(expedienteDaApi(resposta('10:00')).funcionamento), metodo: 'PUT', corpo: { horarios: [{ diaDaSemana: 1, abre: '10:00', fecha: '18:00' }] }, resposta: resposta('10:00') },
  { nome: 'mensagens', executar: (a) => a.salvarMensagensExpediente({ mensagemForaDoHorario: 'Voltamos às dez', mensagemLojaFechada: 'Até amanhã' }), metodo: 'PUT', corpo: { mensagemForaDoHorario: 'Voltamos às dez', mensagemLojaFechada: 'Até amanhã' }, resposta: resposta('08:00', { mensagemForaDoHorario: 'Voltamos às dez', mensagemLojaFechada: 'Até amanhã' }) },
  { nome: 'controle automático', executar: (a) => a.voltarAoHorario(), metodo: 'POST', corpo: { controle: 'Automatico' }, resposta: resposta() },
  { nome: 'abertura manual', executar: (a) => a.abrirLojaComCaixa({ caixaJaAberto: true }), metodo: 'POST', corpo: { controle: 'ForcarAberta' }, resposta: resposta('08:00', { controleManual: 'ForcarAberta' }) },
  { nome: 'fechamento manual', executar: (a) => a.fecharLojaComJustificativa('  Encerramos mais cedo  '), metodo: 'POST', corpo: { controle: 'ForcarFechada', justificativa: 'Encerramos mais cedo' }, resposta: resposta('08:00', { controleManual: 'ForcarFechada' }) },
]
for (const mutacao of mutacoes) {
  for (const durante of [false, true]) {
    await prova(`GET ${durante ? 'durante' : 'antes de'} ${mutacao.nome} não reverte confirmação`, async () => {
      const h = preparar()
      let leitura = durante ? null : h.acoes.recarregarExpediente()
      const gravacao = mutacao.executar(h.acoes)
      const escrita = h.chamadas.at(-1)
      if (durante) leitura = h.acoes.recarregarExpediente()
      const consulta = h.chamadas.find((c) => !c.opcoes.metodo)
      assert.equal(escrita.url, mutacao.metodo === 'POST' ? `${BASE}/controle` : BASE)
      assert.equal(escrita.opcoes.metodo, mutacao.metodo)
      assert.deepEqual(JSON.parse(JSON.stringify(escrita.opcoes.corpo)), mutacao.corpo)
      if (mutacao.nome === 'horários') assert.equal(escrita.opcoes.identidadeEsperada, `${g.sessao.empresa.id}:dona`)
      escrita.resolver(mutacao.resposta)
      await gravacao
      consulta.resolver(resposta('06:00', { mensagemForaDoHorario: 'Antiga', controleManual: 'ForcarFechada' }))
      assert.equal(await leitura, undefined, 'leitura invalidada não finge confirmação')
      assert.deepEqual(h.sincronizacoes(), [{ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(mutacao.resposta) }])
    })
  }
}

for (const mutacao of mutacoes.filter((m) => m.metodo === 'PUT')) {
  await prova(`${mutacao.nome}: sucesso após Hall hidrata provider atual sem recarga manual`, async () => {
    const h = preparar(), gravacao = mutacao.executar(h.acoes)
    const outro = h.criarProvider(), antiga = outro.acoes.recarregarExpediente()
    h.chamadas[0].resolver(mutacao.resposta)
    await gravacao
    h.chamadas[1].resolver(resposta())
    assert.equal(await antiga, undefined)
    assert.deepEqual(outro.sincronizacoes(), [{ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(mutacao.resposta) }])
    assert.equal(h.sincronizacoes().length, 0)
    assert.equal(h.chamadas.length, 2, 'confirmação chega sem GET adicional ou formulário aberto')
  })

  for (const falharRecuperacao of [false, true]) {
    await prova(`${mutacao.nome}: falha após Hall ${falharRecuperacao ? 'mostra erro de recuperação' : 'hidrata provider atual'}`, async () => {
      const h = preparar(), gravacao = mutacao.executar(h.acoes)
      const outro = h.criarProvider(), inicial = outro.acoes.recarregarExpediente()
      const erro = new Error('Falha de gravação')
      h.chamadas[0].rejeitar(erro)
      await assert.rejects(gravacao, (e) => e === erro)
      assert.equal(h.chamadas.length, 3, 'uma única recuperação automática')
      assert.equal(h.chamadas[2].url, BASE)
      h.chamadas[1].resolver(resposta('06:00'))
      if (falharRecuperacao) {
        h.chamadas[2].rejeitar(new Error('Falha na leitura de recuperação'))
        assert.equal(await inicial, undefined)
        assert.equal(outro.sincronizacoes().length, 0)
        assert.ok(outro.eventos.some((e) => e.tipo === acao.AVISO_API && e.mensagem.includes('Falha na leitura de recuperação')))
      } else {
        h.chamadas[2].resolver(resposta())
        assert.deepEqual(await inicial, expedienteDaApi(resposta()))
        assert.deepEqual(outro.sincronizacoes(), [{ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(resposta()) }])
      }
      assert.equal(h.sincronizacoes().length, 0)
    })
  }
}

for (const falharRecuperacao of [false, true]) {
  await prova(`recuperação de controle antigo entrega ${falharRecuperacao ? 'erro' : 'confirmação'} ao provider atual`, async () => {
    const h = preparar(), gravacao = h.acoes.voltarAoHorario()
    const outro = h.criarProvider(), inicial = outro.acoes.recarregarExpediente()
    h.chamadas[0].rejeitar(new Error('Falha ao mudar controle'))
    await gravacao
    assert.equal(h.chamadas.length, 3, 'recuperação usa o mesmo endpoint')
    h.chamadas[1].resolver(resposta())
    if (falharRecuperacao) {
      h.chamadas[2].rejeitar(new Error('Falha ao recuperar expediente'))
      assert.equal(await inicial, undefined)
      assert.equal(outro.sincronizacoes().length, 0)
      assert.ok(outro.eventos.some((e) => e.tipo === acao.AVISO_API && e.mensagem.includes('Falha ao recuperar expediente')))
    } else {
      h.chamadas[2].resolver(resposta('10:00'))
      assert.deepEqual(await inicial, expedienteDaApi(resposta('10:00')))
      assert.deepEqual(outro.sincronizacoes(), [{ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(resposta('10:00')) }])
    }
    assert.equal(h.sincronizacoes().length, 0, 'provider anterior não recebe a leitura de recuperação')
  })
}

await prova('GET antigo passa a devolver leitura fresca iniciada após gravação', async () => {
  const h = preparar(), antiga = h.acoes.recarregarExpediente()
  const gravacao = h.acoes.salvarHorariosExpediente(expedienteDaApi(resposta('10:00')).funcionamento)
  h.chamadas[1].resolver(resposta('10:00'))
  await gravacao
  const recente = h.acoes.recarregarExpediente()
  h.chamadas[0].resolver(resposta())
  h.chamadas[2].resolver(resposta('10:00'))
  assert.equal(await antiga, await recente)
  assert.equal(h.sincronizacoes().length, 2)
})

await prova('falha de gravação invalida GET em voo e preserva erro da ação', async () => {
  const h = preparar(), gravacao = h.acoes.salvarMensagensExpediente({ mensagemForaDoHorario: 'Nova' })
  const leitura = h.acoes.recarregarExpediente(), erro = new Error('Sem conexão')
  h.chamadas[0].rejeitar(erro)
  await assert.rejects(gravacao, (e) => e === erro)
  h.chamadas[1].resolver(resposta())
  assert.equal(await leitura, undefined)
  assert.equal(h.sincronizacoes().length, 0)
})

await prova('falha de controle ainda recarrega valor confirmado e 403 permanece orientação', async () => {
  const h = preparar(), gravacao = h.acoes.voltarAoHorario()
  h.chamadas[0].rejeitar(new Error('Sem conexão'))
  await gravacao
  assert.equal(h.chamadas[1].url, BASE)
  h.chamadas[1].resolver(resposta())
  await new Promise((r) => setImmediate(r))
  assert.equal(h.sincronizacoes().length, 1)
  assert.ok(h.eventos.some((e) => e.mensagem?.includes('Loja não mudou')))
  const negada = h.acoes.voltarAoHorario()
  h.chamadas[2].rejeitar(Object.assign(new Error('Proibido'), { status: 403 }))
  await negada
  assert.equal(h.chamadas.length, 3)
  assert.ok(h.eventos.some((e) => e.mensagem?.startsWith('Só gerente ou dona')))
})

await prova('erros de leitura atuais mantêm aviso, 403 continua silencioso', async () => {
  const h = preparar(), negada = h.acoes.recarregarExpediente()
  h.chamadas[0].rejeitar(Object.assign(new Error('Proibido'), { status: 403 }))
  assert.equal(await negada, undefined)
  assert.equal(h.eventos.length, 0)
  const falha = h.acoes.recarregarExpediente()
  h.chamadas[1].rejeitar(new Error('Sem conexão'))
  assert.equal(await falha, undefined)
  assert.equal(h.eventos[0].mensagem, 'Expediente não carregou: Sem conexão')
})

await prova('revisão de leitura é isolada por empresa e usuário', async () => {
  const h = preparar(), leitura = h.acoes.recarregarExpediente()
  const outro = preparar(), gravacao = outro.acoes.salvarHorariosExpediente(expedienteDaApi(resposta('10:00')).funcionamento)
  outro.chamadas[0].resolver(resposta('10:00'))
  await gravacao
  h.chamadas[0].resolver(resposta())
  assert.deepEqual(await leitura, expedienteDaApi(resposta()))
  assert.equal(h.sincronizacoes().length, 1)
  assert.equal(outro.sincronizacoes().length, 1)
})

console.log(`${provas} provas de concorrência do expediente passaram.`)
