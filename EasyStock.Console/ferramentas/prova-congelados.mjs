// Prova do congelamento (#1891, rodada de estabilização #1889): `dominio/congelados.js` é a lista
// única do que está fora da jornada "pedido a dinheiro". A prova confere que cada item da lista
// existe no catálogo (a lista não aponta para o nada) e está de fato fora da tela:
//   (a) módulo congelado some do hall e do menu nos dois modos, e a rota `#/m/<id>` cai no hall;
//   (b) tela congelada some do menu e da busca nos dois modos, e a rota cai em outra tela viva;
//   (c) os pontos avulsos das features passam pelo gate `congelado('<chave>')`: nenhum literal da
//       Lalamove ou do canal E-mail fica fora do bloco guardado.
// Descongelar um item é tirá-lo da lista; esta prova então deixa de exigir a ausência dele.
//
//   node ferramentas/prova-congelados.mjs
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* resolução padrão */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    if (url.endsWith('.json')) return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    return proximo(url, contexto)
  },
})

const { CONGELADOS, moduloCongelado, telaCongelada, congelado } = await import('../src/dominio/congelados.js')
const { MODULOS, moduloPorId, modulosDoHall, telasDoMenu, buscarDestinosDoConsole, filtrarModulos, hashDoModulo } = await import('../src/dominio/modulos.js')
const { ROTA_HALL, ROTA_MODULO, rotaDaHash } = await import('../src/dominio/rota.js')
const { guiaDoConsole } = await import('../src/dominio/guiaDoConsole.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }
const fonte = (caminho) => readFileSync(new URL('../src/' + caminho, import.meta.url), 'utf8')
const MODOS = [{ fonteApi: true }, { fonteApi: false }]
const nomeDoModo = (modo) => (modo.fonteApi ? 'API' : 'demonstração')

// ---------------------------------------------------------------- a lista

confere('a lista é pura (sem import), congelada e com as três partes preenchidas', () => {
  assert.doesNotMatch(fonte('dominio/congelados.js'), /^\s*import\s/m, 'congelados.js não importa nada')
  assert.ok(Object.isFrozen(CONGELADOS) && Object.isFrozen(CONGELADOS.modulos) && Object.isFrozen(CONGELADOS.telas) && Object.isFrozen(CONGELADOS.avulsos))
  for (const parte of ['modulos', 'telas', 'avulsos']) {
    assert.ok(Array.isArray(CONGELADOS[parte]) && CONGELADOS[parte].length > 0, `${parte} tem itens`)
    assert.equal(new Set(CONGELADOS[parte]).size, CONGELADOS[parte].length, `${parte} sem repetição`)
  }
  for (const chave of CONGELADOS.telas) assert.match(chave, /^[a-z-]+\/[a-z-]+$/, `${chave} no formato modulo/tela`)
  assert.equal(congelado('chave-que-nao-existe'), false)
  assert.equal(moduloCongelado('atendimento'), false, 'o Atendimento nunca congela')
  assert.equal(telaCongelada('financeiro', 'caixa'), false, 'o Caixa do dia nunca congela')
})

// ---------------------------------------------------------------- (a) módulos

confere('(a) cada módulo congelado existe no catálogo e some do hall, do menu, da busca e da rota nos dois modos', () => {
  for (const id of CONGELADOS.modulos) {
    const modulo = moduloPorId(id)
    assert.ok(modulo, `${id} existe em MODULOS (a lista não aponta para o nada)`)
    assert.ok(moduloCongelado(id))
    for (const modo of MODOS) {
      const hall = modulosDoHall(modo)
      assert.ok(!hall.some((m) => m.id === id), `${id} fora do hall (${nomeDoModo(modo)})`)
      assert.deepEqual(telasDoMenu(modulo, modo), [], `${id} sem menu, logo fora do trilho e da Gestão (${nomeDoModo(modo)})`)
      assert.equal(rotaDaHash(hashDoModulo(id), modo).tipo, ROTA_HALL, `#/m/${id} cai no hall (${nomeDoModo(modo)})`)
      for (const tela of modulo.telas) {
        assert.equal(rotaDaHash(hashDoModulo(id, tela.id), modo).tipo, ROTA_HALL, `#/m/${id}/${tela.id} cai no hall`)
      }
      for (const termo of [modulo.nome, modulo.resumo, ...modulo.telas.map((t) => t.rotulo)]) {
        assert.ok(!buscarDestinosDoConsole(hall, termo).some((d) => d.modulo.id === id), `buscar "${termo}" não devolve ${id}`)
        assert.ok(!filtrarModulos(hall, termo).some((m) => m.id === id), `filtrar "${termo}" não devolve ${id}`)
      }
    }
  }
  assert.equal(modulosDoHall({ fonteApi: true }).length, MODULOS.length - CONGELADOS.modulos.length, 'o hall perde exatamente os congelados')
  assert.equal(MODULOS.length, 8, 'MODULOS segue inteiro: congelar não apaga o catálogo')
})

// ---------------------------------------------------------------- (b) telas

confere('(b) cada tela congelada existe no catálogo, some do menu e da busca, e a rota cai em outra tela viva', () => {
  for (const chave of CONGELADOS.telas) {
    const [moduloId, telaId] = chave.split('/')
    const modulo = moduloPorId(moduloId)
    assert.ok(modulo, `${chave}: módulo existe`)
    const tela = modulo.telas.find((t) => t.id === telaId)
    assert.ok(tela, `${chave}: tela existe em MODULOS`)
    assert.ok(telaCongelada(moduloId, telaId))
    const href = hashDoModulo(moduloId, telaId)
    for (const modo of MODOS) {
      assert.ok(!telasDoMenu(modulo, modo).some((t) => t.id === telaId), `${chave} fora do menu (${nomeDoModo(modo)})`)
      const hall = modulosDoHall(modo)
      for (const termo of [tela.rotulo, modulo.nome]) {
        assert.ok(!buscarDestinosDoConsole(hall, termo).some((d) => d.href === href), `buscar "${termo}" não devolve ${href}`)
      }
      if (moduloCongelado(moduloId)) continue
      const rota = rotaDaHash(href, modo)
      assert.notEqual(rota.tipo, ROTA_HALL, `${href}: o módulo segue vivo, a rota não cai no hall`)
      assert.notEqual(rota.tela, telaId, `${href} cai em outra tela`)
      if (rota.tipo === ROTA_MODULO) assert.notEqual(rota.aba, tela.aba, `${href} não monta a aba congelada`)
    }
  }
  for (const moduloId of new Set(CONGELADOS.telas.map((c) => c.split('/')[0]))) {
    if (moduloCongelado(moduloId)) continue
    assert.ok(telasDoMenu(moduloPorId(moduloId), { fonteApi: true }).length > 0, `${moduloId} ainda tem tela viva (senão seria módulo congelado)`)
  }
})

confere('Financeiro descongelado: o gerencial volta no modo API e a demonstração fica só com o Caixa', () => {
  assert.deepEqual(telasDoMenu(moduloPorId('financeiro'), { fonteApi: true }).map((t) => t.id), ['caixa', 'visao-geral', 'pagar', 'receber', 'extrato', 'relatorios', 'categorias', 'centros-custo'])
  assert.deepEqual(telasDoMenu(moduloPorId('financeiro'), { fonteApi: false }).map((t) => t.id), ['caixa'])
  assert.equal(rotaDaHash('#/m/financeiro', { fonteApi: true }).aba, 'caixa')
  assert.equal(rotaDaHash('#/m/financeiro/pagar', { fonteApi: true }).aba, 'financeiro-pagar')
})

confere('Financeiro por perfil (#2095, F17): o Operador vê só o Caixa; cada tela pede a ação que a API exige', () => {
  const telas = (acoes) => telasDoMenu(moduloPorId('financeiro'), { fonteApi: true, acoes }).map((t) => t.id)
  const operador = { visualizarContasPagar: false, visualizarContasReceber: false, gerenciarCategoriasFinanceiras: false, gerenciarCentrosCusto: false, gerenciarCaixa: true }
  assert.deepEqual(telas(operador), ['caixa'])
  assert.deepEqual(telas({}), ['caixa'], 'matriz sem as ações do financeiro não abre o gerencial')
  assert.deepEqual(telas({ visualizarContasPagar: true }), ['caixa', 'visao-geral', 'pagar', 'extrato', 'relatorios', 'categorias', 'centros-custo'])
  assert.deepEqual(telas({ visualizarContasReceber: true }), ['caixa', 'visao-geral', 'receber', 'extrato', 'relatorios', 'categorias', 'centros-custo'])
  // O GET de categorias e centros exige visualizar contas: só gerenciar abriria uma tela sem saída.
  assert.deepEqual(telas({ gerenciarCategoriasFinanceiras: true }), ['caixa'])
  assert.deepEqual(telas({ gerenciarCentrosCusto: true }), ['caixa'])
  assert.deepEqual(telas({ visualizarContasPagar: true, visualizarContasReceber: true }), ['caixa', 'visao-geral', 'pagar', 'receber', 'extrato', 'relatorios', 'categorias', 'centros-custo'])
})

// ---------------------------------------------------------------- textos que mentiriam

confere('o resumo do módulo e o guia da Ajuda não prometem o que está congelado', () => {
  if (telaCongelada('financeiro', 'extrato')) {
    assert.doesNotMatch(moduloPorId('financeiro').resumo, /contas|extrato|relat/i, 'resumo do Financeiro sem o gerencial')
  }
  const guia = guiaDoConsole({ rota: {}, fonteApi: true, permite: () => true })
  assert.ok(!guia.some((m) => moduloCongelado(m.id)), 'módulo congelado fora do guia')
  for (const m of guia) for (const t of m.telas) assert.ok(!telaCongelada(m.id, t.id), `${m.id}/${t.id} fora do guia`)
  const financeiro = guia.find((m) => m.id === 'financeiro')
  if (financeiro && telaCongelada('financeiro', 'pagar')) {
    assert.doesNotMatch([financeiro.faz, ...financeiro.passos].join(' '), /contas a pagar|contas a receber|extrato|relat[óo]rios|categorias/i)
  }
  if (congelado('lalamove')) {
    assert.doesNotMatch(semComentarios(fonte('features/gestao/integracoes/AbaIntegracoesApi.jsx')), /lalamove/i, 'Integrações não promete Lalamove no despacho')
  }
})

// ---------------------------------------------------------------- (c) gates nas features

// Comentários não chegam à tela: saem antes da varredura. URL com `://` não é comentário.
function semComentarios(codigo) {
  return codigo
    .replace(/\{\/\*[\s\S]*?\*\/\}/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/.*$/gm, '')
}

// Cada ocorrência de um literal proibido tem de estar depois de `congelado('<chave>')` e no mesmo
// bloco (nenhuma função ou constante de nível superior começa entre o gate e o literal). Vale tanto
// para o gate na mesma linha (`!congelado('x') && <Botao>…`) quanto para o retorno cedo
// (`if (congelado('x')) return null`) no topo do componente.
function exigirGate(arquivo, chave, literais) {
  const gate = `congelado('${chave}')`
  const original = semComentarios(fonte(arquivo))
  assert.match(original, /import \{[^}]*\bcongelado\b[^}]*\} from '(\.\.\/)+dominio\/congelados'/, `${arquivo} importa congelado de dominio/congelados`)
  assert.ok(original.includes(gate), `${arquivo} usa ${gate}`)
  const marca = '\u0000GATE\u0000'
  const codigo = original.replaceAll(gate, marca)
  let ocorrencias = 0
  for (const literal of literais) {
    const re = new RegExp(literal, 'gi')
    for (const m of codigo.matchAll(re)) {
      ocorrencias += 1
      const antes = codigo.slice(0, m.index)
      const ultimoGate = antes.lastIndexOf(marca)
      assert.ok(ultimoGate >= 0, `${arquivo}: "${m[0]}" aparece antes de qualquer ${gate}`)
      const entre = codigo.slice(ultimoGate, m.index)
      assert.ok(!/\r?\n(export )?(function|const|let) \w+/.test(entre), `${arquivo}: "${m[0]}" fora do bloco guardado por ${gate}`)
    }
  }
  return ocorrencias
}

confere('(c) Lalamove nas Entregas, na Ficha e na Comanda só existe atrás de congelado(\'lalamove\')', () => {
  assert.ok(congelado('lalamove'), 'a chave lalamove está na lista')
  assert.ok(exigirGate('features/entregas/EntregasDoDia.jsx', 'lalamove', ['Organizar transportes', 'Cotar esta janela', 'lalamove']) >= 2)
  assert.ok(exigirGate('features/ficha-cliente/BlocoFreteApi.jsx', 'lalamove', ['web\\.lalamove\\.com', 'Consultar Lalamove', 'lalamove']) >= 3)
  assert.ok(exigirGate('features/ficha-cliente/ComandaDoCliente.jsx', 'lalamove', ['lalamove']) >= 1)
})

const { NOMES_DE_CANAL } = await import('../src/infra/catalogo.js')
confere('(c) o chip E-mail do Balcão só existe atrás de congelado(\'canal-email\'); CANAIS continua com o canal', () => {
  assert.ok(congelado('canal-email'), 'a chave canal-email está na lista')
  assert.ok(exigirGate('features/caixa-de-entrada/BarraBalcao.jsx', 'canal-email', ["'E-mail'"]) >= 1)
  assert.ok(NOMES_DE_CANAL.includes('E-mail'), 'o catálogo de canais não perde o E-mail (canalDaConversa e prova-1432 dependem)')
})

confere('os quatro chips de cobrança não entram no congelamento', () => {
  assert.doesNotMatch(fonte('features/ficha-cliente/EscolhaDeMeio.jsx'), /congelad/, 'EscolhaDeMeio sem gate')
})

// ---------------------------------------------------------------- Primeiros passos (#2070)

const { PASSOS, abasDoPasso } = await import('../src/dominio/configuracaoInicial.js')
confere('nenhum passo dos Primeiros passos aponta para tela congelada, e a própria tela não está congelada', () => {
  const lugarDaAba = (aba) => MODULOS.flatMap((m) => m.telas.map((t) => ({ m, t }))).find(({ t }) => t.aba === aba)
  let abas = 0
  for (const passo of PASSOS) {
    for (const aba of abasDoPasso(passo.id)) {
      const lugar = lugarDaAba(aba)
      assert.ok(lugar, `${passo.id}: a aba ${aba} existe no catálogo`)
      assert.ok(!moduloCongelado(lugar.m.id), `${passo.id}: módulo ${lugar.m.id} congelado`)
      assert.ok(!telaCongelada(lugar.m.id, lugar.t.id), `${passo.id}: tela ${lugar.m.id}/${lugar.t.id} congelada`)
      abas += 1
    }
  }
  // Exato: um passo novo que monte uma aba muda este número e passa por esta conferência.
  assert.equal(abas, 18, 'os passos montam 18 abas do catálogo')
  assert.ok(!moduloCongelado('configuracoes') && !telaCongelada('configuracoes', 'primeiros-passos'))
  assert.ok(telasDoMenu(moduloPorId('configuracoes'), { fonteApi: true }).some((t) => t.id === 'primeiros-passos'))
})

console.log(`\n${passou} verificações passaram.`)
