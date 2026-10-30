import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { transformWithOxc } from 'vite'
import { renderToStaticMarkup } from 'react-dom/server'
import { mapaDaViagem, tituloDaViagem, viagemDoPedido } from '../src/dominio/mapaViagem.js'

const pedidoId = 'a1111111-1111-4111-8111-111111111111'
const outraId = 'b2222222-2222-4222-8222-222222222222'
const parada = (id, ordem, endereco) => ({ pedidoId: id, ordem, endereco })
const viagem = viagemDoPedido({ pedidoId, viagem: { id: 'viagem-real-da-fixture', situacao: 'EmRota', saiuEm: '2026-10-29T15:00:00Z',
  paradas: [parada(pedidoId, 2, 'Rua do destino, 2, São Paulo'), parada(outraId, 1, 'Rua da primeira parada, 1, São Paulo')] } }, pedidoId)
assert.equal(viagem.paradas[0].pedidoId, outraId)
assert.equal(viagem.saiuEm, '2026-10-29T15:00:00Z')
assert.equal(tituloDaViagem(viagem), 'Viagem em rota')
assert.equal(viagemDoPedido({ pedidoId, viagem: null }, pedidoId), null)
assert.throws(() => viagemDoPedido({ pedidoId: outraId, viagem }, pedidoId))
assert.throws(() => viagemDoPedido({ pedidoId, viagem: { ...viagem, paradas: [parada(outraId, 1, 'Outra parada')] } }, pedidoId))
console.log('ok vínculo do pedido, ordem, estado e saída vêm exclusivamente da viagem retornada')

const semChave = mapaDaViagem({ viagem, pedidoId })
assert.equal(semChave.embedUrl, null)
const externa = new URL(semChave.mapsUrl)
assert.equal(externa.origin, 'https://www.google.com')
assert.equal(externa.pathname, '/maps/dir/')
assert.equal(externa.searchParams.get('api'), '1')
assert.equal(externa.searchParams.get('origin'), viagem.paradas[0].endereco)
assert.equal(externa.searchParams.get('destination'), viagem.paradas[1].endereco)
assert.equal(externa.searchParams.has('key'), false)
console.log('ok sem chave pública não há embed; Maps URL oficial não contém credenciais')

const comChave = mapaDaViagem({ viagem, pedidoId, chaveEmbed: 'chave-publica-restrita-de-teste' })
const iframe = new URL(comChave.embedUrl)
assert.equal(iframe.origin, 'https://www.google.com')
assert.equal(iframe.pathname, '/maps/embed/v1/directions')
assert.equal(iframe.searchParams.get('origin'), viagem.paradas[0].endereco)
assert.equal(iframe.searchParams.get('destination'), viagem.paradas[1].endereco)
assert.equal(iframe.searchParams.get('key'), 'chave-publica-restrita-de-teste')
assert.equal(iframe.searchParams.has('center'), false)
assert.equal(iframe.searchParams.has('location'), false)
console.log('ok embed directions oficial usa os endereços persistidos, sem GPS ou centro inventado')

const unico = mapaDaViagem({ viagem: { ...viagem, paradas: [parada(pedidoId, 1, 'Destino único')] }, pedidoId, chaveEmbed: 'teste' })
assert.equal(new URL(unico.embedUrl).pathname, '/maps/embed/v1/place')
assert.equal(new URL(unico.embedUrl).searchParams.get('q'), 'Destino único')
const semViagem = mapaDaViagem({ endereco: 'Rua & Número 10 / São Paulo?', chaveEmbed: 'teste' })
assert.equal(new URL(semViagem.embedUrl).searchParams.get('q'), 'Rua & Número 10 / São Paulo?')
assert.equal(mapaDaViagem({ endereco: '' }).mapsUrl, null)
assert.equal(mapaDaViagem({ viagem: { ...viagem, paradas: [parada(pedidoId, 1, null)] }, pedidoId, endereco: 'Cadastro mudou', chaveEmbed: 'teste' }).embedUrl, null)
console.log('ok destino único usa place, sem viagem não cria roteiro e snapshot ausente não usa outro endereço')

const longa = { ...viagem, paradas: Array.from({ length: 23 }, (_, i) => parada(i === 0 ? pedidoId : `pedido-${i}`, i + 1, `Rua ${i}, São Paulo`)) }
const limitada = mapaDaViagem({ viagem: longa, pedidoId, chaveEmbed: 'teste' })
assert.equal(limitada.roteiro, false)
assert.equal(new URL(limitada.embedUrl).pathname, '/maps/embed/v1/place')
assert.equal(limitada.roteiroExterno, false)
assert.equal(new URL(limitada.mapsUrl).pathname, '/maps/search/')
const seis = mapaDaViagem({ viagem: { ...longa, paradas: longa.paradas.slice(0, 6) }, pedidoId, chaveEmbed: 'teste' })
assert.equal(new URL(seis.embedUrl).searchParams.get('waypoints').split('|').length, 4)
assert.equal(seis.roteiroExterno, false)
console.log('ok limites oficiais de waypoints não truncam viagens em silêncio; link móvel abre o destino quando necessário')
const urlComponente = new URL('../src/features/ficha-cliente/MapaViagem.jsx', import.meta.url)
const compilado = (await transformWithOxc(readFileSync(urlComponente, 'utf8'), urlComponente.pathname, { jsx: { runtime: 'automatic' } })).code
registerHooks({
  resolve(specifier, context, next) {
    if (context.parentURL === urlComponente.href && specifier.endsWith('/mapaViagem')) return next(specifier + '.js', context)
    return next(specifier, context)
  },
  load(url, context, next) {
    if (url === urlComponente.href) return { format: 'module', shortCircuit: true, source: compilado }
    if (url.endsWith('/MapaViagem.module.css')) return { format: 'module', shortCircuit: true, source: 'export default new Proxy({}, { get: (_, nome) => nome })' }
    return next(url, context)
  },
})
const { MapaDaViagem } = await import(urlComponente.href)
const html = renderToStaticMarkup(MapaDaViagem({ viagem, pedidoId, nomeCliente: 'Cliente da viagem' }))
assert.match(html, /conteudo semMapa/)
assert.match(html, /<p class="indisponivel">Mapa indisponível neste ambiente\.<\/p>/)
assert.doesNotMatch(html, /<iframe/)
assert.match(html, /Abrir roteiro no Maps/)
assert.match(html, /Sem posição em tempo real/)
assert.match(html, /Rua da primeira parada, 1, São Paulo/)
assert.match(html, /Rua do destino, 2, São Paulo/)
const css = readFileSync(new URL('../src/features/ficha-cliente/MapaViagem.module.css', import.meta.url), 'utf8')
assert.doesNotMatch(css.match(/\.indisponivel\s*\{([^}]+)\}/)[1], /height|padding|background/)
assert.match(css, /\.mapa iframe\s*\{[^}]*min-height: 280px/)
console.log('ok sem chave o componente real mostra aviso compacto, link e paradas; reserva de altura só no iframe')
console.log('6 grupos do mapa/viagem aprovados. Esta prova não certifica uma chave Maps nem pixels do provedor.')
