/* eslint-disable no-console */
// Prova da #2185, pacote de efeitos, parte de tela (relatórios F-2185-efeitos-paridade e E-2185-estado-acao).
// Componentes reais (PainelAtendimento, Composer, BalaoAssistente, PainelConversas, ModalEncerrar, ModalCliente,
// PilhaDeAvisos) com CSS real, em Chromium, sobre o reducer de verdade e ações que anotam as chamadas.
// Cada conferência leva o id do achado (Fxx = Axx do relatório F, Exx = Axx do E).
//
//   F2/F23  digitar e enviar outra coisa não apaga o campo; letras durante o "assumir" ficam
//   F6      Esc no campo de mensagem não fecha o Assistente nem rouba o foco
//   F13/E11 "Usar no rascunho" anexa e não vale com o campo fechado
//   E10     "Ver comanda" em conversa concluída fica desligado com o motivo
//   E08/F32 a despedida do Concluir nasce desmarcada quando não pode sair; modal órfão fecha
//   F9      anexo escolhido sobrevive à troca de conversa
//   F11     Instagram no modo API não oferece gravar áudio
//   F16     Esc e toque no fundo não descartam o que foi digitado
//   F22     "Reabrindo…" é da conversa que pediu
//   E19     conversa sem cadastro não oferece modelo aprovado
//   E18     sugestão proativa não roda com o Assistente respondendo
//   E25     selo "Assistente" só é botão quando há regra para abrir
//   F25/F18 conversa aberta a partir de "Concluídas" ganha destaque; título e chip dizem o mesmo número
//   F26     abrir pelo aviso na tela leva o foco ao campo
//   F28     o aviso na tela espera o ponteiro
//   F31     o fio volta ao fim quando o painel escondido reaparece
//
//   PLAYWRIGHT_MODULE=<pasta do playwright> node ferramentas/prova-2185-efeitos-tela.mjs
import assert from 'node:assert/strict'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'
import react from '@vitejs/plugin-react'

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url)).replaceAll('\\', '/').replace(/\/$/, '')
const pasta = resolve(raiz, '.build/prova-2185-efeitos')
mkdirSync(pasta, { recursive: true })

writeFileSync(resolve(pasta, 'sem-demonstracao.js'), 'export const demonstracao = null\n')
// A marca lê o PNG por `new URL(..., import.meta.url)`, que não existe no IIFE da prova.
writeFileSync(resolve(pasta, 'marca-falsa.jsx'), 'export const Marca = () => null\n')
writeFileSync(resolve(pasta, 'entrada.jsx'), `
import React, { useMemo, useReducer, useState, useSyncExternalStore } from 'react'
import { createRoot } from 'react-dom/client'
import { ContextoAcoes, ContextoCatalogo, ContextoEstado } from '/src/aplicacao/contextos.js'
import { ContextoAcessoModulos } from '/src/aplicacao/acessoModulos.jsx'
import { reducer } from '/src/aplicacao/reducer.js'
import { estadoInicial } from '/src/aplicacao/reducer/estadoInicial.js'
import * as A from '/src/aplicacao/acoes.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { BalaoAssistente } from '/src/features/assistente/BalaoAssistente.jsx'
import { PainelConversas } from '/src/features/caixa-de-entrada/PainelConversas.jsx'
import { ModalEncerrar } from '/src/features/encerramento/ModalEncerrar.jsx'
import { ModalCliente } from '/src/features/ficha-cliente/ModalCliente.jsx'
import { ProvedorDeAviso } from '/src/componentes/Aviso.jsx'
import { PilhaDeAvisos } from '/src/app/PilhaDeAvisos.jsx'
import { Moldura } from '/src/app/Moldura.jsx'
import { ModalNota } from '/src/features/notas/ModalNota.jsx'
import { criarRascunhosDeNotas } from '/src/aplicacao/rascunhosDeNotas.js'
import { BlocoColaborativoIa } from '/src/features/ficha-cliente/AreaColaborativaIa.jsx'
import { identidadeDaMontagem } from '/src/aplicacao/montagemFicha.js'
import { useTamanhoTela } from '/src/hooks/useTamanhoTela.js'
import { CANAIS } from '/src/infra/catalogo.js'
import { REGRAS_PADRAO } from '/src/dominio/automacao.js'
import { TIPOS_DE_AVISO } from '/src/dominio/aviso.js'
import { PAUSA_POR_ASSUMIR } from '/src/dominio/automatico.js'
import { conversasDoBalcao, conversasEncerradasDoBalcao, conversasBloqueadasDoBalcao } from '/src/dominio/conversa.js'
import moldura from '/src/app/moldura.module.css'
import '/src/estilos/base.css'

const q = new URLSearchParams(location.search)
const agora = Date.now()
const noop = () => {}
const chamadas = []
window.chamadas = chamadas
const marcarPainel = (naTela) => chamadas.push(['painelNaTela', naTela])
window.controle = { assumirLento: q.get('lento') === '1', liberar: null }
const h = (min) => new Date(agora - min * 60000).toISOString()
const SESSAO = { usuario: { id: 'u1', nome: 'Eu' }, empresa: { id: 'e1' } }

function conversa(id, extra = {}) {
  return {
    id, nome: 'Cliente ' + id, canal: 'WhatsApp', estado: 'Aberto', conta: 'cliente', clienteId: 'cl-' + id,
    cliente: { tags: [], notas: [], pedidos: 0, telefone: '11999990000' }, pedido: null, passagem: null,
    mensagens: [{ id: 'm0-' + id, dir: 'in', texto: 'Oi, tem lasanha hoje?', em: h(10) }], ultimaEm: h(10), ...extra,
  }
}
const AUTOMATICAS = [
  { id: 'au1', dir: 'out', texto: 'Resposta do agente sem regra', em: h(9), automatica: true, origemAutomatica: 'agente' },
  { id: 'au2', dir: 'out', texto: 'Seu pedido entrou no preparo', em: h(8), automatica: true, regra: 'esteira-preparo' },
]
const MUITAS = Array.from({ length: 40 }, (_, i) => ({ id: 'mm' + i, dir: i % 2 ? 'out' : 'in', texto: 'Mensagem ' + i + ' do fio para rolar', em: h(300 - i) }))
const LISTA = [
  ...Array.from({ length: 4 }, (_, i) => conversa('aberta' + i)),
  ...Array.from({ length: 2 }, (_, i) => conversa('fechada' + i, { estado: 'Encerrado' })),
  conversa('bloq0', { bloqueio: { motivo: 'Calote', em: h(500) } }),
]
const CONVERSAS = [
  conversa('robo'), conversa('meu'), conversa('janela', { mensagens: [{ id: 'mj', dir: 'in', texto: 'Oi', em: h(60 * 72) }], ultimaEm: h(60 * 72) }),
  conversa('lead', { conta: 'lead', clienteId: null }),
  conversa('leadjanela', { conta: 'lead', clienteId: null, mensagens: [{ id: 'mlj', dir: 'in', texto: 'Oi', em: h(60 * 72) }], ultimaEm: h(60 * 72) }),
  conversa('insta', { canal: 'Instagram' }),
  conversa('enc1', { estado: 'Encerrado' }), conversa('enc2', { estado: 'Encerrado' }),
  conversa('auto', { mensagens: [{ id: 'mx', dir: 'in', texto: 'Oi', em: h(12) }, ...AUTOMATICAS] }),
  conversa('longa', { mensagens: MUITAS }),
  ...LISTA,
]
const PAUSADAS = { meu: PAUSA_POR_ASSUMIR, janela: PAUSA_POR_ASSUMIR, lead: PAUSA_POR_ASSUMIR, leadjanela: PAUSA_POR_ASSUMIR, insta: PAUSA_POR_ASSUMIR, auto: PAUSA_POR_ASSUMIR, longa: PAUSA_POR_ASSUMIR }
const memoriaDeNotas = criarRascunhosDeNotas('e1:u1', null)
window.memoriaDeNotas = memoriaDeNotas
const catalogo = { janelas: [], canais: CANAIS, respostasProntas: [], cardapio: [], linhas: [], adicionais: {}, galeria: [], motivosBloqueio: [], prefixosCepAtendidos: [] }

function iniciar() {
  const base = estadoInicial({ conversas: CONVERSAS, catalogo, regras: REGRAS_PADRAO, modoAgente: 'simulado' })
  return { ...base, selecionadaId: q.get('inicial') ?? 'robo', automaticoPausado: PAUSADAS }
}

function Nota() {
  const estadoDasNotas = useSyncExternalStore(memoriaDeNotas.assinar, memoriaDeNotas.ler)
  return estadoDasNotas.rascunhos.map((nota) => (
    <ModalNota key={nota.chave} visivel={estadoDasNotas.ativa === nota.chave} nomeCliente={nota.nome} rascunho={nota}
      aoGuardar={(campo) => memoriaDeNotas.guardar(nota.chave, campo)} aoFechar={() => memoriaDeNotas.pausar()} aoSalvar={async () => true} />
  ))
}

function Cena() {
  const tamanho = useTamanhoTela()
  const [estado, despachar] = useReducer(reducer, null, iniciar)
  const [ocultoNoCelular, setOculto] = useState(false)
  const [avisos, setAvisos] = useState(q.get('aviso') === '1' ? [
    { id: 'av1', tipo: TIPOS_DE_AVISO.MENSAGEM_NOVA, titulo: 'Cliente meu', corpo: 'Oi de novo', conversaId: 'meu' },
    ...(q.get('dois') ? [{ id: 'av2', tipo: TIPOS_DE_AVISO.MENSAGEM_NOVA, titulo: 'Cliente lead', corpo: 'Outro aviso', conversaId: 'lead' }] : []),
  ] : [])
  const [nomeSincronizado, setNomeSincronizado] = useState(null)
  window.sincronizarNome = setNomeSincronizado
  window.trocar = (id) => despachar({ tipo: A.SELECIONAR_CONVERSA, id })
  window.esconder = setOculto
  window.filtrar = (chave, valor) => despachar({ tipo: A.MUDAR_FILTRO, chave, valor })
  window.chegou = () => despachar({ tipo: A.ENVIAR_MENSAGEM, id: estado.selecionadaId, texto: 'Mensagem que chegou com a aba escondida', mensagemId: 'esc-' + Date.now(), agora: Date.now() })
  window.estadoCena = () => estado
  const acoes = useMemo(() => ({
    descarregarRascunho: (d) => despachar({ tipo: A.DEFINIR_RASCUNHO, ...d, doCampo: true }),
    definirRascunho: (id, texto) => despachar({ tipo: A.DEFINIR_RASCUNHO, id, texto }),
    enviar: (id, texto, opcoes = {}) => {
      chamadas.push(['enviar', id, texto])
      despachar({ tipo: A.ENVIAR_MENSAGEM, id, texto, mensagemId: 'msg-' + chamadas.length, agora: Date.now(), ...opcoes })
      return true
    },
    enviarMidia: async (id, molde) => { chamadas.push(['midia', id]); return true },
    assumirAtendimento: async (id) => {
      chamadas.push(['assumir', id])
      if (window.controle.assumirLento) await new Promise((r) => { window.controle.liberar = r })
      despachar({ tipo: A.CONFIRMAR_RESPONSAVEL_API, id, situacao: { pausaApi: PAUSA_POR_ASSUMIR } })
      return true
    },
    devolverAutomatico: noop, selecionar: (id) => { chamadas.push(['selecionar', id]); despachar({ tipo: A.SELECIONAR_CONVERSA, id }) },
    abrirEncerramento: (id) => despachar({ tipo: A.ABRIR_ENCERRAMENTO, id }),
    fecharEncerramento: () => { chamadas.push(['fecharEncerramento']); despachar({ tipo: A.FECHAR_ENCERRAMENTO }) },
    encerrarComResumo: async () => undefined, cancelarPedido: async () => ({ ok: true }),
    reabrir: () => new Promise(() => {}),
    listarAtendentes: async () => [], transferirConversa: async () => ({ ok: true }),
    mudarFiltro: (chave, valor) => despachar({ tipo: A.MUDAR_FILTRO, chave, valor }),
    listarNaoEntregues: async () => [],
    obterLinkCardapio: async (id) => ({ url: 'https://loja.test/c/' + id, numeroComanda: null, daLoja: true }),
    salvarNota: async () => true,
    perguntarAssistente: async () => ({ texto: 'Posso sugerir.', acoes: JSON.parse(window.respostaAssistente ?? '[]') }),
    listarProgramadas: async () => [], programarMensagem: async () => ({ id: 'p1' }), cancelarProgramada: async () => ({}),
    listarModelos: async () => [], carregarClienteDaConversa: async () => true,
    salvarCadastroRapido: async () => ({}), editarDadoCliente: noop,
    concluirLembrete: noop, criarLembrete: noop, marcarLembretesVistos: noop, alternarLoja: noop,
    fecharAvisoApi: noop, pausarAvisos: noop, retomarAvisos: noop,
    pedirNotificacaoDoNavegador: noop, alternarSom: noop,
  }), [])
  const selecionada = estado.conversas.find((c) => c.id === estado.selecionadaId) ?? null
  const usuario = SESSAO.usuario
  const visiveis = conversasDoBalcao(estado.conversas, estado.filtros, agora, estado.automaticoPausado, true, [], null, usuario)
  const ctx = {
    ...estado, selecionada, agora, aberta: true, rascunho: estado.rascunhos[estado.selecionadaId] ?? '', escritaDeFora: estado.escritasDeFora[estado.selecionadaId] ?? 0, horarioDaLoja: null, fonteApi: true, sessao: SESSAO, historico: [],
    visiveis, encerradasDoBalcao: conversasEncerradasDoBalcao(estado.conversas, estado.filtros, usuario),
    bloqueadasDoBalcao: conversasBloqueadasDoBalcao(estado.conversas, estado.filtros, usuario),
    marcarPainelDaConversa: marcarPainel, pagamentosNaoVistos: new Set(), avisosNaTela: avisos, dispensarAviso: (id) => setAvisos((a) => a.filter((x) => x.id !== id)),
    pausarAvisos: () => chamadas.push(['pausarAvisos']), retomarAvisos: () => chamadas.push(['retomarAvisos']), permissaoNotificacao: q.get('dois') ? 'default' : 'granted', fidelidade: { resgatesPorCadastro: {}, config: {}, cupons: [], recompensas: [], sorteios: [] },
    encerrando: estado.ui.encerrando, encerrandoNumero: null,
    lembretes: [], vistos: {}, eventosSonoros: [], consultaLembretes: { carregar: noop },
  }
  const acesso = { permite: () => true, acoes: { reabrirConversa: true, controlarLoja: true, editarRespostas: true }, aoSair: null, notificacoes: null }
  const cena = q.get('cena') ?? 'painel'
  return (
    <ContextoAcessoModulos.Provider value={acesso}><ContextoEstado value={ctx}><ContextoAcoes value={acoes}><ContextoCatalogo value={catalogo}>
      {cena === 'encerrar' && <ProvedorDeAviso><ModalEncerrar conversaId={q.get('conversa') ?? 'robo'} aoFechar={acoes.fecharEncerramento} /></ProvedorDeAviso>}
      {cena === 'ficha' && selecionada && <ModalCliente conversa={nomeSincronizado ? { ...selecionada, nome: nomeSincronizado } : selecionada} aoFechar={() => chamadas.push(['fecharFicha'])} />}
      {cena === 'bloco' && selecionada && (
        <BlocoColaborativoIa
          bloco={{ id: 'b1', geracaoId: 'g1', tipo: 'resposta', acao: 'rascunho', texto: 'Confirmo às 12h.', conversaId: selecionada.id, identidadeSessao: identidadeDaMontagem() }}
          aoRascunho={(texto) => { chamadas.push(['rascunhoDaFicha', texto]); return true }}
        />
      )}
      {cena === 'nota' && <Nota />}
      {cena === 'moldura' && (
        <>
          <Moldura
            tamanho={tamanho} aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} aoAbrirCardapio={noop}
            aoAbrirAutomacoes={noop} aoAbrirGestao={noop} aoAbrirEntregas={noop} aoAbrirAjuda={noop} aoRascunho={noop}
            simulando={false} aoAlternarSimulacoes={null} larguras={{ balcao: 320, ficha: 360, cardapio: 420 }} aoRedimensionar={noop}
            slotEditorNota={null} solicitacaoPainel={null}
          />
          <PilhaDeAvisos />
        </>
      )}
      {cena === 'painel' && (
        <div style={{ display: 'flex', height: '100dvh' }}>
          <section className={moldura.coluna + ' ' + moldura.entrada} style={{ width: 340, flexShrink: 0 }} aria-label="Lista">
            <PainelConversas />
          </section>
          <div hidden={ocultoNoCelular} style={ocultoNoCelular ? undefined : { display: 'contents' }}>
            <section className={moldura.coluna + ' ' + moldura.centro}>
              <PainelAtendimento aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} visivel={!ocultoNoCelular} />
            </section>
          </div>
          <BalaoAssistente sugestaoAgente={null} aoAbrirTela={(tela) => chamadas.push(['abrirTela', tela])} areaDoBalcao={{ tela: 1280, balcao: 340, ficha: 360, colunas: true }} />
          <PilhaDeAvisos />
        </div>
      )}
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos.Provider>
  )
}
createRoot(document.getElementById('root')).render(<Cena />)
`)

process.env.VITE_FONTE_DADOS = 'api'
const compilado = await build({
  root: raiz, configFile: false, logLevel: 'silent',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [react()],
  // O reducer importa a porta da demonstração (await de módulo). Na prova ela é sempre nula, como no modo API.
  resolve: { alias: [
    { find: /^.*\/infra\/demonstracao(\.js)?$/, replacement: resolve(pasta, 'sem-demonstracao.js') },
    { find: /^.*\/componentes\/Marca(\.jsx)?$/, replacement: resolve(pasta, 'marca-falsa.jsx') },
  ] },
  build: { write: false, minify: false, cssCodeSplit: false, lib: { entry: '.build/prova-2185-efeitos/entrada.jsx', formats: ['iife'], name: 'Prova' } },
})
const saidas = (Array.isArray(compilado) ? compilado[0] : compilado).output
const codigo = saidas.find((a) => a.type === 'chunk').code
const estilo = String(saidas.find((a) => a.type === 'asset' && a.fileName.endsWith('.css'))?.source ?? '')
const pagina = `<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>${estilo}</style><div id="root"></div><script>${codigo.replaceAll('</script', '<\\/script')}</script></html>`

const ORIGEM = 'http://prova.local'
const navegador = await chromium.launch({ headless: true })
const erros = []
let passou = 0
const confere = async (descricao, fn) => {
  if (process.env.FILTRO && !descricao.includes(process.env.FILTRO)) return
  try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) {
    erros.push(descricao)
    console.log(`FALHA ${descricao}\n      ${String(erro.message).split('\n').filter(Boolean).slice(0, 4).join(' ')}`)
  }
}

async function abrir(consulta = '', { width = 1280, height = 760, toque = false } = {}) {
  const ctx = await navegador.newContext({ viewport: { width, height }, reducedMotion: 'reduce', serviceWorkers: 'block', hasTouch: toque, isMobile: toque })
  await ctx.addInitScript(() => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova', expiraEm: Date.now() + 3600000, empresa: { id: 'e1' }, usuario: { id: 'u1', nome: 'Eu' } }))
  })
  const rede = { sugestoes: 0 }
  await ctx.route(ORIGEM + '/**', (rota) => {
    const url = new URL(rota.request().url())
    if (!url.pathname.startsWith('/api/')) return rota.fulfill({ contentType: 'text/html', body: pagina })
    if (url.pathname.endsWith('/sugestao')) {
      rede.sugestoes += 1
      return rota.fulfill({ json: { success: true, data: { texto: 'Vou verificar o horário de retirada para você.' } } })
    }
    return rota.fulfill({ json: { success: true, data: [] } })
  })
  const p = await ctx.newPage()
  p.setDefaultTimeout(7000)
  p.on('pageerror', (e) => erros.push('pageerror: ' + e.message))
  p.on('console', (m) => { if (m.type() === 'error') erros.push('console: ' + m.text().slice(0, 300)) })
  await p.goto(`${ORIGEM}/?${consulta}`)
  return { ctx, p, rede }
}
const campo = (p) => p.getByLabel('Mensagem para o cliente', { exact: true })
const chamadasDe = (p) => p.evaluate(() => window.chamadas.slice())
const esperarPainel = async (p) => { await campo(p).waitFor() }
const respostaAssistente = (p, acoes) => p.evaluate((a) => { window.respostaAssistente = JSON.stringify(a) }, acoes)

try {
  // --- F2: o texto digitado sobrevive ao envio do cardápio --------------------------------------------
  await confere('F2. enviar o cardápio pelo menu não apaga o que ela estava digitando', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await campo(p).fill('Bom dia, Maria, já confirmo')
    await p.getByRole('button', { name: 'Mais opções da conversa', exact: true }).click()
    await p.getByRole('menuitem', { name: 'Enviar cardápio' }).click()
    await p.getByRole('button', { name: 'Gerar e enviar' }).click()
    await p.getByRole('dialog').waitFor({ state: 'detached', timeout: 4000 })
    const envios = (await chamadasDe(p)).filter((c) => c[0] === 'enviar')
    assert.equal(envios.length, 1)
    assert.match(envios[0][2], /loja\.test\/c\/meu/)
    assert.equal(await campo(p).inputValue(), 'Bom dia, Maria, já confirmo')
    await ctx.close()
  })
  await confere('F2. enviar o próprio texto pelo campo esvazia o campo', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await campo(p).fill('Segue o link')
    await campo(p).press('Enter')
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'enviar'))
    assert.equal(await campo(p).inputValue(), '')
    await ctx.close()
  })

  // --- F23: letras digitadas enquanto o servidor assume --------------------------------------------------------
  await confere('F23. o que ela digita enquanto o assumir espera fica no campo; só o texto enviado sai', async () => {
    const { ctx, p } = await abrir('inicial=robo&lento=1')
    await esperarPainel(p)
    await campo(p).fill('Primeira mensagem')
    await campo(p).press('Enter')
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'assumir'))
    await campo(p).pressSequentially(' e outra coisa', { delay: 5 })
    await p.evaluate(() => window.controle.liberar())
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'enviar'))
    const envios = (await chamadasDe(p)).filter((c) => c[0] === 'enviar')
    assert.deepEqual(envios.map((c) => c[2]), ['Primeira mensagem'])
    await p.waitForFunction(() => document.querySelector('textarea')?.value === 'e outra coisa')
    assert.equal(await campo(p).inputValue(), 'e outra coisa')
    await ctx.close()
  })

  // --- F6: Esc no campo de mensagem ---------------------------------------------------------------------------
  await confere('F6. Esc no campo de mensagem não fecha o Assistente nem tira o foco do campo', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
    await p.getByRole('dialog', { name: /Assistente/ }).waitFor()
    await campo(p).click()
    await campo(p).press('Escape')
    assert.equal(await p.getByRole('dialog', { name: /Assistente/ }).isVisible(), true, 'o Assistente segue aberto')
    assert.equal(await campo(p).evaluate((el) => document.activeElement === el), true, 'o foco continua no campo')
    await ctx.close()
  })
  await confere('F6. Esc dentro do painel do Assistente continua fechando e devolve o foco ao gatilho', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
    const pergunta = p.getByLabel('Pergunte ao assistente sobre esta conversa')
    await pergunta.waitFor()
    await pergunta.press('Escape')
    await p.getByRole('dialog', { name: /Assistente/ }).waitFor({ state: 'detached' })
    assert.equal(await p.getByRole('button', { name: 'Abrir assistente', exact: true }).evaluate((el) => document.activeElement === el), true)
    await ctx.close()
  })

  // --- F13 / E11 / E10: ações propostas pelo Assistente --------------------------------------------------------
  const pedirAoAssistente = async (p, acoes) => {
    await respostaAssistente(p, acoes)
    await p.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
    const pergunta = p.getByLabel('Pergunte ao assistente sobre esta conversa')
    await pergunta.fill('sugira')
    await pergunta.press('Control+Enter')
  }
  await confere('F13. "Usar no rascunho" soma ao que ela já escreveu', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await campo(p).fill('Oi Maria,')
    await pedirAoAssistente(p, [{ tipo: 'rascunho', texto: 'Confirmo às 12h.' }])
    await p.getByRole('button', { name: 'Usar no rascunho', exact: true }).click()
    await p.waitForFunction(() => document.querySelector('textarea[id]')?.value?.includes('Confirmo'))
    assert.equal(await campo(p).inputValue(), 'Oi Maria, Confirmo às 12h.')
    await ctx.close()
  })
  await confere('E11. com o campo fechado (janela de 24 h) "Usar no rascunho" nasce desligado, com o motivo', async () => {
    const { ctx, p } = await abrir('inicial=janela')
    await campo(p).waitFor()
    await pedirAoAssistente(p, [{ tipo: 'rascunho', texto: 'Confirmo às 12h.' }])
    const botao = p.getByRole('button', { name: 'Usar no rascunho', exact: true })
    await botao.waitFor()
    assert.equal(await botao.isDisabled(), true)
    const id = await botao.getAttribute('aria-describedby')
    assert.match(await p.locator(`[id="${id}"]`).innerText(), /Janela|janela/)
    await ctx.close()
  })
  await confere('E10. "Abrir cardápio" em conversa concluída fica desligado com o motivo', async () => {
    const { ctx, p } = await abrir('inicial=enc1')
    await p.getByText('Conversa concluída').waitFor()
    await pedirAoAssistente(p, [{ tipo: 'abrir_tela', tela: 'cardapio' }])
    const botao = p.getByRole('button', { name: 'Abrir cardápio', exact: true })
    await botao.waitFor()
    assert.equal(await botao.isDisabled(), true)
    const id = await botao.getAttribute('aria-describedby')
    assert.equal(await p.locator(`[id="${id}"]`).innerText(), 'Conversa concluída: não há comanda para montar.')
    await ctx.close()
  })

  // --- E08 / F32: modal Concluir --------------------------------------------------------------------------------
  await confere('E08. despedida nasce marcada quando a mensagem pode sair', async () => {
    const { ctx, p } = await abrir('cena=encerrar&conversa=meu&inicial=meu')
    const caixa = p.getByRole('checkbox', { name: 'Mandar mensagem de encerramento ao cliente' })
    await caixa.waitFor()
    assert.equal(await caixa.isChecked(), true)
    assert.equal(await caixa.isDisabled(), false)
    await ctx.close()
  })
  await confere('E08. janela de 24 h fechada: despedida nasce desmarcada, desligada e com o motivo', async () => {
    const { ctx, p } = await abrir('cena=encerrar&conversa=janela&inicial=janela')
    const caixa = p.getByRole('checkbox', { name: 'Mandar mensagem de encerramento ao cliente' })
    await caixa.waitFor()
    assert.equal(await caixa.isChecked(), false)
    assert.equal(await caixa.isDisabled(), true)
    assert.match(await p.locator('#sem-despedida').innerText(), /A despedida não pode sair agora\./)
    assert.equal(await p.getByLabel('Mensagem de encerramento', { exact: true }).count(), 0, 'nenhum campo de texto à toa')
    await ctx.close()
  })
  await confere('F32. conversa que não existe mais fecha o modal em vez de ficar marcada como aberta', async () => {
    const { ctx, p } = await abrir('cena=encerrar&conversa=inexistente&inicial=meu')
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'fecharEncerramento'), null, { timeout: 4000 })
    await p.getByText('Esta conversa já não está aberta.').waitFor({ timeout: 3000 })
    await ctx.close()
  })

  // --- F9: anexo sobrevive à troca de conversa ------------------------------------------------------------------------
  await confere('F9. a foto escolhida continua na prévia depois de trocar de conversa e voltar', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64')
    await p.locator('input[type="file"]').setInputFiles({ name: 'bolo.png', mimeType: 'image/png', buffer: png })
    await p.getByText('bolo.png', { exact: true }).waitFor()
    await p.evaluate(() => window.trocar('robo'))
    await p.getByText('Assistente respondendo').first().waitFor()
    assert.equal(await p.getByText('bolo.png', { exact: true }).count(), 0, 'a prévia é da outra conversa')
    await p.evaluate(() => window.trocar('meu'))
    await p.getByText('bolo.png', { exact: true }).waitFor({ timeout: 3000 })
    await ctx.close()
  })

  const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64')
  const midias = async (p) => (await chamadasDe(p)).filter((c) => c[0] === 'midia').length
  await confere('F23. foto com legenda: o que ela digita durante o assumir fica no campo', async () => {
    const { ctx, p } = await abrir('inicial=robo&lento=1')
    await esperarPainel(p)
    await p.locator('input[type="file"]').setInputFiles({ name: 'bolo.png', mimeType: 'image/png', buffer: PNG })
    await p.getByText('bolo.png', { exact: true }).waitFor()
    await campo(p).fill('Legenda')
    await campo(p).press('Enter')
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'assumir'))
    await campo(p).pressSequentially(' e mais', { delay: 5 })
    await p.evaluate(() => window.controle.liberar())
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'midia'))
    await p.waitForFunction(() => document.querySelector('textarea')?.value === 'e mais')
    assert.equal(await midias(p), 1)
    await ctx.close()
  })
  await confere('F23. foto removida enquanto o assumir espera não sai', async () => {
    const { ctx, p } = await abrir('inicial=robo&lento=1')
    await esperarPainel(p)
    await p.locator('input[type="file"]').setInputFiles({ name: 'bolo.png', mimeType: 'image/png', buffer: PNG })
    await p.getByText('bolo.png', { exact: true }).waitFor()
    await campo(p).press('Enter')
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'assumir'))
    await p.getByRole('button', { name: /^Remover/ }).first().click()
    await p.getByText('bolo.png', { exact: true }).waitFor({ state: 'detached' })
    await p.evaluate(() => window.controle.liberar())
    await p.waitForTimeout(250)
    assert.equal(await midias(p), 0)
    await ctx.close()
  })

  // --- F11: microfone ---------------------------------------------------------------------------------------------------
  await confere('F11. Instagram no modo API não oferece gravar áudio; WhatsApp oferece', async () => {
    const { ctx, p } = await abrir('inicial=insta')
    await esperarPainel(p)
    assert.equal(await p.getByRole('button', { name: 'Gravar áudio' }).count(), 0)
    await p.evaluate(() => window.trocar('meu'))
    await p.getByRole('button', { name: 'Gravar áudio' }).waitFor()
    await ctx.close()
  })

  // --- F16: janelas não perdem o que foi digitado --------------------------------------------------------------------------
  await confere('F16. Programar mensagem: Esc e toque no fundo com texto mexido não fecham; Fechar fecha', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    await p.getByRole('menuitem', { name: 'Programar' }).click()
    const janela = p.getByRole('dialog', { name: 'Programar mensagem' })
    await janela.waitFor()
    await janela.locator('textarea').first().fill('Lembrete da entrega de amanhã')
    await p.keyboard.press('Escape')
    assert.equal(await janela.isVisible(), true, 'Esc não descarta')
    await p.mouse.click(4, 4)
    assert.equal(await janela.isVisible(), true, 'o toque no fundo não descarta')
    await janela.getByRole('button', { name: 'Fechar', exact: true }).click()
    await janela.waitFor({ state: 'detached' })
    await ctx.close()
  })
  await confere('F16. Programar mensagem sem nada digitado ainda fecha com Esc', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: 'Mais ferramentas', exact: true }).click()
    await p.getByRole('menuitem', { name: 'Programar' }).click()
    const janela = p.getByRole('dialog', { name: 'Programar mensagem' })
    await janela.waitFor()
    await p.keyboard.press('Escape')
    await janela.waitFor({ state: 'detached' })
    await ctx.close()
  })
  await confere('F16. Ficha: cadastro rápido pela metade não fecha com Esc; sem digitar, fecha', async () => {
    const { ctx, p } = await abrir('cena=ficha&inicial=lead')
    const janela = p.getByRole('dialog', { name: 'Cadastro do cliente' }) // #2437: a janela da ficha é "Cadastro do cliente" desde a #2260
    await janela.waitFor()
    await janela.getByLabel('Endereço', { exact: true }).first().fill('Rua das Flores, 120, Centro')
    await p.keyboard.press('Escape')
    assert.equal(await janela.isVisible(), true, 'a digitação não se perde')
    assert.deepEqual((await chamadasDe(p)).filter((c) => c[0] === 'fecharFicha'), [])
    await ctx.close()
    const outra = await abrir('cena=ficha&inicial=lead')
    const fresca = outra.p.getByRole('dialog', { name: 'Cadastro do cliente' })
    await fresca.waitFor()
    await outra.p.keyboard.press('Escape')
    await outra.p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'fecharFicha'))
    await outra.ctx.close()
  })
  await confere('F16. Ficha: dado que chega da sincronização sem ninguém digitar não segura a janela', async () => {
    const { ctx, p } = await abrir('cena=ficha&inicial=lead')
    const janela = p.getByRole('dialog', { name: 'Cadastro do cliente' })
    await janela.waitFor()
    await p.evaluate(() => window.sincronizarNome('Nome que veio do WhatsApp'))
    await janela.getByText('Nome que veio do WhatsApp').first().waitFor()
    await janela.getByLabel('Endereço', { exact: true }).first().waitFor()
    await p.keyboard.press('Escape')
    await p.waitForTimeout(200)
    assert.deepEqual((await chamadasDe(p)).filter((c) => c[0] === 'fecharFicha'), [['fecharFicha']])
    await ctx.close()
  })

  // --- F22: "Reabrindo…" ------------------------------------------------------------------------------------------------------
  await confere('F22. "Reabrindo…" é da conversa que pediu; a outra concluída segue com o botão ligado', async () => {
    const { ctx, p } = await abrir('inicial=enc1')
    await p.getByRole('button', { name: 'Reabrir conversa' }).click()
    await p.getByRole('button', { name: 'Reabrindo…' }).waitFor()
    await p.evaluate(() => window.trocar('enc2'))
    const outro = p.getByRole('button', { name: 'Reabrir conversa' })
    await outro.waitFor()
    assert.equal(await outro.isDisabled(), false)
    await p.evaluate(() => window.trocar('enc1'))
    await p.getByRole('button', { name: 'Reabrindo…' }).waitFor()
    await ctx.close()
  })

  // --- E19 ------------------------------------------------------------------------------------------------------------------------
  await confere('E19. conversa sem cadastro com a janela fechada não oferece modelo aprovado; com cadastro oferece', async () => {
    const { ctx, p } = await abrir('inicial=leadjanela')
    await campo(p).waitFor()
    assert.equal(await campo(p).getAttribute('placeholder'), 'Mensagem indisponível nesta conversa')
    await p.evaluate(() => window.trocar('janela'))
    await p.waitForFunction(() => document.querySelector('textarea[id]')?.placeholder?.startsWith('Janela fechada'))
    await ctx.close()
  })

  // --- E18 --------------------------------------------------------------------------------------------------------------------------
  await confere('E18. sugestão proativa não roda com o Assistente respondendo; roda quando a conversa é dela', async () => {
    const { ctx, p, rede } = await abrir('inicial=robo')
    await esperarPainel(p)
    await p.waitForTimeout(2600)
    assert.equal(rede.sugestoes, 0)
    assert.equal(await p.getByRole('button', { name: 'Usar no rascunho', exact: true }).count(), 0)
    await p.evaluate(() => window.trocar('meu'))
    await p.getByRole('button', { name: 'Usar no rascunho', exact: true }).waitFor({ timeout: 6000 })
    assert.equal(rede.sugestoes, 1)
    await ctx.close()
  })

  // --- E25 --------------------------------------------------------------------------------------------------------------------------
  await confere('E25. selo "Assistente" só é botão quando há regra para abrir', async () => {
    const { ctx, p } = await abrir('inicial=auto')
    await p.getByText('Resposta do agente sem regra').waitFor()
    const fio = p.getByRole('log', { name: 'Mensagens da conversa' })
    const comoBotao = await fio.getByRole('button', { name: /Assistente/ }).count()
    assert.equal(comoBotao, 1, 'só a mensagem da regra esteira-preparo')
    assert.equal(await fio.getByText('Assistente', { exact: true }).count(), 2, 'as duas mostram o selo')
    await ctx.close()
  })

  // --- F25 / F18 ---------------------------------------------------------------------------------------------------------------------
  await confere('F25. conversa aberta a partir de "Concluídas" ganha o destaque da selecionada', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: /^Todas/ }).click() // #2437: a lista abre em "Precisa de você"; "Concluídas" só existe em "Todas"
    await p.getByRole('button', { name: /^Concluídas/ }).click()
    await p.getByText('Cliente fechada0').first().click()
    await p.waitForFunction(() => document.querySelector('[aria-current="true"]') !== null)
    assert.match(await p.locator('[aria-current="true"]').first().innerText(), /fechada0/)
    await ctx.close()
  })
  await confere('F18. chip "Todas" conta só as abertas, sem encerradas nem bloqueadas; o título não repete o número', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    const abertas = await p.evaluate(() => window.estadoCena().conversas.filter((c) => c.estado !== 'Encerrado' && !c.bloqueio).length)
    // #2437: o título deixou de trazer número na #2232 (L1); o total fica só no chip "Todas".
    assert.equal((await p.locator('h2', { hasText: 'Conversas' }).first().innerText()).replace(/\D/g, ''), '')
    assert.equal((await p.getByRole('button', { name: /^Todas/ }).innerText()).replace(/\D/g, ''), String(abertas))
    await ctx.close()
  })

  // --- F26 / F28 --------------------------------------------------------------------------------------------------------------------------
  await confere('F26. abrir pelo aviso na tela leva a conversa e o foco ao campo de mensagem', async () => {
    const { ctx, p } = await abrir('inicial=robo&aviso=1')
    await esperarPainel(p)
    assert.equal(await p.evaluate(() => document.activeElement?.tagName), 'BODY', 'antes de abrir o foco não está no campo')
    // O aviso na tela, não o cartão de mesmo nome na lista.
    await p.getByRole('status', { name: 'Avisos do atendimento' }).getByRole('button', { name: 'Cliente meu Oi de novo' }).click()
    await p.waitForFunction(() => document.activeElement?.tagName === 'TEXTAREA' && document.activeElement.labels?.[0]?.textContent === 'Mensagem para o cliente')
    assert.match(await p.locator('h2[data-foco-reserva]').innerText(), /Cliente meu/)
    await ctx.close()
  })

  await confere('F28. ponteiro ou foco em cima do aviso na tela pausa o relógio; sair retoma', async () => {
    const { ctx, p } = await abrir('inicial=robo&aviso=1')
    await esperarPainel(p)
    const aviso = p.getByRole('status', { name: 'Avisos do atendimento' }).getByRole('button', { name: 'Cliente meu Oi de novo' })
    const eventos = async () => (await chamadasDe(p)).map((c) => c[0]).filter((n) => n.endsWith('Avisos'))
    await aviso.hover()
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'pausarAvisos'))
    await p.mouse.move(2, 2)
    await p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'retomarAvisos'))
    assert.deepEqual(await eventos(), ['pausarAvisos', 'retomarAvisos'])
    await aviso.focus()
    await p.waitForFunction(() => window.chamadas.filter((c) => c[0] === 'pausarAvisos').length === 2)
    await p.getByRole('searchbox').focus()
    await p.waitForFunction(() => window.chamadas.filter((c) => c[0] === 'retomarAvisos').length === 2)
    await ctx.close()
  })
  await confere('F28. clicar na pilha com o mouse não deixa os avisos presos: tirar o ponteiro retoma', async () => {
    const { ctx, p } = await abrir('inicial=robo&aviso=1&dois=1')
    await esperarPainel(p)
    const pilha = p.getByRole('status', { name: 'Avisos do atendimento' })
    await pilha.getByRole('button', { name: 'Avisar também com o console minimizado' }).click()
    await p.mouse.move(2, 2)
    await p.waitForTimeout(300)
    const eventos = (await chamadasDe(p)).map((c) => c[0]).filter((n) => n.endsWith('Avisos'))
    assert.deepEqual(eventos.slice(0, 1), ['pausarAvisos'], 'o ponteiro pausou')
    assert.equal(eventos.at(-1), 'retomarAvisos', 'o foco do clique não segura a pilha')
    await ctx.close()
  })

  // --- F31 --------------------------------------------------------------------------------------------------------------------------------------
  await confere('F31. o fio escondido (outra aba) volta ao fim quando o painel reaparece', async () => {
    const { ctx, p } = await abrir('inicial=longa', { width: 390, height: 780 })
    await esperarPainel(p)
    const noFim = () => p.evaluate(() => {
      const log = document.querySelector('[role="log"]')
      let el = log.parentElement
      while (el && !/auto|scroll/.test(getComputedStyle(el).overflowY)) el = el.parentElement
      return el.scrollHeight - el.scrollTop - el.clientHeight
    })
    await p.waitForTimeout(200)
    assert.ok(await noFim() < 40, 'abre no fim')
    await p.evaluate(() => window.esconder(true))
    await p.evaluate(() => window.chegou())
    await p.evaluate(() => window.esconder(false))
    await p.waitForTimeout(150)
    assert.ok(await noFim() < 40, 'depois de voltar, no fim: ' + await noFim())
    await ctx.close()
  })

  // --- E11 na Ficha ---------------------------------------------------------------------------------------------
  await confere('E11. Ficha: "Usar no rascunho" fica desligado com o campo fechado e ligado com o campo aberto', async () => {
    const fechado = await abrir('cena=bloco&inicial=janela')
    const botao = fechado.p.getByRole('button', { name: 'Usar no rascunho', exact: true })
    await botao.waitFor()
    assert.equal(await botao.isDisabled(), true)
    const id = await botao.getAttribute('aria-describedby')
    assert.match(await fechado.p.locator(`[id="${id}"]`).innerText(), /Janela|janela/)
    await fechado.ctx.close()
    const aberto = await abrir('cena=bloco&inicial=meu')
    const livre = aberto.p.getByRole('button', { name: 'Usar no rascunho', exact: true })
    await livre.waitFor()
    assert.equal(await livre.isDisabled(), false)
    await livre.click()
    await aberto.p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'rascunhoDaFicha'))
    await aberto.ctx.close()
  })

  // --- F29 -------------------------------------------------------------------------------------------------------
  await confere('F29. nota proposta pela IA entra no editor já aberto, somada ao que ela tinha escrito', async () => {
    const { ctx, p } = await abrir('cena=nota&inicial=meu')
    await p.evaluate(() => window.memoriaDeNotas.abrir('meu', 'Cliente meu', ''))
    const editor = p.getByLabel('Texto da nota de Cliente meu')
    await editor.waitFor()
    await editor.fill('Cliente pediu sem cebola.')
    await p.evaluate(() => window.memoriaDeNotas.abrir('meu', 'Cliente meu', 'Alérgico a camarão.'))
    await p.waitForFunction(() => document.querySelector('textarea')?.value.includes('camarão'))
    assert.equal(await editor.inputValue(), 'Cliente pediu sem cebola.' + String.fromCharCode(10, 10) + 'Alérgico a camarão.')
    await ctx.close()
  })

  // --- Moldura (celular): F8, E29 e a contagem da aba ---------------------------------------------------
  const celular = { width: 390, height: 780 }
  const aba = (p, nome) => p.getByRole('tab', { name: nome, exact: nome !== 'Conversas' })
  await confere('F8. no celular, abrir a conversa pelo aviso na tela leva à aba Conversa', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=nenhuma&aviso=1', celular)
    await aba(p, 'Conversas').waitFor()
    assert.equal(await aba(p, 'Conversas').getAttribute('aria-selected'), 'true')
    await p.getByRole('status', { name: 'Avisos do atendimento' }).getByRole('button', { name: 'Cliente meu Oi de novo' }).click()
    await p.waitForFunction(() => [...document.querySelectorAll('[role="tab"]')].find((t) => /^Mensagens$/.test(t.textContent.trim()))?.getAttribute('aria-selected') === 'true')
    await ctx.close()
  })
  await confere('F24. no celular, o painel da conversa só conta como à vista na aba Conversa', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=meu', celular)
    await aba(p, 'Conversas').waitFor()
    const naTela = async () => (await chamadasDe(p)).filter((c) => c[0] === 'painelNaTela').at(-1)?.[1]
    assert.equal(await naTela(), false)
    await aba(p, 'Mensagens').click() // #2437: a aba da conversa se chama "Mensagens" desde a #2232
    await p.waitForFunction(() => window.chamadas.filter((c) => c[0] === 'painelNaTela').at(-1)?.[1] === true)
    await aba(p, 'Conversas').click()
    await p.waitForFunction(() => window.chamadas.filter((c) => c[0] === 'painelNaTela').at(-1)?.[1] === false)
    await ctx.close()
  })
  await confere('F24. no computador, a conversa está sempre à vista com a moldura montada', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=meu')
    await p.getByRole('navigation', { name: 'Navegação' }).waitFor()
    assert.equal((await chamadasDe(p)).filter((c) => c[0] === 'painelNaTela').at(-1)?.[1], true)
    await ctx.close()
  })
  await confere('F18. a aba "Conversas" do celular mostra o mesmo número do chip "Precisa de você" (#2232 L11)', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=nenhuma', celular)
    await aba(p, 'Conversas').waitFor()
    // #2437: desde a #2232 (L11) a aba conta o que pede ação, o mesmo número do chip "Precisa de você", e não o total de abertas.
    const precisam = (await p.getByRole('button', { name: /^Precisa de você/ }).innerText()).replace(/\D/g, '')
    assert.ok(Number(precisam) > 0)
    assert.equal((await aba(p, 'Conversas').innerText()).replace(/[^0-9]/g, ''), precisam)
    await p.evaluate(() => window.filtrar('aba', 'todas'))
    await p.waitForTimeout(100)
    assert.equal((await aba(p, 'Conversas').innerText()).replace(/[^0-9]/g, ''), precisam, 'trocar a aba da lista não muda o número da aba do celular')
    await ctx.close()
  })
  await confere('E29. no modo API o F2 não é capturado (não há Simular)', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=nenhuma')
    await p.getByRole('navigation', { name: 'Navegação' }).waitFor()
    const impediu = await p.evaluate(() => {
      const evento = new KeyboardEvent('keydown', { key: 'F2', cancelable: true, bubbles: true })
      window.dispatchEvent(evento)
      return evento.defaultPrevented
    })
    assert.equal(impediu, false)
    await ctx.close()
  })
} finally {
  await navegador.close()
}

const semPagina = [...new Set(erros.filter((e) => e.startsWith('pageerror') || e.startsWith('console')))]
console.log(`\nprova-2185-efeitos-tela: ${passou} conferências ok, ${erros.length} falha(s)${semPagina.length ? `\n  ${semPagina.slice(0, 6).join('\n  ')}` : ''}`)
if (erros.length) process.exit(1)
