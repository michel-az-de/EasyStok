import { DndContext } from '@dnd-kit/core'
import { rectSortingStrategy, SortableContext, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { indicadoresDoHall } from '../../dominio/pulsoDoHall'
import { hashDoModulo } from '../../dominio/modulos'
import { alcaDoLink, useCliqueAposArrasto, useSensoresDoHall } from './arrastoDoHall'
import { colisoesDoHall } from './colisoesDoHall'
import css from './hall.module.css'

const frentes = {
  atendimento: { nome: 'Atendimento pendente', icone: 'mensagem', acao: 'Abrir Atendimento', tela: 'balcao' },
  cozinha: { nome: 'Em preparo ou na fila', icone: 'producao', acao: 'Ver fila de preparo', tela: 'fila' },
  entregas: { nome: 'Prontos para sair', icone: 'entrega', acao: 'Organizar entregas', tela: 'painel' },
  financeiro: { nome: 'Total do caixa', icone: 'caixa', acao: 'Conferir caixa', tela: 'caixa' },
}
const hora = (em) => new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' }).format(em)
const dinheiro = (valor) => valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

function Indicador({ item, movimentos }) {
  const frente = frentes[item.id]
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: item.id, transition: movimentos ? { duration: 180, easing: 'cubic-bezier(0.2, 0, 0, 1)' } : null,
  })
  const valor = item.valor === null ? '—' : item.id === 'financeiro' ? dinheiro(item.valor) : `${item.valor}${item.parcial ? '+' : ''}`
  // Só o que pede ação aparece por escrito: leitura que falhou e caixa de outro dia aberto.
  const aviso = item.estado === 'erro'
    ? `${item.em ? `Desatualizado · dados das ${hora(item.em)}` : 'Não foi possível atualizar'}${item.erro ? `: ${item.erro}` : ''}`
    : item.alerta
  return <li ref={setNodeRef} className={isDragging ? css.arrastando : undefined}
    style={{ transform: CSS.Transform.toString(transform), transition }}>
    <a ref={setActivatorNodeRef} data-indicador={item.id} data-estado={item.estado} className={css.indicador}
      href={hashDoModulo(item.id, frente.tela)} title={item.detalhe || undefined} {...alcaDoLink(attributes, listeners)}>
      <span className={css.cabecaIndicador}><MarcaIcone nome={frente.icone} tamanho={20} />{frente.nome}</span>
      <strong className={css.valor}>{valor}</strong>
      {aviso && <span className={css.frescor}><MarcaIcone nome="atencao" tamanho={16} />{aviso}</span>}
      <span className={css.oculto}>{frente.acao}</span>
    </a>
  </li>
}

// #1810: o hall mostra só o número e o caminho. A explicação de cada número fica no título do
// cartão (passar o mouse) e o frescor só aparece quando a leitura falhou ou está velha.
// #1839 D: os números arrastam direto para a ordem que a dona quiser; quem lê as fontes é o Hall.
export function PulsoDoHall({ ids, foco, fontes, movimentos = true, aoMover }) {
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
        <ul className={css.indicadores} onClickCapture={clique.onClickCapture}>
          {indicadores.map((item) => <Indicador key={item.id} item={item} movimentos={movimentos} />)}
        </ul>
      </SortableContext>
    </DndContext>
  </section>
}
