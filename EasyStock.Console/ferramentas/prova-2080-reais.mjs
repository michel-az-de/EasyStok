// Prova da #2080: o console lê e escreve reais de um jeito só.
//
// A banca (#2073, revisores D e E) achou o mesmo valor lido de quatro jeitos: "100" era R$ 1,00 no
// preço do prato e R$ 100,00 na porção; "1.500" virava R$ 1,50 na zona de frete e "1.234,56" virava
// NaN na corrida; colar "1,234.56" no campo do caixa cortava para R$ 1,23; o formatador tinha dois
// sinais de negativo e `moeda(-0.001)` dava "-R$ 0,00".
//
// Esta prova trava a regra: "100" é R$ 100,00 em todo campo, o texto que a tela escreve é o que ela
// relê, e o formatador tem um sinal só. A fronteira (nenhum leitor ou formatador fora do módulo
// único) é de `verificar-reais.mjs`.
//
// Roda com: node ferramentas/prova-2080-reais.mjs

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

const formato = await import('../src/dominio/formato.js')
const porcoes = await import('../src/dominio/porcoes.js')
const entregas = await import('../src/dominio/entregasApi.js')
const campo = await import('../src/componentes/moedaNoCampo.js')
const { moeda, lerMoeda, lerReais, textoDosCentavos } = formato

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    falhas.push(descricao)
    const motivo = String(erro.message).split(String.fromCharCode(10)).filter(Boolean).slice(0, 4).join(' | ')
    console.log('FALHA ' + descricao + String.fromCharCode(10) + '      ' + motivo)
  }
}
const NBSP = String.fromCharCode(160)
const duro = (texto) => texto.replaceAll(' ', NBSP)

// --- E10: um formatador, um sinal ------------------------------------------------------------
confere('E10: negativo tem um sinal só (menos de verdade, colado ao R$)', () => {
  assert.equal(moeda(-3), duro('−R$ 3,00'))
  assert.equal(moeda(-1234.5), duro('−R$ 1.234,50'))
  assert.equal(moeda(3), duro('R$ 3,00'))
  assert.ok(!moeda(-3).includes('-'), 'sem hífen')
})
confere('E10: o que arredonda para zero não leva sinal (nem -0)', () => {
  assert.equal(moeda(-0.001), duro('R$ 0,00'))
  assert.equal(moeda(-0), duro('R$ 0,00'))
  assert.equal(moeda(0.004), duro('R$ 0,00'))
  assert.equal(moeda(-0.01), duro('−R$ 0,01'))
})
confere('E10: sem valor mostra traço, não derruba a tela', () => {
  assert.equal(moeda(null), '—')
  assert.equal(moeda(undefined), '—')
})
confere('E10: o texto do campo (componentes) e o moeda (domínio) escrevem igual', () => {
  for (const centavos of [0, 1, 7, 99, 100, 1850, 123456, 150000, 9999999, 100000000]) {
    assert.equal(moeda(centavos / 100), duro('R$ ') + campo.textoDosCentavos(centavos), `centavos ${centavos}`)
    assert.equal(textoDosCentavos(centavos), campo.textoDosCentavos(centavos), `texto de ${centavos}`)
  }
  assert.equal(textoDosCentavos(null), '')
})

// --- leitor único ----------------------------------------------------------------------------
confere('leitor: o mesmo texto vale o mesmo valor em qualquer campo', () => {
  const casos = [
    ['100', 10000], ['12,50', 1250], ['12.50', 1250], ['1.500', 150000], ['1.234,56', 123456],
    ['1,234.56', 123456], ['R$ 18,90', 1890], ['R$ 7', 700], ['0,5', 50], ['18,9', 1890], ['1.000.000', 100000000],
  ]
  for (const [texto, centavos] of casos) assert.equal(lerReais(texto), centavos, texto)
})
confere('leitor: sem número é NaN, e o sinal de menos é respeitado', () => {
  for (const vazio of ['', '  ', 'abc', 'R$', null, undefined]) assert.ok(Number.isNaN(lerReais(vazio)), String(vazio))
  assert.equal(lerReais('-5'), -500)
  assert.equal(lerReais('R$ -18,90'), -1890)
})

// --- D4: prato e porção leem igual, e salvar sem tocar não muda o valor ----------------------
confere('D4: "100" é R$ 100,00 no preço do prato e na porção', () => {
  assert.equal(lerReais('100') / 100, 100)
  assert.equal(porcoes.precoDoTexto('100'), 100)
  assert.equal(porcoes.precoDoTexto('1.500'), 1500)
})
confere('D4: o valor que vem da API volta igual (prato e porção)', () => {
  const precos = [0.07, 1, 28, 28.5, 34.9, 100, 999.99, 1000, 1234.56, 1500, 99999.99]
  for (const preco of precos) {
    const centavos = Math.round(preco * 100)
    assert.equal(lerMoeda(textoDosCentavos(centavos)), centavos, `prato ${preco}`)
    const [linha] = porcoes.rascunhoDasPorcoes([{ id: 'p1', rotulo: '300 g', preco, peso: '300 g', padrao: true }])
    assert.equal(porcoes.corpoDasPorcoes([linha])[0].preco, preco, `porção ${preco}`)
    assert.equal(porcoes.porcoesMudaram([{ id: 'p1', rotulo: '300 g', preco, peso: '300 g', padrao: true }], [linha]), false, `sem tocar ${preco}`)
  }
})

// --- D8: zona, faixa de frete e corrida leem como o caixa ------------------------------------
const FORM_FRETE = (valor) => ({
  cep: '05500-000', numero: '100', raioKm: '5', gratisKm: '0,5', fatorRota: '1,4', faixas: [{ ateKm: '5', valor }],
})
confere('D8: faixa de frete — "1.500" é R$ 1.500,00 e "1.234,56" é válido', () => {
  assert.equal(entregas.corpoFreteRaio(FORM_FRETE('1.500')).faixas[0].valorCentavos, 150000)
  assert.equal(entregas.corpoFreteRaio(FORM_FRETE('1.234,56')).faixas[0].valorCentavos, 123456)
  assert.equal(entregas.corpoFreteRaio(FORM_FRETE('R$ 18,90')).faixas[0].valorCentavos, 1890)
  assert.equal(entregas.erroDoFormularioFreteRaio(FORM_FRETE('1.234,56'), true), null)
  assert.match(entregas.erroDoFormularioFreteRaio(FORM_FRETE('abc'), true), /valor/i)
})
confere('D8: zona de frete — o valor lido é o do caixa', () => {
  const zona = (valor) => entregas.corpoZona({ label: 'Centro', valor, tempoEstimadoMinutos: '40', ordem: '1', cobertura: 'bairros', bairros: 'Centro' }).valor
  assert.equal(zona('1.500'), 1500)
  assert.equal(zona('1.234,56'), 1234.56)
  assert.equal(zona('7,50'), 7.5)
  assert.equal(zona('12.5'), 12.5)
})
confere('D8: corrida — "1.234,56" é custo válido, não NaN', () => {
  const corpo = (custo) => entregas.corpoCorrida({ empresa: 'Uber', custo, motorista: '' }).custoCentavos
  assert.equal(corpo('1.234,56'), 123456)
  assert.equal(corpo('1.500'), 150000)
  assert.equal(corpo('R$ 18,90'), 1890)
  assert.equal(entregas.erroDoFormularioCorrida({ empresa: 'Uber', custo: '1.234,56', motorista: '' }), null)
  assert.match(entregas.erroDoFormularioCorrida({ empresa: 'Uber', custo: 'abc', motorista: '' }), /custo/i)
})
confere('D8: o valor gravado volta ao formulário no mesmo formato do campo e relê igual', () => {
  const faixa = entregas.formularioDoFreteRaio({
    configurado: true, raioMaxMetros: 5000, faixaGratisMetros: 0, fatorRota: 1.4, faixas: [{ ateMetros: 5000, valorCentavos: 150050 }],
  }).faixas[0].valor
  assert.equal(faixa, '1.500,50')
  assert.equal(entregas.corpoFreteRaio(FORM_FRETE(faixa)).faixas[0].valorCentavos, 150050)
  const custo = entregas.formularioDaCorrida({ provedor: 'Uber', custoCentavos: 123456, motorista: null }).custo
  assert.equal(custo, '1.234,56')
  assert.equal(entregas.corpoCorrida({ empresa: 'Uber', custo, motorista: '' }).custoCentavos, 123456)
  const zona = entregas.camposDaZona({ label: 'Centro', valor: 1234.5, tempoEstimadoMinutos: 40, ordem: 1, bairros: ['Centro'] }).valor
  assert.equal(zona, '1.234,50')
  assert.equal(entregas.corpoZona({ ...entregas.camposDaZona({ label: 'Centro', valor: 1234.5, bairros: ['Centro'] }), bairros: 'Centro' }).valor, 1234.5)
})

// --- D10: colar no campo de reais -------------------------------------------------------------
confere('D10: colar "1,234.56" ou "1.234,56" mantém os centavos', () => {
  for (const colado of ['1,234.56', '1.234,56', 'R$ 1.234,56', 'R$ 1,234.56']) {
    const texto = campo.textoDigitadoEmReais(colado)
    assert.equal(lerMoeda(texto), 123456, `${colado} -> ${texto}`)
  }
  assert.equal(lerMoeda(campo.textoDigitadoEmReais('1,234,567.89')), 123456789)
})
confere('D10: o que já funcionava segue igual', () => {
  const digitar = (teclas) => [...teclas].reduce((texto, tecla) => campo.textoDigitadoEmReais(texto + tecla), '')
  assert.equal(digitar('1234.56'), '1234.56')
  assert.equal(digitar('12,5'), '12,5')
  assert.equal(lerMoeda(digitar('1.500')), 150000)
  assert.equal(lerMoeda(digitar('100')), 10000)
  assert.equal(campo.textoDigitadoEmReais('12,345'), '12,34', 'três casas decimais: corta (e o campo avisa)')
})
confere('D10: quando os centavos são cortados o campo sabe, para avisar', () => {
  assert.equal(campo.centavosCortados('1,500'), true)
  assert.equal(campo.centavosCortados('12,345'), true)
  assert.equal(campo.centavosCortados('1,50'), false)
  assert.equal(campo.centavosCortados('1,234.56'), false)
  assert.equal(campo.centavosCortados('1.500'), false)
  assert.equal(campo.centavosCortados('100'), false)
})

console.log(`${String.fromCharCode(10)}${passou} ok, ${falhas.length} falha(s)`)
if (falhas.length > 0) process.exit(1)
