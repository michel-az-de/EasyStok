import { useEffect, useState } from 'react'
import {
  DndContext, DragOverlay, KeyboardSensor, MouseSensor, pointerWithin, rectIntersection, TouchSensor,
  useDroppable, useSensor, useSensors,
} from '@dnd-kit/core'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { ORDEM_DAS_LINHAS, itensDetalhados } from '../../dominio/pedido'
import { estaBloqueada } from '../../dominio/conversa'
import { passoPorId } from '../../dominio/esteira'
import { PASSOS_DA_COZINHA, ROTULO_DO_TOQUE, pedidosNaCozinha, respostaAoSoltar } from '../../dominio/cozinha'
import { opcoesDoDespacho } from '../../dominio/despacho'
import { CartaoCozinha, CartaoErguido } from './CartaoCozinha'
import css from './cozinha.module.css'

// Quanto tempo a recusa fica escrita no cartão e quanto dura o "assentar".
// A recusa fica o bastante para ler a frase inteira de relance, com a mão
// ocupada (6 s); o assentar acompanha `--dur-longa` do CSS. O pedido só muda
// de coluna quando o Balcão devolve o estado pelo espelho
// (`useEspelhoDeCozinha`), e com o Balcão em aba de fundo isso passou de
// 700 ms na medição: o assentar conta a partir da CHEGADA, e desiste depois de
// 5 s sem resposta.
const MS_DA_RECUSA = 6000
const MS_DO_ASSENTAR = 500
const MS_SEM_ESPELHO = 5000

// Arrastar e soltar (issue #7, rodada 11). Mouse arrasta depois de 6 px e o
// toque depois de 250 ms parado, os mesmos números da seção 9 da direção
// visual (arrasto do cardápio), para não brigar com a rolagem da coluna no
// tablet. Mouse e toque em sensores separados: com PointerSensor o dedo que
// só quer rolar a coluna viraria arrasto no sexto pixel.
const rotuloDaColuna = (id) => passoPorId(id)?.rotulo ?? id

// Teclado: setas para a esquerda e para a direita pulam de coluna em coluna
// (o KeyboardSensor padrão anda 25 px por tecla, e cinco colunas de 220 px
// virariam dezenas de toques).
function coordenadasEntreColunas(evento, { context: { collisionRect, droppableRects, droppableContainers } }) {
  const direita = evento.code === 'ArrowRight'
  if ((!direita && evento.code !== 'ArrowLeft') || !collisionRect) return undefined
  evento.preventDefault()
  const centro = collisionRect.left + collisionRect.width / 2
  const alvos = droppableContainers.getEnabled()
    .map((coluna) => droppableRects.get(coluna.id))
    .filter(Boolean)
    .filter((r) => (direita ? r.left + r.width / 2 > centro + 1 : r.left + r.width / 2 < centro - 1))
  if (alvos.length === 0) return undefined
  const distancia = (r) => Math.abs(r.left + r.width / 2 - centro)
  const alvo = alvos.reduce((a, b) => (distancia(a) <= distancia(b) ? a : b))
  return { x: alvo.left + (alvo.width - collisionRect.width) / 2, y: collisionRect.top }
}

// Ponteiro primeiro (solta onde a mão está); sem ponteiro, que é o teclado,
// a coluna que mais cobre o cartão.
const colisaoPorColuna = (args) => {
  const soba = pointerWithin(args)
  return soba.length > 0 ? soba : rectIntersection(args)
}

// Anúncios para leitor de tela em PT-BR (o padrão do dnd-kit fala inglês).
const respostaDoArrasto = (dados, destino) => respostaAoSoltar(dados.estado, destino, { bloqueado: dados.bloqueado })
const ANUNCIOS = {
  onDragStart: ({ active }) => {
    const d = active.data.current
    return `Pegou o pedido ${d.numero} de ${d.nome}, em ${rotuloDaColuna(d.estado)}.`
  },
  onDragOver: ({ active, over }) => {
    const d = active.data.current
    if (!over) return 'Fora das colunas.'
    const resposta = respostaDoArrasto(d, over.id)
    if (resposta.aceita) return `Sobre ${rotuloDaColuna(over.id)}. Soltar aqui leva o pedido para ${rotuloDaColuna(over.id)}.`
    if (resposta.motivo) return `Sobre ${rotuloDaColuna(over.id)}. Não dá para soltar aqui. ${resposta.motivo}`
    return `De volta a ${rotuloDaColuna(over.id)}, onde o pedido já está.`
  },
  onDragEnd: ({ active, over }) => {
    const d = active.data.current
    const resposta = over ? respostaDoArrasto(d, over.id) : { aceita: false, motivo: null }
    if (resposta.aceita) return `Pedido de ${d.nome} foi para ${rotuloDaColuna(over.id)}.`
    if (resposta.motivo) return `Pedido de ${d.nome} voltou para ${rotuloDaColuna(d.estado)}. ${resposta.motivo}`
    return `Pedido de ${d.nome} ficou em ${rotuloDaColuna(d.estado)}.`
  },
  onDragCancel: ({ active }) => {
    const d = active.data.current
    return `Arrasto cancelado. Pedido de ${d.nome} ficou em ${rotuloDaColuna(d.estado)}.`
  },
}
const INSTRUCOES = {
  draggable: 'Para mudar o pedido de coluna, aperte Espaço ou Enter. As setas para a esquerda e para a direita'
    + ' escolhem a coluna, Espaço ou Enter solta e Esc cancela. Só a coluna do próximo passo aceita o pedido.',
}

// Com movimento reduzido o cartão não voa de volta: some e reaparece no lugar.
// O dnd-kit anima pela Web Animations API, que o CSS global de base.css não
// alcança; mesma leitura que `PainelCardapio.jsx` faz.
const reduzMovimento = () => typeof window !== 'undefined'
  && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
const VOLTA_AO_LUGAR = { duration: 220, easing: 'cubic-bezier(.2, 0, 0, 1)' }

// A coluna é o alvo do soltar. Durante o arrasto ela mostra, antes de a mão
// chegar, se aceita: a do próximo passo ganha a borda tracejada da marca e o
// convite "Solte aqui" com o nome do toque (o mesmo texto do botão); com o cartão em cima de uma coluna que recusa, ela
// fica no tom de perigo e escreve o motivo (a recusa é visível antes, não só
// depois do soltar).
function ColunaCozinha({ passo, resposta, arrastando, acao, children }) {
  const { setNodeRef, isOver } = useDroppable({ id: passo.id })
  const aceita = arrastando && resposta?.aceita
  const recusa = arrastando && isOver && !resposta?.aceita && resposta?.motivo
  const classes = [
    css.coluna, aceita && css.colunaAlvo, aceita && isOver && css.colunaSobre, recusa && css.colunaRecusa,
    arrastando && !aceita && !isOver && css.colunaQuieta,
  ].filter(Boolean).join(' ')
  return (
    <section ref={setNodeRef} className={classes} aria-label={passo.rotulo}>
      {children}
      {aceita && (
        <p className={css.dicaColuna} aria-hidden="true">
          <Icone nome="check" tamanho={16} /> Solte aqui: {acao}
        </p>
      )}
      {recusa && (
        <p className={`${css.dicaColuna} ${css.dicaRecusa}`} aria-hidden="true">
          <Icone nome="circle-x" tamanho={16} /> {resposta.motivo}
        </p>
      )}
    </section>
  )
}

// Conteúdo da tela de Cozinha: colunas pelos passos da esteira (US-037) e o
// filtro por linha de produto (UC-04, fluxo alternativo A: "a dona filtra a
// tela por linha e trata cada fila separadamente"). Colunas vêm de
// `PASSOS_DA_COZINHA`, que é dado (dominio/esteira.js), não código: um passo
// novo na esteira não pede tocar aqui.
export function ConteudoCozinha({
  conversas, janelas, cardapio, linhas, agora, acoes, destacados, entregadores = [],
}) {
  const [linhaFiltro, setLinhaFiltro] = useState(null)
  // Entregue recolhe atrás de uma contagem (quem cozinha olha o que ainda
  // precisa de ação, não o que já saiu). Mesmo mecanismo de GrupoRecolhido
  // em caixa-de-entrada/PainelConversas.jsx: contagem + chevron, abre com
  // um toque, fecha por padrão a cada carga da tela.
  const [entregueAberto, setEntregueAberto] = useState(false)
  // Estado do arrasto (issue #7): quem está na mão, sobre qual coluna, a
  // recusa que ficou escrita no cartão e o cartão que acabou de assentar.
  const [arrastadoId, setArrastadoId] = useState(null)
  const [sobreId, setSobreId] = useState(null)
  const [recusa, setRecusa] = useState(null)
  const [assentando, setAssentando] = useState(null)
  const [pedindoEntregadorDe, setPedindoEntregadorDe] = useState(null)

  useEffect(() => {
    if (!recusa) return undefined
    const t = setTimeout(() => setRecusa(null), MS_DA_RECUSA)
    return () => clearTimeout(t)
  }, [recusa])

  const sensores = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 5 } }),
    // Rolagem instantânea: com a rolagem suave do padrão, Espaço logo depois
    // da seta soltava o cartão na coluna de antes (medido na rodada 11).
    useSensor(KeyboardSensor, { coordinateGetter: coordenadasEntreColunas, scrollBehavior: 'auto' }),
  )

  const pedidos = pedidosNaCozinha(conversas)
  // Issue #17: despacho com entregador de um toque (cadastrado ou digitado
  // hoje) e os campos de veículo, placa e empresa.
  const opcoesDespacho = opcoesDoDespacho(conversas, entregadores)
  const chegou = Boolean(assentando) && pedidos.some(
    (c) => c.id === assentando.id && c.pedido.estado === assentando.destino,
  )
  useEffect(() => {
    if (!assentando) return undefined
    const t = setTimeout(() => setAssentando(null), chegou ? MS_DO_ASSENTAR : MS_SEM_ESPELHO)
    return () => clearTimeout(t)
  }, [assentando, chegou])
  const arrastado = arrastadoId ? pedidos.find((c) => c.id === arrastadoId) : null
  const respostaPara = (destino) => (arrastado
    ? respostaAoSoltar(arrastado.pedido.estado, destino, { bloqueado: estaBloqueada(arrastado) })
    : null)

  const passaNoFiltro = (conversa) => {
    if (!linhaFiltro) return true
    const itens = itensDetalhados(conversa.pedido, cardapio)
    return itens.some((l) => l.produto?.linha === linhaFiltro)
  }

  const colunas = PASSOS_DA_COZINHA.map((passo) => ({
    passo,
    cartoes: pedidos.filter((c) => c.pedido.estado === passo.id).filter(passaNoFiltro),
  }))

  const aoPegar = ({ active }) => {
    setArrastadoId(active.id)
    setRecusa(null)
  }
  const aoPassar = ({ over }) => setSobreId(over?.id ?? null)
  const aoLargar = () => {
    setArrastadoId(null)
    setSobreId(null)
  }
  // Soltar = o mesmo toque do botão (RN-31): o mesmo `acoes.avancarEsteira`,
  // e por ele o mesmo reducer, o mesmo aviso ao cliente (RN-32) e a mesma
  // trava de bloqueio (RN-14). Recusa não despacha nada: escreve o motivo no
  // cartão, que voltou para a coluna de onde saiu.
  const aoSoltar = ({ active, over }) => {
    aoLargar()
    const conversa = pedidos.find((c) => c.id === active.id)
    if (!over || !conversa) return
    const resposta = respostaAoSoltar(conversa.pedido.estado, over.id, { bloqueado: estaBloqueada(conversa) })
    if (!resposta.aceita) {
      if (resposta.motivo) setRecusa({ id: conversa.id, motivo: resposta.motivo })
      return
    }
    // US-040: sem entregador resolvido, a MESMA pergunta que o botão abre.
    if (active.data.current?.precisaDeEntregador) {
      setPedindoEntregadorDe(conversa.id)
      return
    }
    acoes.avancarEsteira(conversa.id, over.id)
    setAssentando({ id: conversa.id, destino: over.id })
  }

  const respostaSobre = sobreId ? respostaPara(sobreId) : null
  const animacaoDeVolta = respostaSobre?.aceita || reduzMovimento() ? null : VOLTA_AO_LUGAR

  return (
    <DndContext
      sensors={sensores} collisionDetection={colisaoPorColuna}
      accessibility={{ announcements: ANUNCIOS, screenReaderInstructions: INSTRUCOES }}
      onDragStart={aoPegar} onDragOver={aoPassar} onDragEnd={aoSoltar} onDragCancel={aoLargar}
    >
      <div className={css.conteudo}>
        <div className={css.filtros} role="radiogroup" aria-label="Filtrar por linha de produto">
          <Chip papel="escolha" ativo={linhaFiltro === null} onClick={() => setLinhaFiltro(null)}>
            Todas as linhas
          </Chip>
          {ORDEM_DAS_LINHAS.map((chave) => (
            <Chip key={chave} papel="escolha" ativo={linhaFiltro === chave} onClick={() => setLinhaFiltro(chave)}>
              {linhas[chave]?.rotulo ?? chave}
            </Chip>
          ))}
        </div>

        <div className={css.colunas}>
          {colunas.map(({ passo, cartoes }) => {
            const recolhivel = passo.id === 'entregue'
            return (
              <ColunaCozinha
                key={passo.id} passo={passo} resposta={respostaPara(passo.id)} arrastando={Boolean(arrastado)}
                acao={arrastado && ROTULO_DO_TOQUE[arrastado.pedido.estado]}
              >
                {recolhivel ? (
                  <button
                    type="button"
                    className={css.tituloRecolhivel}
                    aria-expanded={entregueAberto}
                    onClick={() => setEntregueAberto((v) => !v)}
                  >
                    <Icone nome="chevron-right" tamanho={16} className={entregueAberto ? css.chevronAberto : ''} />
                    {passo.rotulo} <span className={css.contagem}>{cartoes.length}</span>
                  </button>
                ) : (
                  <h2>
                    {passo.rotulo} <span className={css.contagem}>{cartoes.length}</span>
                  </h2>
                )}
                {(!recolhivel || entregueAberto) && (
                  <ul className={css.listaColuna}>
                    {cartoes.length === 0 && (
                      <li>
                        <Vazio titulo="Nenhum pedido">Os cartões aparecem aqui conforme o pedido avança.</Vazio>
                      </li>
                    )}
                    {cartoes.map((conversa) => (
                      <CartaoCozinha
                        key={conversa.id}
                        conversa={conversa}
                        janelas={janelas}
                        cardapio={cardapio}
                        linhas={linhas}
                        agora={agora}
                        linhaFiltro={linhaFiltro}
                        acoes={acoes}
                        destacado={destacados?.has(conversa.id) ?? false}
                        recusa={recusa?.id === conversa.id ? recusa.motivo : null}
                        assentando={chegou && assentando.id === conversa.id}
                        aCaminho={!chegou && assentando?.id === conversa.id}
                        pedindoEntregador={pedindoEntregadorDe === conversa.id}
                        aoPedirEntregador={(aberto) => setPedindoEntregadorDe(aberto ? conversa.id : null)}
                        opcoesDespacho={opcoesDespacho}
                      />
                    ))}
                  </ul>
                )}
              </ColunaCozinha>
            )
          })}
        </div>
      </div>

      <DragOverlay dropAnimation={animacaoDeVolta}>
        {arrastado && (
          <CartaoErguido
            conversa={arrastado} janelas={janelas} cardapio={cardapio} linhas={linhas} agora={agora}
            linhaFiltro={linhaFiltro}
          />
        )}
      </DragOverlay>
    </DndContext>
  )
}
