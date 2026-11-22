// #3149: regras do dicionário da loja no domínio puro (a API é a autoridade; a tela antecipa). Caracteres especiais só com escapes.
import assert from 'node:assert/strict'
import {
  DICIONARIO_SIGNIFICADO_MAXIMO, DICIONARIO_TERMO_MAXIMO, DICIONARIO_TERMOS_MAXIMO,
  dicionarioParaSalvar, erroDoDicionario, mesmoDicionario,
} from '../src/dominio/dicionarioDaLoja.js'

const um = (termo, significado = 'quer dizer algo') => [{ termo, significado }]
assert.deepEqual([DICIONARIO_TERMOS_MAXIMO, DICIONARIO_TERMO_MAXIMO, DICIONARIO_SIGNIFICADO_MAXIMO], [60, 40, 120])

// Aceitos
assert.equal(erroDoDicionario([]), null, 'lista vazia apaga e é válida')
assert.equal(erroDoDicionario(um('PF', 'prato feito do dia')), null)
assert.equal(erroDoDicionario(um('x'.repeat(40), 'y'.repeat(120))), null, 'nos limites')
assert.equal(erroDoDicionario(um('fam\u{1F468}‍\u{1F469}‍\u{1F467}')), null, 'emoji com U+200D é aceito')
assert.equal(erroDoDicionario(Array.from({ length: 60 }, (_, i) => ({ termo: `t${i}`, significado: 's' }))), null, '60 termos')

// Recusados, com a mensagem dizendo qual
assert.match(erroDoDicionario(Array.from({ length: 61 }, (_, i) => ({ termo: `t${i}`, significado: 's' }))), /até 60 termos/)
assert.match(erroDoDicionario(um('  ')), /linha 1 está sem termo/)
assert.match(erroDoDicionario([...um('a'), ...um('')]), /linha 2 está sem termo/)
assert.match(erroDoDicionario(um('PF', '  ')), /"PF" quer dizer/)
assert.match(erroDoDicionario(um('x'.repeat(41))), /passa de 40/)
assert.match(erroDoDicionario(um('PF', 'a\nb')), /quebra de linha/, 'quebra de linha')
assert.match(erroDoDicionario(um('a\tb')), /quebra de linha ou caractere invisível/, 'controle')
assert.match(erroDoDicionario(um('a\u0085b')), /invisível/, 'controle C1')
assert.match(erroDoDicionario(um('a"b')), /não pode ter aspas/, 'aspas no termo')
assert.equal(erroDoDicionario(um('PF', 'o "prato" do dia')), null, 'aspas no significado seguem aceitas')
const invisiveis = [0x00AD, 0x061C, 0x200B, 0x200E, 0x200F, 0x2028, 0x2029, 0x2060, 0x3164, 0xFEFF,
  0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069, 0xE0000, 0xE0020, 0xE007F]
for (const codigo of invisiveis) {
  const c = String.fromCodePoint(codigo)
  const nome = `U+${codigo.toString(16).toUpperCase().padStart(4, '0')}`
  assert.match(erroDoDicionario(um(`a${c}b`)) ?? '', /invisível/, `${nome} no termo`)
  assert.match(erroDoDicionario(um('PF', `a${c}b`)) ?? '', /invisível/, `${nome} no significado`)
}
assert.equal(erroDoDicionario(um('a‍b')), null, 'U+200D sozinho também passa')

// Repetido, sem diferença de maiúscula nem de acento
assert.match(erroDoDicionario([...um('Açaí'), ...um('acai')]), /"acai" repete o termo da linha 1/)
assert.match(erroDoDicionario([...um('PF'), ...um(' pf ')]), /repete/)
assert.equal(erroDoDicionario([...um('PF'), ...um('PFs')]), null)
assert.match(erroDoDicionario([...um('\u2764\uFE0F'), ...um('\u2764')]) ?? '', /repete/, 'marca sem espaçamento (U+FE0F) não conta')
assert.match(erroDoDicionario([...um('e\u0301'), ...um('E')]) ?? '', /repete/, 'acento combinado')

// Salvar apara; comparar ignora só espaços nas pontas e chaves extras
assert.deepEqual(dicionarioParaSalvar([{ chave: 3, termo: ' PF ', significado: ' prato  ' }]), [{ termo: 'PF', significado: 'prato' }])
assert.equal(mesmoDicionario([{ chave: 1, termo: 'PF', significado: 'a' }], [{ termo: ' PF', significado: 'a ' }]), true)
assert.equal(mesmoDicionario(um('PF', 'a'), um('PF', 'b')), false)
assert.equal(mesmoDicionario(undefined, []), true)
assert.equal(mesmoDicionario([...um('a'), ...um('b')], [...um('b'), ...um('a')]), false, 'ordem conta')
console.log('#3149 domínio: limites, caracteres recusados, aspas no termo, repetido sem maiúscula nem acento, aparar e comparar.')
