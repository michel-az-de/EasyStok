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
//   E19     (#3206) conversa sem cadastro também oferece a mensagem padrão
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
import { ContextoRascunhosDosAjustes, useGravacoesDosAjustes } from '/src/aplicacao/rascunhosDosAjustes.js'
import { reducer } from '/src/aplicacao/reducer.js'
import { estadoInicial } from '/src/aplicacao/reducer/estadoInicial.js'
import * as A from '/src/aplicacao/acoes.js'
import { PainelAtendimento } from '/src/features/atendimento/PainelAtendimento.jsx'
import { BalaoAssistente } from '/src/features/assistente/BalaoAssistente.jsx'
import { PainelConversas } from '/src/features/caixa-de-entrada/PainelConversas.jsx'
import { ModalEncerrar } from '/src/features/encerramento/ModalEncerrar.jsx'
import { ModalCliente } from '/src/features/ficha-cliente/ModalCliente.jsx'
import { ModalGestao } from '/src/features/gestao/ModalGestao.jsx'
import { ProvedorDeAviso } from '/src/componentes/Aviso.jsx'
import { PilhaDeAvisos } from '/src/app/PilhaDeAvisos.jsx'
import { Moldura } from '/src/app/Moldura.jsx'
import { tokensDaGeometria } from '/src/dominio/paineis.js'
import { ModalNota } from '/src/features/notas/ModalNota.jsx'
import { criarRascunhosDeNotas } from '/src/aplicacao/rascunhosDeNotas.js'
import { BlocoColaborativoIa } from '/src/features/ficha-cliente/AreaColaborativaIa.jsx'
import { identidadeDaMontagem } from '/src/aplicacao/montagemFicha.js'
import { chaveDaAssistencia } from '/src/aplicacao/contextoAssistencia.js'
import { useTamanhoTela } from '/src/hooks/useTamanhoTela.js'
import { largurasDaTela } from '/src/hooks/larguras.js'
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
  // #3234 (E31): cabecalhoCheio=1 dá a 'meu' nome longo e comanda, o pior caso da linha do cabeçalho.
  // #3234 (E32): falhas=1 dá a 'meu' quatro envios seguidos que esgotaram o tempo, como na produção.
  conversa('robo'), conversa('meu', {
    ...(q.get('cabecalhoCheio') === '1' ? { nome: 'Maria Aparecida dos Santos Albuquerque', numeroComanda: 4821 } : {}),
    ...(q.get('falhas') === '1' ? { mensagens: [{ id: 'm0-meu', dir: 'in', texto: 'Oi, tem lasanha hoje?', em: h(10) }, ...[9, 8, 7, 6].map((min, i) => ({ id: 'mf' + i, dir: 'out', texto: 'Tem sim, separo uma?', status: 'falhou', erro: 'The operation was canceled. Envio incerto.', em: h(min) }))] } : {}),
    // falhas=2: uma recusa seguida de um tempo esgotado com o mesmo texto; reenviar a recusa a deixa igual à vizinha.
    ...(q.get('falhas') === '2' ? { mensagens: [{ id: 'm0-meu', dir: 'in', texto: 'Oi, tem lasanha hoje?', em: h(10) }, { id: 'rf0', dir: 'out', texto: 'Tem sim, separo uma?', status: 'falhou', erro: 'Não enviada: Sem conexão', em: h(9) }, { id: 'rf1', dir: 'out', texto: 'Tem sim, separo uma?', status: 'falhou', erro: 'The operation was canceled. Envio incerto.', em: h(8) }] } : {}),
    // falhas=3 (A2b): o caso da produção, quatro envios do mesmo modelo, com o mesmo texto, que esgotaram o tempo.
    // O texto é o que o servidor grava: cabeçalho, corpo e rodapé por quebra de linha.
    ...(q.get('falhas') === '3' ? { mensagens: [{ id: 'm0-meu', dir: 'in', texto: 'Oi, tem lasanha hoje?', em: h(10) }, ...[9, 8, 7, 6].map((min, i) => ({ id: 'mm' + i, dir: 'out', texto: 'Pedido 21 pronto\\nOi Ana, seu pedido 21 está pronto para retirar.\\nEasyStok', modelo: true, modeloNome: 'pedido_pronto', status: 'falhou', erro: 'The operation was canceled. Envio incerto.', em: h(min) }))] } : {}),
  }), conversa('janela', { mensagens: [{ id: 'mj', dir: 'in', texto: 'Oi', em: h(60 * 72) }], ultimaEm: h(60 * 72) }),
  conversa('lead', { conta: 'lead', clienteId: null }),
  conversa('leadjanela', { conta: 'lead', clienteId: null, mensagens: [{ id: 'mlj', dir: 'in', texto: 'Oi', em: h(60 * 72) }], ultimaEm: h(60 * 72) }),
  conversa('insta', { canal: 'Instagram' }),
  conversa('enc1', { estado: 'Encerrado' }), conversa('enc2', { estado: 'Encerrado' }),
  conversa('auto', { mensagens: [{ id: 'mx', dir: 'in', texto: 'Oi', em: h(12) }, ...AUTOMATICAS] }),
  conversa('longa', { mensagens: MUITAS }),
  ...LISTA,
]
// #3234 (E33): passagem=1 dá a 'meu' uma transferência com motivo, que continua na faixa abaixo do cabeçalho.
// #3374 (E33): respondida=1 põe a última fala na loja em 'robo' (Assistente) e 'meu' (atendente). Sem cliente esperando
// não há motivo à vista (#3318), e o modo sobe para o selo do cabeçalho.
if (q.get('respondida') === '1') {
  for (const [id, extra] of [['robo', { automatica: true, origemAutomatica: 'agente' }], ['meu', {}]]) {
    const c = CONVERSAS.find((x) => x.id === id)
    c.mensagens = [...c.mensagens, { id: 'resp-' + id, dir: 'out', texto: 'Temos sim, sai em 20 min.', em: h(5), ...extra }]
    c.ultimaEm = h(5)
  }
}
if (q.get('passagem') === '1') CONVERSAS.find((c) => c.id === 'meu').passagem = { motivo: 'Restrição alimentar: alergia a camarão', em: null, assumida: false }
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
  // #3265 (C6): a moldura recebe as larguras da regra de produção (e as salvas, por ?larguras=), não números fixos.
  const larguras = largurasDaTela(JSON.parse(q.get('larguras') ?? '{}'), window.innerWidth)
  const [gestaoAberta, abrirGestao] = useState(false)
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
  // #3234 (E32): a atendente manda o mesmo texto de novo e ele esgota o tempo igual.
  window.falhouIgual = () => {
    const mensagemId = 'mf-mais-' + Date.now()
    despachar({ tipo: A.ENVIAR_MENSAGEM, id: estado.selecionadaId, texto: 'Tem sim, separo uma?', mensagemId, agora: Date.now() })
    despachar({ tipo: A.FALHAR_ENVIO_API, id: estado.selecionadaId, mensagemId, erro: 'The operation was canceled. Envio incerto.' })
  }
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
    // #3234 (E32): com falhas, o fio ganha Reenviar (que esgota o tempo de novo), reagir e apagar, como no modo API.
    ...(q.get('falhas') ? {
      reenviar: async (id, mensagemId) => {
        chamadas.push(['reenviar', id, mensagemId])
        despachar({ tipo: A.FALHAR_ENVIO_API, id, mensagemId, erro: 'The operation was canceled. Envio incerto.' })
        return null
      },
      reagir: async () => null, apagar: async () => null,
    } : {}),
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
          bloco={{ id: 'b1', geracaoId: 'g1', tipo: 'resposta', acao: 'rascunho', texto: 'Confirmo às 12h.', conversaId: selecionada.id, identidadeSessao: identidadeDaMontagem(), contextoAssistencia: chaveDaAssistencia(selecionada, identidadeDaMontagem()) }}
          aoRascunho={(texto) => { chamadas.push(['rascunhoDaFicha', texto]); return true }}
        />
      )}
      {cena === 'nota' && <Nota />}
      {cena === 'moldura' && (
        <>
          <Moldura
            tamanho={tamanho} aoAbrirNota={noop} aoAbrirGaleria={noop} aoAbrirBiblioteca={noop} aoAbrirCardapio={noop}
            aoAbrirAutomacoes={noop} aoAbrirGestao={() => abrirGestao(true)} aoAbrirEntregas={noop} aoAbrirAjuda={noop} aoRascunho={noop}
            simulando={false} aoAlternarSimulacoes={null} larguras={larguras} aoRedimensionar={noop}
            slotEditorNota={null} solicitacaoPainel={null}
          />
          <PilhaDeAvisos />
          {gestaoAberta && <ModalGestao aoFechar={() => abrirGestao(false)} />}
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
          {/* #3234 (E33): ficha=1 ocupa a coluna do cliente (360 px), e a conversa fica com a largura real do Balcão. */}
          {q.get('ficha') === '1' && <aside aria-hidden="true" style={{ width: 360, flexShrink: 0 }} />}
          <BalaoAssistente sugestaoAgente={null} aoAbrirTela={(tela) => chamadas.push(['abrirTela', tela])} areaDoBalcao={{ tela: 1280, balcao: 340, ficha: 360, colunas: true }} />
          <PilhaDeAvisos />
        </div>
      )}
    </ContextoCatalogo></ContextoAcoes></ContextoEstado></ContextoAcessoModulos.Provider>
  )
}
function Ajustes() {
  const [rascunhos, definir] = useState({})
  const gravacoes = useGravacoesDosAjustes()
  return <ContextoRascunhosDosAjustes.Provider value={{ rascunhos, definir: (chave, valor) => definir(atual => ({ ...atual, [chave]: valor })), ...gravacoes }}><Cena /></ContextoRascunhosDosAjustes.Provider>
}
// #3265 (C6): como em main.jsx, a partida aplica o token do trilho; sem ele o trilho media 130 px, e não 76.
for (const [nome, valor] of Object.entries(tokensDaGeometria())) document.documentElement.style.setProperty(nome, valor)
createRoot(document.getElementById('root')).render(<Ajustes />)
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

  // #3282: com o Assistente atendendo, a 1a vez da sessão pergunta antes de assumir; o diálogo confirma com o verbo do botão.
  const confirmarEnvio = async (p) => {
    const dialogo = p.getByRole('dialog', { name: 'Enviar e assumir esta conversa?' })
    await dialogo.waitFor({ timeout: 3000 })
    await dialogo.getByRole('button', { name: 'Enviar e assumir', exact: true }).click()
  }

  // --- F23: letras digitadas enquanto o servidor assume --------------------------------------------------------
  await confere('F23. o que ela digita enquanto o assumir espera fica no campo; só o texto enviado sai', async () => {
    const { ctx, p } = await abrir('inicial=robo&lento=1')
    await esperarPainel(p)
    await campo(p).fill('Primeira mensagem')
    await campo(p).press('Enter')
    await confirmarEnvio(p)
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
  // #3363 (S2): o Esc no campo agora leva o foco ao cartão da conversa aberta (a atendente "desiste" de digitar e volta à
  // lista). O que o F6 protege continua: o Esc do campo não fecha o Assistente nem manda o foco para o gatilho dele.
  await confere('F6. Esc no campo de mensagem não fecha o Assistente nem leva o foco ao gatilho dele; vai ao cartão da conversa aberta', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    await p.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
    await p.getByRole('dialog', { name: /Assistente/ }).waitFor()
    await campo(p).click()
    await campo(p).press('Escape')
    assert.equal(await p.getByRole('dialog', { name: /Assistente/ }).isVisible(), true, 'o Assistente segue aberto')
    assert.equal(await p.evaluate(() => Boolean(document.activeElement?.matches('[role=list] button[aria-current="true"]'))), true, 'o foco foi ao cartão da conversa aberta')
    assert.equal(await p.evaluate(() => Boolean(document.activeElement?.closest('[role=dialog]'))), false, 'o foco foi para dentro do painel do Assistente')
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
    assert.equal(await p.locator(`[id="${id}"]`).innerText(), 'Conversa concluída: não há pedido para montar.')
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
    // #3318: "robo" tem pergunta sem resposta há 10 min; a barra diz o mesmo "Precisa de você" do cartão.
    await p.locator('[data-controle-conversa]').getByText('Precisa de você').first().waitFor()
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
    await confirmarEnvio(p)
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
    await confirmarEnvio(p)
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
    const gravar = p.getByRole('button', { name: 'Gravar áudio' })
    await gravar.waitFor()
    // #3355: com o campo vazio o próximo passo é escrever; o Gravar é opção e não disputa o laranja com Assumir e Itens.
    // Cores normalizadas num canvas de 1 px: `color-mix` e `rgb()` serializam diferente.
    const pixel = (css) => p.evaluate((css) => { const c = document.createElement('canvas').getContext('2d'); c.fillStyle = css; c.fillRect(0, 0, 1, 1); return [...c.getImageData(0, 0, 1, 1).data].join() }, css)
    const fundo = async (alvo) => pixel(await alvo.evaluate((e) => getComputedStyle(e).backgroundColor))
    const token = async (nome) => pixel(await p.evaluate((nome) => { const d = document.createElement('i'); d.style.background = `var(${nome})`; document.body.append(d); const c = getComputedStyle(d).backgroundColor; d.remove(); return c }, nome))
    assert.equal(await fundo(gravar), await token('--preenchimento'), 'o Gravar é secundário, não laranja cheio')
    // Desabilitado (conversa sem assumir) fica mais leve que o habilitado: sem fundo, com o tracejado do Botao. Em
    // produção (25/11) uma regra posterior de `.acoes .enviar:disabled` pintava `--preenchimento` por cima. O defeito
    // é de cascata: basta o estado `:disabled`.
    await gravar.evaluate((e) => { e.disabled = true })
    assert.equal(await fundo(gravar), await pixel('transparent'), 'o Gravar desabilitado não tem fundo')
    await gravar.evaluate((e) => { e.disabled = false })
    await gravar.hover()
    await p.waitForTimeout(250)
    const noHover = await fundo(gravar)
    assert.notEqual(noHover, await token('--preenchimento-forte'), 'o hover não se confunde com o desabilitado')
    assert.notEqual(noHover, await token('--preenchimento'), 'o hover muda o fundo')
    await p.mouse.move(0, 0)
    await campo(p).fill('oi')
    assert.equal(await fundo(p.getByRole('button', { name: /^Enviar/ }).first()), await token('--acao'), 'com texto, o Enviar segue laranja')
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
    const janela = p.getByRole('dialog', { name: 'Ficha do cliente' })
    await janela.waitFor()
    await janela.getByLabel('Endereço', { exact: true }).first().fill('Rua das Flores, 120, Centro')
    await p.keyboard.press('Escape')
    assert.equal(await janela.isVisible(), true, 'a digitação não se perde')
    assert.deepEqual((await chamadasDe(p)).filter((c) => c[0] === 'fecharFicha'), [])
    await ctx.close()
    const outra = await abrir('cena=ficha&inicial=lead')
    const fresca = outra.p.getByRole('dialog', { name: 'Ficha do cliente' })
    await fresca.waitFor()
    await outra.p.keyboard.press('Escape')
    await outra.p.waitForFunction(() => window.chamadas.some((c) => c[0] === 'fecharFicha'))
    await outra.ctx.close()
  })
  await confere('F16. Ficha: dado que chega da sincronização sem ninguém digitar não segura a janela', async () => {
    const { ctx, p } = await abrir('cena=ficha&inicial=lead')
    const janela = p.getByRole('dialog', { name: 'Ficha do cliente' })
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
  await confere('E19. (#3206) conversa sem cadastro com a janela fechada também oferece a mensagem padrão; o servidor decide', async () => {
    const { ctx, p } = await abrir('inicial=leadjanela')
    await campo(p).waitFor()
    // #3234 (C2): o campo deixou de repetir "Janela fechada"; o sinal da oferta é a frase que leva a Respostas.
    assert.ok((await campo(p).getAttribute('placeholder')).startsWith('Escolha uma mensagem padrão'), 'lead sem cadastro deve oferecer a mensagem padrão')
    const respostas = p.getByRole('button', { name: /Respostas/ }).first()
    assert.equal(await respostas.isEnabled(), true, 'o botão Respostas fica habilitado para o lead sem cadastro')
    assert.equal(await respostas.getAttribute('aria-haspopup'), 'true', 'com a janela fechada, Respostas abre a lista de mensagens padrão')
    await p.evaluate(() => window.trocar('janela'))
    await p.waitForFunction(() => document.querySelector('textarea[id]')?.placeholder?.startsWith('Escolha uma mensagem padrão'))
    await ctx.close()
  })

  // --- E30/E31 (#3234, grupo A1) ---------------------------------------------------------------------------------------------------
  for (const width of [1440, 1920]) {
    await confere(`E30. (#3234 C2-C4) ${width}: "Janela fechada" uma vez só, sem Escolher modelo, faixa numa linha e o campo apontando Respostas`, async () => {
      const { ctx, p } = await abrir('inicial=janela', { width, height: 900 })
      await campo(p).waitFor()
      const m = await p.evaluate(() => {
        const area = document.querySelector('[data-area-envio]')
        const faixa = area.querySelector('[class*=bloqueio]')
        const texto = faixa.querySelector('p')
        return {
          // O que se vê (sem o texto de leitor de tela) e o que o leitor ouve ao focar o título.
          cabecalho: (() => { const c = document.querySelector('[data-cabecalho-conversa]').cloneNode(true); c.querySelectorAll('.sr').forEach((e) => e.remove()); return c.textContent })(),
          tituloLido: document.querySelector('[data-cabecalho-conversa] h2').textContent,
          naArea: (area.innerText.match(/Janela fechada/g) ?? []).length,
          placeholder: document.querySelector('textarea[id]').placeholder,
          faixa: faixa.innerText.replace(/\s+/g, ' ').trim(),
          altura: texto.getBoundingClientRect().height,
          linha: parseFloat(getComputedStyle(texto).lineHeight),
        }
      })
      assert.doesNotMatch(m.cabecalho, /Janela fechada/, 'o cabeçalho repete o que a faixa diz')
      assert.match(m.tituloLido, /Janela fechada/, 'o foco vai ao título ao trocar de conversa: o leitor de tela precisa ouvir a janela')
      assert.equal(m.naArea, 1, `"Janela fechada" aparece ${m.naArea} vezes junto do campo`)
      assert.doesNotMatch(m.placeholder, /Janela fechada/, 'o campo repete o que a faixa diz')
      assert.equal(await p.getByRole('button', { name: /Escolher modelo/ }).count(), 0, 'a faixa ainda tem a segunda entrada para a mesma lista')
      assert.match(m.faixa, /^Janela fechada\. Só mensagem padrão do WhatsApp sai até ele responder\.$/, 'a faixa perdeu o porquê')
      assert.match(m.placeholder, /Respostas/, 'o campo não diz onde está a mensagem padrão')
      assert.ok(m.altura <= m.linha * 1.5, `a faixa quebrou: ${m.altura} px para uma linha de ${m.linha} px`)
      await ctx.close()
    })
  }
  for (const width of [1280, 1440]) {
    await confere(`E31. (#3234 C2) ${width}: com a janela aberta o cabeçalho mostra o prazo numa linha, com nome longo e comanda`, async () => {
      const { ctx, p } = await abrir('inicial=meu&cabecalhoCheio=1', { width, height: 900 })
      await campo(p).waitFor()
      const linha = p.locator('[data-cabecalho-conversa] h2 small')
      assert.match(await linha.innerText(), /4821 ·\s+WhatsApp · Janela aberta por \d+ h \d+ min/)
      const { altura, alturaLinha } = await linha.evaluate((el) => ({ altura: el.getBoundingClientRect().height, alturaLinha: parseFloat(getComputedStyle(el).lineHeight) }))
      assert.ok(altura <= alturaLinha * 1.5, `a linha do cabeçalho quebrou: ${altura} px para ${alturaLinha} px`)
      await ctx.close()
    })
  }
  await confere('E32. (#3234 C1) 4 envios seguidos que falharam iguais viram 1 balão com 1 aviso; Ver as tentativas mostra as 4', async () => {
    const { ctx, p } = await abrir('inicial=meu&falhas=1', { width: 1440, height: 900 })
    await campo(p).waitFor()
    const contar = () => p.evaluate(() => ({
      baloes: [...document.querySelectorAll('[role=log] *')].filter((e) => e.childElementCount === 0 && e.textContent === 'Tem sim, separo uma?').length,
      alertas: [...document.querySelectorAll('[role=log] [role=alert]')].map((e) => e.textContent.trim()),
      // Na tela (não só para o leitor): a frase da falha aparece uma vez, aberto ou fechado.
      frases: (document.querySelector('[role=log]').textContent.match(/Pode ter chegado/g) ?? []).length,
      rotulos: [...document.querySelector('[role=log]').textContent.matchAll(/Tentativa \d de \d/g)].map((m) => m[0]),
    }))
    const fechado = await contar()
    assert.equal(fechado.baloes, 1, `${fechado.baloes} balões iguais à vista`)
    assert.equal(fechado.alertas.length, 1, `${fechado.alertas.length} avisos de falha`)
    assert.match(fechado.alertas[0], /4 tentativas sem resposta do WhatsApp\. Pode ter chegado/)
    const ver = p.getByRole('button', { name: 'Ver as 4 tentativas' })
    assert.equal(await ver.getAttribute('aria-expanded'), 'false')
    await ver.click()
    const aberto = await contar()
    assert.equal(aberto.baloes, 4, 'Ver as tentativas não mostrou as 4')
    assert.equal(aberto.alertas.length, 1, 'abrir as tentativas repetiu o aviso')
    assert.equal(aberto.frases, 1, `abrir as tentativas repetiu a frase na tela (${aberto.frases} vezes)`)
    assert.deepEqual(aberto.rotulos, ['Tentativa 1 de 4', 'Tentativa 2 de 4', 'Tentativa 3 de 4'])
    assert.equal(await p.getByRole('button', { name: 'Ocultar as tentativas' }).getAttribute('aria-expanded'), 'true')
    // Reenviar só na mais recente; reagir e apagar (o menu da mensagem) em cada uma das quatro.
    const log = p.getByRole('log')
    assert.equal(await log.getByRole('button', { name: 'Reenviar' }).count(), 1, 'Reenviar fora da mais recente')
    assert.equal(await log.getByRole('button', { name: 'Mais opções da mensagem' }).count(), 5, 'as tentativas perderam o menu da mensagem')
    // A mais recente do grupo, reenviada e esgotada de novo, continua a cabeça: o grupo não se parte em dois avisos.
    await log.getByRole('button', { name: 'Reenviar' }).click()
    await p.getByText('Tentei de novo e o WhatsApp demorou outra vez', { exact: false }).waitFor()
    const reenviado = await contar()
    assert.equal(reenviado.alertas.length, 1, `reenviar a mais recente partiu o grupo (${reenviado.alertas.length} avisos)`)
    assert.deepEqual(reenviado.rotulos, ['Tentativa 1 de 4', 'Tentativa 2 de 4', 'Tentativa 3 de 4'], 'o grupo perdeu a mais recente')
    await p.getByRole('button', { name: 'Ocultar as tentativas' }).click()
    assert.equal((await contar()).baloes, 1, 'fechado, o grupo reenviado não voltou a 1 balão')
    // Aberto, o grupo continua aberto quando chega outra falha igual: a nova vira a cabeça e as 4 viram tentativas.
    await p.getByRole('button', { name: 'Ver as 4 tentativas' }).click()
    await p.evaluate(() => window.falhouIgual())
    await p.getByText('5 tentativas sem resposta do WhatsApp', { exact: false }).waitFor()
    const alternar = log.getByRole('button', { name: /tentativas$/ })
    const cresceu = {
      aberto: await alternar.getAttribute('aria-expanded'),
      rotulos: (await contar()).rotulos,
    }
    assert.deepEqual(cresceu, { aberto: 'true', rotulos: ['Tentativa 1 de 5', 'Tentativa 2 de 5', 'Tentativa 3 de 5', 'Tentativa 4 de 5'] },
      'o grupo aberto fechou quando chegou outra falha igual')
    await ctx.close()

    // O Reenviar de uma falha solta que esgota o tempo de novo deixa a falha igual à vizinha: o balão clicado fica à
    // vista (o mesmo nó, sem remontar) com o retorno do reenvio, em vez de sumir dentro de um grupo fechado.
    const outra = await abrir('inicial=meu&falhas=2', { width: 1440, height: 900 })
    await campo(outra.p).waitFor()
    const reenviarRecusa = outra.p.getByRole('log').getByRole('button', { name: 'Reenviar' }).first()
    await reenviarRecusa.evaluate((b) => { b.closest('[class*=balao]').dataset.clicado = '1' })
    await reenviarRecusa.click()
    await outra.p.getByText('Tentei de novo e o WhatsApp demorou outra vez', { exact: false }).waitFor()
    const depois = await outra.p.evaluate(() => ({
      clicado: document.querySelector('[data-clicado]')?.isConnected ?? false,
      baloes: [...document.querySelectorAll('[role=log] *')].filter((e) => e.childElementCount === 0 && e.textContent === 'Tem sim, separo uma?').length,
    }))
    assert.deepEqual(depois, { clicado: true, baloes: 2 }, 'o balão reenviado sumiu ou remontou')
    await outra.ctx.close()
  })
  await confere('E32c. (#3234 A2b) 4 envios do mesmo modelo que falharam iguais viram 1 balão com 1 aviso e o selo do modelo', async () => {
    const { ctx, p } = await abrir('inicial=meu&falhas=3', { width: 1440, height: 900 })
    await campo(p).waitFor()
    const contar = () => p.evaluate(() => ({
      baloes: [...document.querySelectorAll('[role=log] *')].filter((e) => e.childElementCount === 0 && e.textContent === 'Pedido 21 pronto\nOi Ana, seu pedido 21 está pronto para retirar.\nEasyStok').length,
      alertas: [...document.querySelectorAll('[role=log] [role=alert]')].map((e) => e.textContent.trim()),
      selos: document.querySelectorAll('[role=log] [title=pedido_pronto]').length,
      frases: (document.querySelector('[role=log]').textContent.match(/Pode ter chegado/g) ?? []).length,
      rotulos: [...document.querySelector('[role=log]').textContent.matchAll(/Tentativa \d de \d/g)].map((m) => m[0]),
    }))
    const fechado = await contar()
    assert.deepEqual({ baloes: fechado.baloes, alertas: fechado.alertas.length, frases: fechado.frases, selos: fechado.selos },
      { baloes: 1, alertas: 1, frases: 1, selos: 1 }, 'o mesmo modelo repetido ainda aparece um balão por envio')
    assert.match(fechado.alertas[0], /4 tentativas sem resposta do WhatsApp\. Pode ter chegado/)
    await p.getByRole('button', { name: 'Ver as 4 tentativas' }).click()
    const aberto = await contar()
    assert.deepEqual({ baloes: aberto.baloes, alertas: aberto.alertas.length, frases: aberto.frases, selos: aberto.selos },
      { baloes: 4, alertas: 1, frases: 1, selos: 4 }, 'abrir as tentativas não mostrou as 4 ou repetiu o aviso')
    assert.deepEqual(aberto.rotulos, ['Tentativa 1 de 4', 'Tentativa 2 de 4', 'Tentativa 3 de 4'])
    // Modelo não tem Reenviar (volta por Modelos aprovados), aberto ou fechado.
    assert.equal(await p.getByRole('log').getByRole('button', { name: 'Reenviar' }).count(), 0)
    await ctx.close()
  })
  await confere('E32b. (#3234 C1) a fixture sem as ações não ganha menu nem Reenviar', async () => {
    const { ctx, p } = await abrir('inicial=meu', { width: 1440, height: 900 })
    await campo(p).waitFor()
    assert.equal(await p.getByRole('log').getByRole('button', { name: 'Mais opções da mensagem' }).count(), 0)
    assert.equal(await p.getByRole('log').getByRole('button', { name: 'Reenviar' }).count(), 0)
    await ctx.close()
  })
  await confere('E31. (#3234 C2) 1024: abaixo de 1280 px o prazo folgado sai do cabeçalho (#1761 L4), e a linha não quebra', async () => {
    const { ctx, p } = await abrir('inicial=meu&cabecalhoCheio=1', { width: 1024, height: 768 })
    await campo(p).waitFor()
    const linha = p.locator('[data-cabecalho-conversa] h2 small')
    assert.doesNotMatch(await linha.innerText(), /Janela aberta/)
    const { altura, alturaLinha } = await linha.evaluate((el) => ({ altura: el.getBoundingClientRect().height, alturaLinha: parseFloat(getComputedStyle(el).lineHeight) }))
    assert.ok(altura <= alturaLinha * 1.5, `a linha do cabeçalho quebrou: ${altura} px para ${alturaLinha} px`)
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
    await p.getByRole('button', { name: /^Todas/ }).click()
    await p.getByRole('button', { name: /^Concluídas/ }).click()
    await p.getByText('Cliente fechada0').first().click()
    await p.waitForFunction(() => document.querySelector('[aria-current="true"]') !== null)
    assert.match(await p.locator('[aria-current="true"]').first().innerText(), /fechada0/)
    await ctx.close()
  })
  await confere('F18. chip "Todas" conta abertas sem encerradas nem bloqueadas; título não duplica o número', async () => {
    const { ctx, p } = await abrir('inicial=meu')
    await esperarPainel(p)
    const abertas = await p.evaluate(() => window.estadoCena().conversas.filter((c) => c.estado !== 'Encerrado' && !c.bloqueio).length)
    assert.equal(await p.locator('h2', { hasText: 'Conversas' }).first().innerText(), 'Conversas')
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
  await confere('R4. Gestão aberta da aba Conversas volta às Mensagens pelo canal real, com toque390', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=meu', { ...celular, toque: true })
    await aba(p, 'Conversas').waitFor()
    assert.equal(await aba(p, 'Conversas').getAttribute('aria-selected'), 'true')
    await p.getByRole('button', { name: 'Mais opções', exact: true }).tap()
    await p.getByRole('menuitem', { name: 'Gestão', exact: true }).tap()
    const gestao = p.getByRole('dialog', { name: 'Gestão da loja', exact: true })
    // #2832: no modo API a Gestão abre no painel Agora; a busca das telas fica em Todas as telas.
    await gestao.getByRole('button', { name: 'Todas as telas', exact: true }).tap()
    await gestao.getByRole('button', { name: 'Leads e clientes', exact: true }).tap()
    await gestao.getByRole('button', { name: 'Voltar à conversa', exact: true }).tap()
    assert.equal(await gestao.count(), 0)
    assert.equal(await aba(p, 'Mensagens').getAttribute('aria-selected'), 'true')
    await campo(p).tap()
    await p.screenshot({ path: resolve(raiz, '.build/2395/retorno-mensagens-390.png') })
    await ctx.close()
  })
  await confere('F8. no celular, abrir a conversa pelo aviso na tela leva à aba Mensagens', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=nenhuma&aviso=1', celular)
    await aba(p, 'Conversas').waitFor()
    assert.equal(await aba(p, 'Conversas').getAttribute('aria-selected'), 'true')
    await p.getByRole('status', { name: 'Avisos do atendimento' }).getByRole('button', { name: 'Cliente meu Oi de novo' }).click()
    await p.waitForFunction(() => [...document.querySelectorAll('[role="tab"]')].find((t) => /^Mensagens$/.test(t.textContent.trim()))?.getAttribute('aria-selected') === 'true')
    await ctx.close()
  })
  await confere('F24. no celular, o painel da conversa só conta como à vista na aba Mensagens', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=meu', celular)
    await aba(p, 'Conversas').waitFor()
    const naTela = async () => (await chamadasDe(p)).filter((c) => c[0] === 'painelNaTela').at(-1)?.[1]
    assert.equal(await naTela(), false)
    await aba(p, 'Mensagens').click()
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
  await confere('F18. a aba "Conversas" do celular conta quem precisa de você, mesmo ao listar todas', async () => {
    const { ctx, p } = await abrir('cena=moldura&inicial=nenhuma', celular)
    await aba(p, 'Conversas').waitFor()
    const esperadas = '10' // Nesta massa, 12 abertas e 10 que precisam da atendente.
    assert.equal((await p.getByRole('button', { name: /^Precisa de você/ }).innerText()).replace(/[^0-9]/g, ''), esperadas)
    assert.equal((await aba(p, 'Conversas').innerText()).replace(/[^0-9]/g, ''), esperadas)
    await p.getByRole('button', { name: /^Todas/ }).click()
    await p.waitForTimeout(100)
    assert.equal((await aba(p, 'Conversas').innerText()).replace(/[^0-9]/g, ''), esperadas, 'listar todas não muda o total que precisa da atendente')
    await ctx.close()
  })
  // #3265 (C6): em 1920 a conversa tinha 1176 px e o balão do cliente ficava a ~1.130 px do balão da loja. Com o
  // padrão novo a coluna da conversa fica com 880 px; com larguras estreitas salvas (244/292) a coluna é larga, mas a
  // thread para em 880 px, centrada. (1268 = 1920 - 100 - 260 - 292: o `min-width` de 260 do Balcão vence o 244.)
  for (const [nome, salvas, coluna] of [['padrão', '', 880], ['244/292 salvos', '&larguras=' + encodeURIComponent('{"balcao":244,"ficha":292}'), 1268]]) {
    await confere(`C6. 1920 com ${nome}: a leitura da conversa para em 880 px, centrada`, async () => {
      // A conversa "auto" tem a mensagem do cliente e duas respostas da loja: os dois lados do fio.
      const { ctx, p } = await abrir('cena=moldura&inicial=auto' + salvas, { width: 1920, height: 1080 })
      await p.locator('[role=log]').waitFor()
      const m = await p.evaluate(() => {
        const log = document.querySelector('[role=log]')
        // A área útil do pai (sem a barra de rolagem) é a referência do centro e da largura esperada.
        const caixa = log.getBoundingClientRect(), elPai = log.parentElement, paiR = elPai.getBoundingClientRect()
        const pai = { left: paiR.left + elPai.clientLeft, right: paiR.left + elPai.clientLeft + elPai.clientWidth }
        const campo = document.querySelector('[class*=composer][data-area-envio]').getBoundingClientRect()
        const estilo = getComputedStyle(log)
        const util = caixa.width - parseFloat(estilo.paddingLeft) - parseFloat(estilo.paddingRight)
        const baloes = [...log.querySelectorAll('[class*=balao]')].filter((b) => /entrada|saida/.test(b.className))
        const esquerda = Math.min(...baloes.filter((b) => /entrada/.test(b.className)).map((b) => b.getBoundingClientRect().left))
        const direita = Math.max(...baloes.filter((b) => /saida/.test(b.className)).map((b) => b.getBoundingClientRect().right))
        return { coluna: Math.round(document.querySelector('section[aria-label=Conversa]').getBoundingClientRect().width), paiUtil: elPai.clientWidth, caixa: Math.round(caixa.width), campo: Math.round(campo.width), centroDoCampo: Math.round(campo.left + campo.width / 2 - (caixa.left + caixa.width / 2)), util: Math.round(util), sobraEsq: Math.round(caixa.left - pai.left), sobraDir: Math.round(pai.right - caixa.right), vao: Math.round(direita - esquerda), baloes: baloes.length }
      })
      assert.ok(m.baloes >= 2, `a massa tem balões dos dois lados (${m.baloes})`)
      assert.equal(m.coluna, coluna, 'coluna da conversa')
      assert.ok(m.caixa <= 880 && m.util <= 880, `leitura: caixa ${m.caixa} px, texto ${m.util} px`)
      assert.equal(m.caixa, Math.min(m.paiUtil, 880), 'o fio ocupa a leitura toda, não encolhe ao conteúdo')
      assert.equal(m.campo, m.caixa, 'o campo de escrever tem a largura do fio')
      assert.ok(Math.abs(m.centroDoCampo) <= 1, `o campo fica alinhado ao fio (${m.centroDoCampo} px)`)
      assert.ok(m.vao <= 880, `do balão do cliente ao da loja: ${m.vao} px`)
      assert.ok(Math.abs(m.sobraEsq - m.sobraDir) <= 1, `centrada (${m.sobraEsq} e ${m.sobraDir})`)
      await ctx.close()
    })
  }
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
  await confere('E33. (#3234 C5) no desktop o modo é selo no cabeçalho, sem a faixa de 40 px; a transferência segue na faixa', async () => {
    const medir = (p) => p.evaluate(() => {
      const cabecalho = document.querySelector('[data-cabecalho-conversa]')
      const regiao = document.querySelector('[data-controle-conversa]')
      const assumir = [...document.querySelectorAll('button')].find((b) => b.textContent.trim().startsWith('Assumir conversa'))
      const c = cabecalho.getBoundingClientRect()
      const fio = document.querySelector('[role=log]').parentElement.getBoundingClientRect()
      return {
        noCabecalho: cabecalho.contains(regiao), texto: regiao.innerText.replace(/\s+/g, ' ').trim(),
        alturaCabecalho: Math.round(c.height), vao: Math.round(fio.top - c.bottom),
        assumirNoCabecalho: assumir ? cabecalho.contains(assumir) : null,
        assumirCabe: assumir ? assumir.getBoundingClientRect().right <= c.right + 0.5 : null,
        nomeNumaLinha: (() => { const n = cabecalho.querySelector('h2 > span'); return n.getBoundingClientRect().height <= parseFloat(getComputedStyle(n).lineHeight) * 1.5 })(),
      }
    })
    for (const [largura, altura] of [[1280, 800], [1440, 900], [1920, 1080]]) {
      // #3374: depois da #3318, a 'robo' mostra o motivo ("Cliente falou por último…"), e o motivo à vista fica na
      // faixa (conferido logo abaixo). O selo no cabeçalho vale para o modo sem motivo nem transferência.
      {
        const { ctx, p } = await abrir('inicial=robo', { width: largura, height: altura })
        await p.getByRole('region', { name: 'Quem responde esta conversa' }).waitFor()
        const m = await medir(p)
        const cena = `robo em ${largura}: ${JSON.stringify(m)}`
        assert.equal(m.noCabecalho, false, `o motivo de precisar de você subiu para o selo e sumiu da vista (${cena})`)
        assert.match(m.texto, /Precisa de você/, cena)
        assert.match(m.texto, /Cliente falou por último/, cena)
        assert.ok(m.alturaCabecalho <= 72, `o cabeçalho cresceu (${cena})`)
        await ctx.close()
      }
      // #3374: com o Assumir na tela o modo fica na faixa, como o código do selo decide (`!acoesTela.assumir`): selo e
      // Assumir juntos quebram a linha em 1280 e 1440 (crítica de 23/11). O cabeçalho com o Assumir é a C8 do plano.
      {
        const { ctx, p } = await abrir('inicial=robo&respondida=1', { width: largura, height: altura })
        await p.getByRole('region', { name: 'Quem responde esta conversa' }).waitFor()
        const m = await medir(p)
        const cena = `robo respondida em ${largura}: ${JSON.stringify(m)}`
        assert.equal(m.noCabecalho, false, `selo e Assumir subiram juntos para o cabeçalho (${cena})`)
        assert.equal(m.assumirCabe, true, cena)
        await ctx.close()
      }
      for (const [inicial, selo, comAssumir] of [['meu', 'Você respondendo', false]]) {
        const { ctx, p } = await abrir(`inicial=${inicial}&respondida=1`, { width: largura, height: altura })
        await p.getByRole('region', { name: 'Quem responde esta conversa' }).waitFor()
        const m = await medir(p)
        const cena = `${inicial} em ${largura}: ${JSON.stringify(m)}`
        // #3374 (C0): selo desligado (C8 mediu 79 px em 1280 e 1440, ver SELO_NO_CABECALHO). O modo segue na faixa de uma
        // linha e o cabeçalho fica em 64 px.
        assert.equal(m.noCabecalho, false, `o selo subiu antes da C8 (${cena})`)
        assert.match(m.texto, new RegExp(selo), cena)
        assert.ok(m.alturaCabecalho <= 64, `o cabeçalho cresceu (${cena})`)
        assert.equal(m.nomeNumaLinha, true, `o nome quebrou (${cena})`)
        if (comAssumir) assert.deepEqual([m.assumirNoCabecalho, m.assumirCabe], [true, true], `Assumir fora do cabeçalho (${cena})`)
        await ctx.close()
      }
    }
    // A transferência diz quem passou e por quê: é frase, não cabe num selo, e segue na faixa com o Assumir.
    const { ctx, p } = await abrir('inicial=meu&passagem=1', { width: 1440, height: 900 })
    await p.getByRole('region', { name: 'Quem responde esta conversa' }).waitFor()
    const passou = await medir(p)
    assert.equal(passou.noCabecalho, false, `a transferência subiu para o cabeçalho: ${JSON.stringify(passou)}`)
    assert.match(passou.texto, /alergia a camarão/)
    await ctx.close()
  })
} finally {
  await navegador.close()
}

const semPagina = [...new Set(erros.filter((e) => e.startsWith('pageerror') || e.startsWith('console')))]
console.log(`\nprova-2185-efeitos-tela: ${passou} conferências ok, ${erros.length} falha(s)${semPagina.length ? `\n  ${semPagina.slice(0, 6).join('\n  ')}` : ''}`)
if (erros.length) process.exit(1)
