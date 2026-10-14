import assert from 'node:assert/strict'
import { complementoDaResposta, complementoDeTexto, podeAceitarComplemento, perguntaDeComplemento } from '../src/dominio/complementoDeTexto.js'

const texto = 'Bom dia! Podemos '
assert.equal(complementoDeTexto(texto, texto + 'ajudar com seu pedido?'), 'ajudar com seu pedido?')
for (const resposta of [null, '', 'Claro! ' + texto, '"' + texto + 'fim"', texto, texto + ' ', texto + '\nExplicação', texto + '{}', texto + '```', texto + 'x'.repeat(241), texto + 'fim. Explicação: exemplo'])
  assert.equal(complementoDeTexto(texto, resposta), '')
assert.equal(complementoDeTexto(texto, texto + 'ajudar?', texto.length + 2), '')
const acoes = [{ tipo: 'rascunho', texto: texto + 'ajudar?' }]
assert.equal(complementoDaResposta(texto, { texto: '', acoes }), 'ajudar?')
assert.equal(complementoDaResposta(texto, { texto: 'Preparei uma sugestão.', acoes }), 'ajudar?')
assert.equal(complementoDaResposta(texto, { texto: '', acoes: [{ tipo: 'rascunho', texto: 'Outro prefixo' }] }), '')
assert.equal(complementoDaResposta(texto, { texto: '', acoes }, texto.length + 2), '')
for (const tipo of ['nota_interna', 'abrir_tela', 'enviar_cardapio', 'retomar_automatico'])
  assert.equal(complementoDaResposta(texto, { texto: '', acoes: [{ tipo, texto: texto + 'ajudar?' }] }), '')
assert.equal(complementoDaResposta(texto, { texto: texto + 'ajudar?', acoes: null }), 'ajudar?')
assert.equal(complementoDaResposta(texto, { texto: '', acoes: {} }), '')
const evento = { key: 'Tab', currentTarget: { selectionStart: texto.length, selectionEnd: texto.length } }
assert.equal(podeAceitarComplemento(evento, texto, 'ajudar?'), true)
assert.equal(podeAceitarComplemento(evento, texto, ''), false)
for (const alteracao of [{ shiftKey: true }, { ctrlKey: true }, { altKey: true }, { metaKey: true }, { isComposing: true }, { nativeEvent: { isComposing: true } }, { keyCode: 229 }, { key: 'Enter' }, { currentTarget: { selectionStart: 0, selectionEnd: texto.length } }, { currentTarget: { selectionStart: 2, selectionEnd: 2 } }])
  assert.equal(podeAceitarComplemento({ ...evento, ...alteracao }, texto, 'ajudar?'), false)
assert.ok(perguntaDeComplemento('x'.repeat(1000), 'cliente').length <= 2000)
assert.match(perguntaDeComplemento(texto, 'cozinha'), /observação do pedido para a cozinha/)
console.log('OK #1757: prefixo exato, formato, limite, teclado, seleção, IME e tamanho do contrato.')
