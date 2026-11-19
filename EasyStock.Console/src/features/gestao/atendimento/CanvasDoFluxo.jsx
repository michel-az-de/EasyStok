import { memo, useEffect, useMemo, useRef, useState } from 'react'
import {
  Background, Controls, Handle, MarkerType, Position, ReactFlow, ReactFlowProvider, applyNodeChanges, useReactFlow, useStore, useStoreApi,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Icone } from '../../../componentes/Icone'
import { NO_ALTURA, NO_LARGURA, ordemDoFluxo } from '../../../dominio/desenhoDoFluxo'
import css from './canvasDoFluxo.module.css'

// #2855: o desenho do fluxo do Assistente como canvas (React Flow). Este arquivo é carregado sob demanda
// (React.lazy em AbaFluxo) para a biblioteca não entrar no bundle inicial do console.
// As posições vêm de dominio/desenhoDoFluxo; aqui só se liga isso ao React Flow.

const LADOS = { left: Position.Left, right: Position.Right, top: Position.Top, bottom: Position.Bottom }
const ROTULOS = {
  'controls.ariaLabel': 'Controles do desenho',
  'controls.zoomIn.ariaLabel': 'Aproximar',
  'controls.zoomOut.ariaLabel': 'Afastar',
  'controls.fitView.ariaLabel': 'Enquadrar o desenho na tela',
  'controls.interactive.ariaLabel': 'Travar ou liberar o desenho',
  'minimap.ariaLabel': 'Mapa do desenho',
  'node.a11yDescription.default': 'Arraste para mover. Use o botão do nó para abrir o ajuste.',
  'node.a11yDescription.keyboardDisabled': 'Arraste para mover. Use o botão do nó para abrir o ajuste.',
  'edge.a11yDescription.default': 'Ligação entre etapas.',
  'handle.ariaLabel': 'Ponto de ligação',
}
const MOVIMENTO_MINIMO_DO_TOQUE = 5
// Folga de 56 px em volta do enquadramento (os botões de zoom ficam no canto). Zoom mínimo 0.8: abaixo disso o texto fica ilegível, e o resto se alcança arrastando.
const ZOOM_LEGIVEL = 0.8
// #2857 (M1): no canvas embutido o enquadramento nunca fica abaixo deste zoom; o que não cabe se alcança arrastando.
const ZOOM_MINIMO_EMBUTIDO = 0.7
// Folga em volta do enquadramento; a esquerda é maior porque os botões de zoom ficam no canto de baixo e não podem cobrir nó.
const RESERVA_DOS_CONTROLES = 64
const FOLGA = { top: '56px', right: '56px', bottom: '56px', left: `${RESERVA_DOS_CONTROLES + 8}px` }
// Enquadra sempre tudo o que foi pedido: o zoom cai abaixo do legível só se não couber (o resto não fica cortado). No Expandir sobra tela, então amplia até 1.5.
const enquadrar = (cheio) => ({ padding: FOLGA, maxZoom: cheio ? 1.5 : 1, minZoom: 0.15 })

function Alcas() {
  return (
    <>
      {Object.entries(LADOS).map(([lado, posicao]) => (
        <span key={lado}>
          <Handle id={`${lado}-s`} type="source" position={posicao} className={css.alca} isConnectable={false} />
          <Handle id={`${lado}-t`} type="target" position={posicao} className={css.alca} isConnectable={false} />
        </span>
      ))}
      {/* #2857: o tronco do desenho em coluna sai de baixo, à esquerda, e desce ao lado dos itens recuados. */}
      <Handle id="tronco-s" type="source" position={Position.Bottom} className={`${css.alca} ${css.alcaTronco}`} isConnectable={false} />
    </>
  )
}

const NoDoFluxo = memo(function NoDoFluxo({ data }) {
  const { id, tipo, icone, titulo, subtitulo, descricao, selecionado, desligado, grupo, aberto, escala, ligada, travada, ativo, podeEditar, ocupado } = data
  const comChave = tipo === 'ferramenta' ? !travada : tipo === 'roteiro'
  const valorDaChave = tipo === 'ferramenta' ? ligada : ativo
  const inicio = useRef(null)
  const ativar = (evento) => {
    const saiu = inicio.current && Math.hypot(evento.clientX - inicio.current.x, evento.clientY - inicio.current.y) > MOVIMENTO_MINIMO_DO_TOQUE
    if (!saiu) data.aoAtivar(id)
  }
  return (
    <div className={`${css.no} ${css['no_' + tipo]} ${desligado ? css.desligado : ''} ${selecionado ? css.selecionado : ''}`}
      data-no={id} data-selecionado={selecionado || undefined} style={{ width: NO_LARGURA, height: NO_ALTURA }}>
      <Alcas />
      {/* Grupo só diz se está aberto (aria-expanded); os demais dizem se estão escolhidos (aria-pressed). O texto inteiro vai no title. */}
      <button type="button" className={css.corpo} title={descricao} aria-pressed={grupo ? undefined : selecionado} aria-expanded={grupo ? aberto : undefined}
        onPointerDown={(e) => { inicio.current = { x: e.clientX, y: e.clientY } }} onClick={ativar}>
        <span className={css.icone} aria-hidden="true"><Icone nome={icone} tamanho={20} /></span>
        <span className={css.texto}>
          <strong>{titulo}</strong>
          {subtitulo && <span>{subtitulo}</span>}
        </span>
        {grupo && <span className={css.abrir} aria-hidden="true"><Icone nome={aberto ? 'menos' : 'mais'} tamanho={16} /></span>}
        {tipo === 'portao' && (
          <span className={css.ordem}>
            {escala && <Icone nome="usuarios" tamanho={16} rotulo="Passa a conversa para a equipe" />}
          </span>
        )}
      </button>
      {tipo === 'ferramenta' && travada && <span className={css.cadeado}><Icone nome="lock" tamanho={16} rotulo="Sempre ligada" /></span>}
      {comChave && (
        // #2857: o rótulo em volta dá à chave um alvo de toque de 48 px; o desenho dela é o do canvas (neutra ligada, em alerta desligada).
        <label className={`${css.alvoDaChave} nodrag nopan`} onClick={(e) => e.stopPropagation()} onPointerDown={(e) => e.stopPropagation()}>
          <input type="checkbox" role="switch" checked={Boolean(valorDaChave)} aria-checked={Boolean(valorDaChave)}
            aria-label={tipo === 'ferramenta' ? `Ferramenta ${titulo}` : `Etapa ${data.indice + 1}: ${data.nome}`}
            disabled={!podeEditar || ocupado} onChange={() => data.aoAlternar(data)} />
        </label>
      )}
    </div>
  )
})

const TIPOS_DE_NO = { fluxo: NoDoFluxo }

function paraAresta(a) {
  const equipe = a.tom === 'equipe'
  const cor = equipe ? 'var(--marca)' : 'var(--linha-media)'
  return {
    id: a.id, source: a.origem, target: a.destino, sourceHandle: `${a.origemLado}-s`, targetHandle: `${a.destinoLado}-t`,
    type: 'smoothstep', label: a.rotulo, focusable: false,
    markerEnd: a.semSeta ? undefined : { type: MarkerType.ArrowClosed, color: cor, width: 18, height: 18 },
    style: { stroke: cor, strokeWidth: equipe ? 2.5 : 2, strokeDasharray: a.tracejada ? '7 6' : undefined },
    labelStyle: { fill: 'var(--tinta)', fontWeight: 700, fontSize: 14 },
    labelBgStyle: { fill: 'var(--papel)', stroke: 'var(--marca)', strokeWidth: 1 },
    labelBgPadding: [8, 4], labelBgBorderRadius: 8,
  }
}

// #2857 (M1): enquadra os nós pedidos sem passar de ZOOM_MINIMO_EMBUTIDO para baixo. Se não cabem nesse zoom, ancora o canto
// superior esquerdo (o pai do grupo e os primeiros itens) e o resto fica a um arrasto. Devolve false quando o fitView basta.
function enquadrarLegivel({ desenho, ids, api, setViewport }) {
  const { width, height } = api.getState()
  const nos = ids?.length ? desenho.nos.filter((n) => ids.includes(n.id)) : desenho.nos
  if (!nos.length || !width || !height) return false
  const minX = Math.min(...nos.map((n) => n.x)), maxX = Math.max(...nos.map((n) => n.x)) + NO_LARGURA
  const minY = Math.min(...nos.map((n) => n.y)), maxY = Math.max(...nos.map((n) => n.y)) + NO_ALTURA
  const util = { w: width - (RESERVA_DOS_CONTROLES + 8) - 56, h: height - 112 }
  const cabe = Math.min(util.w / (maxX - minX), util.h / (maxY - minY))
  if (cabe >= ZOOM_MINIMO_EMBUTIDO) return false
  const zoom = ZOOM_MINIMO_EMBUTIDO
  setViewport({ x: RESERVA_DOS_CONTROLES + 8 - minX * zoom, y: 56 - minY * zoom, zoom }, { duration: 0 })
  return true
}

// #2857: a coluna do celular não usa fitView, que encolhe tudo até caber e deixa o texto ilegível. A largura da
// coluna (com o trilho e o corredor) ocupa a tela, sem passar do tamanho real, e o topo vai para o grupo aberto;
// o resto da coluna fica a um arrasto.
const MARGEM_DO_TRILHO = 24
const MARGEM_DO_CORREDOR = 28
const MARGEM_DA_TELA = 8
function medirColuna(desenho, largura, reserva = 0) {
  const esquerda = Math.min(...desenho.nos.map((n) => n.x)) - MARGEM_DO_TRILHO
  const direita = Math.max(...desenho.nos.map((n) => n.x)) + NO_LARGURA + MARGEM_DO_CORREDOR
  return { esquerda, zoom: Math.min(1, (largura - 2 * MARGEM_DA_TELA - reserva) / (direita - esquerda)) }
}
function enquadrarColuna({ desenho, ids, largura, setViewport, reserva = 0 }) {
  if (!largura || !desenho.nos.length) return
  const { esquerda, zoom } = medirColuna(desenho, largura, reserva)
  const alvo = ids?.length ? desenho.nos.find((n) => n.id === ids[0]) : null
  const topo = alvo ? alvo.y : Math.min(...desenho.nos.map((n) => n.y))
  setViewport({ x: MARGEM_DA_TELA + reserva - esquerda * zoom, y: MARGEM_DA_TELA + 8 - topo * zoom, zoom }, { duration: 0 })
}
// No celular, fora do Expandir, o dedo rola a página e o desenho não se mexe: a área cresce até mostrar a coluna inteira.
function alturaDaColuna(desenho, largura) {
  const { zoom } = medirColuna(desenho, largura)
  return Math.ceil((Math.max(...desenho.nos.map((n) => n.y)) + NO_ALTURA) * zoom + 2 * MARGEM_DA_TELA + 8)
}

// `desenho` ({ nos, arestas, orientacao, compacto? }), `dadosDoNo(dados)` junta o estado da tela (selecionado, chave) aos dados
// do nó, `foco` ({ ids }) manda enquadrar quando muda (orientação ou grupo aberto). `interativo` falso: nada arrasta,
// nem o desenho nem os nós, e o toque fica livre para rolar a página.
function Interno({ desenho, dadosDoNo, foco, rotuloDoDesenho, interativo, aoMedirAltura, cheio, centrarNaSelecao }) {
  const { fitView, setViewport, setCenter, getZoom } = useReactFlow()
  const api = useStoreApi()
  const [posicoes, setPosicoes] = useState([])
  const formato = `${desenho.orientacao}${desenho.compacto ? ':coluna' : ''}`
  const formatoAnterior = useRef(formato)

  useEffect(() => {
    // Na coluna (#2857) abrir um grupo empurra para baixo o que vem depois, então as posições sempre recomeçam.
    const mudouDeLado = formatoAnterior.current !== formato || desenho.compacto
    formatoAnterior.current = formato
    setPosicoes((antes) => {
      const porId = new Map(antes.map((n) => [n.id, n]))
      return desenho.nos.map((n) => {
        const velho = porId.get(n.id)
        return { id: n.id, type: 'fluxo', position: velho && !mudouDeLado ? velho.position : { x: n.x, y: n.y }, measured: velho?.measured, data: {} }
      })
    })
  }, [desenho, formato])

  const nos = useMemo(() => {
    const dados = new Map(desenho.nos.map((n) => [n.id, n.dados]))
    // O React Flow tira o ponteiro do nó que não arrasta nem seleciona; aqui o nó é um botão e precisa do toque.
    return posicoes.map((n) => ({ ...n, style: { pointerEvents: 'all' }, data: dadosDoNo(dados.get(n.id) ?? {}) }))
  }, [posicoes, desenho, dadosDoNo])
  const arestas = useMemo(() => desenho.arestas.map(paraAresta), [desenho])

  const largura = useStore((s) => s.width)
  const ultimo = useRef({ nos })
  ultimo.current = { nos }
  const colunaCrescida = !interativo && Boolean(desenho.compacto)
  useEffect(() => {
    // No Expandir o desenho se enquadra ao abrir e quando a largura muda, mesmo sem foco pedido.
    if (!foco && !cheio) return undefined
    const t = setTimeout(() => {
      if (desenho.compacto) enquadrarColuna({ desenho, ids: colunaCrescida ? null : foco?.ids, largura, setViewport, reserva: interativo ? RESERVA_DOS_CONTROLES : 0 })
      else if (!cheio && enquadrarLegivel({ desenho, ids: foco?.ids, api, setViewport })) return
      else fitView({ nodes: foco?.ids?.length ? foco.ids.map((id) => ({ id })) : undefined, ...enquadrar(cheio), duration: 0 })
    }, 150)
    return () => clearTimeout(t)
    // O desenho entra só pelo `compacto`: mexer numa chave não pode reenquadrar a tela.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [foco, cheio, interativo, fitView, setViewport, largura, desenho.compacto, colunaCrescida])

  // Gaveta do ajuste no celular: depois de escolher uma etapa, o nó vai para o terço de cima da área que sobrou.
  useEffect(() => {
    if (!centrarNaSelecao) return undefined
    const t = setTimeout(() => {
      const no = ultimo.current.nos.find((n) => n.data?.selecionado && !n.id.startsWith('grupo:'))
      if (!no) return
      const { height } = api.getState()
      const zoom = Math.min(1, Math.max(getZoom(), ZOOM_LEGIVEL))
      // O nó fica centrado na área à direita dos botões de zoom, não debaixo deles.
      setCenter(no.position.x + NO_LARGURA / 2 - (interativo ? RESERVA_DOS_CONTROLES / 2 / zoom : 0), no.position.y + NO_ALTURA / 2 + (height / 6) / zoom, { zoom, duration: 0 })
    }, 280)
    return () => clearTimeout(t)
  }, [centrarNaSelecao, interativo, api, getZoom, setCenter])

  useEffect(() => {
    if (!aoMedirAltura) return
    aoMedirAltura(colunaCrescida && largura ? alturaDaColuna(desenho, largura) : null)
  }, [aoMedirAltura, colunaCrescida, desenho, largura])

  return (
    <ReactFlow
      className={css.canvas} nodes={nos} edges={arestas} nodeTypes={TIPOS_DE_NO}
      onNodesChange={(mudancas) => setPosicoes((atual) => applyNodeChanges(mudancas, atual))}
      fitView fitViewOptions={enquadrar(cheio)} minZoom={0.15} maxZoom={1.6}
      nodesDraggable={interativo} panOnDrag={interativo} zoomOnPinch={interativo} zoomOnScroll={interativo} preventScrolling={interativo}
      nodesConnectable={false} nodesFocusable={false} edgesFocusable={false} elementsSelectable={false} selectNodesOnDrag={false}
      zoomOnDoubleClick={false} ariaLabelConfig={ROTULOS} aria-label={rotuloDoDesenho} proOptions={{ hideAttribution: false }}
      attributionPosition="top-right"
    >
      <Background gap={22} size={1.5} color="var(--linha)" />
      {interativo && <Controls position="bottom-left" showInteractive={false} fitViewOptions={enquadrar(cheio)} />}
    </ReactFlow>
  )
}

export default function CanvasDoFluxo({ cheio = false, interativo = true, ...props }) {
  const [alturaDaArea, setAlturaDaArea] = useState(null)
  const ordem = useMemo(() => ordemDoFluxo(props.desenho), [props.desenho])
  const classes = [css.area, cheio ? css.cheia : '', interativo ? '' : css.semToque].filter(Boolean).join(' ')
  return (
    <div className={classes} data-orientacao={props.desenho.orientacao} data-interativo={interativo}
      style={alturaDaArea && !cheio ? { height: alturaDaArea } : undefined}>
      {/* O desenho não diz a ordem para o leitor de tela: esta lista diz, do começo ao fim do turno. */}
      <ol className="sr" aria-label="Ordem do fluxo, do começo ao fim do turno">
        {ordem.map((item) => <li key={item.id}>{item.texto}</li>)}
      </ol>
      <ReactFlowProvider><Interno {...props} cheio={cheio} interativo={interativo} aoMedirAltura={setAlturaDaArea} /></ReactFlowProvider>
    </div>
  )
}
