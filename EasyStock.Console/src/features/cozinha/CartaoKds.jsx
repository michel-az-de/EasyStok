import { useDraggable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { avisoDeInicio, gruposDoKds, nomeNaComanda, proximoStatusKds, tempoAteInicio, urgenciaDoPedido } from '../../dominio/kds'
import css from './cozinha.module.css'

// Cartão do KDS no modo API (issue #1446), no desenho do cartão do protótipo
// (`CartaoCozinha.jsx`): itens por linha com porção e observação, botão de um
// toque, arrastar para a próxima coluna. A cor vem com o rótulo escrito (RN-30).
// O movimento conta o que mudou: entra quando chega, assenta quando muda de
// coluna, sai quando deixa a fila, e o atrasado pulsa. Com movimento reduzido o
// CSS global (`estilos/base.css`) para tudo e fica só a cor e o texto.
const COR_DA_URGENCIA = { atrasado: 'cor-atraso', agora: 'cor-agora', logo: 'cor-esperando' }

const corDoCartao = (pedido, urgencia) => COR_DA_URGENCIA[urgencia]
  ?? (pedido.status === 'aguardando' ? 'cor-esperando' : 'cor-preparo')

export function CartaoKds({
  pedido, agora, linhas, linhaFiltro, movendo = false, novo = false, mudou = false, saindo = false, recusa = null,
  aoAvancar, aoImprimir, aoAbrir,
}) {
  const proximo = saindo ? null : proximoStatusKds(pedido.status)
  const urgencia = urgenciaDoPedido(pedido, agora)
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, isDragging } = useDraggable({
    id: pedido.id,
    disabled: !proximo || movendo,
    attributes: { roleDescription: 'pedido arrastável' },
    data: { numero: pedido.numeroCurto, nome: nomeNaComanda(pedido), status: pedido.status },
  })

  const classes = [
    css.cartao, css[corDoCartao(pedido, urgencia)],
    proximo && css.cartaoArrastavel,
    novo && css.cartaoEntra, mudou && css.cartaoAssenta, saindo && css.cartaoSai,
    urgencia === 'atrasado' && css.cartaoAtrasado,
    isDragging && css.cartaoOrigem, movendo && css.cartaoACaminho, recusa && css.cartaoRecusado,
  ].filter(Boolean).join(' ')

  return (
    <li ref={saindo ? undefined : setNodeRef} className={classes} inert={saindo || undefined} {...(saindo ? {} : listeners)}>
      <CorpoKds
        pedido={pedido} agora={agora} linhas={linhas} linhaFiltro={linhaFiltro} urgencia={urgencia}
        alca={proximo && (
          <button
            ref={setActivatorNodeRef} type="button" className={css.alca}
            aria-label={`Mover pedido ${pedido.numeroCurto} de ${nomeNaComanda(pedido)}`} {...attributes}
          >
            <Icone nome="grip-vertical" tamanho={20} />
          </button>
        )}
      />
      {recusa && (
        <p className={css.motivoRecusa}><Icone nome="circle-x" tamanho={16} />{recusa}</p>
      )}
      {proximo && (
        <Botao
          largo variante="primario" className={css.botaoAvancar} disabled={movendo}
          onClick={() => aoAvancar(pedido.id, proximo.status)}
        >
          {movendo ? 'Enviando…' : proximo.toque}
        </Botao>
      )}
      {!saindo && (
        <div className={css.acoesCartao}>
          <Botao variante="texto" icone="nota" onClick={() => aoAbrir(pedido.id)}>Ver comanda</Botao>
          <Botao variante="texto" icone="printer" onClick={() => aoImprimir(pedido)}>Canhoto</Botao>
        </div>
      )}
    </li>
  )
}

// O cartão na mão durante o arrasto (DragOverlay): cópia sem ação, `inert`.
export function CartaoKdsErguido({ pedido, agora, linhas, linhaFiltro }) {
  const urgencia = urgenciaDoPedido(pedido, agora)
  return (
    <div className={`${css.cartao} ${css[corDoCartao(pedido, urgencia)]} ${css.cartaoErguido}`} inert>
      <CorpoKds
        pedido={pedido} agora={agora} linhas={linhas} linhaFiltro={linhaFiltro} urgencia={urgencia}
        alca={<span className={css.alca}><Icone nome="grip-vertical" tamanho={20} /></span>}
      />
    </div>
  )
}

function CorpoKds({ pedido, agora, linhas, linhaFiltro, urgencia, alca }) {
  const aviso = avisoDeInicio(pedido, agora)
  const todos = gruposDoKds(pedido, linhas)
  const grupos = linhaFiltro ? todos.filter((g) => g.chave === linhaFiltro) : todos
  const fracao = tempoAteInicio(pedido, agora)

  return (
    <>
      <div className={css.cabecaCartao}>
        <span className={css.numeroCartao}>{pedido.numeroCurto} · {nomeNaComanda(pedido)}</span>
        {alca}
      </div>
      <p className={css.rotuloSituacao}>{aviso ? aviso.texto : pedido.statusRotulo}</p>
      {fracao !== null && (
        <div
          className={`${css.barraTempo} ${urgencia ? css[`barra-${urgencia}`] : ''}`}
          role="presentation"
        >
          <span style={{ transform: `scaleX(${fracao})` }} />
        </div>
      )}
      {pedido.janela && (
        <p className={css.horaJanela}>
          <Icone nome="relogio" tamanho={16} /> {pedido.janela.label}
        </p>
      )}
      {grupos.map((grupo) => (
        <div key={grupo.chave} className={css.grupo}>
          <span className={css.rotuloGrupo}>{grupo.rotulo}</span>
          <ul className={css.itens}>
            {grupo.itens.map((linha) => (
              <li key={linha.sku} className={css.item}>
                <span className={css.qtd}>{linha.qtd}×</span>
                <span className={css.nomeItem}>{linha.produto.nome}</span>
                <span className={css.porcao}>{linha.produto.porcao}</span>
                {linha.obs && <span className={css.obs}>{linha.obs}</span>}
              </li>
            ))}
          </ul>
        </div>
      ))}
      {pedido.observacoes && <p className={css.obsPedido}>{pedido.observacoes}</p>}
    </>
  )
}
