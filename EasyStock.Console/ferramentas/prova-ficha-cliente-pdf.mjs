import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
import { montarPdf, A4 } from '../src/dominio/pdf.js'

globalThis.chamadasFichaPdf = []
globalThis.arquivoFichaPdf = new Blob([montarPdf({ titulo: 'Ficha de teste', paginas: [{ ...A4, desenhos: [{ tipo: 'texto', x: 40, y: 40, tamanho: 12, texto: 'Cliente de teste' }] }] })], { type: 'application/pdf' })
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) return proximo(spec + '.js', contexto)
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/api/cliente.js')) return { format: 'module', shortCircuit: true,
      source: 'export const chamarApi=(url,opcoes)=>{globalThis.chamadasFichaPdf.push({url,opcoes});return Promise.resolve(globalThis.arquivoFichaPdf)}' }
    return proximo(url, contexto)
  },
})
const { gerarFichaClientePdf } = await import('../src/aplicacao/fichaClientePdf.js')
const id = 'a1111111-1111-4111-8111-111111111111'
const resultado = await gerarFichaClientePdf(id)
assert.equal(chamadasFichaPdf[0].url, `/api/clientes/${id}/ficha.pdf`)
assert.deepEqual(chamadasFichaPdf[0].opcoes, { arquivo: true })
assert.equal(new TextDecoder().decode(resultado.bytes.slice(0, 5)), '%PDF-')
assert.equal(resultado.nomeArquivo, `ficha-cliente-${id}.pdf`)
for (const invalido of ['https://externo.invalid', '../outro', 'sem-cadastro', '']) await assert.rejects(gerarFichaClientePdf(invalido), /Cliente inválido/)
assert.equal(chamadasFichaPdf.length, 1, 'ID inválido não chama a API')
globalThis.arquivoFichaPdf = new Blob(['<!doctype html><title>Resposta HTML</title>'], { type: 'text/html' })
await assert.rejects(gerarFichaClientePdf(id), /não foi recebida em PDF/, 'HTML não é oferecido como PDF')
console.log('PDF da ficha: download binário autenticado, bytes reais, ID restrito e recusa de HTML aprovados.')
