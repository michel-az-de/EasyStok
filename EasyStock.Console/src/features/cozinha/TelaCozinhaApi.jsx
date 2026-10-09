import { useEffect, useState } from 'react'
import {
  DndContext, DragOverlay, KeyboardSensor, MouseSensor, TouchSensor, useDroppable, useSensor, useSensors,
} from '@dnd-kit/core'
import { useRelogio } from '../../hooks/useRelogio'
import { dataIsoNoFuso, horaCurta } from '../../dominio/formato'
import {
  COLUNAS_KDS, canhotoDoKds, filtrarPorLinha, impressoesDaCozinha, respostaAoSoltarKds, textoDaCozinhaVazia,
} from '../../dominio/kds'
import { ORDEM_DAS_LINHAS } from '../../dominio/pedido'
import { pdfDoCanhoto } from '../../dominio/impressao'
import { useCozinhaApi } from '../../aplicacao/useCozinhaApi'
import { imprimirPdf } from '../../componentes/pdfNoNavegador'
import { Botao } from '../../componentes/Botao'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { CartaoKds, CartaoKdsErguido } from './CartaoKds'
import { ComandaKds } from './ComandaKds'
import { colisaoPorColuna, coordenadasEntreColunas } from './arrastoEntreColunas'
import css from './cozinha.module.css'

// Cozinha no modo API (F05, issue #1218): a fila real do KDS sobre Pedido (S19),
// ao vivo pelo SSE de operação (S18), com início previsto e atraso (S21).
// Issue #1446 (homologação de 07/10): o que a cozinha do protótipo faz e a do
// modo API não fazia. O canhoto é o MESMO PDF de 80 mm do protótipo
// (`dominio/impressao.js`); a fila de impressão da API vira a gaveta de canhotos
// (RN-27); "Ver comanda" abre a comanda do cliente; arrastar leva o pedido ao
// próximo passo; o filtro separa as linhas; e o quadro se mexe quando algo muda.
// A cozinha da demonstração (`TelaCozinha`, espelho do Balcão) segue intacta.
const ABERTURA = Date.now()
const MS_DA_RECUSA = 6000
const UM_DIA_MS = 86400000

const reduzMovimento = () => typeof window !== 'undefined'
  && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
const VOLTA_AO_LUGAR = { duration: 220, easing: 'cubic-bezier(.2, 0, 0, 1)' }

const rotuloDaColuna = (status) => COLUNAS_KDS.find((c) => c.status === status)?.rotulo ?? status
const ANUNCIOS = {
  onDragStart: ({ active }) => `Pegou o pedido ${active.data.current.numero} de ${active.data.current.nome}.`,
  onDragOver: ({ active, over }) => {
    if (!over) return 'Fora das colunas.'
    const r = respostaAoSoltarKds(active.data.current.status, over.id)
    if (r.aceita) return `Sobre ${rotuloDaColuna(over.id)}. Soltar aqui avança o pedido.`
    return r.motivo ? `Sobre ${rotuloDaColuna(over.id)}. ${r.motivo}` : `De volta a ${rotuloDaColuna(over.id)}.`
  },
  onDragEnd: ({ active, over }) => {
    const r = over ? respostaAoSoltarKds(active.data.current.status, over.id) : { aceita: false }
    return r.aceita ? `Pedido de ${active.data.current.nome} foi para ${rotuloDaColuna(over.id)}.` : 'O pedido ficou onde estava.'
  },
  onDragCancel: () => 'Arrasto cancelado. O pedido ficou onde estava.',
}
const INSTRUCOES = {
  draggable: 'Para mudar o pedido de coluna, aperte Espaço ou Enter. As setas escolhem a coluna,'
    + ' Espaço ou Enter solta e Esc cancela. Só a coluna do próximo passo aceita o pedido.',
}

export function TelaCozinhaApi({ linhas }) {
  const agora = useRelogio(ABERTURA, undefined, { real: true })
  // #1474: o pedido pago para amanhã não aparecia em lugar nenhum. Hoje ou Amanhã, como em
  // Entregas do dia; a API recebe a data só quando não é hoje.
  const [amanha, setAmanha] = useState(false)
  const dataDaFila = amanha ? dataIsoNoFuso(agora + UM_DIA_MS) : null
  const {
    pedidos, erro, aoVivo, movendo, novos, mudaram, saindo, impressoes,
    avancar, reimprimir, confirmarImpressao, avisar, limparErro,
  } = useCozinhaApi({ data: dataDaFila })
  const [linhaFiltro, setLinhaFiltro] = useState(null)
  const [abertoId, setAbertoId] = useState(null)
  const [arrastadoId, setArrastadoId] = useState(null)
  const [sobreId, setSobreId] = useState(null)
  const [recusa, setRecusa] = useState(null)

  useEffect(() => {
    if (!recusa) return undefined
    const t = setTimeout(() => setRecusa(null), MS_DA_RECUSA)
    return () => clearTimeout(t)
  }, [recusa])

  const sensores = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: coordenadasEntreColunas, scrollBehavior: 'auto' }),
  )

  // O MESMO PDF de 80 mm do protótipo: sem preço, "não é cupom fiscal", texto desenhado
  // pelo próprio PDF (a térmica imprime como imagem, sem a fonte interna).
  const imprimir = (pedido) => {
    try {
      imprimirPdf(pdfDoCanhoto(canhotoDoKds(pedido, linhas)))
      return true
    } catch (e) {
      avisar(`O canhoto não saiu: ${e.message}`)
      return false
    }
  }

  const lista = pedidos ?? []
  const filaDeCanhotos = impressoesDaCozinha(impressoes, lista)
  const imprimirDaFila = () => {
    const topo = filaDeCanhotos[0]
    const pedido = topo && lista.find((p) => p.id === topo.pedidoId)
    if (pedido && imprimir(pedido)) confirmarImpressao(topo.id)
  }

  const aberto = abertoId ? lista.find((p) => p.id === abertoId) : null
  const arrastado = arrastadoId ? lista.find((p) => p.id === arrastadoId) : null
  const respostaPara = (status) => (arrastado ? respostaAoSoltarKds(arrastado.status, status) : null)
  const respostaSobre = sobreId ? respostaPara(sobreId) : null
  const fantasmas = reduzMovimento() ? [] : saindo
  const visiveis = filtrarPorLinha(lista, linhaFiltro)
  const vazia = pedidos !== null && lista.length === 0

  const aoLargar = () => { setArrastadoId(null); setSobreId(null) }
  const aoSoltar = ({ active, over }) => {
    aoLargar()
    const pedido = lista.find((p) => p.id === active.id)
    if (!over || !pedido) return
    const r = respostaAoSoltarKds(pedido.status, over.id)
    if (r.aceita) avancar(pedido.id, over.id)
    else if (r.motivo) setRecusa({ id: pedido.id, motivo: r.motivo })
  }

  return (
    <div className={css.pagina}>
      <header className={css.topoPagina}>
        <h1>Cozinha</h1>
        <span className={`${css.aoVivo} ${aoVivo ? css.aoVivoLigado : ''}`}>
          <span className={css.pontoVivo} aria-hidden="true" />
          {aoVivo ? 'Ao vivo' : 'Atualizando a cada 15 s'}
        </span>
        <span className={css.horaTopo}>{horaCurta(new Date(agora).toISOString())}</span>
      </header>
      {erro && (
        <p className={css.motivoRecusa} role="alert">
          <Icone nome="circle-x" tamanho={16} /> {erro}
          <Botao variante="texto" onClick={limparErro}>Fechar</Botao>
        </p>
      )}
      {pedidos === null
        ? <p className={css.aviso}>Carregando a fila da cozinha…</p>
        : (
          <DndContext
            sensors={sensores} collisionDetection={colisaoPorColuna}
            accessibility={{ announcements: ANUNCIOS, screenReaderInstructions: INSTRUCOES }}
            onDragStart={({ active }) => { setArrastadoId(active.id); setRecusa(null) }}
            onDragOver={({ over }) => setSobreId(over?.id ?? null)}
            onDragEnd={aoSoltar} onDragCancel={aoLargar}
          >
            <div className={css.conteudo}>
              <div className={css.filtros}>
                <div className={css.chipsFiltro} role="radiogroup" aria-label="Dia da fila">
                  <Chip papel="escolha" reservarIcone ativo={!amanha} onClick={() => setAmanha(false)}>Hoje</Chip>
                  <Chip papel="escolha" reservarIcone ativo={amanha} onClick={() => setAmanha(true)}>Amanhã</Chip>
                </div>
                <div className={css.chipsFiltro} role="radiogroup" aria-label="Filtrar por linha de produto">
                  <Chip papel="escolha" ativo={linhaFiltro === null} onClick={() => setLinhaFiltro(null)}>
                    Todas as linhas
                  </Chip>
                  {ORDEM_DAS_LINHAS.map((chave) => (
                    <Chip key={chave} papel="escolha" ativo={linhaFiltro === chave} onClick={() => setLinhaFiltro(chave)}>
                      {linhas[chave]?.rotulo ?? chave}
                    </Chip>
                  ))}
                </div>
                {filaDeCanhotos.length > 0 && (
                  <output className={css.gavetaCanhotos} aria-live="polite">
                    <Icone nome="printer" tamanho={20} />
                    <span>{filaDeCanhotos.length === 1 ? '1 canhoto na fila' : `${filaDeCanhotos.length} canhotos na fila`}</span>
                    <Botao variante="primario" icone="printer" onClick={imprimirDaFila}>Imprimir</Botao>
                  </output>
                )}
              </div>
              {vazia && (
                <p className={css.cozinhaEmDia}>{textoDaCozinhaVazia({ amanha })}</p>
              )}
              <div className={css.colunas}>
                {COLUNAS_KDS.map((coluna) => {
                  const cartoes = visiveis.filter((p) => p.status === coluna.status)
                  const saindoDaqui = fantasmas.filter((p) => p.status === coluna.status)
                  return (
                    <ColunaKds
                      key={coluna.status} coluna={coluna} resposta={respostaPara(coluna.status)}
                      arrastando={Boolean(arrastado)}
                      acao={arrastado && COLUNAS_KDS.find((c) => c.status === arrastado.status)?.toque}
                    >
                      <h2>
                        {coluna.rotulo}{' '}
                        <span key={cartoes.length} className={`${css.contagem} ${css.contagemPula}`}>{cartoes.length}</span>
                      </h2>
                      <ul className={css.listaColuna}>
                        {/* #1474: com a fila toda vazia o aviso do topo basta; não repete em cada coluna. */}
                        {!vazia && cartoes.length === 0 && saindoDaqui.length === 0 && (
                          <li><Vazio titulo="Nenhum pedido">Os cartões aparecem aqui conforme o pedido avança.</Vazio></li>
                        )}
                        {cartoes.map((p) => (
                          <CartaoKds
                            key={p.id} pedido={p} agora={agora} linhas={linhas} linhaFiltro={linhaFiltro}
                            movendo={movendo.has(p.id)} novo={novos.has(p.id)} mudou={mudaram.has(p.id)}
                            recusa={recusa?.id === p.id ? recusa.motivo : null}
                            aoAvancar={avancar} aoImprimir={imprimir} aoAbrir={setAbertoId}
                          />
                        ))}
                        {saindoDaqui.map((p) => (
                          <CartaoKds key={`saindo-${p.id}`} pedido={p} agora={agora} linhas={linhas} linhaFiltro={linhaFiltro} saindo />
                        ))}
                      </ul>
                    </ColunaKds>
                  )
                })}
              </div>
            </div>
            <DragOverlay dropAnimation={respostaSobre?.aceita || reduzMovimento() ? null : VOLTA_AO_LUGAR}>
              {arrastado && <CartaoKdsErguido pedido={arrastado} agora={agora} linhas={linhas} linhaFiltro={linhaFiltro} />}
            </DragOverlay>
          </DndContext>
        )}
      {aberto && (
        <ComandaKds
          pedido={aberto} agora={agora} linhas={linhas} movendo={movendo.has(aberto.id)}
          aoAvancar={avancar} aoImprimir={imprimir} aoReimprimir={reimprimir} aoFechar={() => setAbertoId(null)}
        />
      )}
    </div>
  )
}

// A coluna é o alvo do soltar, com o mesmo convite e a mesma recusa da cozinha do protótipo.
function ColunaKds({ coluna, resposta, arrastando, acao, children }) {
  const { setNodeRef, isOver } = useDroppable({ id: coluna.status })
  const aceita = arrastando && resposta?.aceita
  const recusa = arrastando && isOver && !resposta?.aceita && resposta?.motivo
  const classes = [
    css.coluna, aceita && css.colunaAlvo, aceita && isOver && css.colunaSobre, recusa && css.colunaRecusa,
    arrastando && !aceita && !isOver && css.colunaQuieta,
  ].filter(Boolean).join(' ')
  return (
    <section ref={setNodeRef} className={classes} aria-label={coluna.rotulo}>
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
