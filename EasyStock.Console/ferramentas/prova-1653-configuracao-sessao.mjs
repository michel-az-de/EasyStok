import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// Cliente, sessão, renovação e configuração são reais; só disco e HTTP são controlados.
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
const storage = () => {
  const dados = new Map()
  return { getItem: (chave) => dados.get(chave) ?? null, setItem: (chave, valor) => dados.set(chave, valor), removeItem: (chave) => dados.delete(chave) }
}
globalThis.sessionStorage = storage()
globalThis.localStorage = storage()
globalThis.window = new EventTarget()
Object.defineProperty(globalThis, 'navigator', { value: { locks: { request: (_nome, acao) => acao() } }, configurable: true })

const sessao = await import('../src/infra/api/sessao.js')
const { chamarApi } = await import('../src/infra/api/cliente.js')
const { salvarConfiguracao } = await import('../src/infra/api/configuracaoApi.js')
const { criarAcoesExpedienteApi } = await import('../src/aplicacao/api/expediente.js')
const { expedienteDaApi } = await import('../src/infra/api/expedienteApi.js')
let sequencia = 0
const jwt = (empresaId, pessoa, persistente = false) => `fixture.${Buffer.from(JSON.stringify({
  empresaId, sub: pessoa, exp: Math.floor(Date.now() / 1000) + 3600, sessaoPersistente: persistente ? 'true' : undefined, jti: ++sequencia,
})).toString('base64url')}.fixture`
const abrir = (empresa = 'empresa-A', pessoa = 'pessoa-A', persistente = false, vencida = false) => {
  const atual = sessao.atualizarTokens({ empresa: { id: empresa }, usuario: { id: pessoa } }, {
    accessToken: jwt(empresa, pessoa, persistente), refreshToken: `refresh-${sequencia}`, expiresIn: 3600,
  })
  if (vencida) atual.expiraEm = Date.now() - 1000
  return sessao.gravarSessao(atual)
}
const resposta = (data, status = 200) => new Response(JSON.stringify({ data }), { status })
const adiado = () => { let resolver; return { promessa: new Promise((r) => { resolver = r }), resolver: (valor) => resolver(valor) } }
const configuracao = { ativo: true, tom: 'acolhedor', nivelSugestao: 'Discreto', saudacaoPrimeiroContato: 'Olá', saudacaoRetorno: 'Olá de novo', fraseEspera: 'Um instante', mensagemForaArea: 'Fora da área', respiroMinutos: 40, tempoPreparoPadraoMinutos: 60, slaRespostaMinutos: 5, modeloRetomadaNome: 'modelo_novo', modeloRetomadaIdioma: 'pt_BR' }
const geral = { ...configuracao, modeloRetomadaNome: 'modelo_antigo' }
let chamadas, aprovadas = 0
const http = (responder) => {
  globalThis.fetch = async (url, opcoes) => {
    const chamada = { url, metodo: opcoes.method, token: opcoes.headers.Authorization, corpo: opcoes.body && JSON.parse(opcoes.body) }
    chamadas.push(chamada)
    return responder(chamada)
  }
}
async function provar(nome, executar) {
  sessao.limparSessao(); chamadas = []
  await executar()
  aprovadas++
  console.log('ok ' + nome)
}
const gravacoes = () => chamadas.filter((chamada) => chamada.metodo === 'PUT')

await provar('mesma conta conclui os dois PUTs com o token esperado', async () => {
  const conta = abrir()
  http(({ url }) => resposta(url.endsWith('/modelo-retomada') ? configuracao : geral))
  assert.deepEqual(await salvarConfiguracao(configuracao), configuracao)
  assert.equal(gravacoes().length, 2)
  assert.ok(gravacoes().every((chamada) => chamada.token === `Bearer ${conta.token}`))
})

for (const troca of ['empresa', 'pessoa', 'sair']) {
  await provar(`${troca === 'sair' ? 'sair' : `trocar ${troca}`} durante o primeiro PUT impede o segundo e preserva a sessão nova`, async () => {
    abrir()
    const recebeu = adiado(), espera = adiado()
    http(() => { recebeu.resolver(); return espera.promessa })
    const envio = salvarConfiguracao(configuracao)
    await recebeu.promessa
    const nova = troca === 'sair' ? (sessao.limparSessao(), null) : abrir(troca === 'empresa' ? 'empresa-B' : 'empresa-A', troca === 'pessoa' ? 'pessoa-B' : 'pessoa-A')
    espera.resolver(resposta(geral))
    await assert.rejects(envio, /sessão foi encerrada ou trocada/)
    assert.equal(gravacoes().length, 1)
    assert.equal(sessao.lerSessao()?.token ?? null, nova?.token ?? null)
  })
}

await provar('troca entre garantirSessao e fetch impede até o primeiro PUT', async () => {
  abrir()
  http(() => resposta(geral))
  const envio = salvarConfiguracao(configuracao)
  const nova = abrir('empresa-B', 'pessoa-B')
  await assert.rejects(envio, (erro) => erro.codigo === 'SESSAO_ALTERADA')
  assert.equal(chamadas.length, 0)
  assert.equal(sessao.lerSessao().token, nova.token)
})

for (const etapa of [1, 2]) {
  for (const trocarConta of [false, true]) {
    await provar(`${trocarConta ? 'bloqueia troca de conta' : 'permite a mesma identidade'} durante refresh antes do PUT ${etapa}`, async () => {
      const original = abrir('empresa-A', 'pessoa-A', true, etapa === 1)
      const recebeuRefresh = adiado(), esperaRefresh = adiado()
      http(({ url }) => {
        if (url.endsWith('/refresh')) { recebeuRefresh.resolver(); return esperaRefresh.promessa }
        if (url.endsWith('/logout')) return resposta(null)
        if (!url.endsWith('/modelo-retomada') && etapa === 2) sessao.gravarSessao({ ...sessao.lerSessao(), expiraEm: Date.now() - 1000 })
        return resposta(url.endsWith('/modelo-retomada') ? configuracao : geral)
      })
      const envio = salvarConfiguracao(configuracao)
      await recebeuRefresh.promessa
      const nova = trocarConta ? abrir('empresa-B', 'pessoa-B') : null
      const tokenRenovado = jwt('empresa-A', 'pessoa-A', true)
      esperaRefresh.resolver(resposta({ accessToken: tokenRenovado, refreshToken: 'refresh-renovado', expiresIn: 3600 }))
      if (trocarConta) {
        await assert.rejects(envio, /sessão foi encerrada ou trocada/)
        assert.equal(gravacoes().length, etapa - 1)
        assert.equal(sessao.lerSessao().token, nova.token)
        assert.equal(chamadas.filter((chamada) => chamada.url.endsWith('/logout')).length, 1, 'revoga somente o refresh que chegou atrasado')
      } else {
        await envio
        assert.equal(gravacoes().length, 2)
        assert.equal(gravacoes()[0].token, `Bearer ${etapa === 1 ? tokenRenovado : original.token}`)
        assert.equal(gravacoes()[1].token, `Bearer ${tokenRenovado}`)
      }
    })
  }
}

await provar('401 encerra a sessão sem repetir escrita ou iniciar o segundo PUT', async () => {
  abrir()
  http(() => resposta(null, 401))
  await assert.rejects(salvarConfiguracao(configuracao), (erro) => erro.status === 401)
  assert.equal(gravacoes().length, 1)
  assert.equal(sessao.lerSessao(), null)
})

await provar('chamada sem identidadeEsperada conserva o contrato existente', async () => {
  const conta = abrir('empresa-B', 'pessoa-B')
  http(() => resposta({ confirmado: true }))
  assert.deepEqual(await chamarApi('/consulta'), { confirmado: true })
  assert.equal(chamadas[0].token, `Bearer ${conta.token}`)
})
const expediente = { horarios: [{ diaDaSemana: 1, abre: '10:00:00', fecha: '18:00:00' }], controleManual: 'Automatico', mensagemForaDoHorario: 'Voltamos às dez', mensagemLojaFechada: 'Até amanhã' }
const prepararExpediente = () => {
  const eventos = []
  const acoes = criarAcoesExpedienteApi({ despachar: (e) => eventos.push(e), estadoRef: { current: expedienteDaApi(expediente) } })
  return { acoes, eventos }
}
for (const [nome, salvar] of [
  ['horários', (acoes) => acoes.salvarHorariosExpediente(expedienteDaApi(expediente).funcionamento)],
  ['mensagens', (acoes) => acoes.salvarMensagensExpediente(expediente)],
]) {
  await provar(`${nome}: ação criada em A não envia edição após entrar em B`, async () => {
    abrir()
    const { acoes, eventos } = prepararExpediente(), nova = abrir('empresa-B', 'pessoa-B')
    http(() => resposta(expediente))
    await assert.rejects(salvar(acoes), (erro) => erro.codigo === 'SESSAO_ALTERADA')
    assert.equal(chamadas.length, 0)
    assert.equal(eventos.length, 0)
    assert.equal(sessao.lerSessao().token, nova.token)
  })

  for (const trocar of [false, true]) {
    await provar(`${nome}: refresh pendente ${trocar ? 'bloqueia nova conta' : 'permite a mesma conta'}`, async () => {
      abrir('empresa-A', 'pessoa-A', true, true)
      const { acoes, eventos } = prepararExpediente(), recebeuRefresh = adiado(), esperaRefresh = adiado()
      http(({ url }) => {
        if (url.endsWith('/refresh')) { recebeuRefresh.resolver(); return esperaRefresh.promessa }
        if (url.endsWith('/logout')) return resposta(null)
        return resposta(expediente)
      })
      const envio = salvar(acoes)
      await recebeuRefresh.promessa
      const nova = trocar ? abrir('empresa-B', 'pessoa-B') : null
      const tokenRenovado = jwt('empresa-A', 'pessoa-A', true)
      esperaRefresh.resolver(resposta({ accessToken: tokenRenovado, refreshToken: 'refresh-expediente', expiresIn: 3600 }))
      if (trocar) {
        await assert.rejects(envio, (erro) => erro.codigo === 'SESSAO_ALTERADA')
        assert.equal(gravacoes().length, 0)
        assert.equal(eventos.length, 0)
        assert.equal(sessao.lerSessao().token, nova.token)
      } else {
        assert.deepEqual(await envio, expedienteDaApi(expediente))
        assert.equal(gravacoes().length, 1)
        assert.equal(gravacoes()[0].url, '/api/atendimento/expediente')
        assert.equal(gravacoes()[0].token, `Bearer ${tokenRenovado}`)
        assert.equal(eventos.length, 1)
      }
    })
  }
}

console.log(`Sessão #1653: ${aprovadas} cenários aprovados com cliente e renovação reais.`)
