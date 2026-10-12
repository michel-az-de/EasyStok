// Prova da #1632 (F2): impressos do pedido em HTML servidos pela API.
//
// Cobre as funções puras e o armazenamento do aparelho:
//   A. preferências de impressão: padrões, leitura e gravação, valor avariado e storage
//      indisponível (a escolha vale para a visita);
//   B. montagem do endereço do impresso e do canhoto de cozinha;
//   C. a aba Impressão mora em Configurações e só existe no modo API.
// Diálogo de impressão, iframe e envio do logo exigem navegador e API.
//
// Roda com: node ferramentas/prova-1632-impressos.mjs

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
  DOCUMENTOS, PAPEIS, PAPEL_PADRAO, caminhoDoImpresso, normalizarPreferenciasImpressao,
  trocarLogoNaTermica, trocarPapelPadrao,
} = await import('../src/dominio/impressos.js')
const { CHAVE_PREFERENCIAS_IMPRESSAO, gravarPreferenciasImpressao, lerPreferenciasImpressao } =
  await import('../src/infra/preferenciasImpressao.js')
const { moduloPorId, telasDoMenu } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const PADRAO = { versao: 1, papeis: { pedido: 'cupom-80', agendamento: 'a5', entrega: 'etiqueta-10x15', cozinha: 'cupom-80' }, logoTermica: false }

function armazenamentoFalso(inicial = {}) {
  const dados = new Map(Object.entries(inicial))
  return {
    dados,
    getItem: (chave) => (dados.has(chave) ? dados.get(chave) : null),
    setItem: (chave, valor) => { dados.set(chave, String(valor)) },
  }
}
const armazenamentoNegado = {
  getItem() { throw new Error('SecurityError') },
  setItem() { throw new Error('QuotaExceededError') },
}

// --- A. Preferências --------------------------------------------------------------------------
confere('catálogo: quatro documentos e cinco papéis, com os rótulos da tela', () => {
  assert.deepEqual(DOCUMENTOS.map((d) => d.valor), ['pedido', 'agendamento', 'entrega', 'cozinha'])
  assert.deepEqual(DOCUMENTOS.map((d) => d.rotulo), ['Pedido', 'Agendamento', 'Etiqueta de entrega', 'Canhoto de cozinha'])
  assert.deepEqual(PAPEIS.map((p) => p.valor), ['a4', 'a5', 'etiqueta-10x15', 'cupom-80', 'cupom-58'])
  assert.deepEqual(PAPEIS.map((p) => p.rotulo), ['A4', 'A5', 'Etiqueta 10×15', 'Bobina 80 mm', 'Bobina 58 mm'])
})

confere('padrões: pedido e cozinha na bobina de 80, agendamento em A5, entrega na etiqueta, sem logo na térmica', () => {
  assert.deepEqual(PAPEL_PADRAO, PADRAO.papeis)
  assert.deepEqual(normalizarPreferenciasImpressao(null), PADRAO)
  assert.deepEqual(normalizarPreferenciasImpressao(undefined), PADRAO)
})

confere('valor avariado volta ao padrão do documento; logo só liga com true de verdade', () => {
  const lido = normalizarPreferenciasImpressao({ papeis: { pedido: 'a4', agendamento: 'carta', entrega: 7, extra: 'a4' }, logoTermica: 'sim' })
  assert.deepEqual(lido.papeis, { ...PADRAO.papeis, pedido: 'a4' })
  assert.equal(lido.logoTermica, false)
  assert.equal(normalizarPreferenciasImpressao({ logoTermica: true }).logoTermica, true)
  assert.deepEqual(normalizarPreferenciasImpressao('lixo'), PADRAO)
})

confere('trocar papel e logo devolve preferências novas, sem mexer nas antigas', () => {
  const inicial = normalizarPreferenciasImpressao(null)
  const trocada = trocarPapelPadrao(inicial, 'entrega', 'a4')
  assert.equal(trocada.papeis.entrega, 'a4')
  assert.equal(inicial.papeis.entrega, 'etiqueta-10x15')
  assert.deepEqual(trocarPapelPadrao(inicial, 'entrega', 'carta'), inicial)
  assert.deepEqual(trocarPapelPadrao(inicial, 'recibo', 'a4'), inicial)
  assert.equal(trocarLogoNaTermica(inicial, true).logoTermica, true)
  assert.equal(trocarLogoNaTermica(trocarLogoNaTermica(inicial, true), false).logoTermica, false)
})

confere('aparelho sem nada gravado lê os padrões', () => {
  assert.deepEqual(lerPreferenciasImpressao(armazenamentoFalso()), PADRAO)
})

confere('storage indisponível antes de qualquer escolha: lê os padrões e não quebra', () => {
  assert.deepEqual(lerPreferenciasImpressao(armazenamentoNegado), PADRAO)
})

confere('gravar e ler de volta no aparelho', () => {
  const aparelho = armazenamentoFalso()
  const escolha = trocarLogoNaTermica(trocarPapelPadrao(PADRAO, 'pedido', 'cupom-58'), true)
  assert.equal(gravarPreferenciasImpressao(escolha, aparelho), true)
  assert.deepEqual(JSON.parse(aparelho.dados.get(CHAVE_PREFERENCIAS_IMPRESSAO)), escolha)
  assert.deepEqual(lerPreferenciasImpressao(aparelho), escolha)
})

confere('o que está gravado é normalizado na leitura (JSON quebrado ou papel que não existe mais)', () => {
  assert.deepEqual(lerPreferenciasImpressao(armazenamentoFalso({ [CHAVE_PREFERENCIAS_IMPRESSAO]: '{quebrado' })).papeis.agendamento, 'a5')
  const velho = armazenamentoFalso({ [CHAVE_PREFERENCIAS_IMPRESSAO]: JSON.stringify({ papeis: { cozinha: 'cupom-76' }, logoTermica: true }) })
  assert.deepEqual(lerPreferenciasImpressao(velho), { ...PADRAO, logoTermica: true })
})

confere('storage indisponível: gravar avisa que não guardou, mas a escolha vale para a visita', () => {
  const escolha = trocarPapelPadrao(PADRAO, 'cozinha', 'etiqueta-10x15')
  assert.equal(gravarPreferenciasImpressao(escolha, armazenamentoNegado), false)
  assert.deepEqual(lerPreferenciasImpressao(armazenamentoNegado), escolha)
  // Aparelho que perdeu a chave (dados limpos) também segue com a escolha da visita.
  assert.deepEqual(lerPreferenciasImpressao(armazenamentoFalso()), escolha)
})

confere('sem armazenamento informado usa o do ambiente; sem ele (Node) não quebra', () => {
  assert.doesNotThrow(() => gravarPreferenciasImpressao(PADRAO))
  assert.deepEqual(lerPreferenciasImpressao(), PADRAO)
})

// --- B. Endereço do impresso ------------------------------------------------------------------
const ID = '3f2b8c1e-6d4a-4b7f-9a10-5c2e8d7f1a22'

confere('pedido, agendamento e entrega saem de /impresso com documento, modelo e logoTermica', () => {
  assert.equal(
    caminhoDoImpresso(ID, { documento: 'pedido', modelo: 'cupom-80', logoTermica: false }),
    `/api/pedidos/${ID}/impresso?documento=pedido&modelo=cupom-80&logoTermica=false`,
  )
  assert.equal(
    caminhoDoImpresso(ID, { documento: 'agendamento', modelo: 'a5', logoTermica: true }),
    `/api/pedidos/${ID}/impresso?documento=agendamento&modelo=a5&logoTermica=true`,
  )
  assert.equal(
    caminhoDoImpresso(ID, { documento: 'entrega', modelo: 'etiqueta-10x15' }),
    `/api/pedidos/${ID}/impresso?documento=entrega&modelo=etiqueta-10x15&logoTermica=false`,
  )
})

confere('canhoto de cozinha sai de /comanda, sem o parâmetro documento', () => {
  assert.equal(
    caminhoDoImpresso(ID, { documento: 'cozinha', modelo: 'cupom-58', logoTermica: true }),
    `/api/pedidos/${ID}/comanda?modelo=cupom-58&logoTermica=true`,
  )
})

confere('todo documento com todo papel monta um endereço do contrato', () => {
  for (const { valor: documento } of DOCUMENTOS) {
    for (const { valor: modelo } of PAPEIS) {
      const url = new URL(caminhoDoImpresso(ID, { documento, modelo, logoTermica: true }), 'http://api')
      assert.equal(url.pathname, `/api/pedidos/${ID}/${documento === 'cozinha' ? 'comanda' : 'impresso'}`)
      assert.equal(url.searchParams.get('modelo'), modelo)
      assert.equal(url.searchParams.get('logoTermica'), 'true')
      assert.equal(url.searchParams.get('documento'), documento === 'cozinha' ? null : documento)
    }
  }
})

confere('papel ausente ou desconhecido cai no padrão do documento; logoTermica só vira true com true', () => {
  assert.match(caminhoDoImpresso(ID, { documento: 'agendamento' }), /modelo=a5&logoTermica=false$/)
  assert.match(caminhoDoImpresso(ID, { documento: 'entrega', modelo: 'carta', logoTermica: 'true' }), /modelo=etiqueta-10x15&logoTermica=false$/)
})

confere('sem id de pedido ou com documento fora do catálogo não há endereço', () => {
  assert.throws(() => caminhoDoImpresso(null, { documento: 'pedido' }), /Pedido sem identificação/)
  assert.throws(() => caminhoDoImpresso('', { documento: 'pedido' }), /Pedido sem identificação/)
  assert.throws(() => caminhoDoImpresso(ID, { documento: 'recibo' }), /Documento de impressão desconhecido/)
  assert.throws(() => caminhoDoImpresso(ID), /Documento de impressão desconhecido/)
})

// --- C. Aba Impressão -------------------------------------------------------------------------
confere('Impressão é tela de Configurações, só no modo API, e a rota abre a aba', () => {
  const configuracoes = moduloPorId('configuracoes')
  assert.ok(telasDoMenu(configuracoes, { fonteApi: true }).some((t) => t.id === 'impressao' && t.rotulo === 'Impressão'))
  assert.ok(!telasDoMenu(configuracoes, { fonteApi: false }).some((t) => t.id === 'impressao'))
  assert.equal(rotaDaHash('#/m/configuracoes/impressao', { fonteApi: true }).aba, 'impressao')
})

console.log(`\n${passou} verificações passaram.`)
