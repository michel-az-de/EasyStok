import { memo, useEffect, useMemo, useRef, useState } from 'react'
import {
  Background, Controls, Handle, MarkerType, Position, ReactFlow, ReactFlowProvider, applyNodeChanges, useReactFlow, useStore,
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
        // #2857: o rótulo em volta dá à chave um alvo de toque de 44 px; o desenho dela continua o do base.css.
        <label className={`${css.alvoDaChave} nodrag nopan`}>
          <input type="checkbox" role="switch" checked={Boolean(valorDaChave)} aria-checked={Boolean(valorDaChave)}
            aria-label={tipo === 'ferramenta' ? `Ferramenta ${titulo}` : `Etapa ${data.indice + 1} do roteiro`}
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

// #2857: a coluna do celular não usa fitView, que encolhe tudo até caber e deixa o texto ilegível. A largura da
// coluna (com o trilho e o corredor) ocupa a tela, sem passar do tamanho real, e o topo vai para o grupo aberto;
// o resto da coluna fica a um arrasto.
const MARGEM_DO_TRILHO = 24
const MARGEM_DO_CORREDOR = 28
const MARGEM_DA_TELA = 8
function enquadrarColuna({ desenho, ids, largura, setViewport }) {
  if (!largura || !desenho.nos.length) return
  const esquerda = Math.min(...desenho.nos.map((n) => n.x)) - MARGEM_DO_TRILHO
  const direita = Math.max(...desenho.nos.map((n) => n.x)) + NO_LARGURA + MARGEM_DO_CORREDOR
  const zoom = Math.min(1, (largura - 2 * MARGEM_DA_TELA) / (direita - esquerda))
  const alvo = ids?.length ? desenho.nos.find((n) => n.id === ids[0]) : null
  const topo = alvo ? alvo.y : Math.min(...desenho.nos.map((n) => n.y))
  setViewport({ x: MARGEM_DA_TELA - esquerda * zoom, y: MARGEM_DA_TELA + 8 - topo * zoom, zoom }, { duration: 0 })
}

// `desenho` ({ nos, arestas, orientacao, compacto? }), `dadosDoNo(dados)` junta o estado da tela (selecionado, chave) aos dados
// do nó, `foco` ({ ids }) manda enquadrar quando muda (orientação ou grupo aberto).
function Interno({ desenho, dadosDoNo, foco, rotuloDoDesenho }) {
  const { fitView, setViewport } = useReactFlow()
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
    return posicoes.map((n) => ({ ...n, data: dadosDoNo(dados.get(n.id) ?? {}) }))
  }, [posicoes, desenho, dadosDoNo])
  const arestas = useMemo(() => desenho.arestas.map(paraAresta), [desenho])

  const largura = useStore((s) => s.width)
  useEffect(() => {
    if (!foco) return undefined
    const t = setTimeout(() => {
      if (desenho.compacto) enquadrarColuna({ desenho, ids: foco.ids, largura, setViewport })
      else fitView({ nodes: foco.ids?.length ? foco.ids.map((id) => ({ id })) : undefined, padding: 0.12, maxZoom: 1, minZoom: 0.55, duration: 0 })
    }, 150)
    return () => clearTimeout(t)
    // O desenho entra só pelo `compacto`: mexer numa chave não pode reenquadrar a tela.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [foco, fitView, setViewport, largura, desenho.compacto])

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
