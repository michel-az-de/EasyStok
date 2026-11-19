import { memo, useEffect, useMemo, useRef, useState } from 'react'
import {
  Background, Controls, Handle, MarkerType, Position, ReactFlow, ReactFlowProvider, applyNodeChanges, useReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Icone } from '../../../componentes/Icone'
import { NO_ALTURA, NO_LARGURA } from '../../../dominio/desenhoDoFluxo'
import css from './canvasDoFluxo.module.css'

// #2855: o desenho do fluxo do Assistente como canvas (React Flow). Este arquivo é carregado sob demanda
// (React.lazy em PainelAssistente) para a biblioteca não entrar no bundle inicial do console.
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

function Alcas() {
  return Object.entries(LADOS).map(([lado, posicao]) => (
    <span key={lado}>
      <Handle id={`${lado}-s`} type="source" position={posicao} className={css.alca} isConnectable={false} />
      <Handle id={`${lado}-t`} type="target" position={posicao} className={css.alca} isConnectable={false} />
    </span>
  ))
}

const NoDoFluxo = memo(function NoDoFluxo({ data }) {
  const { id, tipo, icone, titulo, subtitulo, selecionado, desligado, grupo, aberto, escala, ordem, ligada, travada, ativo, podeEditar, ocupado } = data
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
      <button type="button" className={css.corpo} aria-pressed={selecionado} aria-expanded={grupo ? aberto : undefined}
        onPointerDown={(e) => { inicio.current = { x: e.clientX, y: e.clientY } }} onClick={ativar}>
        <span className={css.icone}><Icone nome={icone} tamanho={20} /></span>
        <span className={css.texto}>
          <strong>{tipo === 'ferramenta' ? titulo.replaceAll('_', '_\u200b') : titulo}</strong>
          {subtitulo && <span>{subtitulo}</span>}
        </span>
        {grupo && <span className={css.abrir}><Icone nome={aberto ? 'menos' : 'mais'} tamanho={16} rotulo={aberto ? 'Recolher' : 'Abrir'} /></span>}
        {tipo === 'portao' && (
          <span className={css.ordem}>
            {ordem}
            {escala && <Icone nome="usuarios" tamanho={16} rotulo="Passa a conversa para a equipe" />}
          </span>
        )}
      </button>
      {tipo === 'ferramenta' && travada && <span className={css.cadeado}><Icone nome="lock" tamanho={16} rotulo="Sempre ligada" /></span>}
      {comChave && (
        <input type="checkbox" role="switch" className={`${css.chave} nodrag nopan`} checked={Boolean(valorDaChave)} aria-checked={Boolean(valorDaChave)}
          aria-label={tipo === 'ferramenta' ? `Ferramenta ${titulo}` : `Etapa ${data.indice + 1} do roteiro`}
          disabled={!podeEditar || ocupado} onChange={() => data.aoAlternar(data)} />
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
    markerEnd: { type: MarkerType.ArrowClosed, color: cor, width: 18, height: 18 },
    style: { stroke: cor, strokeWidth: equipe ? 2.5 : 2, strokeDasharray: a.tracejada ? '7 6' : undefined },
    labelStyle: { fill: 'var(--tinta)', fontWeight: 700, fontSize: 14 },
    labelBgStyle: { fill: 'var(--papel)', stroke: 'var(--marca)', strokeWidth: 1 },
    labelBgPadding: [8, 4], labelBgBorderRadius: 8,
  }
}

// `desenho` ({ nos, arestas, orientacao }), `dadosDoNo(dados)` junta o estado da tela (selecionado, chave) aos dados
// do nó, `foco` ({ ids }) manda enquadrar quando muda (orientação ou grupo aberto).
function Interno({ desenho, dadosDoNo, foco, rotuloDoDesenho }) {
  const { fitView } = useReactFlow()
  const [posicoes, setPosicoes] = useState([])
  const orientacaoAnterior = useRef(desenho.orientacao)

  useEffect(() => {
    const mudouDeLado = orientacaoAnterior.current !== desenho.orientacao
    orientacaoAnterior.current = desenho.orientacao
    setPosicoes((antes) => {
      const porId = new Map(antes.map((n) => [n.id, n]))
      return desenho.nos.map((n) => {
        const velho = porId.get(n.id)
        return { id: n.id, type: 'fluxo', position: velho && !mudouDeLado ? velho.position : { x: n.x, y: n.y }, measured: velho?.measured, data: {} }
      })
    })
  }, [desenho])

  const nos = useMemo(() => {
    const dados = new Map(desenho.nos.map((n) => [n.id, n.dados]))
    return posicoes.map((n) => ({ ...n, data: dadosDoNo(dados.get(n.id) ?? {}) }))
  }, [posicoes, desenho, dadosDoNo])
  const arestas = useMemo(() => desenho.arestas.map(paraAresta), [desenho])

  useEffect(() => {
    if (!foco) return undefined
    const t = setTimeout(() => fitView({ nodes: foco.ids?.length ? foco.ids.map((id) => ({ id })) : undefined, padding: 0.12, maxZoom: 1, minZoom: window.matchMedia?.('(max-width: 899px)').matches ? 0.3 : 0.55, duration: 0 }), 150)
    return () => clearTimeout(t)
  }, [foco, fitView])

  return (
    <ReactFlow
      className={css.canvas} nodes={nos} edges={arestas} nodeTypes={TIPOS_DE_NO}
      onNodesChange={(mudancas) => setPosicoes((atual) => applyNodeChanges(mudancas, atual))}
      fitView fitViewOptions={{ padding: 0.12, maxZoom: 1, minZoom: 0.55 }} minZoom={0.15} maxZoom={1.6}
      nodesConnectable={false} nodesFocusable={false} edgesFocusable={false} elementsSelectable={false} selectNodesOnDrag={false}
      zoomOnDoubleClick={false} ariaLabelConfig={ROTULOS} aria-label={rotuloDoDesenho} proOptions={{ hideAttribution: false }}
      attributionPosition="top-right"
    >
      <Background gap={22} size={1.5} color="var(--linha)" />
      <Controls position="bottom-left" showInteractive={false} fitViewOptions={{ padding: 0.12, maxZoom: 1, minZoom: 0.55 }} />
    </ReactFlow>
  )
}

export default function CanvasDoFluxo(props) {
  return (
    <div className={css.area} data-orientacao={props.desenho.orientacao}>
      <ReactFlowProvider><Interno {...props} /></ReactFlowProvider>
    </div>
  )
}
