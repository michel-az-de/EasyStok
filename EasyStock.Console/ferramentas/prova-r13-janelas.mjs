// Prova da issue #42 (rodada 13, registro 104): configuração das janelas de
// entrega pela Thati (D9, RN-21, RN-22, Q2). Domínio puro e reducer de
// verdade, sem tela. Falha sem o código desta rodada (funções/ações não
// existiam antes) e passa depois.
//
// Mesmo gancho de `prova-bloqueio-preparo.mjs`: o código de `src/` importa
// sem extensão (o Vite resolve, o Node puro não).
//
// Roda com: node ferramentas/prova-r13-janelas.mjs

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

const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')
const acao = await import('../src/aplicacao/acoes.js')
const entrega = await import('../src/dominio/entrega.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const semTravessao = (texto) => assert.ok(!/[–—]/.test(texto ?? ''), texto)

// --- Domínio: faixa e validação -----------------------------------------------
confere('faixaDeHorarios monta o mesmo formato que o resto do app já lê ("Xh30 às Yh30")', () => {
  assert.equal(entrega.faixaDeHorarios('11:30', '12:30'), '11h30 às 12h30')
  assert.equal(entrega.inicioDaFaixa(entrega.faixaDeHorarios('11:30', '12:30'), Date.now()) != null, true)
})

const CAMPOS_VALIDOS = {
  horaInicio: '11:30', horaFim: '12:30', capacidade: 4, corteMinutos: 60,
  diasSemana: [1, 2, 3, 4, 5], capacidadePorLinha: null,
}

confere('erroDaJanela aceita campos completos e corretos', () => {
  assert.equal(entrega.erroDaJanela(CAMPOS_VALIDOS), null)
})
confere('erroDaJanela recusa fim antes (ou igual) do início', () => {
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, horaFim: '11:00' }), /depois do início/)
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, horaFim: '11:30' }), /depois do início/)
})
confere('erroDaJanela recusa capacidade não inteira ou menor que 1', () => {
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, capacidade: 0 }), /Capacidade/)
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, capacidade: 2.5 }), /Capacidade/)
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, capacidade: null }), /Capacidade/)
})
confere('erroDaJanela recusa corte negativo', () => {
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, corteMinutos: -5 }), /Corte/)
})
confere('erroDaJanela exige pelo menos um dia da semana', () => {
  assert.match(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, diasSemana: [] }), /dia da semana/)
})
confere('erroDaJanela recusa capacidade por linha incompleta (proposta Q2)', () => {
  assert.match(
    entrega.erroDaJanela({ ...CAMPOS_VALIDOS, capacidadePorLinha: { servir: 2, casa: null } }),
    /por linha/,
  )
})
confere('mensagens de erro sem travessão (R13 exige zero travessão)', () => {
  semTravessao(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, horaFim: '11:00' }))
  semTravessao(entrega.erroDaJanela({ ...CAMPOS_VALIDOS, capacidade: 0 }))
})

// --- Domínio: fábrica, pausa e exclusão ---------------------------------------
confere('criarJanela nasce ativa, com faixa alinhada a horaInicio/horaFim', () => {
  const j = entrega.criarJanela('jx', CAMPOS_VALIDOS)
  assert.equal(j.ativa, true)
  assert.equal(j.faixa, '11h30 às 12h30')
  assert.equal(j.capacidade, 4)
  assert.deepEqual(j.diasSemana, [1, 2, 3, 4, 5])
})
confere('pausarJanela/reativarJanela só mexem em `ativa`', () => {
  const j = entrega.criarJanela('jx', CAMPOS_VALIDOS)
  const pausada = entrega.pausarJanela(j)
  assert.equal(pausada.ativa, false)
  assert.equal(pausada.faixa, j.faixa)
  assert.equal(entrega.reativarJanela(pausada).ativa, true)
})

const CONVERSA_COM_PEDIDO_ATIVO = {
  id: 'c1', pedido: { numero: '2026-0001', janela: 'j1', estado: 'preparo' },
}
const CONVERSA_COM_PEDIDO_ENTREGUE = {
  id: 'c2', pedido: { numero: '2026-0002', janela: 'j1', estado: 'entregue' },
}

confere('janela com pedido ativo não pode ser excluída (Aceite: só pausa)', () => {
  const j = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  assert.equal(entrega.janelaTemPedidos(j, [CONVERSA_COM_PEDIDO_ATIVO]), true)
  assert.equal(entrega.podeExcluirJanela(j, [CONVERSA_COM_PEDIDO_ATIVO]), false)
})
confere('janela só com pedido já entregue pode ser excluída (mesma régua de `pedidosDaJanela`)', () => {
  const j = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  assert.equal(entrega.podeExcluirJanela(j, [CONVERSA_COM_PEDIDO_ENTREGUE]), true)
})
confere('janela sem pedido nenhum pode ser excluída', () => {
  const j = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  assert.equal(entrega.podeExcluirJanela(j, []), true)
})

// --- Domínio: visibilidade (ativa + dia da semana) ----------------------------
confere('janelaVisivelHoje esconde janela pausada', () => {
  const j = entrega.pausarJanela(entrega.criarJanela('j1', CAMPOS_VALIDOS))
  assert.equal(entrega.janelaVisivelHoje(j, Date.now()), false)
})
confere('janelaVisivelHoje respeita o dia da semana marcado', () => {
  const domingo = Date.parse('2026-09-27T12:00:00-03:00') // domingo
  const j = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, diasSemana: [1, 2, 3, 4, 5] }) // seg a sex
  assert.equal(entrega.diaDaSemanaDe(domingo), 0)
  assert.equal(entrega.janelaVisivelHoje(j, domingo), false)
  const segunda = Date.parse('2026-09-28T12:00:00-03:00')
  assert.equal(entrega.janelaVisivelHoje(j, segunda), true)
})
confere('janela sem diasSemana (semente antiga) continua visível todo dia', () => {
  const j = { id: 'antiga', faixa: '11h30 às 12h30', capacidade: 4, ativa: true }
  assert.equal(entrega.janelaVisivelHoje(j, Date.now()), true)
})
confere('ocupacaoDeHoje esconde a pausada, mas mantém quem já é o `atual`', () => {
  // `TODOS_OS_DIAS` em vez de CAMPOS_VALIDOS (seg a sex): esta prova roda em
  // qualquer dia da semana, sem depender de que dia é hoje.
  const campos = { ...CAMPOS_VALIDOS, diasSemana: entrega.TODOS_OS_DIAS }
  const ativa = entrega.criarJanela('j1', campos)
  const pausada = entrega.pausarJanela(entrega.criarJanela('j2', campos))
  const lista = entrega.ocupacaoDeHoje([ativa, pausada], [], Date.now())
  assert.deepEqual(lista.map((o) => o.id), ['j1'])
  const comAtual = entrega.ocupacaoDeHoje([ativa, pausada], [], Date.now(), 'j2')
  assert.deepEqual(comAtual.map((o) => o.id).sort(), ['j1', 'j2'])
})

// --- Domínio: corte por janela -------------------------------------------------
confere('corte por janela substitui o chão de MINUTOS_DE_CORTE quando presente', () => {
  const comeca = entrega.inicioDaFaixa('11h30 às 12h30', Date.now())
  // 80 min antes do início: dentro do corte de 90 (>= comeca - 90min), fora
  // do chão de sempre (MINUTOS_DE_CORTE = 60). Só o corte por janela explica
  // o resultado.
  const agora = comeca - 80 * 60000
  const j90 = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, corteMinutos: 90 })
  const ocupacao = entrega.ocupacaoDaJanela(j90, [], agora)
  assert.equal(ocupacao.emCorte, true, 'corte de 90 min já pegou 80 min antes')
  assert.match(entrega.motivoDoBloqueio(ocupacao), /90 min/)
})

// --- Domínio: respiro mínimo (RN-22) -------------------------------------------
confere('ajustarRespiroMinimo nunca desce do chão do RN-22 (40 min)', () => {
  assert.equal(entrega.ajustarRespiroMinimo(10), entrega.RESPIRO_MINIMO_RN22)
  assert.equal(entrega.ajustarRespiroMinimo(60), 60)
})
confere('faixaParaCliente aceita respiro configurado sem quebrar quem não passa o terceiro argumento', () => {
  const agora = Date.now()
  const faixa = '11h30 às 12h30'
  assert.equal(typeof entrega.faixaParaCliente(faixa, agora), 'string')
  assert.equal(typeof entrega.faixaParaCliente(faixa, agora, 90), 'string')
})

// --- Domínio: proposta Q2, capacidade por linha --------------------------------
const CARDAPIO_TESTE = [
  { sku: 'SERVIR-1', linha: 'servir' },
  { sku: 'CASA-1', linha: 'casa' },
]
function conversaComItens(id, janela, skus) {
  return { id, pedido: { numero: id, janela, estado: 'preparo', itens: skus.map((sku) => ({ sku, qtd: 1 })) } }
}

confere('sem capacidadePorLinha, a conta é a de sempre (por pedido, capacidade total)', () => {
  const j = entrega.criarJanela('j1', CAMPOS_VALIDOS) // capacidadePorLinha: null
  const conversas = [conversaComItens('c1', 'j1', ['SERVIR-1', 'CASA-1'])]
  const ocupacao = entrega.ocupacaoDaJanela(j, conversas, Date.now(), null, CARDAPIO_TESTE)
  assert.equal(ocupacao.porLinha, null)
  assert.equal(ocupacao.ocupadas, 1)
})
confere('com capacidadePorLinha, pedido misto ocupa vaga NAS DUAS linhas (proposta Q2)', () => {
  const j = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, capacidade: 10, capacidadePorLinha: { servir: 1, casa: 1 } })
  const conversas = [conversaComItens('c1', 'j1', ['SERVIR-1', 'CASA-1'])]
  const ocupacao = entrega.ocupacaoDaJanela(j, conversas, Date.now(), null, CARDAPIO_TESTE)
  assert.deepEqual(ocupacao.porLinha.servir, { capacidade: 1, ocupadas: 1, vagas: 0 })
  assert.deepEqual(ocupacao.porLinha.casa, { capacidade: 1, ocupadas: 1, vagas: 0 })
  // Capacidade TOTAL da janela ainda tem sobra (10), mas a linha mais
  // apertada zera a vaga: "impedir pedido encavalado" (a pergunta da
  // Tatiana) significa a vaga global respeitar o lado mais restritivo.
  assert.equal(ocupacao.vagas, 0)
  assert.match(entrega.motivoDoBloqueio(ocupacao), /Linha/)
})
confere('com capacidadePorLinha, pedido só de uma linha não consome a outra', () => {
  const j = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, capacidade: 10, capacidadePorLinha: { servir: 2, casa: 1 } })
  const conversas = [conversaComItens('c1', 'j1', ['SERVIR-1'])]
  const ocupacao = entrega.ocupacaoDaJanela(j, conversas, Date.now(), null, CARDAPIO_TESTE)
  assert.equal(ocupacao.porLinha.servir.ocupadas, 1)
  assert.equal(ocupacao.porLinha.casa.ocupadas, 0)
  assert.equal(ocupacao.vagas, 1) // servir: 2-1=1 vaga, é o gargalo (casa 1-0=1 empata, total 10-1=9)
})
confere('sem cardápio (chamador antigo), capacidadePorLinha some sem quebrar', () => {
  const j = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, capacidadePorLinha: { servir: 1, casa: 1 } })
  const ocupacao = entrega.ocupacaoDaJanela(j, [conversaComItens('c1', 'j1', ['SERVIR-1'])], Date.now())
  assert.equal(ocupacao.porLinha, null)
})

// --- Reducer: CRUD de janela ----------------------------------------------------
function estadoComJanelas(janelas, conversas = []) {
  return estadoInicial({
    conversas, catalogo: { janelas, cardapio: CARDAPIO_TESTE, respiroMinutos: 40 }, regras: [], modoAgente: 'sugerir',
  })
}

confere('reducer CRIAR_JANELA acrescenta ao catálogo', () => {
  const inicio = estadoComJanelas([])
  const depois = reducer(inicio, { tipo: acao.CRIAR_JANELA, id: 'nova', campos: CAMPOS_VALIDOS })
  assert.equal(depois.catalogo.janelas.length, 1)
  assert.equal(depois.catalogo.janelas[0].id, 'nova')
  assert.equal(depois.catalogo.janelas[0].faixa, '11h30 às 12h30')
})
confere('reducer CRIAR_JANELA com campos inválidos não muda o estado (reducer não confia só na tela)', () => {
  const inicio = estadoComJanelas([])
  const depois = reducer(inicio, { tipo: acao.CRIAR_JANELA, id: 'nova', campos: { ...CAMPOS_VALIDOS, capacidade: 0 } })
  assert.equal(depois, inicio)
})
confere('reducer EDITAR_JANELA troca os campos da janela certa, sem mexer nas outras', () => {
  const j1 = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  const j2 = entrega.criarJanela('j2', CAMPOS_VALIDOS)
  const inicio = estadoComJanelas([j1, j2])
  const depois = reducer(inicio, {
    tipo: acao.EDITAR_JANELA, id: 'j1', campos: { ...CAMPOS_VALIDOS, capacidade: 9 },
  })
  assert.equal(depois.catalogo.janelas.find((j) => j.id === 'j1').capacidade, 9)
  assert.equal(depois.catalogo.janelas.find((j) => j.id === 'j2').capacidade, 4)
})
confere('reducer PAUSAR_JANELA e REATIVAR_JANELA alternam `ativa`', () => {
  const j1 = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  const inicio = estadoComJanelas([j1])
  const pausado = reducer(inicio, { tipo: acao.PAUSAR_JANELA, id: 'j1' })
  assert.equal(pausado.catalogo.janelas[0].ativa, false)
  const reativado = reducer(pausado, { tipo: acao.REATIVAR_JANELA, id: 'j1' })
  assert.equal(reativado.catalogo.janelas[0].ativa, true)
})
confere('reducer EXCLUIR_JANELA remove só quem não tem pedido', () => {
  const j1 = entrega.criarJanela('j1', CAMPOS_VALIDOS)
  const j2 = entrega.criarJanela('j2', CAMPOS_VALIDOS)
  const inicio = estadoComJanelas([j1, j2], [conversaComItens('c1', 'j1', ['SERVIR-1'])])
  inicio.conversas[0].pedido.estado = 'preparo'
  const bloqueado = reducer(inicio, { tipo: acao.EXCLUIR_JANELA, id: 'j1' })
  assert.equal(bloqueado, inicio, 'j1 tem pedido ativo: reducer recusa e devolve o mesmo estado')
  const excluido = reducer(inicio, { tipo: acao.EXCLUIR_JANELA, id: 'j2' })
  assert.deepEqual(excluido.catalogo.janelas.map((j) => j.id), ['j1'])
})
confere('reducer AJUSTAR_RESPIRO_MINIMO trava o piso do RN-22', () => {
  const inicio = estadoComJanelas([])
  const depois = reducer(inicio, { tipo: acao.AJUSTAR_RESPIRO_MINIMO, minutos: 10 })
  assert.equal(depois.catalogo.respiroMinutos, 40)
  const subiu = reducer(inicio, { tipo: acao.AJUSTAR_RESPIRO_MINIMO, minutos: 75 })
  assert.equal(subiu.catalogo.respiroMinutos, 75)
})
confere('reducer AVANCAR_ESTEIRA usa o respiro do catálogo no aviso de "em preparo" (RN-22 ajustável)', () => {
  const j1 = entrega.criarJanela('j1', { ...CAMPOS_VALIDOS, horaInicio: '00:00', horaFim: '01:00' })
  const conversa = {
    id: 'c1', cadastroId: 'cad-1', nome: 'Cliente Teste', estado: 'Em atendimento',
    responsavel: 'Thatiane', mensagens: [], bloqueio: null,
    pedido: { numero: '2026-0001', estado: 'pago', janela: 'j1', entregador: null, itens: [], agradecimentoEnviado: false, pagamentos: [] },
  }
  const base = estadoInicial({
    conversas: [conversa], catalogo: { janelas: [j1], cardapio: [], respiroMinutos: 90 }, regras: [], modoAgente: 'sugerir',
  })
  const agora = Date.parse('2026-09-27T00:10:00-03:00')
  const depois = reducer(base, {
    tipo: acao.AVANCAR_ESTEIRA, id: 'c1', passo: 'preparo', agora, mensagemId: 'm1', posEntregaId: 'p1',
  })
  const avisoTexto = depois.conversas[0].mensagens.map((m) => m.texto).join(' | ')
  // Respiro de 90 min: a faixa que sai para o cliente precisa refletir isso,
  // não o chão de 40 escondido de novo.
  assert.match(avisoTexto, /1h40|01h40/)
})

console.log(`\n${passou} verificações passaram.`)
