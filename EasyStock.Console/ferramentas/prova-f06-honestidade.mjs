/* eslint-disable no-console */
// Prova da F06 (issue #1236): no modo API, nenhuma ação do console muda só o navegador
// fingindo que gravou. Monta as ações de verdade (`criarAcoes` + `comApi`) com `fetch`
// falso e confere:
//   1. toda chave está em "ligada" (comApi trocou por uma que fala com a API), "só da tela"
//      ou "avisa" (`aplicacao/api/naoLigadas.js`), e as listas não citam ação que não existe;
//   2. cada "avisa" só despacha AVISO_API e não chama a API;
//   3. Encerrar pelo modal faz POST .../encerrar (e manda a despedida marcada pelo envio real);
//   4. foto no WhatsApp vai por multipart ao S02; áudio, arquivo, peça e outros canais avisam;
//   5. o aviso sobrevive à sincronização de 5 s e só some quando a dona fecha.
//
//   node ferramentas/prova-f06-honestidade.mjs

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
    // `fonteDados.js` lê `import.meta.env` do Vite; aqui a fonte é a API.
    if (url.endsWith('/infra/fonteDados.js')) {
      return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
    }
    // A massa (JSON) entra pelo import do Vite; no node vira módulo.
    if (url.endsWith('.json')) {
      return { format: 'module', shortCircuit: true, source: 'export default ' + readFileSync(new URL(url), 'utf8') }
    }
    return proximo(url, contexto)
  },
})

const chamadas = []
globalThis.fetch = async (url, opcoes = {}) => {
  chamadas.push({ url, metodo: opcoes.method ?? 'GET', corpo: opcoes.body })
  const data = { id: 'm-api', direcao: 'Saida', status: 'Enviada', autor: 'Dona', enviadaEm: '2026-09-30T15:00:00Z', tipoConteudo: 'Texto', texto: 'ok' }
  return new Response(JSON.stringify({ data }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const acao = await import('../src/aplicacao/acoes.js')
const { criarAcoes } = await import('../src/aplicacao/criarAcoes.js')
const { comApi } = await import('../src/aplicacao/acoesApi.js')
const { NAO_LIGADAS, SO_DA_TELA } = await import('../src/aplicacao/api/naoLigadas.js')
const { reducer, estadoInicial } = await import('../src/aplicacao/reducer.js')

const faltas = []
const confere = (descricao, ok, detalhe = '') => {
  if (ok) console.log('ok    ' + descricao)
  else { faltas.push(descricao); console.log('FALHA ' + descricao + (detalhe ? `\n      ${detalhe}` : '')) }
}
const esvaziar = async () => { for (let i = 0; i < 5; i += 1) await new Promise((r) => setTimeout(r, 0)) }

const AGORA = Date.parse('2026-09-30T15:00:00Z')
const conversa = (id, canal) => ({
  id, canal, nome: 'Cliente', estado: 'Em atendimento', mensagens: [], pedido: null,
  cliente: { notas: [], tags: [] }, janelaExpiraEm: new Date(AGORA + 3600000).toISOString(),
})
const estadoRef = {
  current: {
    conversas: [conversa('c1', 'WhatsApp'), conversa('c2', 'Instagram')],
    catalogo: { cardapio: [], janelas: [], canais: [] },
    funcionamento: {}, lojaAberta: true, sincronizacao: { estado: 'ok', aviso: null },
  },
}

function montar() {
  const despachos = []
  const despachar = (a) => despachos.push(a)
  const agoraRef = { current: AGORA }
  const locais = criarAcoes({
    despachar, agoraRef, estadoRef, pendentes: { current: [] },
    consultarAgente: async () => {}, perguntarAssistente: async () => '', pedirNotificacaoDoNavegador: async () => {},
  })
  const api = comApi(locais, { despachar, agoraRef, estadoRef })
  return { locais, api, despachos }
}

// 1. Classificação completa.
{
  const { locais, api } = montar()
  const naoLigadas = new Set(Object.keys(NAO_LIGADAS))
  const daTela = new Set(SO_DA_TELA)
  const ligadas = Object.keys(api).filter((nome) => !naoLigadas.has(nome) && api[nome] !== locais[nome])
  const semDono = Object.keys(api).filter((nome) => !naoLigadas.has(nome) && !daTela.has(nome) && !ligadas.includes(nome))
  confere(`toda ação tem dono no modo API (${Object.keys(api).length} ações)`, semDono.length === 0, `sem dono: ${semDono.join(', ')}`)
  const fantasmas = [...naoLigadas, ...daTela].filter((nome) => !(nome in locais))
  confere('as listas só citam ações que existem', fantasmas.length === 0, `não existem: ${fantasmas.join(', ')}`)
  const nasDuas = [...daTela].filter((nome) => naoLigadas.has(nome))
  confere('nenhuma ação é "só da tela" e "avisa" ao mesmo tempo', nasDuas.length === 0, nasDuas.join(', '))
  const telaTrocada = [...daTela].filter((nome) => api[nome] !== locais[nome])
  confere('"só da tela" continua local (comApi não troca)', telaTrocada.length === 0, telaTrocada.join(', '))
  for (const nome of ['encerrarComResumo', 'enviarMidia', 'encerrarAtendimento', 'enviar']) {
    confere(`${nome} é ligada pela API`, ligadas.includes(nome))
  }
}

// 2. Cada "avisa" só avisa.
{
  const mexeram = []
  for (const nome of Object.keys(NAO_LIGADAS)) {
    const { api, despachos } = montar()
    chamadas.length = 0
    try { api[nome]('c1', { id: 'x', sku: 'x', numero: '1' }, 'x', 'x') } catch (erro) { mexeram.push(`${nome} (lançou: ${erro.message})`); continue }
    await esvaziar()
    const soAviso = despachos.length === 1 && despachos[0].tipo === acao.AVISO_API && /ainda não ligado/.test(despachos[0].mensagem)
    if (!soAviso || chamadas.length > 0) mexeram.push(`${nome} -> ${despachos.map((d) => d.tipo).join('+') || 'nada'}${chamadas.length ? ` + ${chamadas.length} chamada(s)` : ''}`)
  }
  confere(`as ${Object.keys(NAO_LIGADAS).length} ações "avisa" só despacham AVISO_API`, mexeram.length === 0, mexeram.slice(0, 8).join('; '))
}

// 3. Encerrar pelo modal.
{
  const { api, despachos } = montar()
  chamadas.length = 0
  api.encerrarComResumo('c1', AGORA, { enviarMensagem: true, textoMensagem: 'Obrigada!', avisoEmail: true, avisoSms: false })
  await esvaziar()
  const rotas = chamadas.map((c) => `${c.metodo} ${c.url}`)
  confere('Encerrar manda a despedida pelo envio real', rotas[0] === 'POST /api/atendimento/conversas/c1/mensagens'
    && JSON.parse(chamadas[0].corpo).texto === 'Obrigada!', rotas.join(', '))
  confere('Encerrar faz POST .../c1/encerrar', rotas.includes('POST /api/atendimento/conversas/c1/encerrar'), rotas.join(', ') || 'nenhuma chamada')
  confere('Encerrar não grava resumo local nem e-mail simulado', !despachos.some((d) => d.tipo === acao.ENCERRAR_COM_RESUMO),
    despachos.map((d) => d.tipo).join(', '))
}
{
  const { api } = montar()
  chamadas.length = 0
  api.encerrarComResumo('c1', AGORA, { enviarMensagem: false, textoMensagem: 'Obrigada!' })
  await esvaziar()
  confere('Encerrar sem despedida não manda mensagem', chamadas.length === 1 && chamadas[0].url.endsWith('/c1/encerrar'),
    chamadas.map((c) => c.url).join(', '))
}

// 4. Mídia.
const FOTO = 'data:image/png;base64,iVBORw0KGgo='
{
  const { api, despachos } = montar()
  chamadas.length = 0
  api.enviarMidia('c1', { formato: 'imagem', arte: FOTO, texto: 'Olha a lasanha', nomeArquivo: 'lasanha.png', legenda: 'Olha a lasanha' })
  await esvaziar()
  const envio = chamadas.find((c) => c.url === '/api/atendimento/conversas/c1/mensagens/imagem')
  confere('foto no WhatsApp vai por POST .../mensagens/imagem', Boolean(envio && envio.metodo === 'POST'), chamadas.map((c) => c.url).join(', ') || 'nenhuma chamada')
  confere('foto vai como multipart com file e legenda', Boolean(envio?.corpo instanceof FormData
    && envio.corpo.get('file') instanceof Blob && envio.corpo.get('legenda') === 'Olha a lasanha'))
  confere('foto nasce "enviando" e não "lida"', despachos.some((d) => d.tipo === acao.ENVIAR_MIDIA && d.status === 'enviando'),
    despachos.map((d) => `${d.tipo}:${d.status ?? ''}`).join(', '))
}
for (const [descricao, id, molde] of [
  ['áudio', 'c1', { formato: 'audio', arte: 'data:audio/webm;base64,AAAA', duracaoMs: 1000, texto: 'Mensagem de áudio' }],
  ['arquivo PDF', 'c1', { formato: 'arquivo', arte: 'data:application/pdf;base64,AAAA', texto: 'a.pdf' }],
  ['peça da galeria', 'c1', { formato: 'peca', arte: FOTO, nome: 'Lasanha', texto: 'Lasanha' }],
  ['figurinha', 'c1', { formato: 'figurinha', arte: FOTO, texto: 'oi' }],
  ['foto em canal sem envio de mídia (Instagram)', 'c2', { formato: 'imagem', arte: FOTO, texto: 'x', nomeArquivo: 'x.png' }],
]) {
  const { api, despachos } = montar()
  chamadas.length = 0
  api.enviarMidia(id, molde)
  await esvaziar()
  confere(`${descricao} avisa e não sai`, chamadas.length === 0 && despachos.length === 1 && despachos[0].tipo === acao.AVISO_API,
    `${despachos.map((d) => d.tipo).join(', ')} / ${chamadas.length} chamada(s)`)
}

// 5. O aviso sobrevive à sincronização e some quando a dona fecha.
{
  let estado = { ...estadoInicial({ conversas: [], catalogo: { cardapio: [], janelas: [], canais: [] }, regras: [] }), sincronizacao: { estado: 'ok', aviso: null } }
  estado = reducer(estado, { tipo: acao.AVISO_API, mensagem: 'Pedido não criado: x' })
  estado = reducer(estado, { tipo: acao.SINCRONIZAR_CONVERSAS, conversas: [] })
  confere('aviso sobrevive à sincronização de 5 s', estado.sincronizacao.aviso === 'Pedido não criado: x', String(estado.sincronizacao.aviso))
  confere('existe a ação de fechar o aviso', typeof acao.FECHAR_AVISO_API === 'string')
  estado = reducer(estado, { tipo: acao.FECHAR_AVISO_API })
  confere('fechar o aviso limpa a faixa', estado.sincronizacao.aviso === null, String(estado.sincronizacao.aviso))
}

if (faltas.length > 0) {
  console.error(`\nprova F06 (honestidade do modo API): ${faltas.length} falha(s)`)
  process.exit(1)
}
console.log('\nprova F06 (honestidade do modo API): ok')
