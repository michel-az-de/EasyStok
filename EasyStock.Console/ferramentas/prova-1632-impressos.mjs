// Prova da #1632 (F2): impressos do pedido em HTML servidos pela API. Perfis por papel e geometria da
// bobina entram com a #1756 (seções D e E).
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
  DOCUMENTOS, PAPEIS, PAPEL_PADRAO, PERFIS_IMPRESSAO, caminhoDoImpresso, normalizarPreferenciasImpressao,
  orientacaoDoPapel, perfilDeImpressao, trocarLogoNaTermica, trocarPapelPadrao, trocarPerfilPadrao,
} = await import('../src/dominio/impressos.js')
const { paginaDoImpresso } = await import('../src/componentes/geometriaImpresso.js')
const { CHAVE_PREFERENCIAS_IMPRESSAO, gravarPreferenciasImpressao, lerPreferenciasImpressao } =
  await import('../src/infra/preferenciasImpressao.js')
const { moduloPorId, telasDoMenu } = await import('../src/dominio/modulos.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const PADRAO = {
  versao: 2,
  papeis: { pedido: 'cupom-80', agendamento: 'a5', entrega: 'etiqueta-10x15', cozinha: 'cupom-80' },
  perfis: { pedido: 'termica-80', agendamento: 'hp-a5', entrega: 'etiqueta-10x15', cozinha: 'termica-80' },
  logoTermica: false,
}

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
  // Preferência da versão 1 (só papel) ganha o perfil do papel guardado, sem perder a escolha.
  const v1 = armazenamentoFalso({ [CHAVE_PREFERENCIAS_IMPRESSAO]: JSON.stringify({ versao: 1, papeis: { pedido: 'a4' } }) })
  assert.deepEqual(lerPreferenciasImpressao(v1).perfis, { ...PADRAO.perfis, pedido: 'hp-a4' })
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

// --- D. Perfis por papel (#1756) ---------------------------------------------------------------
confere('perfis: sistema aceita todo papel; cada impressora só o papel que ela tem', () => {
  assert.deepEqual(PERFIS_IMPRESSAO.map((p) => p.valor), ['sistema', 'hp-a4', 'hp-a5', 'termica-80', 'termica-58', 'etiqueta-10x15'])
  assert.deepEqual(perfilDeImpressao('sistema').papeis, PAPEIS.map((p) => p.valor))
  for (const perfil of PERFIS_IMPRESSAO.slice(1)) assert.equal(perfil.papeis.length, 1, perfil.valor)
  // Todo papel do catálogo tem uma impressora dedicada.
  for (const { valor } of PAPEIS) assert.ok(PERFIS_IMPRESSAO.slice(1).some((p) => p.papeis.includes(valor)), valor)
  assert.equal(perfilDeImpressao('fax').valor, 'sistema')
})

confere('perfil gravado incompatível com o papel volta ao perfil do papel; sistema é sempre aceito', () => {
  const lido = normalizarPreferenciasImpressao({ papeis: { pedido: 'a4', agendamento: 'a5' }, perfis: { pedido: 'termica-80', agendamento: 'sistema', entrega: 'impressora-x' } })
  assert.equal(lido.perfis.pedido, 'hp-a4')
  assert.equal(lido.perfis.agendamento, 'sistema')
  assert.equal(lido.perfis.entrega, 'etiqueta-10x15')
})

confere('trocar o perfil leva o papel junto; trocar o papel leva o perfil junto', () => {
  const termica = trocarPerfilPadrao(PADRAO, 'agendamento', 'termica-58')
  assert.deepEqual([termica.perfis.agendamento, termica.papeis.agendamento], ['termica-58', 'cupom-58'])
  const sistema = trocarPerfilPadrao(PADRAO, 'pedido', 'sistema')
  assert.deepEqual([sistema.perfis.pedido, sistema.papeis.pedido], ['sistema', 'cupom-80'], 'sistema mantém o papel')
  const a4 = trocarPapelPadrao(PADRAO, 'pedido', 'a4')
  assert.deepEqual([a4.perfis.pedido, a4.papeis.pedido], ['hp-a4', 'a4'])
  const noSistema = trocarPapelPadrao(sistema, 'pedido', 'a5')
  assert.equal(noSistema.perfis.pedido, 'sistema', 'no diálogo do sistema qualquer papel serve')
  assert.deepEqual(trocarPerfilPadrao(PADRAO, 'pedido', 'fax'), PADRAO)
  assert.deepEqual(trocarPerfilPadrao(PADRAO, 'recibo', 'hp-a4'), PADRAO)
})

confere('orientação por formato: medida, escala 100% e sem margens nem cabeçalhos', () => {
  for (const { valor } of PAPEIS) assert.match(orientacaoDoPapel(valor), /escala 100%/, valor)
  assert.match(orientacaoDoPapel('cupom-58'), /58 mm/)
  assert.match(orientacaoDoPapel('cupom-80'), /80 mm/)
  assert.match(orientacaoDoPapel('cupom-80'), /comprimento acompanha o conteúdo/)
  assert.match(orientacaoDoPapel('etiqueta-10x15'), /100 × 150 mm/)
  assert.match(orientacaoDoPapel('a5'), /148 × 210 mm/)
  assert.match(orientacaoDoPapel('a4'), /210 × 297 mm/)
})

// --- E. Geometria medida antes do diálogo (#1756) ----------------------------------------------
const MM = 96 / 25.4
const folha = (classe, larguraMm, alturaMm, transborda = false) => ({ classes: ['im', classe], largura: larguraMm * MM, altura: alturaMm * MM, transborda })

confere('bobina: @page com a largura do rolo e o comprimento medido da via mais longa', () => {
  assert.equal(paginaDoImpresso([folha('s-80', 80, 212.3)]), '@page{size:80mm 214mm;margin:0}')
  assert.equal(paginaDoImpresso([folha('s-58', 58, 120)]), '@page{size:58mm 121mm;margin:0}')
  assert.equal(paginaDoImpresso([folha('s-80', 80, 90), folha('s-80', 80, 140)]), '@page{size:80mm 141mm;margin:0}')
})

confere('folha e etiqueta de tamanho fixo não ganham @page novo', () => {
  assert.equal(paginaDoImpresso([folha('s-a4', 210, 297)]), null)
  assert.equal(paginaDoImpresso([folha('s-a5', 148, 210)]), null)
  assert.equal(paginaDoImpresso([folha('s-1015', 100, 150), folha('s-1015', 100, 120)]), null)
})

confere('conteúdo mais largo que o papel ou etiqueta que não cabe barram a impressão com o motivo', () => {
  assert.throws(() => paginaDoImpresso([folha('s-58', 58, 100, true)]), /ultrapassa a largura do papel/)
  assert.throws(() => paginaDoImpresso([folha('s-80', 84, 100)]), /ultrapassa a largura do papel/)
  assert.throws(() => paginaDoImpresso([folha('s-1015', 100, 151)]), /não cabe em uma etiqueta de 10×15 cm/)
  assert.throws(() => paginaDoImpresso([folha('s-carta', 216, 279)]), /formato de papel conhecido/)
})

confere('página sem folha de impresso (HTML antigo ou vazio) segue como veio', () => {
  assert.equal(paginaDoImpresso([]), null)
})

console.log(`\n${passou} verificações passaram.`)
