/* eslint-disable no-console */
// Prova da #1808 itens 3 e 5: a IA no cardápio.
//   - cada controle da lista de Itens tem uma explicação curta (tooltip);
//   - cada prato com pendência mostra a dica estática; quem edita pede a dica da IA;
//   - o banco de imagens pede pares imagem x prato e só publica depois da confirmação, pelo
//     POST imagens/{id}/publicar que já existe.
//
//   node ferramentas/prova-1808-ia-cardapio.mjs

import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

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
const importar = async (caminho) => { try { return await import(caminho) } catch (erro) { console.log(`(importação de ${caminho}: ${erro.message})`); return {} } }

const dicas = await importar('../src/dominio/dicasDoCardapio.js')
const fonte = (caminho) => readFileSync(new URL(caminho, import.meta.url), 'utf8')

await confere('a dica estática junta o que falta, na ordem do servidor', () => {
  assert.equal(dicas.dicaEstatica(['SemFoto', 'SemCategoria']), 'Adicione uma foto do prato pronto; escolha uma categoria para o cliente achar o prato.')
  assert.equal(dicas.dicaEstatica(['DescricaoCurta']), 'Aumente a descrição para pelo menos 40 caracteres.')
  assert.equal(dicas.dicaEstatica([]), '')
  assert.equal(dicas.dicaEstatica(undefined), '')
  assert.equal(dicas.dicaEstatica(['Desconhecida']), '')
})

await confere('todo controle da lista tem explicação curta', () => {
  for (const chave of ['filtros', 'categoria', 'hoje', 'noSite', 'arquivar', 'validar', 'ordem', 'foto', 'editar', 'maisAcoes']) {
    assert.ok(dicas.EXPLICACAO_DOS_CONTROLES[chave]?.length > 10, `falta a explicação de ${chave}`)
  }
})

await confere('a lista de Itens usa as explicações, a dica estática e a dica da IA', () => {
  const aba = fonte('../src/features/gestao/cardapio/AbaCardapioApi.jsx')
  assert.match(aba, /EXPLICACAO_DOS_CONTROLES/)
  assert.match(aba, /dicaEstatica\(item\.pendencias\)/)
  assert.match(aba, /pedirDica=\{podeEditar \? gestao\.dicaDoPrato : null\}/, 'só quem edita pede a dica da IA')
  const api = fonte('../src/infra/api/cardapioApi.js')
  assert.match(api, /pendencias: i\.pendencias \?\? \[\]/)
})

await confere('as sugestões do acervo pedem confirmação e publicam pelo fluxo existente', () => {
  const sugestoes = fonte('../src/features/gestao/acervo/SugestoesDoAcervo.jsx')
  const api = fonte('../src/infra/api/acervoApi.js')
  assert.match(api, /\/sugestoes`, \{ metodo: 'POST' \}/)
  assert.match(sugestoes, /a\.api\.publicarImagem\(/)
  assert.match(sugestoes, /Confirmar publicação/)
  assert.doesNotMatch(sugestoes, /useEffect\([^)]*sugerirAssociacoes/, 'não pede à IA ao abrir a tela')
  assert.match(fonte('../src/features/gestao/acervo/AbaAcervo.jsx'), /<SugestoesDoAcervo /)
})

console.log(`\n${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
