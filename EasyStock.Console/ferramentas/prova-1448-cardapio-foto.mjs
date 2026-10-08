/* eslint-disable no-console */
// Prova da #1448 (homologação de 07/10): o painel "Cardápio de hoje" mostra a foto real que vem da
// API, organizado por categoria, com busca, e manda a foto do item ao cliente pela rota de #1439.
//   1. URL gravada com host antigo (`ez-api.92.113.33.60.sslip.io/files/cardapios/...`) é exibida
//      pela rota de mídia da API (`/api/public/cardapio/fotos/...`), que passa pelo /api da origem;
//   2. o item da comanda chega da API com a foto já normalizada (capa e galeria);
//   3. itens agrupados pela categoria da vitrine, "Outros" no fim; busca sem acento e por palavras;
//   4. "Enviar foto" vai por POST .../mensagens/imagem-cardapio com id, índice 0 e legenda,
//      sem o navegador baixar a URL;
//   5. o chat do site continua sem aceitar foto no console até o widget do site desenhar imagem.
//
//   node ferramentas/prova-1448-cardapio-foto.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body })
  const data = { id: 'm-api', direcao: 'Saida', status: 'Enviada', autor: 'Dona', enviadaEm: '2026-10-07T15:00:00Z', tipoConteudo: 'Imagem', texto: 'ok' }
  return new Response(JSON.stringify({ data }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const faltas = []
const confere = (descricao, ok, detalhe = '') => {
  if (ok) console.log('ok    ' + descricao)
  else { faltas.push(descricao); console.log('FALHA ' + descricao + (detalhe ? `\n      ${detalhe}` : '')) }
}

const vitrine = await import('../src/dominio/vitrineCardapio.js')
const { produtoDaApi } = await import('../src/infra/api/comandaApi.js')

const ANTIGA = 'https://ez-api.92.113.33.60.sslip.io/files/cardapios/e/s/i/angulo.webp'
const ROTA = '/api/public/cardapio/fotos/e/s/i/angulo.webp'

// 1. Exibição pela rota de mídia da API, de qualquer host.
{
  const { urlDeExibicaoDaFoto } = vitrine
  confere('URL do host antigo vira a rota de mídia da API', urlDeExibicaoDaFoto(ANTIGA) === ROTA, urlDeExibicaoDaFoto(ANTIGA))
  confere('URL do host atual também passa pela rota', urlDeExibicaoDaFoto('https://api.easystok.online/files/cardapios/e/s/i/angulo.webp') === ROTA)
  confere('URL relativa /files/cardapios/ passa pela rota', urlDeExibicaoDaFoto('/files/cardapios/e/s/i/angulo.webp') === ROTA)
  confere('base da API absoluta vai na frente', urlDeExibicaoDaFoto(ANTIGA, 'https://api.x') === 'https://api.x' + ROTA)
  confere('query string da URL gravada não vai para a rota', urlDeExibicaoDaFoto(ANTIGA + '?v=2') === ROTA)
  confere('foto que não é do storage do cardápio fica como está',
    urlDeExibicaoDaFoto('https://cdn.exemplo.com/foto.jpg') === 'https://cdn.exemplo.com/foto.jpg'
    && urlDeExibicaoDaFoto('data:image/png;base64,AAAA') === 'data:image/png;base64,AAAA')
  confere('mídia do atendimento não vira foto pública', urlDeExibicaoDaFoto('https://h/files/atendimento/e/c/f.jpg') === 'https://h/files/atendimento/e/c/f.jpg')
  confere('sem URL, sem foto', urlDeExibicaoDaFoto(null) === null && urlDeExibicaoDaFoto('  ') === null)
}

// 2. O item da comanda já chega com a foto normalizada.
{
  const capa = 'https://ez-api.92.113.33.60.sslip.io/files/cardapios/e/s/i/capa.webp'
  const item = produtoDaApi({
    id: 'item-1', nome: 'Lasanha Bolonhesa Clássica', linha: 'prepararEmCasa', pesoExibicao: '600 g', precoCentavos: 3800,
    categoria: 'Lasanhas', imagemUrl: capa, fotos: [capa, ANTIGA],
  })
  confere('capa normalizada', item.foto === '/api/public/cardapio/fotos/e/s/i/capa.webp', item.foto)
  confere('galeria normalizada na mesma ordem', item.fotos.join() === ['/api/public/cardapio/fotos/e/s/i/capa.webp', ROTA].join(), item.fotos.join())
  const soCapa = produtoDaApi({ id: 'item-2', nome: 'X', precoCentavos: 100, imagemUrl: capa })
  confere('sem galeria, a capa é a única foto', soCapa.fotos.length === 1 && soCapa.fotos[0] === soCapa.foto)
  confere('a foto mostrada é a enviada com índice 0', vitrine.fotoDoItem(item) === item.fotos[0])
  const semFoto = produtoDaApi({ id: 'item-3', nome: 'Y', precoCentavos: 100 })
  confere('item sem foto fica sem foto (o painel mostra a carta desenhada)', semFoto.foto === null && semFoto.fotos.length === 0
    && vitrine.fotoDoItem(semFoto) === null)
}

// 3. Categoria e busca.
{
  const itens = [
    { sku: 'a', nome: 'Lasanha Bolonhesa', categoria: 'Lasanhas', porcao: '600 g' },
    { sku: 'b', nome: 'Pão de alho', categoria: null, porcao: '4 un' },
    { sku: 'c', nome: 'Ravióli de abóbora', categoria: 'Massas recheadas', porcao: '500 g' },
    { sku: 'd', nome: 'Lasanha Verde', categoria: 'Lasanhas', porcao: '1 kg' },
  ]
  const grupos = vitrine.agruparPorCategoria(itens)
  confere('agrupa pela categoria na ordem que a API manda, "Outros" no fim',
    grupos.map((g) => `${g.categoria}:${g.itens.map((i) => i.sku).join('')}`).join(' | ') === 'Lasanhas:ad | Massas recheadas:c | Outros:b',
    JSON.stringify(grupos.map((g) => [g.categoria, g.itens.length])))
  confere('busca ignora acento e caixa', vitrine.filtrarCardapio(itens, 'RAVIOLI').map((i) => i.sku).join() === 'c')
  confere('busca por várias palavras (nome e porção)', vitrine.filtrarCardapio(itens, 'lasanha 600').map((i) => i.sku).join() === 'a')
  confere('busca pela categoria', vitrine.filtrarCardapio(itens, 'recheadas').map((i) => i.sku).join() === 'c')
  confere('busca vazia mostra tudo', vitrine.filtrarCardapio(itens, '  ').length === 4)
}

// 4. "Enviar foto" reaproveita a rota de #1439.
{
  const acao = await import('../src/aplicacao/acoes.js')
  const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
  const { comApi } = await import('../src/aplicacao/acoesApi.js')
  const AGORA = Date.parse('2026-10-07T15:00:00Z')
  const estadoRef = {
    current: {
      conversas: [{
        id: 'c1', canal: 'WhatsApp', nome: 'Cliente', estado: 'Em atendimento', mensagens: [], pedido: null,
        cliente: { notas: [], tags: [] }, janelaExpiraEm: new Date(AGORA + 3600000).toISOString(),
      }],
      catalogo: { cardapio: [], janelas: [], canais: [] },
      funcionamento: {}, lojaAberta: true, sincronizacao: { estado: 'ok', aviso: null },
    },
  }
  const despachos = []
  const despachar = (a) => despachos.push(a)
  const agoraRef = { current: AGORA }
  const locais = criarAcoes({
    despachar, agoraRef, estadoRef, pendentes: { current: [] },
    consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  })
  const api = comApi(locais, { despachar, agoraRef, estadoRef })

  const item = produtoDaApi({
    id: 'item-1', nome: 'Lasanha Bolonhesa Clássica', linha: 'prepararEmCasa', pesoExibicao: '600 g', precoCentavos: 3800,
    imagemUrl: ANTIGA,
  })
  const mensagem = vitrine.mensagemDaFotoDoItem(item)
  confere('item sem foto não gera mensagem', vitrine.mensagemDaFotoDoItem({ sku: 'x', nome: 'x', fotos: [] }) === null)
  chamadas.length = 0
  const enviado = await api.enviarMidia('c1', mensagem)
  const rota = chamadas.map((c) => `${c.metodo} ${c.url}`)
  confere('vai por POST .../mensagens/imagem-cardapio', enviado === true && rota.length === 1
    && rota[0] === 'POST /api/atendimento/conversas/c1/mensagens/imagem-cardapio', rota.join(', ') || 'nenhuma chamada')
  const corpo = chamadas[0] && typeof chamadas[0].corpo === 'string' ? JSON.parse(chamadas[0].corpo) : {}
  confere('manda id do item, índice 0 e legenda com porção e preço', corpo.cardapioItemId === 'item-1' && corpo.indice === 0
    && /^Lasanha Bolonhesa Clássica\n600 g · R\$\s?38,00$/.test(corpo.legenda ?? ''), JSON.stringify(corpo))
  confere('o navegador não baixa a foto', !chamadas.some((c) => String(c.url).includes('/files/') || String(c.url).includes('/cardapio/fotos/')))
  confere('espera a confirmação do EasyStok', despachos.at(-1)?.tipo === acao.CONFIRMAR_ENVIO_API)
}

// 5. Chat do site: aviso honesto até o widget do site desenhar imagem (dependência na #1448).
{
  const { CANAIS } = await import('../src/infra/catalogo.js')
  const { motivoDeFormato, canalPorNome } = await import('../src/dominio/canal.js')
  const chat = canalPorNome(CANAIS, 'Chat do site')
  confere('chat do site segue sem foto no console, com o motivo à vista', motivoDeFormato(chat, 'foto') === 'Chat do site não aceita foto.',
    String(motivoDeFormato(chat, 'foto')))
}

if (faltas.length > 0) {
  console.error(`\nprova 1448 (cardápio com foto e categoria): ${faltas.length} falha(s)`)
  process.exit(1)
}
console.log('\nprova 1448 (cardápio com foto e categoria): ok')
