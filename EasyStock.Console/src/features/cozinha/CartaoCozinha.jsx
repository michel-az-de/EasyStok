import { useEffect, useRef } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { EscolhaDeEntregador } from '../../componentes/EscolhaDeEntregador'
import { Icone } from '../../componentes/Icone'
import { agruparPorLinha, itensDetalhados, numeroCurto } from '../../dominio/pedido'
import { motivoParaNaoAvancar, proximoPasso } from '../../dominio/esteira'
import { estaBloqueada } from '../../dominio/conversa'
import { ICONE_DO_PASSO, ROTULO_DO_TOQUE, situacaoDoCartao } from '../../dominio/cozinha'
import { entregadorResolvido } from '../../dominio/viagem'
import { numeroParaEntregador, partesDoEntregador } from '../../dominio/despacho'
import css from './cozinha.module.css'

// Cartão de pedido da cozinha (US-037, US-038, RN-29 a RN-32). As três
// perguntas do método aplicadas a cada elemento:
//   cor + rótulo escrito      -> RN-30, cor nunca decide sozinha.
//   hora da janela            -> UC-04 passo 3, "prende no quadro por horário".
//   itens por linha, porção,
//   observação                -> RN-29/RN-33, mesmo agrupamento do canhoto.
//   botão de um toque só      -> RN-31/US-038, mão suja de farinha.
//   arrastar para a coluna    -> issue #7 (rodada 11, pedido do dono): o
//                                mesmo avanço do botão, por gesto. O botão
//                                continua sendo o caminho de um toque só.
// O aviso ao cliente (RN-32) não é um elemento à parte: `acoes.avancarEsteira`
// já dispara pelo mesmo reducer do Balcão, o cartão só entrega o toque.
//
// A escolha de entregador (US-040) subiu para `ConteudoCozinha`: soltar na
// coluna "Em entrega" precisa abrir a MESMA pergunta que o botão abre.
export function CartaoCozinha({
  conversa, janelas, cardapio, linhas, agora, linhaFiltro, acoes, destacado = false,
  recusa = null, assentando = false, aCaminho = false, pedindoEntregador = false, aoPedirEntregador,
  opcoesDespacho,
}) {
  const { pedido } = conversa
  const proximo = proximoPasso(pedido.estado)
  const bloqueado = estaBloqueada(conversa)
  // US-040: mesma pergunta da ficha, um toque a mais só quando falta saber
  // quem leva (RN-31 continua um toque só no caso comum, resolvido antes).
  const precisaDeEntregador = proximo?.id === 'entrega' && !entregadorResolvido(pedido)
  // RN-14 / US-019: mesma resposta que o reducer usa para recusar o toque.
  const motivo = proximo && motivoParaNaoAvancar(proximo.id, { bloqueado })
  const numero = numeroCurto(pedido.numero)

  // Entregue não tem próximo passo: o cartão não se arrasta. Bloqueado se
  // arrasta de propósito: a recusa com o motivo aparece ao soltar, em vez
  // de o cartão simplesmente não responder à mão.
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, isDragging } = useDraggable({
    id: conversa.id,
    disabled: !proximo,
    attributes: { roleDescription: 'pedido arrastável' },
    data: { nome: conversa.nome, numero, estado: pedido.estado, bloqueado, precisaDeEntregador },
  })

  // O cartão que assentou nasce de novo na coluna nova (outra lista): o foco
  // do teclado iria para o corpo da página. Devolve o foco à alça, para quem
  // arrasta pelo teclado seguir de onde parou.
  const alcaRef = useRef(null)
  useEffect(() => {
    if (assentando) alcaRef.current?.focus({ preventScroll: true })
  }, [assentando])

  const classes = [
    css.cartao, css['cor-' + situacaoDoCartao(pedido, janelas, agora).chave],
    destacado && css.cartaoNovo, proximo && css.cartaoArrastavel,
    isDragging && css.cartaoOrigem, aCaminho && css.cartaoACaminho, assentando && css.cartaoAssenta,
    recusa && css.cartaoRecusado,
  ].filter(Boolean).join(' ')

  return (
    <li ref={setNodeRef} className={classes} {...listeners}>
      <CorpoDoCartao
        conversa={conversa} janelas={janelas} cardapio={cardapio} linhas={linhas} agora={agora}
        linhaFiltro={linhaFiltro}
        alca={proximo && (
          // Teclado entra pela alça (o dnd-kit só ativa o KeyboardSensor no
          // `activatorNode`); mouse e toque pegam o cartão inteiro.
          <button
            ref={(no) => { setActivatorNodeRef(no); alcaRef.current = no }} type="button" className={css.alca}
            aria-label={`Mover pedido ${numero} de ${conversa.nome}`} {...attributes}
          >
            <Icone nome="grip-vertical" tamanho={20} />
          </button>
        )}
      />

      {/* Uma linha só para o motivo: a recusa do arrasto por bloqueio é o
          mesmo texto do botão desabilitado, então realça a linha que já existe
          em vez de repetir a frase. */}
      {(recusa || motivo) && (
        <p className={recusa ? css.motivoRecusa : css.motivoDesabilitado}>
          {recusa && <Icone nome="circle-x" tamanho={16} />}
          {recusa ?? motivo}
        </p>
      )}
      {proximo && (
        <Botao
          largo
          variante="primario"
          icone={ICONE_DO_PASSO[proximo.id]}
          className={css.botaoAvancar}
          disabled={Boolean(motivo)}
          onClick={() => (precisaDeEntregador
            ? aoPedirEntregador(true)
            : acoes.avancarEsteira(conversa.id, proximo.id))}
        >
          {ROTULO_DO_TOQUE[pedido.estado] ?? proximo.rotulo}
        </Botao>
      )}
      {pedindoEntregador && precisaDeEntregador && (
        <EscolhaDeEntregador
          {...opcoesDespacho}
          pergunta={`Quem leva o pedido ${numeroParaEntregador(pedido)}?`}
          aoEscolher={(entregador) => {
            aoPedirEntregador(false)
            acoes.avancarEsteira(conversa.id, proximo.id, entregador)
          }}
        />
      )}
    </li>
  )
}

// O cartão "erguido" que segue a mão durante o arrasto (DragOverlay). Cópia
// visual sem ação nenhuma: `inert` tira foco e leitor de tela dele, quem fala
// é o anúncio do arrasto.
export function CartaoErguido({ conversa, janelas, cardapio, linhas, agora, linhaFiltro }) {
  const situacao = situacaoDoCartao(conversa.pedido, janelas, agora)
  const proximo = proximoPasso(conversa.pedido.estado)
  return (
    <div className={`${css.cartao} ${css['cor-' + situacao.chave]} ${css.cartaoErguido}`} inert>
      <CorpoDoCartao
        conversa={conversa} janelas={janelas} cardapio={cardapio} linhas={linhas} agora={agora}
        linhaFiltro={linhaFiltro}
        alca={<span className={css.alca}><Icone nome="grip-vertical" tamanho={20} /></span>}
      />
      {proximo && (
        <Botao largo variante="primario" icone={ICONE_DO_PASSO[proximo.id]} className={css.botaoAvancar} tabIndex={-1}>
          {ROTULO_DO_TOQUE[conversa.pedido.estado] ?? proximo.rotulo}
        </Botao>
      )}
    </div>
  )
}

function CorpoDoCartao({ conversa, janelas, cardapio, linhas, agora, linhaFiltro, alca }) {
  const { pedido } = conversa
  const itens = itensDetalhados(pedido, cardapio)
  const gruposTodos = agruparPorLinha(itens, linhas)
  const grupos = linhaFiltro ? gruposTodos.filter((g) => g.chave === linhaFiltro) : gruposTodos
  const situacao = situacaoDoCartao(pedido, janelas, agora)
  const faixa = janelas.find((j) => j.id === pedido.janela)?.faixa

  return (
    <>
      <div className={css.cabecaCartao}>
        <Icone nome={ICONE_DO_PASSO[pedido.estado]} tamanho={24} />
        <span className={css.numeroCartao}>{numeroCurto(pedido.numero)} · {conversa.nome}</span>
        {alca}
      </div>

      <p className={css.rotuloSituacao}>{situacao.rotulo}</p>

      <LinhaDespacho pedido={pedido} />

      {faixa && (
        <p className={css.horaJanela}>
          <Icone nome="relogio" tamanho={16} /> {faixa}
        </p>
      )}

      {grupos.map((grupo) => (
        <div key={grupo.chave} className={css.grupo}>
          <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
          <ul className={css.itens}>
            {grupo.itens.map((linha) => (
              <li key={linha.sku} className={css.item}>
                <span className={css.qtd}>{linha.qtd}×</span>
                <span className={css.nomeItem}>{linha.produto?.nome}</span>
                <span className={css.porcao}>{linha.produto?.porcao}</span>
                {linha.obs && <span className={css.obs}>{linha.obs}</span>}
              </li>
            ))}
          </ul>
        </div>
      ))}
    </>
  )
}

// Issue #17 (feedback da Thatiane): no despacho, ao lado de quem leva, o
// número do pedido que o entregador confere (a comanda do topo do cartão é
// interna), o veículo e a placa. Só a partir de "Embalado": antes disso
// ninguém saiu nem vai sair ainda.
const PASSOS_DO_DESPACHO = new Set(['embalado', 'entrega', 'entregue'])

function LinhaDespacho({ pedido }) {
  if (!PASSOS_DO_DESPACHO.has(pedido.estado)) return null
  const quem = partesDoEntregador(pedido.estado === 'embalado' ? entregadorResolvido(pedido) : pedido.entregador)
  return (
    <div className={css.despacho}>
      <span className={css.numeroEntrega} title="Número que o entregador confere na retirada">
        Pedido {numeroParaEntregador(pedido)}
      </span>
      <span className={css.quemLeva}>
        <Icone nome="moto" tamanho={16} />
        <span>
          {quem
            ? <><b>{quem.nome}</b>{quem.detalhes.length > 0 && ` · ${quem.detalhes.join(' · ')}`}</>
            : 'Entregador a definir'}
        </span>
      </span>
    </div>
  )
}
