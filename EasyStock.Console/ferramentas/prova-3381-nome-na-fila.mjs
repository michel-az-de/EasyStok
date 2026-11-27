// #3381 (r3 P1): o nome do cliente na fila tem UMA regra de corte: primeiro nome inteiro + inicial do sobrenome
// ("Thais R."); o resto é só `text-overflow: ellipsis` no CSS. Nunca abreviação com ponto no meio de palavra.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const { apresentacaoDoNome } = await import('../src/dominio/nomeDoContato.js')
const curto = (n) => apresentacaoDoNome(n).curto

assert.equal(curto('Thais Ribeiro'), 'Thais R.')
assert.equal(curto('Yamaser Yamasaki'), 'Yamaser Y.', 'sobrenome longo vira inicial, não "Yamas."')
assert.equal(curto('Maria de Fátima Albuquerque'), 'Maria A.', 'partícula some, sobrenome vira inicial')
assert.equal(curto('Ana Clara Montenegro Souza'), 'Ana S.')
assert.equal(curto('Notelet Eletrônicos'), 'Notelet E.')
assert.equal(curto('Notelet Eletrônicos Ltda'), 'Notelet Eletrônicos')
assert.equal(curto('Supermercado Bom Preço Comércio de Alimentos Eireli'), 'Supermercado Bom Preço Comércio de Alimentos')
assert.equal(curto('Beatriz'), 'Beatriz', 'uma palavra fica como veio (o CSS corta com reticências)')
assert.equal(curto('Anastácia-Maria Vasconcelos'), 'Anastácia-Maria V.', 'primeiro nome composto fica inteiro')
assert.equal(curto('Renan O'), 'Renan O', 'já curto: não muda')
for (const n of ['Yamaser Yamasaki', 'Notelet Eletrônicos', 'Maria de Fátima Albuquerque', 'Joaquim Bartolomeu Nascimento Filho 🏪']) {
  for (const palavra of curto(n).split(' ')) assert.ok(!/\p{L}{2,}\.$/u.test(palavra) || palavra === curto(n).split(' ')[0], `"${curto(n)}": ponto no meio de palavra`)
}
console.log('prova-3381-nome-na-fila: ok')
