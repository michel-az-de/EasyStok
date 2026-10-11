import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'
import { registerHooks } from 'node:module'

registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal abaixo */ }
  }
  return proximo(spec, contexto)
} })

const { contextoDaAjuda, copiarDiagnostico, diagnosticoDoConsole } = await import('../src/features/ajuda/diagnosticoDoConsole.js')
const { focarInicioDaTela } = await import('../src/app/focoDaNavegacao.js')
const { comandasLocaisDaVisita, restaurarComandasDaVisita } = await import('../src/aplicacao/comandasDaVisita.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const { SINCRONIZAR_CONVERSAS } = await import('../src/aplicacao/acoes.js')
const { novoPedido, comItem, semItem } = await import('../src/dominio/pedido.js')
const { MODULOS, hashDoModulo, telasDoMenu } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')
const { identidadeDaSessao } = await import('../src/infra/api/sessao.js')

const telasCatalogadas = MODULOS.flatMap(modulo => telasDoMenu(modulo, { fonteApi: true }).map(tela => ({ modulo, tela })))
const resultados = []
const provar = async (nome, fn) => { await fn(); resultados.push({ nome, resultado: 'aprovado' }); console.log('ok ' + nome) }
const rota = rotaDaHash('#/m/atendimento/balcao', { fonteApi: true })
const local = comItem(comItem(novoPedido('fixture-local'), 'prato-fixture', { obs: 'sem queijo fixture' }), 'prato-fixture', { obs: 'sem queijo fixture' })
const conversa = pedido => ({ id: 'conversa-fixture', nome: 'Nome atual fixture', clienteId: 'cliente-fixture', mensagens: [{ id: 'msg-fixture', texto: 'Texto atual fixture' }], pedido })
const estado = conversas => estadoInicial({ conversas, catalogo: { cardapio: [], canais: [], janelas: [] }, regras: [], modoAgente: 'simulado' })
const sincronizar = (atual, recebidas, memoria) => {
  const evento = { tipo: SINCRONIZAR_CONVERSAS, conversas: recebidas }
  return reducer(restaurarComandasDaVisita(atual, evento, memoria), evento)
}

await provar('comanda local vai e volta com quantidade e observação pelo reducer real', () => {
  const memoria = comandasLocaisDaVisita([conversa(local)])
  const atual = sincronizar(estado([]), [conversa(null)], memoria)
  assert.deepEqual(atual.conversas[0].pedido, local)
  assert.equal(atual.conversas[0].pedido.itens[0].qtd, 2)
  assert.equal(atual.conversas[0].pedido.itens[0].obs, 'sem queijo fixture')
})
await provar('memória da visita contém apenas pedido local, sem ficha e mensagens', () => {
  const memoria = comandasLocaisDaVisita([conversa(local), { id: 'confirmado', pedido: { ...local, pedidoId: 'pedido-fixture' } }])
  assert.deepEqual(Object.keys(memoria), ['conversa-fixture'])
  assert.equal(memoria['conversa-fixture'], local)
  assert.doesNotMatch(JSON.stringify(memoria), /Nome atual|Texto atual|clienteId|mensagens/)
})
await provar('conversa atual da API conserva nome, cliente e mensagens atuais ao restaurar comanda', () => {
  const recebida = { ...conversa(null), nome: 'Nome novo fixture', clienteId: 'cliente-novo-fixture', mensagens: [{ id: 'nova', texto: 'Mensagem nova fixture' }] }
  const atual = sincronizar(estado([]), [recebida], comandasLocaisDaVisita([conversa(local)]))
  assert.equal(atual.conversas[0].nome, recebida.nome)
  assert.equal(atual.conversas[0].clienteId, recebida.clienteId)
  assert.deepEqual(atual.conversas[0].mensagens, recebida.mensagens)
})
await provar('pedido confirmado recebido da API prevalece e limpa a memória local', () => {
  const confirmado = { ...novoPedido('pedido-servidor-fixture'), pedidoId: 'id-servidor', itens: [{ sku: 'outro-prato', qtd: 3, obs: 'servidor' }], meio: 'pix' }
  const atual = sincronizar(estado([]), [conversa(confirmado)], comandasLocaisDaVisita([conversa(local)]))
  assert.equal(atual.conversas[0].pedido.pedidoId, 'id-servidor')
  assert.deepEqual(atual.conversas[0].pedido.itens, confirmado.itens)
  assert.deepEqual(comandasLocaisDaVisita(atual.conversas), {})
})
await provar('retirar todos os itens limpa a memória da comanda', () => {
  assert.deepEqual(comandasLocaisDaVisita([conversa(semItem(local, 'prato-fixture'))]), {})
})
await provar('memória não ressuscita conversa ausente da lista atual da API', () => {
  assert.deepEqual(sincronizar(estado([]), [], comandasLocaisDaVisita([conversa(local)])).conversas, [])
})
await provar('estado local já montado prevalece sobre snapshot antigo da visita', () => {
  const editado = comItem(local, 'prato-fixture', { obs: 'sem queijo fixture' })
  const atual = sincronizar(estado([conversa(editado)]), [conversa(null)], comandasLocaisDaVisita([conversa(local)]))
  assert.equal(atual.conversas[0].pedido.itens[0].qtd, 3)
})
await provar('nova comanda após pedido finalizado continua local como no fluxo existente', () => {
  const finalizado = { ...novoPedido('anterior-fixture'), pedidoId: 'anterior', estado: 'entregue' }
  const atual = sincronizar(estado([]), [conversa(finalizado)], comandasLocaisDaVisita([conversa(local)]))
  assert.deepEqual(atual.conversas[0].pedido, local)
})
await provar('identidade muda por conta/empresa, sem mudar só por refresh de token', () => {
  const sessao = { usuario: { id: 'usuario-fixture' }, empresa: { id: 'empresa-fixture' }, token: 'token-a' }
  assert.equal(identidadeDaSessao(sessao), identidadeDaSessao({ ...sessao, token: 'token-b' }))
  assert.notEqual(identidadeDaSessao(sessao), identidadeDaSessao({ ...sessao, usuario: { id: 'outra-conta' } }))
  assert.notEqual(identidadeDaSessao(sessao), identidadeDaSessao({ ...sessao, empresa: { id: 'outra-empresa' } }))
  const app = readFileSync(new URL('../src/app/App.jsx', import.meta.url), 'utf8')
  assert.match(app, /AppComAcesso key=\{identidadeDaSessao\(sessao\)\}/)
  assert.match(app, /const comandasDaVisita = useRef\(\{\}\)/)
})
await provar('cada tela real recebe ajuda com título do catálogo e passos', () => {
  assert.ok(telasCatalogadas.length > 0)
  for (const { modulo, tela } of telasCatalogadas) {
    const caminho = rotaDaHash(hashDoModulo(modulo.id, tela.id), { fonteApi: true })
    assert.equal(caminho.modulo, modulo.id)
    assert.equal(caminho.tela, tela.id)
    assert.ok(caminho.tipo)
    const atual = contextoDaAjuda(caminho, true)
    assert.equal(atual.titulo, `${modulo.nome} · ${tela.rotulo}`)
    assert.ok(atual.passos.length && atual.passos.every(p => typeof p === 'string' && p.trim()))
    if (modulo.id === 'entregas' && tela.id === 'agenda') {
      assert.equal(caminho.tipo, 'entregas')
      assert.match(atual.passos.join(' '), /Primeiro dia[\s\S]*sete dias[\s\S]*reservas ativas[\s\S]*Atualizar agenda/)
      assert.notEqual(rotaDaHash(hashDoModulo(modulo.id, tela.id), { fonteApi: false }).tela, 'agenda')
    }
  }
})
await provar('hash/histórico usam rotas existentes; origem inválida volta ao catálogo seguro', () => {
  const destinos = ['#/', '#/m/atendimento/balcao', '#/m/cozinha', '#/m/entregas', '#/m/atendimento/balcao', '#/']
  assert.deepEqual(destinos.map(hash => rotaDaHash(hash, { fonteApi: true }).tipo), ['hall', 'principal', 'cozinha', 'entregas', 'principal', 'hall'])
  assert.equal(contextoDaAjuda({ tipo: 'foo', modulo: 'https://invalido/?token=secreto' }, true).titulo, 'Módulos')
})
await provar('produção antiga da Cozinha orienta para cadastro real sem fingir escrita', () => {
  const caminho = rotaDaHash('#/m/cozinha/producao', { fonteApi: true })
  const api = contextoDaAjuda(caminho, true)
  assert.match(api.passos[0], /ainda não está ligada/)
  assert.equal(hashDoModulo(api.atalho.modulo, api.atalho.tela), '#/m/producao/producao')
  assert.equal(contextoDaAjuda(caminho, false).atalho, null)
})
await provar('diagnóstico usa allowlist e nunca inclui sessão, URL ou erro arbitrário', () => {
  const texto = diagnosticoDoConsole({ rota: { ...rota, conversaId: 'cliente-secreto', query: 'token-secreto' }, fonteApi: true, versao: '97f3e157', onLine: true,
    token: 'JWT-secreto', url: 'https://servidor/?cpf=cpf-secreto', empresa: 'empresa-secreta', erro: 'nome-secreto', usuario: 'pessoa-secreta' })
  assert.match(texto, /Atendimento · Balcão/)
  assert.match(texto, /serviço ainda não verificado/)
  assert.doesNotMatch(texto, /secreto|secreta|servidor|cpf=|JWT/)
})
await provar('diagnóstico recusa versão não canônica e distingue offline, serviço e rede ausente', () => {
  assert.match(diagnosticoDoConsole({ rota, fonteApi: true, versao: 'valor-pessoal', onLine: false }), /Versão: não informada[\s\S]*offline indicado/)
  assert.match(diagnosticoDoConsole({ rota: null, fonteApi: false }), /Tela: Módulos[\s\S]*demonstração[\s\S]*não informada/)
})
await provar('cópia só confirma depois de writeText resolver', async () => {
  let resolver, enviado
  const espera = new Promise(resolve => { resolver = resolve })
  let confirmado = false
  const resultado = copiarDiagnostico('diagnóstico fixture', { writeText: texto => { enviado = texto; return espera } }).then(ok => { confirmado = ok; return ok })
  await Promise.resolve()
  assert.equal(enviado, 'diagnóstico fixture')
  assert.equal(confirmado, false)
  resolver()
  assert.equal(await resultado, true)
})
await provar('clipboard ausente ou recusado permite cópia manual sem alegar sucesso', async () => {
  assert.equal(await copiarDiagnostico('fixture', undefined), false)
  assert.equal(await copiarDiagnostico('fixture', { writeText: () => Promise.reject(new Error('permissão recusada fixture')) }), false)
})
await provar('foco de navegação chega ao início real da tela e não altera o hash', () => {
  let foco = null
  const documento = { querySelector: seletor => seletor === '[data-inicio-tela]' ? { focus: opcoes => { foco = opcoes } } : null }
  assert.equal(focarInicioDaTela(documento), true)
  assert.deepEqual(foco, { preventScroll: true })
})
await provar('foco não rouba interação de modal aberto nem tenta focar tela inexistente', () => {
  let consultas = 0
  assert.equal(focarInicioDaTela({ querySelector: seletor => { consultas++; assert.equal(seletor, 'dialog[open]'); return {} } }), false)
  assert.equal(consultas, 1)
  assert.equal(focarInicioDaTela({ querySelector: () => null }), false)
})

const resumo = { provas: resultados.length, aprovadas: resultados.length, telasCatalogadas: telasCatalogadas.length, resultados,
  limites: ['HTTP, dados e clipboard controlados; sem entrevistas humanas ou envio externo.', 'Foco, layout, histórico real e isolamento de conta na composição exigem verificação no navegador.'] }
if (process.env.SUPERBANCA_EVIDENCIA) writeFileSync(process.env.SUPERBANCA_EVIDENCIA, JSON.stringify(resumo, null, 2))
console.log(`${resultados.length} provas de navegação/suporte aprovadas.`)
