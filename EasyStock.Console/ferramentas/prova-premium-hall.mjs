import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { transformWithOxc } from 'vite'

registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução original */ }
    }
    return proximo(spec, contexto)
  },
})

const { MODULOS, filtrarModulos, modulosDoHall } = await import('../src/dominio/modulos.js')
const {
  alternarFavoritoDoHall, favoritosDoHall, favoritosPrimeiroNoHall, moverModuloDoHall,
  normalizarPreferenciasHall, ordenarModulosDoHall, restaurarOrdemDoHall,
} = await import('../src/dominio/preferenciasHall.js')
const { chaveDasPreferenciasHall, gravarPreferenciasHall, lerPreferenciasHall } = await import('../src/aplicacao/preferenciasHall.js')
const { colisoesDoHall } = await import('../src/features/hall/colisoesDoHall.js')

const resultados = []
function provar(nome, executar) {
  executar()
  resultados.push(nome)
  console.log('ok ' + nome)
}
const catalogo = MODULOS.map((modulo) => modulo.id)
const inicial = normalizarPreferenciasHall(null)
const api = modulosDoHall({ fonteApi: true })
const ids = (modulos) => modulos.map((modulo) => modulo.id)
const sessao = (empresa, usuario) => ({ empresa: { id: empresa, nome: 'Nome privado da empresa' }, usuario: { id: usuario, nome: 'Nome privado' }, token: 'token-privado' })
const storage = () => {
  const dados = new Map()
  return { dados, getItem: (chave) => dados.get(chave) ?? null, setItem: (chave, valor) => dados.set(chave, valor) }
}

provar('entrada padrão conserva catálogo e não cria favoritos', () => {
  assert.deepEqual(inicial.ordem, catalogo)
  assert.deepEqual(inicial.favoritos, [])
})
provar('preferência inválida, vazia ou antiga não derruba a navegação', () => {
  for (const valor of [null, false, 4, 'texto', [], { ordem: {}, favoritos: 'atendimento' }]) {
    assert.deepEqual(normalizarPreferenciasHall(valor), inicial)
  }
})
provar('IDs estranhos e repetidos são removidos, catálogo novo continua acessível', () => {
  const valor = normalizarPreferenciasHall({ ordem: ['atendimento', 'inexistente', 'atendimento'], favoritos: ['cozinha', 'cozinha', { id: 'entregas' }, 'inexistente'] })
  assert.equal(valor.ordem[0], 'atendimento')
  assert.deepEqual(new Set(valor.ordem), new Set(catalogo))
  assert.deepEqual(valor.favoritos, ['cozinha'])
})
provar('normalização não altera o objeto guardado para Desfazer', () => {
  const valor = { ordem: ['entregas', 'atendimento'], favoritos: ['atendimento'] }
  const antes = structuredClone(valor)
  normalizarPreferenciasHall(valor)
  assert.deepEqual(valor, antes)
})
provar('favoritar não reorganiza os módulos silenciosamente', () => {
  const favorito = alternarFavoritoDoHall(inicial, 'atendimento')
  assert.deepEqual(favorito.ordem, inicial.ordem)
  assert.deepEqual(favorito.favoritos, ['atendimento'])
  assert.deepEqual(alternarFavoritoDoHall(favorito, 'atendimento'), inicial)
})
provar('favorito inexistente não entra na preferência', () => {
  assert.deepEqual(alternarFavoritoDoHall(inicial, 'modulo-privado'), inicial)
})
provar('favorito salvo nunca devolve módulo fora da permissão', () => {
  const preferencia = normalizarPreferenciasHall({ favoritos: catalogo })
  const permitidos = api.filter((modulo) => ['atendimento', 'entregas'].includes(modulo.id))
  assert.deepEqual(ids(favoritosDoHall(permitidos, preferencia)), ['atendimento', 'entregas'])
  assert.deepEqual(favoritosDoHall([], preferencia), [])
})
provar('área indisponível não cria atalho que falha ao abrir', () => {
  const preferencia = normalizarPreferenciasHall({ favoritos: [api[0].id] })
  assert.deepEqual(favoritosDoHall([{ ...api[0], disponivel: false, href: null }], preferencia), [])
})
provar('ordenar não reintroduz módulos proibidos em nenhuma combinação de acesso', () => {
  const preferencia = normalizarPreferenciasHall({ ordem: [...catalogo].reverse(), favoritos: catalogo })
  for (let mascara = 0; mascara < 2 ** api.length; mascara++) {
    const permitidos = api.filter((_, indice) => mascara & (1 << indice))
    assert.deepEqual(new Set(ids(ordenarModulosDoHall(permitidos, preferencia))), new Set(ids(permitidos)))
    assert.deepEqual(new Set(ids(favoritosDoHall(permitidos, preferencia))), new Set(ids(permitidos.filter((modulo) => modulo.disponivel))))
  }
})
provar('mover para frente conserva todos os módulos, favoritos e snapshot anterior', () => {
  const atual = alternarFavoritoDoHall(inicial, 'entregas')
  const antes = structuredClone(atual)
  const movida = moverModuloDoHall(atual, 'entregas', 'cardapio')
  assert.equal(movida.ordem[0], 'entregas')
  assert.deepEqual(movida.favoritos, ['entregas'])
  assert.deepEqual(new Set(movida.ordem), new Set(catalogo))
  assert.deepEqual(atual, antes)
})
provar('mover primeiro para o fim e retornar recupera a ordem original', () => {
  const movida = moverModuloDoHall(inicial, catalogo[0], catalogo.at(-1))
  assert.equal(movida.ordem.at(-1), catalogo[0])
  assert.deepEqual(moverModuloDoHall(movida, catalogo[0], catalogo[1]), inicial)
})
provar('drop cancelado, próprio módulo ou ID inválido conserva a ordem', () => {
  for (const [origem, destino] of [['atendimento', null], ['inexistente', 'cozinha'], ['cozinha', 'inexistente'], ['cozinha', 'cozinha']]) {
    assert.deepEqual(moverModuloDoHall(inicial, origem, destino), inicial)
  }
})
provar('subir entre módulos permitidos não exige conhecer módulos ocultos', () => {
  const permitidos = api.filter((modulo) => ['atendimento', 'entregas'].includes(modulo.id))
  const movida = moverModuloDoHall(inicial, 'entregas', 'atendimento')
  assert.deepEqual(ids(ordenarModulosDoHall(permitidos, movida)), ['entregas', 'atendimento'])
})
provar('Favoritos primeiro é explícito e segue catálogo dentro de cada grupo', () => {
  const atual = normalizarPreferenciasHall({ ordem: [...catalogo].reverse(), favoritos: ['entregas', 'atendimento'] })
  const proxima = favoritosPrimeiroNoHall(atual)
  assert.deepEqual(proxima.ordem.slice(0, 2), ['atendimento', 'entregas'])
  assert.deepEqual(proxima.ordem.slice(2), catalogo.filter((id) => !atual.favoritos.includes(id)))
  assert.deepEqual(atual.ordem, [...catalogo].reverse())
})
provar('restaurar ordem original mantém os favoritos escolhidos', () => {
  const atual = normalizarPreferenciasHall({ ordem: [...catalogo].reverse(), favoritos: ['cozinha'] })
  const restaurada = restaurarOrdemDoHall(atual)
  assert.deepEqual(restaurada.ordem, catalogo)
  assert.deepEqual(restaurada.favoritos, ['cozinha'])
})
provar('mudança de token ou nome não mistura nem perde preferências da mesma conta', () => {
  const atual = sessao('empresa-a', 'usuario-a')
  assert.equal(chaveDasPreferenciasHall(atual), chaveDasPreferenciasHall({ ...atual, token: 'outro-token', usuario: { ...atual.usuario, nome: 'Outro nome' } }))
})
provar('outra conta ou empresa usa chave diferente', () => {
  const chave = chaveDasPreferenciasHall(sessao('empresa-a', 'usuario-a'))
  assert.notEqual(chave, chaveDasPreferenciasHall(sessao('empresa-a', 'usuario-b')))
  assert.notEqual(chave, chaveDasPreferenciasHall(sessao('empresa-b', 'usuario-a')))
})
provar('separador dentro do ID não produz colisão entre empresas e contas', () => {
  assert.notEqual(chaveDasPreferenciasHall(sessao('a:b', 'c')), chaveDasPreferenciasHall(sessao('a', 'b:c')))
})
provar('sem sessão identificada, personalização não é gravada em chave comum', () => {
  const disco = storage()
  for (const atual of [null, {}, { usuario: { id: 'conta' } }, { empresa: { id: 'empresa' } }, sessao('', 'conta')]) {
    const chave = chaveDasPreferenciasHall(atual)
    assert.equal(chave, null)
    assert.equal(gravarPreferenciasHall(chave, inicial, disco), false)
  }
  assert.equal(disco.dados.size, 0)
})
provar('preferência persiste por conta e não atravessa troca de empresa', () => {
  const disco = storage()
  const primeira = chaveDasPreferenciasHall(sessao('empresa-a', 'usuario-a'))
  const segunda = chaveDasPreferenciasHall(sessao('empresa-b', 'usuario-a'))
  const preferencia = alternarFavoritoDoHall(moverModuloDoHall(inicial, 'entregas', 'cardapio'), 'entregas')
  assert.equal(gravarPreferenciasHall(primeira, preferencia, disco), true)
  assert.deepEqual(lerPreferenciasHall(primeira, disco), preferencia)
  assert.deepEqual(lerPreferenciasHall(segunda, disco), inicial)
})
provar('Desfazer persiste snapshot anterior sem perder os atalhos', () => {
  const disco = storage()
  const chave = chaveDasPreferenciasHall(sessao('empresa-a', 'usuario-a'))
  const antes = alternarFavoritoDoHall(inicial, 'atendimento')
  gravarPreferenciasHall(chave, favoritosPrimeiroNoHall(antes), disco)
  gravarPreferenciasHall(chave, antes, disco)
  assert.deepEqual(lerPreferenciasHall(chave, disco), antes)
})
provar('somente preferências de apresentação são gravadas, sem nomes, token ou clientes', () => {
  const disco = storage()
  const chave = chaveDasPreferenciasHall(sessao('empresa-a', 'usuario-a'))
  gravarPreferenciasHall(chave, { ...inicial, favoritos: ['atendimento'], nome: 'Nome privado', token: 'token-privado', cliente: { documento: 'documento-privado' } }, disco)
  const gravado = disco.dados.get(chave)
  assert.deepEqual(Object.keys(JSON.parse(gravado)).sort(), ['compacto', 'favoritos', 'foco', 'movimentos', 'ordem', 'paineis', 'versao'])
  assert.doesNotMatch(chave + gravado, /Nome privado|token-privado|documento-privado|cliente/)
})
provar('JSON corrompido volta ao catálogo sem quebrar a tela', () => {
  const disco = storage()
  disco.setItem('chave', '{ quebrado')
  assert.deepEqual(lerPreferenciasHall('chave', disco), inicial)
})
provar('storage bloqueado preserva fallback e retorna falha de persistência', () => {
  const bloqueado = { getItem() { throw Error('bloqueado') }, setItem() { throw Error('bloqueado') } }
  assert.deepEqual(lerPreferenciasHall('chave', bloqueado), inicial)
  assert.equal(gravarPreferenciasHall('chave', inicial, bloqueado), false)
})
provar('getter localStorage bloqueado também é capturado', () => {
  const anterior = Object.getOwnPropertyDescriptor(globalThis, 'localStorage')
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, get() { throw Error('bloqueado') } })
  try {
    assert.deepEqual(lerPreferenciasHall('chave'), inicial)
    assert.equal(gravarPreferenciasHall('chave', inicial), false)
  } finally {
    if (anterior) Object.defineProperty(globalThis, 'localStorage', anterior)
    else delete globalThis.localStorage
  }
})
provar('sem disco, voltar de um módulo conserva a rotina em memória só da conta atual', () => {
  const bloqueado = { getItem() { throw Error('bloqueado') }, setItem() { throw Error('bloqueado') } }
  const primeira = chaveDasPreferenciasHall(sessao('empresa-temporaria-a', 'conta-temporaria'))
  const segunda = chaveDasPreferenciasHall(sessao('empresa-temporaria-b', 'conta-temporaria'))
  const preferencia = alternarFavoritoDoHall(moverModuloDoHall(inicial, 'entregas', 'cardapio'), 'entregas')
  assert.equal(gravarPreferenciasHall(primeira, preferencia, bloqueado), false)
  assert.deepEqual(lerPreferenciasHall(primeira, bloqueado), preferencia)
  assert.deepEqual(lerPreferenciasHall(segunda, bloqueado), inicial)
})
provar('busca conserva a ordem personalizada e a permissão vigente', () => {
  const preferencia = normalizarPreferenciasHall({ ordem: ['entregas', 'atendimento'] })
  const permitidos = api.filter((modulo) => modulo.id === 'atendimento')
  assert.deepEqual(ids(filtrarModulos(ordenarModulosDoHall(permitidos, preferencia), 'balcao')), ['atendimento'])
  assert.deepEqual(filtrarModulos(ordenarModulosDoHall(permitidos, preferencia), 'entregas'), [])
})

const retangulo = (left, top, width, height) => ({ left, top, right: left + width, bottom: top + height, width, height })
const colisao = (pointerCoordinates, collisionRect = retangulo(100, 0, 80, 80)) => ({
  active: { id: 'cardapio' }, pointerCoordinates, collisionRect,
  droppableRects: new Map([['cardapio', retangulo(0, 0, 80, 80)], ['atendimento', retangulo(100, 0, 80, 80)]]),
  droppableContainers: [{ id: 'cardapio' }, { id: 'atendimento' }],
})
provar('soltar fora da grade cancela em vez de puxar cartão para a borda', () => {
  for (const ponto of [{ x: -10, y: 40 }, { x: 200, y: 40 }, { x: 120, y: -10 }, { x: 120, y: 100 }]) {
    assert.deepEqual(colisoesDoHall(colisao(ponto)), [])
  }
})
provar('arrastar sobre cartão ou no intervalo interno mantém alvo útil', () => {
  assert.equal(colisoesDoHall(colisao({ x: 140, y: 40 }))[0].id, 'atendimento')
  assert.ok(colisoesDoHall(colisao({ x: 90, y: 40 })).length > 0)
})
provar('teclado usa coordenadas do sortable sem depender de ponteiro', () => {
  assert.equal(colisoesDoHall(colisao(null))[0].id, 'atendimento')
})

const hallUrl = new URL('../src/features/hall/HallDeModulos.jsx', import.meta.url)
const compilado = await transformWithOxc(readFileSync(hallUrl, 'utf8'), hallUrl.pathname, { jsx: { runtime: 'automatic' } })
provar('JSX final do Hall compila com o mesmo transformador do Vite', () => assert.ok(compilado.code))

// #1810: o Hall renderizado no modo API mostra só saudação, busca, números e áreas. Telas de cada
// área, favoritar, organizar e personalizar saem da primeira vista (ficam em "Ajustar tela inicial").
{
  const { readdirSync } = await import('node:fs')
  const { fileURLToPath, pathToFileURL } = await import('node:url')
  const { join } = await import('node:path')
  const raiz = fileURLToPath(new URL('../src/', import.meta.url))
  const jsx = new Map(await Promise.all(readdirSync(raiz, { recursive: true }).filter((n) => n.endsWith('.jsx')).map(async (nome) => {
    const arquivo = join(raiz, nome)
    const codigo = readFileSync(arquivo, 'utf8').replaceAll('import.meta.env', '({ DEV: false })')
    return [pathToFileURL(arquivo).href, (await transformWithOxc(codigo, arquivo, { jsx: { runtime: 'automatic' } })).code]
  })))
  registerHooks({
    resolve(spec, contexto, proximo) {
      if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
        for (const ext of ['.js', '.jsx']) { try { return proximo(spec + ext, contexto) } catch { /* próximo formato */ } }
      }
      return proximo(spec, contexto)
    },
    load(url, contexto, proximo) {
      if (jsx.has(url)) return { format: 'module', source: jsx.get(url), shortCircuit: true }
      if (url.endsWith('.css')) return { format: 'module', source: 'export default new Proxy({}, { get: (_, p) => String(p) })', shortCircuit: true }
      if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', source: 'export const FONTE_API=true;export const API_BASE="";', shortCircuit: true }
      if (url.endsWith('.json')) return { format: 'module', source: 'export default ' + readFileSync(new URL(url), 'utf8'), shortCircuit: true }
      return proximo(url, contexto)
    },
  })
  const { createElement: h } = await import('react')
  const { renderToStaticMarkup } = await import('react-dom/server')
  const { HallDeModulos } = await import('../src/features/hall/HallDeModulos.jsx')
  const { ContextoAcessoModulos } = await import('../src/aplicacao/acessoModulos.jsx')
  // O interruptor de tema lê o tema do documento no render; no Node, o claro.
  globalThis.document ??= { documentElement: { dataset: { theme: 'light' } } }
  const html = renderToStaticMarkup(h(ContextoAcessoModulos.Provider, { value: { permite: () => true, acoes: {}, aoSair: () => {} } },
    h(HallDeModulos, { fonteApi: true, sessao: null, agora: Date.parse('2026-10-14T18:00:00Z') })))
  provar('#1810: hall mínimo sem telas, favoritar, organizar nem personalizar na primeira vista', () => {
    assert.doesNotMatch(html, /Personalizar painel|Organizar módulos|Favoritar|Telas de |Em preparação|Atualizado às|Agora na casa ·.*Atualizar/)
    assert.equal(html.match(/Ajustar tela inicial/g)?.length, 1)
    for (const modulo of api) assert.match(html, new RegExp(`>${modulo.nome}<`))
  })
  provar('#1810: hall mínimo cabe em até 20 controles (eram 54 em 1280x720)', () => {
    const controles = (html.match(/<(a|button|input|summary|select)[\s>]/g) ?? []).length
    assert.ok(controles <= 20, `${controles} controles`)
  })
}

console.log(JSON.stringify({ prova: 'premium-hall', aprovados: resultados.length, falhas: 0, combinacoesDeAcesso: 2 ** api.length }))
