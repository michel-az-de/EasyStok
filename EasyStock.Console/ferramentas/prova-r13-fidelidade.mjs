// Prova da rodada 13 · Fidelidade e cupons (issue #45, registro 107).
// Exercita o domínio puro (`dominio/fidelidade.js`) e o reducer de verdade
// (cupom aplicado no pedido, resgate de recompensa, e a mensagem de
// pagamento confirmado ganhando a linha de pontos, RN-40 anti-spam).
//
// Mesmo gancho de `ferramentas/prova-bloqueio-preparo.mjs`: o código de
// `src/` importa sem extensão (Vite resolve, Node puro não).
//
// Roda com: node ferramentas/prova-r13-fidelidade.mjs

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
const {
  historicoElegivelParaFidelidade, novoCupom, pontosGanhos, pontosGanhosNoPedido,
  saldoDePontos, totalComDesconto, validarCupom, valorDoDesconto,
} = await import('../src/dominio/fidelidade.js')
const { REGRAS_PADRAO } = await import('../src/dominio/automacao.js')
const { diferencaAposPagamento } = await import('../src/dominio/pagamento.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = Date.parse('2026-09-27T12:00:00-03:00')

// --- Domínio: cupom ----------------------------------------------------------

confere('domínio: desconto percentual e desconto de valor fixo', () => {
  const percentual = novoCupom('c1', { codigo: 'dez', tipoDesconto: 'percentual', valor: 10 })
  const fixo = novoCupom('c2', { codigo: 'quinze', tipoDesconto: 'valor', valor: 15 })
  assert.equal(valorDoDesconto(percentual, 100), 10)
  assert.equal(valorDoDesconto(fixo, 100), 15)
})

confere('domínio: cupom de código nasce em maiúsculas, sem espaço nas pontas', () => {
  const cupom = novoCupom('c1', { codigo: '  bemvinda10  ', valor: 10 })
  assert.equal(cupom.codigo, 'BEMVINDA10')
})

confere('domínio: desconto nunca passa do total do pedido', () => {
  const fixo = novoCupom('c1', { codigo: 'grande', tipoDesconto: 'valor', valor: 500 })
  assert.equal(valorDoDesconto(fixo, 60), 60)
})

confere('domínio: cupom vencido, inativo, sem uso e abaixo do mínimo são recusados', () => {
  const vencido = novoCupom('c1', { codigo: 'v', valor: 10, validade: '2026-01-01' })
  assert.equal(validarCupom(vencido, { agora: AGORA, totalPedido: 100 }).valido, false)

  const inativo = { ...novoCupom('c2', { codigo: 'i', valor: 10 }), ativo: false }
  assert.equal(validarCupom(inativo, { agora: AGORA, totalPedido: 100 }).valido, false)

  const semUso = { ...novoCupom('c3', { codigo: 'u', valor: 10, limiteUsos: 1 }), usos: 1 }
  assert.equal(validarCupom(semUso, { agora: AGORA, totalPedido: 100 }).valido, false)

  const minimo = novoCupom('c4', { codigo: 'm', valor: 10, minimoPedido: 200 })
  assert.equal(validarCupom(minimo, { agora: AGORA, totalPedido: 100 }).valido, false)
})

confere('domínio: cupom válido dentro da validade e do mínimo', () => {
  const cupom = novoCupom('c1', { codigo: 'ok', valor: 10, validade: '2026-12-31', minimoPedido: 50 })
  assert.equal(validarCupom(cupom, { agora: AGORA, totalPedido: 100 }).valido, true)
})

// --- Domínio: pontos (regra 'valor' e regra 'pedidos') ------------------------

confere('domínio: regra "valor" soma ponto por faixa de real gasto, por pedido', () => {
  const regra = { tipo: 'valor', valorPorPonto: 10, pontosPorFaixa: 1 }
  const pedidos = [{ estado: 'entregue', total: 85 }, { estado: 'pago', total: 22 }]
  // floor(85/10)=8 + floor(22/10)=2 -> 10
  assert.equal(pontosGanhos(pedidos, regra), 10)
})

confere('domínio: regra "pedidos" fecha faixa a cada N pedidos pagos', () => {
  const regra = { tipo: 'pedidos', pedidosPorPonto: 5, pontosPorPedidos: 20 }
  const dez = Array.from({ length: 10 }, () => ({ estado: 'entregue', total: 1 }))
  assert.equal(pontosGanhos(dez, regra), 40)
  assert.equal(pontosGanhos(dez.slice(0, 4), regra), 0)
})

confere('domínio: pedido cancelado, aguardando e agendado não pontuam (filtro de elegibilidade)', () => {
  const regra = { tipo: 'valor', valorPorPonto: 10, pontosPorFaixa: 1 }
  const historico = [
    { numero: 'a', estado: 'cancelado', total: 1000 },
    { numero: 'b', estado: 'aguardando', total: 1000 },
    { numero: 'c', estado: 'agendado', total: 1000 },
  ]
  const elegiveis = historicoElegivelParaFidelidade(historico, null)
  assert.equal(elegiveis.length, 0, 'nenhum dos três é elegível')
  assert.equal(pontosGanhos(elegiveis, regra), 0)
})

confere('domínio: saldo desconta o que já foi resgatado', () => {
  const regra = { tipo: 'valor', valorPorPonto: 10, pontosPorFaixa: 1 }
  const pedidos = [{ estado: 'entregue', total: 100 }]
  const resgates = [{ custoPontos: 4 }]
  assert.equal(saldoDePontos(pedidos, resgates, regra), 6)
  assert.equal(saldoDePontos(pedidos, [{ custoPontos: 999 }], regra), 0, 'nunca fica negativo')
})

confere('domínio: pedido AO VIVO estornado sai da conta de fidelidade (pagamento desfeito/estorno)', () => {
  const historico = [{ numero: '2026-0001', estado: 'entregue', total: 85 }]
  const vivoEstornado = { numero: '2026-0001', cobranca: { estornadaEm: AGORA } }
  assert.equal(historicoElegivelParaFidelidade(historico, vivoEstornado).length, 0)
  const vivoIntacto = { numero: '2026-0001', cobranca: { estornadaEm: null } }
  assert.equal(historicoElegivelParaFidelidade(historico, vivoIntacto).length, 1)
})

confere('domínio: pontos ganhos por pedido isolado só existem na regra "valor" (RN-40, mensagem transacional)', () => {
  assert.equal(pontosGanhosNoPedido(85, { tipo: 'valor', valorPorPonto: 10, pontosPorFaixa: 1 }), 8)
  assert.equal(pontosGanhosNoPedido(85, { tipo: 'pedidos', pedidosPorPonto: 5, pontosPorPedidos: 20 }), 0)
})

// --- Reducer -------------------------------------------------------------------

const conversaBase = () => ({
  id: 'c1', cadastroId: 'cad-1', nome: 'Cliente Teste', canal: 'WhatsApp',
  estado: 'Em atendimento', responsavel: 'Thatiane', mensagens: [], cliente: { notas: [], tags: [] },
  pedido: {
    numero: '2026-0001', estado: 'aguardando', janela: 'j3', entregador: null,
    itens: [{ sku: 'LAS-CLA', qtd: 1, obs: '', acrescimo: false, entrouEm: null }],
    agradecimentoEnviado: false, pagamentos: [],
  },
})

const cardapio = [{ sku: 'LAS-CLA', nome: 'Lasanha clássica', preco: 85 }]

// Zera a semente (infra/fidelidadeSemente.js) para as provas ficarem
// determinísticas: cada uma cria só o que precisa, sem contar com o exemplo
// pronto de fábrica.
function estadoComCupom(cupons = []) {
  const base = estadoInicial({
    conversas: [conversaBase()], catalogo: { janelas: [], cardapio }, regras: REGRAS_PADRAO, modoAgente: 'sugerir',
  })
  return { ...base, fidelidade: { ...base.fidelidade, cupons, recompensas: [], sorteios: [] } }
}

confere('reducer: APLICAR_CUPOM válido grava valorDesconto no pedido e soma uso', () => {
  const cupom = novoCupom('cupom-1', { codigo: 'DEZ', tipoDesconto: 'percentual', valor: 10 })
  const depois = reducer(estadoComCupom([cupom]), {
    tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'dez', agora: AGORA,
  })
  const pedido = depois.conversas[0].pedido
  assert.deepEqual(pedido.cupom, { id: 'cupom-1', codigo: 'DEZ' })
  assert.equal(pedido.valorDesconto, 8.5, 'total 85 * 10%')
  assert.equal(depois.fidelidade.cupons[0].usos, 1)
})

confere('reducer: APLICAR_CUPOM inválido (vencido) não muda nada', () => {
  const cupom = novoCupom('cupom-1', { codigo: 'VELHO', valor: 10, validade: '2020-01-01' })
  const antes = estadoComCupom([cupom])
  const depois = reducer(antes, { tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'VELHO', agora: AGORA })
  assert.equal(depois, antes)
})

confere('reducer: totalComDesconto do domínio reflete o desconto gravado no pedido', () => {
  const cupom = novoCupom('cupom-1', { codigo: 'DEZ', tipoDesconto: 'percentual', valor: 10 })
  const aplicado = reducer(estadoComCupom([cupom]), {
    tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'DEZ', agora: AGORA,
  })
  const pedido = aplicado.conversas[0].pedido
  assert.equal(totalComDesconto(pedido, cardapio), 76.5)
})

confere('reducer: GERAR_PEDIDO com cupom aplicado cobra o LÍQUIDO, sem virar "cobrança de diferença"', () => {
  // Este é o caminho de verdade que o Provider dispara (gerarPedido em
  // AtendimentoProvider.jsx): a emissão chega pronta de fora, o reducer só
  // monta a cobrança. Existiu um defeito aqui (registro 107): `criarCobranca`
  // ignorava `pedido.valorDesconto` e cobrava o total bruto por engano.
  const cupom = novoCupom('cupom-1', { codigo: 'DEZ', tipoDesconto: 'percentual', valor: 10 })
  const comCupom = reducer(estadoComCupom([cupom]), {
    tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'DEZ', agora: AGORA,
  })
  const emissao = { identificador: 'cb-teste', copiaECola: 'codigo-teste', link: 'https://pix.teste/1', meio: 'pix' }
  const gerado = reducer(comCupom, {
    tipo: acao.GERAR_PEDIDO, id: 'c1', agora: AGORA, mensagemId: 'm1', cobrancaId: 'm2', emissao,
  })
  const cobranca = gerado.conversas[0].pedido.cobranca
  assert.equal(cobranca.valor, 76.5, 'cobrança sai no total já com desconto')
  assert.equal(cobranca.diferenca, false, 'cobrança normal não é cobrança de diferença/complemento')
  const mensagemCobranca = gerado.conversas[0].mensagens.find((m) => m.id === 'm2')
  assert.match(mensagemCobranca.texto, /Pedido 2026-0001 anotado!/, 'texto normal, não o de "faltou uma parte"')
  assert.match(mensagemCobranca.texto, /Total: R\$\s?76,50/)
})

confere('reducer: REMOVER_CUPOM_DO_PEDIDO limpa o pedido e devolve o uso ao cupom', () => {
  const cupom = novoCupom('cupom-1', { codigo: 'DEZ', tipoDesconto: 'percentual', valor: 10 })
  const aplicado = reducer(estadoComCupom([cupom]), {
    tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'DEZ', agora: AGORA,
  })
  const removido = reducer(aplicado, { tipo: acao.REMOVER_CUPOM_DO_PEDIDO, id: 'c1' })
  const pedido = removido.conversas[0].pedido
  assert.equal(pedido.cupom, null)
  assert.equal(pedido.valorDesconto, 0)
  assert.equal(removido.fidelidade.cupons[0].usos, 0)
})

confere('reducer: CRIAR_CUPOM / ALTERNAR_CUPOM_ATIVO fazem CRUD básico', () => {
  const inicio = estadoComCupom([])
  const criado = reducer(inicio, {
    tipo: acao.CRIAR_CUPOM, cupomId: 'novo', dados: { codigo: 'novo', valor: 5 },
  })
  assert.equal(criado.fidelidade.cupons.length, 1)
  const alternado = reducer(criado, { tipo: acao.ALTERNAR_CUPOM_ATIVO, id: 'novo' })
  assert.equal(alternado.fidelidade.cupons[0].ativo, false)
})

confere('reducer: EDITAR_REGRA_FIDELIDADE só troca os campos enviados', () => {
  const inicio = estadoComCupom([])
  const editado = reducer(inicio, {
    tipo: acao.EDITAR_REGRA_FIDELIDADE, dados: { valorPorPonto: 20 },
  })
  assert.equal(editado.fidelidade.config.valorPorPonto, 20)
  assert.equal(editado.fidelidade.config.tipo, 'valor', 'resto da regra continua')
})

confere('reducer: CRIAR_RECOMPENSA / ALTERNAR_RECOMPENSA_ATIVA fazem CRUD básico', () => {
  const inicio = estadoComCupom([])
  const criado = reducer(inicio, {
    tipo: acao.CRIAR_RECOMPENSA,
    recompensaId: 'r1',
    dados: { tipo: 'frete-gratis', rotulo: 'Frete grátis', custoPontos: 30 },
  })
  assert.equal(criado.fidelidade.recompensas.length, 1)
  const alternado = reducer(criado, { tipo: acao.ALTERNAR_RECOMPENSA_ATIVA, id: 'r1' })
  assert.equal(alternado.fidelidade.recompensas[0].ativo, false)
})

confere('reducer: RESGATAR_RECOMPENSA sem saldo suficiente não muda nada', () => {
  const comRecompensa = reducer(estadoComCupom([]), {
    tipo: acao.CRIAR_RECOMPENSA,
    recompensaId: 'r1',
    dados: { tipo: 'frete-gratis', rotulo: 'Frete grátis', custoPontos: 30 },
  })
  const depois = reducer(comRecompensa, {
    tipo: acao.RESGATAR_RECOMPENSA, cadastroId: 'cad-1', nomeCliente: 'Cliente Teste',
    recompensaId: 'r1', saldoDisponivel: 10, agora: AGORA, resgateId: 'res-1',
  })
  assert.equal(depois, comRecompensa)
})

confere('reducer: RESGATAR_RECOMPENSA com saldo grava o resgate por cadastro', () => {
  const comRecompensa = reducer(estadoComCupom([]), {
    tipo: acao.CRIAR_RECOMPENSA,
    recompensaId: 'r1',
    dados: { tipo: 'frete-gratis', rotulo: 'Frete grátis', custoPontos: 30 },
  })
  const depois = reducer(comRecompensa, {
    tipo: acao.RESGATAR_RECOMPENSA, cadastroId: 'cad-1', nomeCliente: 'Cliente Teste',
    recompensaId: 'r1', saldoDisponivel: 50, agora: AGORA, resgateId: 'res-1',
  })
  assert.equal(depois.fidelidade.resgatesPorCadastro['cad-1'].length, 1)
  assert.equal(depois.fidelidade.resgatesPorCadastro['cad-1'][0].custoPontos, 30)
})

confere('reducer: resgatar recompensa de sorteio grava participante com número da sorte', () => {
  const comSorteio = reducer(estadoComCupom([]), {
    tipo: acao.CRIAR_SORTEIO, sorteioId: 's1', dados: { nome: 'Sorteio de outubro' },
  })
  const comRecompensa = reducer(comSorteio, {
    tipo: acao.CRIAR_RECOMPENSA,
    recompensaId: 'r1',
    dados: {
      tipo: 'sorteio', rotulo: 'Número da sorte', custoPontos: 10, sorteioId: 's1',
    },
  })
  const depois = reducer(comRecompensa, {
    tipo: acao.RESGATAR_RECOMPENSA, cadastroId: 'cad-1', nomeCliente: 'Cliente Teste',
    recompensaId: 'r1', saldoDisponivel: 10, agora: AGORA, resgateId: 'res-1',
  })
  const sorteio = depois.fidelidade.sorteios.find((s) => s.id === 's1')
  assert.equal(sorteio.participantes.length, 1)
  assert.equal(sorteio.participantes[0].numero, 1)
  assert.equal(sorteio.participantes[0].nome, 'Cliente Teste')
})

// --- Integração: pagamento confirmado avisa pontos na MESMA mensagem (RN-40) --

confere('reducer: CONFIRMAR_PAGAMENTO soma a linha de pontos na mensagem automática já existente', () => {
  const conversa = {
    ...conversaBase(),
    pedido: {
      ...conversaBase().pedido,
      estado: 'aguardando',
      cobranca: {
        id: 'cb1', meio: 'pix', valor: 85, valorPago: null, pagaEm: null, liberadaEm: null, estornadaEm: null,
      },
    },
  }
  const inicio = estadoInicial({
    conversas: [conversa], catalogo: { janelas: [], cardapio }, regras: REGRAS_PADRAO, modoAgente: 'sugerir',
  })
  const depois = reducer(inicio, {
    tipo: acao.CONFIRMAR_PAGAMENTO, id: 'c1', agora: AGORA, mensagemId: 'msg-pag',
  })
  const mensagem = depois.conversas[0].mensagens.find((m) => m.id === 'msg-pag')
  assert.ok(mensagem, 'a mensagem de recibo saiu')
  assert.match(mensagem.texto, /ganhou 8 pontos de fidelidade/)
  // RN-40: nenhuma mensagem NOVA além da que já existia (recibo + a de
  // conversão de lead, que não se aplica aqui).
  assert.equal(depois.conversas[0].mensagens.length, 1)
})

confere('reducer: pedido com cupom pago por inteiro NÃO gera "item novo depois do pagamento" fantasma', () => {
  // Achado ao validar na tela (registro 107): sem descontar `valorDesconto`
  // em `dominio/pagamento.js`, o próprio valor do cupom aparecia como
  // "diferença a cobrar" depois do pagamento, oferecendo "Cobrar diferença"
  // de um valor que o cliente já não deve.
  const cupom = novoCupom('cupom-1', { codigo: 'DEZ', tipoDesconto: 'percentual', valor: 10 })
  const comCupom = reducer(estadoComCupom([cupom]), {
    tipo: acao.APLICAR_CUPOM, id: 'c1', codigo: 'DEZ', agora: AGORA,
  })
  const emissao = { identificador: 'cb-teste', copiaECola: 'codigo-teste', link: 'https://pix.teste/1', meio: 'pix' }
  const gerado = reducer(comCupom, {
    tipo: acao.GERAR_PEDIDO, id: 'c1', agora: AGORA, mensagemId: 'm1', cobrancaId: 'm2', emissao,
  })
  const pago = reducer(gerado, {
    tipo: acao.CONFIRMAR_PAGAMENTO, id: 'c1', agora: AGORA, mensagemId: 'm3',
  })
  const pedido = pago.conversas[0].pedido
  assert.equal(pedido.cobranca.valorPago, null, 'baixa à mão sem valor pago explícito')
  assert.equal(diferencaAposPagamento(pedido, cardapio), 0)
})

console.log(`\n${passou} verificações passaram.`)
