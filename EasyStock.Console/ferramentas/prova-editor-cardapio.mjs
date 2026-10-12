/* eslint-disable no-console */
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) return proximo(nome + '.js', contexto)
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})

const { lerFichaDoPrato, salvarFichaDoPrato, salvarFotoDoPrato } = await import('../src/aplicacao/edicaoCardapio.js')
const chamadas = []
let falharFoto = false
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, ...opcoes })
  const foto = url.includes('/foto?')
  if (foto && falharFoto) return new Response(JSON.stringify({ error: { message: 'Envio interrompido' } }), { status: 503 })
  const data = foto ? { url: '/files/cardapios/empresa/prato.webp' }
    : opcoes.method === 'POST' ? { itemId: 'prato-criado' }
      : { nome: 'Nome salvo no servidor', linha: 'PrepararEmCasa', porcao: '800 g', preco: 60, descricao: 'Massa artesanal', ingredientes: 'Trigo e ovos', alergenos: 'Trigo, ovos', fotoUrl: '/files/cardapios/empresa/capa.webp', fotos: ['/files/cardapios/empresa/angulo.webp'], produtoId: 'produto-estoque', porcoes: [{ id: 'porcao-original', rotulo: '800 g', preco: 60, sku: 'LAS-800', padrao: true }] }
  return new Response(JSON.stringify({ data }), { status: 200 })
}

const d = await lerFichaDoPrato('prato-original')
assert.equal(d.nome, 'Nome salvo no servidor', 'Reabrir usa o nome canônico mesmo com uma lista desatualizada')
assert.equal(d.linha, 'casa')
assert.equal(d.porcao, '800 g')
assert.equal(d.preco, 60)
assert.equal(d.produtoId, 'produto-estoque')
assert.equal(d.alergenos, 'Trigo, ovos')
assert.equal(d.foto, '/api/public/cardapio/fotos/empresa/capa.webp')
assert.deepEqual(d.fotos, ['/api/public/cardapio/fotos/empresa/angulo.webp'])
assert.equal(d.porcoes[0].id, 'porcao-original')
assert.equal(d.porcoes[0].sku, 'LAS-800')

const dados = { nome: 'Nhoque', linha: 'casa', porcao: '400 g', preco: 39, descricao: 'Massa artesanal', secaoId: 'categoria' }
const id = await salvarFichaDoPrato(null, dados)
assert.equal(id, 'prato-criado')
const gravado = JSON.parse(chamadas.at(-1).body)
assert.equal(gravado.secaoId, 'categoria')
assert.equal(gravado.linha, 'PrepararEmCasa')
assert.ok(!('porcoes' in gravado), 'Editar informações não apaga porções')
assert.ok(!('produtoId' in gravado), 'Vínculo com estoque não é gravável pelo editor')

const foto = new File(['conteudo-testado-no-backend'], 'nhoque.webp', { type: 'image/webp' })
falharFoto = true
await assert.rejects(() => salvarFotoDoPrato(id, foto), /Envio interrompido/)
falharFoto = false
await salvarFotoDoPrato(id, foto, true)
assert.equal(chamadas.at(-1).url, '/api/atendimento/comanda/cardapio/prato-criado/foto?galeria=true')
assert.equal(chamadas.at(-1).method, 'POST')
assert.ok(chamadas.at(-1).body instanceof FormData)
assert.equal(chamadas.at(-1).body.get('file').name, 'nhoque.webp')
assert.equal(chamadas.filter((c) => c.method === 'POST' && !c.url.includes('/foto?')).length, 1, 'A retentativa da foto usa o id já criado')

await salvarFichaDoPrato(id, { ...dados, ingredientes: 'Trigo, ovos' })
assert.equal(chamadas.at(-1).method, 'PUT')
assert.equal(chamadas.at(-1).url, '/api/atendimento/comanda/cardapio/prato-criado')
const total = chamadas.length
await assert.rejects(() => salvarFotoDoPrato(id, new File(['x'], 'arquivo.svg', { type: 'image/svg+xml' })), /JPG, PNG ou WebP/)
await assert.rejects(() => salvarFotoDoPrato(id, new File([new Uint8Array(6 * 1024 * 1024 + 1)], 'grande.png', { type: 'image/png' })), /até 6 MB/)
assert.equal(chamadas.length, total, 'Arquivo inválido não chega à rede')
console.log('Editor do cardápio: leitura, vínculos, porções preservadas, criação, edição, multipart, falha e retentativa de foto aprovados.')
