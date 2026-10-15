/* eslint-disable no-console */
// Prova da issue #1807, item "Ajuda não leva a lugar nenhum útil" (revisão de 14/10/2026). A Ajuda
// do trilho passa a trazer o guia curto de cada módulo do Console: o que ele faz, os primeiros
// passos e as telas, sem perder o diagnóstico. Só regra pura, sem tela.
//
//   A  todo módulo do catálogo tem guia (o que faz e ao menos um primeiro passo);
//   B  o guia segue o perfil: módulo sem acesso ou sem tela no modo atual não aparece;
//   C  o módulo da tela atual vem primeiro e aberto; o resto fechado, na ordem do catálogo;
//   D  as telas do guia são as do menu no modo atual, com o caminho para abrir cada uma;
//   E  o Atendimento ensina a ligar os avisos com o console fechado;
//   F  o diagnóstico continua igual.
//
//   node ferramentas/prova-1807-ajuda.mjs

import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const guia = await import('../src/dominio/guiaDoConsole.js').catch((erro) => ({ erro }))
const { MODULOS, hashDoModulo, modulosDoHall } = await import('../src/dominio/modulos.js')
const { diagnosticoDoConsole } = await import('../src/features/ajuda/diagnosticoDoConsole.js')

let passou = 0
const falhas = []
const confere = async (descricao, fn) => {
  try {
    await fn()
    passou += 1
    console.log('ok    ' + descricao)
  } catch (erro) {
    falhas.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 3).join(' ')}`)
  }
}

const todos = () => true
const rotaBalcao = { tipo: 'principal' }
const rotaModulo = (modulo, tela) => ({ tipo: 'modulo', modulo, tela })

await confere('0. dominio/guiaDoConsole.js existe com guiaDoConsole', () => {
  assert.equal(guia.erro, undefined, `import falhou: ${guia.erro?.message}`)
  assert.equal(typeof guia.guiaDoConsole, 'function')
})

await confere('A. todo módulo do catálogo tem o que faz e primeiros passos', () => {
  for (const modulo of MODULOS) {
    const item = guia.GUIA_POR_MODULO?.[modulo.id]
    assert.ok(item, `sem guia para ${modulo.id}`)
    assert.ok(item.faz?.length > 20, `${modulo.id} sem "o que faz"`)
    assert.ok(item.passos?.length >= 1, `${modulo.id} sem primeiros passos`)
  }
})

await confere('B. perfil sem acesso não vê o módulo; modo demonstração esconde módulo só da API', () => {
  const soAtendimento = guia.guiaDoConsole({ rota: rotaBalcao, fonteApi: true, permite: (id) => id === 'atendimento' })
  assert.deepEqual(soAtendimento.map((m) => m.id), ['atendimento'])
  const demonstracao = guia.guiaDoConsole({ rota: rotaBalcao, fonteApi: false, permite: todos })
  const comTela = modulosDoHall({ fonteApi: false }).filter((m) => m.disponivel).map((m) => m.id)
  assert.deepEqual(demonstracao.map((m) => m.id).sort(), [...comTela].sort(), 'só módulo com tela no modo atual')
})

await confere('C. o módulo da tela atual vem primeiro e aberto', () => {
  const lista = guia.guiaDoConsole({ rota: rotaModulo('financeiro', 'caixa'), fonteApi: true, permite: todos })
  assert.equal(lista[0].id, 'financeiro')
  assert.equal(lista[0].aberto, true)
  assert.ok(lista.slice(1).every((m) => !m.aberto))
  const resto = lista.slice(1).map((m) => m.id)
  assert.deepEqual(resto, MODULOS.map((m) => m.id).filter((id) => resto.includes(id)), 'resto na ordem do catálogo')
  assert.equal(guia.guiaDoConsole({ rota: rotaBalcao, fonteApi: true, permite: todos })[0].id, 'atendimento',
    'o Balcão é do Atendimento')
})

await confere('D. telas do guia são as do menu, com caminho', () => {
  const producao = guia.guiaDoConsole({ rota: rotaBalcao, fonteApi: true, permite: todos }).find((m) => m.id === 'producao')
  assert.ok(producao.telas.length >= 5)
  assert.deepEqual(producao.telas[0], { id: 'estoque', rotulo: 'Estoque do dia', href: hashDoModulo('producao', 'estoque') })
  const demo = guia.guiaDoConsole({ rota: rotaBalcao, fonteApi: false, permite: todos }).find((m) => m.id === 'atendimento')
  assert.ok(!demo.telas.some((t) => t.id === 'horarios'), 'tela só da API some na demonstração')
})

await confere('E. o Atendimento ensina a ligar os avisos com o console fechado', () => {
  const passos = guia.GUIA_POR_MODULO.atendimento.passos.join(' ')
  assert.match(passos, /Ativar avisos neste aparelho/)
})

await confere('F. diagnóstico segue o mesmo', () => {
  const texto = diagnosticoDoConsole({ rota: rotaBalcao, fonteApi: true, versao: 'abc1234', onLine: true })
  assert.match(texto, /^Diagnóstico do Console EasyStok\nTela: Atendimento · Balcão\nVersão: abc1234/)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
process.exit(falhas.length ? 1 : 0)
