import { useEffect, useRef, useState } from 'react'
import { DndContext } from '@dnd-kit/core'
import { rectSortingStrategy, SortableContext, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Esqueleto } from '../../componentes/Esqueleto'
import { indicadoresDoHall, ROTULO_PRECISA_DE_VOCE } from '../../dominio/pulsoDoHall'
import { moeda } from '../../dominio/formato'
import { hashDoModulo } from '../../dominio/modulos'
import { acaoDoIndicador, severidadeDoIndicador } from '../../dominio/estadoDoHall'
import { alcaDoLink, transicaoDoArrasto, useCliqueAposArrasto, useSensoresDoHall } from './arrastoDoHall'
import { colisoesDoHall } from './colisoesDoHall'
import { IconeDoModulo } from './IconeDoModulo'
import css from './hall.module.css'

// #2258 (H2/H3): o rótulo do cartão vem do domínio (`item.rotulo`, no número certo); `nome` só anuncia o
// movimento. O ícone é o do mapa único dos módulos (`IconeDoModulo`), o mesmo do cartão da grade.
const frentes = {
  atendimento: { nome: ROTULO_PRECISA_DE_VOCE, acao: 'Abrir Atendimento', tela: 'balcao' },
  cozinha: { nome: 'Em preparo ou na fila', acao: 'Ver fila de preparo', tela: 'fila' },
  entregas: { nome: 'Prontos para sair', acao: 'Organizar entregas', tela: 'painel' },
  // #2078: o rótulo do número que o Caixa do dia destaca; sem abrir o caixa vira "Em dinheiro" (`item.rotulo`).
  financeiro: { nome: 'Na gaveta (dinheiro)', acao: 'Conferir caixa', tela: 'caixa' },
}
const hora = (em) => new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' }).format(em)

function Indicador({ item, movimentos, podeFecharCaixa }) {
  const frente = frentes[item.id]
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: item.id, transition: movimentos ? transicaoDoArrasto() : null,
  })
  // #2762 (B10): enquanto a leitura não chegou o número é um esqueleto de linha; quando o valor MUDA por sync
  // (nunca na primeira chegada) o número recebe um realce único, que a troca da chave reinicia.
  const carregando = item.valor === null && item.estado === 'carregando'
  const visto = useRef(item.valor)
  const [mudancas, setMudancas] = useState(0)
  useEffect(() => {
    if (visto.current !== null && item.valor !== null && visto.current !== item.valor) setMudancas((n) => n + 1)
    visto.current = item.valor
  }, [item.valor])
  // #2792: sem leitura, nada de travessão; o texto fica só para o leitor de tela.
  const valor = item.valor === null ? <span className={css.oculto}>Sem leitura</span>
    : item.id === 'financeiro' ? moeda(item.valor) : `${item.valor}${item.parcial ? '+' : ''}`
  // Só o que pede ação aparece por escrito: leitura que falhou e caixa de outro dia aberto.
  const aviso = item.estado === 'erro'
    ? `${item.em ? `Desatualizado · dados das ${hora(item.em)}` : 'Não foi possível atualizar'}${item.erro ? `: ${item.erro}` : ''}`
    : item.alerta
  // #2232 (H6/H9): o que pede atenção vira estado do cartão, na cor de severidade, com a ação que resolve.
  const severidade = severidadeDoIndicador(item)
  const acao = acaoDoIndicador(item, { podeFecharCaixa })
  return <li ref={setNodeRef} className={isDragging ? css.arrastando : undefined}
    style={{ transform: CSS.Transform.toString(transform), transition }}>
    <a ref={setActivatorNodeRef} data-indicador={item.id} data-estado={item.estado} data-severidade={severidade ?? undefined} className={css.indicador}
      href={hashDoModulo(item.id, frente.tela)} title={item.detalhe || undefined} {...alcaDoLink(attributes, listeners)}>
      {/* #2232 (H5): o número vem primeiro e o rótulo embaixo; todos os cartões partem da mesma linha. */}
      <strong key={mudancas} className={mudancas ? `${css.valor} ${css.valorMudou}` : css.valor}>
        {carregando ? <><Esqueleto forma="linha" largura="3ch" altura="var(--t-valor)" /><span className="sr">Carregando…</span></> : valor}
      </strong>
      <span className={css.cabecaIndicador}><IconeDoModulo modulo={{ id: item.id }} tamanho={20} />{item.rotulo ?? frente.nome}</span>
      {aviso && <span className={css.frescor}><MarcaIcone nome="atencao" tamanho={16} />{aviso}</span>}
      {acao && <span className={css.acaoIndicador}>{acao}</span>}
      {/* #2258 (H4): com a ação à vista ("Fechar caixa") o texto lido é esse; o oculto só entra sem ela. */}
      {!acao && <span className={css.oculto}>{frente.acao}</span>}
    </a>
  </li>
}

// #1810: o hall mostra só o número e o caminho. A explicação de cada número fica no título do
// cartão (passar o mouse) e o frescor só aparece quando a leitura falhou ou está velha.
// #1839 D: os números arrastam direto para a ordem que a dona quiser; quem lê as fontes é o Hall.
// #2677 (H3): `podeFecharCaixa` vem da permissão de gerenciar o caixa; sem ela o cartão não oferece "Fechar caixa".
export function PulsoDoHall({ ids, foco, fontes, movimentos = true, podeFecharCaixa = false, aoMover }) {
  const sensores = useSensoresDoHall()
  const clique = useCliqueAposArrasto()
  const indicadores = indicadoresDoHall(fontes, ids)
  if (!ids.length) return null
  const nome = (id) => frentes[id]?.nome ?? 'Número'
  const posicao = (id) => `${ids.indexOf(id) + 1} de ${ids.length}`
  return <section className={css.pulso} aria-labelledby="pulso-titulo">
    <h2 id="pulso-titulo" className={css.oculto}>Agora na casa · {foco}</h2>
    <DndContext sensors={sensores} collisionDetection={colisoesDoHall}
      onDragStart={clique.aoComecar} onDragCancel={clique.aoTerminar}
      onDragEnd={({ active, over }) => { clique.aoTerminar(); if (over && over.id !== active.id) aoMover(active.id, over.id) }}
      accessibility={{
        screenReaderInstructions: { draggable: 'Enter abre. Para mudar a ordem dos números, pressione Espaço, use as setas e Espaço de novo para soltar, ou Escape para cancelar.' },
        announcements: {
          onDragStart: ({ active }) => `${nome(active.id)} selecionado para mover.`,
          onDragOver: ({ active, over }) => over ? `${nome(active.id)} sobre a posição ${posicao(over.id)}.` : `${nome(active.id)} fora da faixa de números.`,
          onDragEnd: ({ active, over }) => over ? `${nome(active.id)} solto na posição ${posicao(over.id)}.` : 'Movimento cancelado. A ordem foi mantida.',
          onDragCancel: () => 'Movimento cancelado. A ordem foi mantida.',
        },
      }}>
      <SortableContext items={ids} strategy={rectSortingStrategy}>
        <ul className={css.indicadores} data-total={indicadores.length} onClickCapture={clique.onClickCapture}>
          {indicadores.map((item) => <Indicador key={item.id} item={item} movimentos={movimentos} podeFecharCaixa={podeFecharCaixa} />)}
        </ul>
      </SortableContext>
    </DndContext>
  </section>
}
