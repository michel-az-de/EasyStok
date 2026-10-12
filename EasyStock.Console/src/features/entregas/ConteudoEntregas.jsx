import { useState } from 'react'
import {
  closestCenter, DndContext, KeyboardSensor, PointerSensor, TouchSensor, useDroppable, useSensor,
  useSensors,
} from '@dnd-kit/core'
import { Chip } from '../../componentes/Chip'
import { horaCurta, plural } from '../../dominio/formato'
import { ocupacaoDeHoje, rotuloDaOcupacao } from '../../dominio/entrega'
import { EXPLICACAO_JANELA, opcoesDoDespacho } from '../../dominio/despacho'
import { agruparPorBairro, bairroDe } from '../../dominio/alcance'
import {
  entregasDeHoje, entregasSoltas, estadoDaEntrega, ordenarEntregas, proximoPassoDeEntrega,
  ordemSugeridaDaViagem, viagensDeHoje,
} from '../../dominio/viagem'
import { CartaoEntrega } from './CartaoEntrega'
import { PainelViagem } from './PainelViagem'
import { ModalGerarRota } from './ModalGerarRota'
import { ModalAlterarAgendamento } from './ModalAlterarAgendamento'
import { PainelAlcance } from './PainelAlcance'
import { ResumoDoDia } from './ResumoDoDia'
import css from './dashboard.module.css'

const CHIPS_ESTADO = [
  { chave: 'preparar', rotulo: 'A preparar' },
  { chave: 'pronta', rotulo: 'Prontas' },
  { chave: 'com-entregador', rotulo: 'Com entregador' },
  { chave: 'entregue', rotulo: 'Entregues' },
  { chave: 'cancelada', rotulo: 'Canceladas' },
]

const CHIPS_ORDEM = [
  { valor: 'proxima', rotulo: 'Mais próxima' },
  { valor: 'longe', rotulo: 'Mais longe' },
  // Issue #17: "janela" sozinha não dizia o que é; o rótulo nomeia e a dica
  // embaixo explica (`EXPLICACAO_JANELA`).
  { valor: 'janela', rotulo: 'Por janela de entrega' },
  { valor: 'bairro', rotulo: 'Por bairro' },
]

function ZonaNovaViagem() {
  const { setNodeRef, isOver } = useDroppable({ id: 'nova-viagem', data: { type: 'nova-viagem' } })
  return (
    <div ref={setNodeRef} className={`${css.zonaNovaViagem} ${isOver ? css.zonaNovaViagemSobre : ''}`}>
      Solte aqui: nova viagem
    </div>
  )
}

// Conteúdo inteiro da tela de Entregas (seção 6): a gaveta e a janela própria
// mostram o MESMO componente, só a moldura muda (`GavetaEntregas.jsx` e
// `TelaEntregas.jsx`). DndContext próprio, aninhado dentro do da Composição
// principal quando é a gaveta: dnd-kit escopa `useDraggable`/`useDroppable`
// pelo contexto mais próximo, então o arrasto daqui nunca colide com o do
// cardápio/comanda (App.jsx não precisa saber que esta tela arrasta nada).
export function ConteudoEntregas({
  conversas, janelas, agora, cardapio, enderecoDaCasa, constantes, acoes, destacados, entregadores = [],
  integracoesLogistica,
}) {
  const [filtroEstados, setFiltroEstados] = useState(() => new Set())
  const [modoOrdem, setModoOrdem] = useState('proxima')
  const [agendamentoDe, setAgendamentoDe] = useState(null)
  const [rotaDe, setRotaDe] = useState(null)
  // Issue #17: filtro por bairro (null = todos) e o painel de alcance.
  const [filtroBairro, setFiltroBairro] = useState(null)
  const [alcanceAberto, setAlcanceAberto] = useState(false)

  // Gerar rota aplica a ordem sugerida na viagem e guarda a anterior para o
  // Desfazer da modal. Ordem que já era a sugerida não muda nada nem oferece
  // desfazer.
  const gerarRota = (viagem) => {
    const anterior = viagem.paradas.map((c) => c.id)
    const sugerida = ordemSugeridaDaViagem(viagem.paradas, janelas, agora).map((c) => c.id)
    const mudou = sugerida.some((id, i) => id !== anterior[i])
    if (mudou) acoes.aplicarOrdemDaViagem(viagem.id, sugerida)
    setRotaDe({ id: viagem.id, anterior: mudou ? anterior : null })
  }

  const sensores = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 5 } }),
    useSensor(KeyboardSensor),
  )

  const todasHoje = entregasDeHoje(conversas)
  const viagens = viagensDeHoje(conversas)
  const viagensPorId = new Map(viagens.map((v) => [v.id, v]))

  const contagemPorEstado = Object.fromEntries(
    CHIPS_ESTADO.map(({ chave }) => [chave, todasHoje.filter((c) => estadoDaEntrega(c.pedido).chave === chave).length]),
  )

  const mostrar = (conversa) => {
    const chave = estadoDaEntrega(conversa.pedido).chave
    if (filtroEstados.size === 0) return chave !== 'entregue' && chave !== 'cancelada'
    return filtroEstados.has(chave)
  }

  const visiveis = entregasSoltas(conversas).filter(mostrar)
  const bairrosDoDia = agruparPorBairro(visiveis)
  // Bairro escolhido que sumiu da lista (a última entrega dele saiu) volta a
  // mostrar todos, em vez de deixar a coluna vazia sem motivo à vista.
  const bairroAtivo = bairrosDoDia.some((g) => g.bairro === filtroBairro) ? filtroBairro : null
  const soltas = ordenarEntregas(
    visiveis.filter((c) => !bairroAtivo || bairroDe(c) === bairroAtivo), modoOrdem,
    { janelas, agora, constantes, viagensPorId },
  )
  const opcoesDespacho = opcoesDoDespacho(conversas, entregadores)

  const alternarFiltro = (chave) => setFiltroEstados((atual) => {
    const novo = new Set(atual)
    if (novo.has(chave)) novo.delete(chave); else novo.add(chave)
    return novo
  })

  // Próxima conferência (seção 6, cabeçalho): o marco "conferir" mais perto,
  // entre viagens e entregas soltas, ainda não ultrapassado.
  const proximosConferir = [
    ...viagens.map((v) => proximoPassoDeEntrega(v.paradas[0], v, janelas, agora, constantes)),
    ...entregasSoltas(conversas).map((c) => proximoPassoDeEntrega(c, null, janelas, agora, constantes)),
  ].filter((p) => p?.chave === 'conferir' && p.quandoMs != null)
  const proximaConferencia = proximosConferir.length
    ? proximosConferir.reduce((a, b) => (a.quandoMs < b.quandoMs ? a : b))
    : null

  const aoTerminarArrasto = ({ active, over }) => {
    if (!over || active.data.current?.type !== 'entrega') return
    if (over.data.current?.type === 'viagem') acoes.porNaViagem(over.data.current.viagemId, active.id)
    else if (over.data.current?.type === 'nova-viagem') acoes.criarViagem(active.id, 'entregador')
  }

  let grupos = null
  if (modoOrdem === 'janela') {
    grupos = ocupacaoDeHoje(janelas, conversas, agora, null, cardapio).map((ocupacao) => ({
      chave: ocupacao.id,
      titulo: `${ocupacao.faixa} · ${rotuloDaOcupacao(ocupacao)}`,
      itens: soltas.filter((c) => c.pedido.janela === ocupacao.id),
    })).filter((g) => g.itens.length > 0)
  } else if (modoOrdem === 'bairro') {
    grupos = agruparPorBairro(soltas).map(({ bairro, itens }) => ({
      chave: bairro, titulo: `${bairro} · ${itens.length === 1 ? '1 entrega' : `${itens.length} entregas`}`, itens,
    }))
  }

  const cartao = (conversa) => (
    <CartaoEntrega
      key={conversa.id} conversa={conversa} janelas={janelas} agora={agora}
      constantes={constantes} cardapio={cardapio} acoes={acoes} opcoesDespacho={opcoesDespacho}
      grupoDaViagem={conversa.pedido.viagem ? viagensPorId.get(conversa.pedido.viagem.id) : null}
      viagensDisponiveis={viagens.map((v, i) => ({ id: v.id, rotulo: String(i + 1) })).filter((v) => v.id !== conversa.pedido.viagem?.id)}
      aoAbrirAgendamento={() => setAgendamentoDe(conversa.id)}
      destacado={destacados?.has(conversa.id) ?? false}
    />
  )

  const conversaDoAgendamento = agendamentoDe && conversas.find((c) => c.id === agendamentoDe)

  return (
    <DndContext sensors={sensores} collisionDetection={closestCenter} onDragEnd={aoTerminarArrasto}>
      <div className={css.dashboard}>
        <div className={css.faixaControles}>
          <div className={css.linhaChips}>
            {CHIPS_ESTADO.map(({ chave, rotulo }) => (
              <Chip key={chave} papel="filtro" ativo={filtroEstados.has(chave)} onClick={() => alternarFiltro(chave)}>
                {rotulo} <span className={css.contadorChip}>{contagemPorEstado[chave]}</span>
              </Chip>
            ))}
          </div>
          <div className={css.linhaChips} role="radiogroup" aria-label="Ordenar entregas">
            <strong>Ordenar:</strong>
            {CHIPS_ORDEM.map(({ valor, rotulo }) => (
              <Chip key={valor} papel="escolha" ativo={modoOrdem === valor} onClick={() => setModoOrdem(valor)}>
                {rotulo}
              </Chip>
            ))}
          </div>
          {modoOrdem === 'janela' && <p className={css.dicaJanela}>{EXPLICACAO_JANELA}</p>}
          {bairrosDoDia.length > 1 && (
            <div className={css.linhaChips} role="radiogroup" aria-label="Filtrar entregas por bairro">
              <strong>Bairro:</strong>
              <Chip papel="escolha" ativo={bairroAtivo === null} onClick={() => setFiltroBairro(null)}>
                Todos <span className={css.contadorChip}>{visiveis.length}</span>
              </Chip>
              {bairrosDoDia.map(({ bairro, itens }) => (
                <Chip key={bairro} papel="escolha" ativo={bairroAtivo === bairro} onClick={() => setFiltroBairro(bairro)}>
                  {bairro} <span className={css.contadorChip}>{itens.length}</span>
                </Chip>
              ))}
            </div>
          )}
          <div className={css.linhaChips}>
            <Chip papel="filtro" ativo={alcanceAberto} icone="map-pin" onClick={() => setAlcanceAberto((v) => !v)}>
              Alcance por bairro
            </Chip>
          </div>
          <ResumoDoDia conversas={conversas} cardapio={cardapio} agora={agora} />
          {proximaConferencia && (
            <p className={css.proximaConferencia}>
              Próxima conferência às {horaCurta(new Date(proximaConferencia.quandoMs).toISOString())},
              em {plural(Math.max(0, Math.round((proximaConferencia.quandoMs - agora) / 60000)), 'minuto', 'minutos')}.
            </p>
          )}
        </div>

        {alcanceAberto && <PainelAlcance conversas={conversas} agora={agora} cardapio={cardapio} />}

        <div className={css.colunas}>
          <section className={css.coluna}>
            <h2>Entregas sem viagem</h2>
            {soltas.length === 0 && <p className={css.proximaConferencia}>Nenhuma entrega solta agora.</p>}
            {grupos
              ? (
                <div className={css.listaColuna}>
                  {grupos.map(({ chave, titulo, itens }) => (
                    <div key={chave} className={css.grupoJanela}>
                      <p className={css.tituloGrupo}>{titulo}</p>
                      <ul className={css.listaColuna}>{itens.map(cartao)}</ul>
                    </div>
                  ))}
                </div>
              )
              : <ul className={css.listaColuna}>{soltas.map(cartao)}</ul>}
            <ZonaNovaViagem />
          </section>

          <section className={css.coluna}>
            <h2>Viagens</h2>
            <ul className={css.listaColuna}>
              {viagens.map((viagem, indice) => (
                <PainelViagem
                  key={viagem.id} viagem={viagem} indice={indice + 1} janelas={janelas} agora={agora}
                  constantes={constantes} acoes={acoes} aoAbrirGerarRota={gerarRota}
                  integracoesLogistica={integracoesLogistica}
                />
              ))}
              {viagens.length === 0 && <p className={css.proximaConferencia}>Nenhuma viagem montada. Arraste uma entrega para o quadro ao lado.</p>}
            </ul>
          </section>
        </div>
      </div>

      {conversaDoAgendamento && (
        <ModalAlterarAgendamento
          conversa={conversaDoAgendamento} conversas={conversas} janelas={janelas} agora={agora}
          cardapio={cardapio}
          aoFechar={() => setAgendamentoDe(null)}
          aoAlterar={(janela, avisar, texto) => {
            acoes.alterarAgendamentoEntrega(conversaDoAgendamento.id, janela, avisar, texto, agora)
            setAgendamentoDe(null)
          }}
        />
      )}

      {rotaDe && (
        <ModalGerarRota
          viagem={viagensPorId.get(rotaDe.id) ?? rotaDe} janelas={janelas} agora={agora} constantes={constantes}
          enderecoDaCasa={enderecoDaCasa} cardapio={cardapio} aoFechar={() => setRotaDe(null)}
          aoDesfazer={rotaDe.anterior ? () => {
            acoes.aplicarOrdemDaViagem(rotaDe.id, rotaDe.anterior)
            setRotaDe(null)
          } : null}
        />
      )}
    </DndContext>
  )
}
