// Prova da #1447 (homologação de 07/10): o console abre num hall com os módulos
// do ERP da Casa da Baba (M1–M8, ADR-0056) e cada módulo tem o próprio menu,
// derivado da rota (ADR-0046 D1, em hash como manda a spec M0.2). As abas do
// antigo modal Gestão viram telas do módulo dono.
//
// Cobre as funções puras: o catálogo (`dominio/modulos.js`), a rota
// (`dominio/rota.js: rotaDaHash`) e o resumo do canal WhatsApp na tela Canais
// (`dominio/canais.js`). A tela em si é validada no navegador.
//
// Roda com: node ferramentas/prova-1447-hall-modulos.mjs

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

const {
  MODULOS, modulosDoHall, moduloPorId, hashDoModulo, telasDoMenu, saudacaoDoHall,
} = await import('../src/dominio/modulos.js')
const {
  ROTA_HALL, ROTA_MODULO, ROTA_PRINCIPAL, ROTA_COZINHA, ROTA_ENTREGAS, ROTA_CARDAPIO_LINK, rotaDaHash,
} = await import('../src/dominio/rota.js')
const { resumoDoWhatsApp, statusDaFalha } = await import('../src/dominio/canais.js')
const { rotuloDaSessao } = await import('../src/dominio/sessao.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// ---------------------------------------------------------------- catálogo

confere('o hall lista os oito módulos do plano, numerados de 1 a 8 e na ordem', () => {
  assert.deepEqual(MODULOS.map((m) => m.numero), [1, 2, 3, 4, 5, 6, 7, 8])
  assert.deepEqual(
    MODULOS.map((m) => m.id),
    ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas'],
  )
  for (const m of MODULOS) {
    assert.ok(m.nome && m.resumo && m.icone, `módulo ${m.id} com nome, resumo e ícone`)
  }
})

// M1.1 (#1481): o Cardápio ganhou a gestão no modo API; na demonstração segue em breve.
confere('Produção ainda não tem tela; Cardápio abre só no modo API', () => {
  const hall = modulosDoHall({ fonteApi: true })
  const porId = Object.fromEntries(hall.map((m) => [m.id, m]))
  assert.equal(porId.producao.disponivel, false)
  assert.equal(porId.producao.href, null, 'card em breve não navega')
  const demonstracao = Object.fromEntries(modulosDoHall({ fonteApi: false }).map((m) => [m.id, m]))
  assert.equal(demonstracao.cardapio.disponivel, false, 'sem API não há o que gerir')
  for (const id of ['cardapio', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas']) {
    assert.equal(porId[id].disponivel, true, `${id} abre`)
    assert.ok(porId[id].href.startsWith('#/m/' + id), `${id} aponta para a própria rota`)
  }
})

confere('Atendimento, Cozinha e Entregas abrem a tela de operação de hoje', () => {
  assert.equal(moduloPorId('atendimento').telas[0].id, 'balcao')
  assert.equal(moduloPorId('cozinha').telas[0].id, 'fila')
  assert.equal(moduloPorId('entregas').telas[0].id, 'painel')
})

confere('cada aba da antiga Gestão mora numa tela do módulo dono', () => {
  const dono = (aba) => MODULOS.find((m) => m.telas.some((t) => t.aba === aba))?.id
  assert.equal(dono('atendimento'), 'atendimento')
  assert.equal(dono('producao'), 'cozinha')
  assert.equal(dono('janelas'), 'entregas')
  assert.equal(dono('caixa'), 'financeiro')
  assert.equal(dono('fidelidade'), 'campanhas')
  assert.equal(dono('integracoes'), 'configuracoes')
  assert.equal(dono('canais'), 'configuracoes', 'WhatsApp Business ganhou tela própria (Canais)')
})

confere('tela que só existe no modo API some do menu no modo demonstração', () => {
  const comApi = telasDoMenu(moduloPorId('atendimento'), { fonteApi: true }).map((t) => t.id)
  const semApi = telasDoMenu(moduloPorId('atendimento'), { fonteApi: false }).map((t) => t.id)
  // #1441: Respostas e automáticas existe nos dois modos (a demonstração grava no navegador).
  assert.deepEqual(comApi, ['balcao', 'horarios', 'respostas'])
  assert.deepEqual(semApi, ['balcao', 'respostas'])
  assert.ok(!telasDoMenu(moduloPorId('configuracoes'), { fonteApi: false }).some((t) => t.id === 'canais'))
})

confere('hash do módulo e da tela', () => {
  assert.equal(hashDoModulo('cozinha'), '#/m/cozinha')
  assert.equal(hashDoModulo('cozinha', 'producao'), '#/m/cozinha/producao')
})

confere('saudação do hall pelo relógio da loja (São Paulo), com o primeiro nome', () => {
  assert.equal(saudacaoDoHall(Date.parse('2026-10-07T11:00:00Z'), 'Thatiane Souza'), 'Bom dia, Thatiane')
  assert.equal(saudacaoDoHall(Date.parse('2026-10-07T17:00:00Z'), 'Felipe'), 'Boa tarde, Felipe')
  // 22h em São Paulo já é dia seguinte no UTC: vale a hora da loja.
  assert.equal(saudacaoDoHall(Date.parse('2026-10-08T01:00:00Z'), null), 'Boa noite')
})

// ---------------------------------------------------------------- rota

confere('sem hash (logo depois do login) é o hall, não o Balcão', () => {
  for (const hash of ['', '#', '#/', undefined, null]) assert.equal(rotaDaHash(hash).tipo, ROTA_HALL)
})

confere('hash desconhecida e módulo inexistente caem no hall', () => {
  assert.equal(rotaDaHash('#/nada-a-ver').tipo, ROTA_HALL)
  assert.equal(rotaDaHash('#/m/financeiro-que-nao-existe').tipo, ROTA_HALL)
})

confere('o módulo Atendimento abre o Balcão de hoje (cockpit, D5)', () => {
  const rota = rotaDaHash('#/m/atendimento', { fonteApi: true })
  assert.equal(rota.tipo, ROTA_PRINCIPAL)
  assert.equal(rota.modulo, 'atendimento')
  assert.equal(rota.tela, 'balcao')
})

confere('Cozinha e Entregas pelo módulo abrem as mesmas telas de #/cozinha e #/entregas', () => {
  assert.equal(rotaDaHash('#/m/cozinha').tipo, ROTA_COZINHA)
  assert.equal(rotaDaHash('#/m/cozinha').modulo, 'cozinha')
  assert.equal(rotaDaHash('#/m/entregas/painel').tipo, ROTA_ENTREGAS)
  assert.equal(rotaDaHash('#/m/entregas/painel').modulo, 'entregas')
})

confere('os apelidos de hoje continuam: #/cozinha, #/entregas e o cardápio por link', () => {
  assert.equal(rotaDaHash('#/cozinha').tipo, ROTA_COZINHA)
  assert.equal(rotaDaHash('#/cozinha').modulo, undefined, 'janela avulsa da cozinha, sem barra de módulo')
  assert.equal(rotaDaHash('#/entregas').tipo, ROTA_ENTREGAS)
  const link = rotaDaHash('#/cardapio-link/c9')
  assert.equal(link.tipo, ROTA_CARDAPIO_LINK)
  assert.equal(link.conversaId, 'c9')
})

confere('tela de ajuste abre dentro do módulo, com o módulo vindo da rota', () => {
  const rota = rotaDaHash('#/m/cozinha/producao')
  assert.deepEqual(rota, { tipo: ROTA_MODULO, modulo: 'cozinha', tela: 'producao', aba: 'producao' })
  assert.equal(rotaDaHash('#/m/financeiro').aba, 'caixa', 'Financeiro abre no caixa')
  assert.equal(rotaDaHash('#/m/configuracoes/canais', { fonteApi: true }).aba, 'canais')
})

confere('tela desconhecida (ou só da API, no modo demonstração) cai na primeira tela do módulo', () => {
  assert.equal(rotaDaHash('#/m/entregas/xyz').tipo, ROTA_ENTREGAS)
  assert.equal(rotaDaHash('#/m/atendimento/horarios', { fonteApi: false }).tipo, ROTA_PRINCIPAL)
  assert.equal(rotaDaHash('#/m/atendimento/horarios', { fonteApi: true }).aba, 'atendimento')
})

confere('módulo em breve digitado à mão abre a moldura do módulo, sem tela', () => {
  assert.deepEqual(rotaDaHash('#/m/producao'), { tipo: ROTA_MODULO, modulo: 'producao', tela: null, aba: null })
})

// ---------------------------------------------------------------- canais

const AGORA = Date.parse('2026-10-07T15:00:00Z')

confere('WhatsApp com número, webhook e mensagem recente: ligado', () => {
  const r = resumoDoWhatsApp({
    phoneNumberId: '123', provider: 'meta', webhookVerificadoEm: '2026-10-01T10:00:00Z',
    ultimaMensagemRecebidaEm: '2026-10-07T14:50:00Z',
  }, AGORA)
  assert.equal(r.tom, 'ok')
  assert.match(r.titulo, /ligado/i)
  // #1474: a linha diz a hora e o dia da última mensagem, no fuso da loja.
  assert.ok(r.linhas.includes('Recebendo mensagens (última às 11:50 de 07/10)'), 'diz quando chegou a última mensagem')
})

confere('provedor simulado (stub) avisa que nada sai para a Meta', () => {
  const r = resumoDoWhatsApp({ phoneNumberId: '123', provider: 'stub' }, AGORA)
  assert.equal(r.tom, 'aviso')
  assert.match(r.titulo, /simulad/i)
})

confere('sem número vinculado pede para conectar', () => {
  const r = resumoDoWhatsApp({ phoneNumberId: null, provider: 'meta' }, AGORA)
  assert.equal(r.tom, 'aviso')
  assert.match(r.titulo, /nenhum número/i)
})

confere('atendimento desligado para a loja (404) e sem permissão (403) não fingem estado', () => {
  assert.match(resumoDoWhatsApp(null, AGORA).titulo, /não está ligado/i)
  const semPermissao = resumoDoWhatsApp({ semPermissao: true }, AGORA)
  assert.equal(semPermissao.tom, 'neutro')
  assert.match(semPermissao.titulo, /administrador/i)
})

confere('webhook nunca verificado e nenhuma mensagem recebida aparecem por escrito', () => {
  const r = resumoDoWhatsApp({ phoneNumberId: '123', provider: 'meta' }, AGORA)
  assert.ok(r.linhas.some((l) => /webhook ainda não verificado/i.test(l)))
  assert.ok(r.linhas.some((l) => /nenhuma mensagem recebida/i.test(l)))
})

confere('falha da API vira estado honesto: 404 = não ligado, 403 = sem permissão, resto = erro', () => {
  assert.equal(statusDaFalha(404), null)
  assert.deepEqual(statusDaFalha(403), { semPermissao: true })
  assert.equal(statusDaFalha(500), undefined)
  assert.equal(statusDaFalha(0), undefined, 'sem conexão não finge que o canal está desligado')
})

// ---------------------------------------------------------------- faixa da sessão

confere('faixa da sessão sem "· null" quando a empresa vem sem nome (visto na homologação)', () => {
  assert.equal(rotuloDaSessao({ usuario: { nome: 'Felipe' }, empresa: { nome: null } }), 'Felipe')
  assert.equal(rotuloDaSessao({ usuario: { nome: 'Felipe' }, empresa: { nome: 'Casa da Baba' } }), 'Felipe · Casa da Baba')
  assert.equal(rotuloDaSessao({ usuario: { nome: 'Thati' } }), 'Thati')
  assert.equal(rotuloDaSessao(null), '')
})

console.log(`\n${passou} verificações passaram.`)
