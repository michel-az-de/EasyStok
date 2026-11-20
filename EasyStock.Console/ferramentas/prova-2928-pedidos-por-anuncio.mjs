/* eslint-disable no-console */
// #2928 (S5): caminho da consulta por período e nome das plataformas da tabela "Pedidos por anúncio".
import assert from 'node:assert/strict'

const { caminhoPedidosPorAnuncio, nomeDaPlataforma } = await import('../src/dominio/pedidosPorAnuncio.js')

assert.equal(caminhoPedidosPorAnuncio({ inicio: '2026-11-01', fim: '2026-11-30' }), '/api/relatorios/pedidos-por-anuncio?de=2026-11-01&ate=2026-11-30', 'período em data pura, como a API espera')
assert.equal(nomeDaPlataforma('google'), 'Google')
assert.equal(nomeDaPlataforma('meta'), 'Meta')
assert.equal(nomeDaPlataforma('outra'), 'Outra')
assert.equal(nomeDaPlataforma('sem_origem'), '—', 'a linha sem origem não tem plataforma')
assert.equal(nomeDaPlataforma('tiktok'), 'tiktok', 'plataforma nova aparece como veio')
assert.equal(nomeDaPlataforma(null), '—')
console.log('prova-2928-pedidos-por-anuncio: ok')
