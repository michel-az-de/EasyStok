import { useRef, useState } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { Anel } from '../../componentes/Anel'
import { Botao } from '../../componentes/Botao'
import { EscolhaDeEntregador } from '../../componentes/EscolhaDeEntregador'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Popover } from '../../componentes/Popover'
import { itensDetalhados, numeroCurto } from '../../dominio/pedido'
import { numeroParaEntregador, textoDoEntregador } from '../../dominio/despacho'
import { estaBloqueada } from '../../dominio/conversa'
import { motivoParaNaoAvancar } from '../../dominio/esteira'
import { sinalVerde } from '../../dominio/cobranca'
import { complementoAReceber } from '../../dominio/pagamento'
import { horaCurta, partesDoEndereco, plural } from '../../dominio/formato'
import { entregadorResolvido, estadoDaEntrega, proximoPassoDeEntrega } from '../../dominio/viagem'
import css from './dashboard.module.css'

const corPeloMinuto = (minutos) => {
  if (minutos == null) return 'ok'
  if (minutos < 0) return 'parado'
  if (minutos > 30) return 'ok'
  if (minutos > 10) return 'atencao'
  return 'parado'
}

const rotuloMinutos = (minutos) => {
  if (minutos == null) return '—'
  if (minutos < 0) return '+' + Math.min(Math.abs(Math.round(minutos)), 99)
  if (minutos > 99) return '1 h'
  return String(Math.round(minutos))
}

// Cartão de entrega (seção 6): anel de quanto falta para o PRÓXIMO marco,
// nome, pílula de estado, e o botão do marco que a leva ao passo seguinte da
// esteira. O menu ⋯ é a alternativa de um ponteiro só ao arrasto (WCAG
// 2.5.7): tudo que dá para arrastar também dá para escolher por aqui.
export function CartaoEntrega({
  conversa, grupoDaViagem, janelas, agora, constantes, cardapio, viagensDisponiveis, acoes,
  aoAbrirAgendamento, destacado = false, opcoesDespacho,
}) {
  const [aberto, setAberto] = useState(false)
  const [menuAberto, setMenuAberto] = useState(false)
  const [confirmando, setConfirmando] = useState(null)
  const [pedindoEntregador, setPedindoEntregador] = useState(false)
  const cardRef = useRef(null)
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: conversa.id, data: { type: 'entrega' },
  })
  // Popover.jsx não é portal: se o cartão está perto da borda da lista que
  // rola (`dashboard.module.css: .listaColuna`), o menu nasce recortado e o
  // clique nem chega a registrar (achado da própria prova). Centralizar o
  // cartão na lista ao abrir garante espaço para o menu embaixo.
  const abrirMenu = () => {
    cardRef.current?.scrollIntoView({ block: 'center' })
    setMenuAberto(true)
  }

  const pedido = conversa.pedido
  const passo = proximoPassoDeEntrega(conversa, grupoDaViagem, janelas, agora, constantes)
  // US-040: só a entrega solta (sem viagem) despacha por aqui sem já ter
  // resolvido quem leva; dentro de uma viagem o botão só aparece depois do
  // chamado achar alguém ou do modo "eu levo", então já chega resolvido.
  const precisaDeEntregador = passo?.chave === 'sair' && !entregadorResolvido(pedido)
  // RN-14 / US-019: mesma resposta que o reducer usa para recusar o toque.
  const motivo = passo && !passo.semAcao
    && motivoParaNaoAvancar(passo.proximoEstado, { bloqueado: estaBloqueada(conversa) })
  const estado = estadoDaEntrega(pedido)
  const minutos = passo ? (passo.quandoMs - agora) / 60000 : null
  const bairro = partesDoEndereco(conversa.cliente?.endereco).bairro
  const totalItens = pedido.itens.reduce((soma, l) => soma + l.qtd, 0)
  const semPagamento = !sinalVerde(pedido)
  const complemento = complementoAReceber(pedido, cardapio) > 0
  // Issue #17: quem levou fica à vista no cartão depois que saiu, com veículo,
  // placa e empresa (a pílula "Com o entregador" sozinha não dizia qual).
  const quemLeva = (pedido.estado === 'entrega' || pedido.estado === 'entregue')
    ? textoDoEntregador(pedido.entregador ?? entregadorResolvido(pedido))
    : null

  const fecharMenu = () => setMenuAberto(false)
  const escolher = (fn) => { fecharMenu(); fn() }

  return (
    <li
      ref={(node) => { setNodeRef(node); cardRef.current = node }}
      className={`${css.cartao} ${isDragging ? css.arrastando : ''} ${destacado ? css.cartaoNovo : ''}`}
    >
      <div className={css.linha1}>
        <button
          type="button" className={css.alcaArrasto} aria-label={'Arrastar entrega de ' + conversa.nome}
          {...attributes} {...listeners}
        >
          <Icone nome="grip-vertical" tamanho={20} />
        </button>
        {passo && (
          <Anel tamanho={44} traco={5} fracao={Math.max(0, Math.min(1, minutos / 60))} tom={corPeloMinuto(minutos)}>
            <span
              className={css.anelNumero} role="timer"
              aria-label={minutos < 0 ? `Atrasado ${Math.round(-minutos)} minutos` : `Próximo marco em ${Math.round(minutos)} minutos`}
            >
              {rotuloMinutos(minutos)}
            </span>
          </Anel>
        )}
        <button type="button" className={css.nomeCartao} onClick={() => setAberto((v) => !v)}>
          {conversa.nome}
        </button>
        <span className={css.pilulaEstado}>
          <Pilula tom={estado.tom} fina>
            <Icone nome={estado.icone} tamanho={16} /> {estado.rotulo}
          </Pilula>
        </span>
        <span className={css.comMenu}>
          <Botao
            variante="secundario" className={css.gatilhoMenu} aria-haspopup="menu" aria-expanded={menuAberto}
            aria-label={'Mais ações da entrega de ' + conversa.nome}
            onClick={() => (menuAberto ? fecharMenu() : abrirMenu())}
          >
            <Icone nome="ellipsis" tamanho={20} />
          </Botao>
          {menuAberto && (
            <Popover rotulo="Mais ações da entrega" posicao="abaixo" aoFechar={fecharMenu}>
              <div className={css.menuAcoes}>
                <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher(() => aoAbrirAgendamento(conversa))}>
                  <Icone nome="relogio" /> Alterar agendamento
                </button>
                {viagensDisponiveis.map((v) => (
                  <button
                    key={v.id} type="button" role="menuitem" className={css.itemMenu}
                    onClick={() => escolher(() => acoes.porNaViagem(v.id, conversa.id))}
                  >
                    <Icone nome="moto" /> Pôr na viagem {v.rotulo}
                  </button>
                ))}
                <button
                  type="button" role="menuitem" className={css.itemMenu}
                  onClick={() => escolher(() => acoes.criarViagem(conversa.id, 'entregador'))}
                >
                  <Icone nome="plus" /> Nova viagem
                </button>
                {pedido.viagem && (
                  <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher(() => acoes.tirarDaViagem(conversa.id))}>
                    <Icone nome="x" /> Tirar da viagem
                  </button>
                )}
                <button type="button" role="menuitem" className={css.itemMenu} onClick={() => escolher(() => acoes.selecionar(conversa.id))}>
                  <Icone nome="conversa" /> Abrir conversa
                </button>
                <button
                  type="button" role="menuitem" className={`${css.itemMenu} ${css.perigo}`}
                  onClick={() => { fecharMenu(); setConfirmando('cancelar') }}
                >
                  <Icone nome="circle-x" /> Cancelar entrega
                </button>
              </div>
            </Popover>
          )}
        </span>
      </div>

      <div className={css.linha2}>
        {/* Issue #17: o número que o entregador confere vem primeiro; a
            comanda é interna, fica ao lado para a cozinha achar o canhoto. */}
        <span>
          <span className={css.numeroEntrega}>Pedido {numeroParaEntregador(pedido)}</span>
          {' · comanda '}{numeroCurto(pedido.numero)} · {plural(totalItens, 'item', 'itens')} · {bairro || 'sem bairro'}
        </span>
        <span className={css.faixaCartao}>{janelas.find((j) => j.id === pedido.janela)?.faixa}</span>
      </div>

      <div className={css.linha3}>
        {passo && !passo.semAcao
          ? (
            <>
              <span>
                {passo.chave === 'conferir' && 'Conferir às ' + horaCurta(new Date(passo.quandoMs).toISOString())}
                {passo.chave === 'sair' && 'Sair às ' + horaCurta(new Date(passo.quandoMs).toISOString())}
                {passo.chave === 'entregar' && 'Entregar até ' + horaCurta(new Date(passo.quandoMs).toISOString())}
              </span>
              <Botao
                variante="primario"
                disabled={Boolean(motivo)}
                onClick={() => {
                  if (passo.proximoEstado === 'entregue') return acoes.marcarParadaEntregue(conversa.id, agora)
                  if (precisaDeEntregador) return setPedindoEntregador(true)
                  return acoes.avancarEsteira(conversa.id, passo.proximoEstado)
                }}
              >
                {passo.rotuloBotao}
              </Botao>
              {pedindoEntregador && precisaDeEntregador && (
                <EscolhaDeEntregador
                  {...opcoesDespacho}
                  pergunta={`Quem leva o pedido ${numeroParaEntregador(pedido)}?`}
                  aoEscolher={(entregador) => {
                    setPedindoEntregador(false)
                    acoes.avancarEsteira(conversa.id, passo.proximoEstado, entregador)
                  }}
                />
              )}
            </>
          )
          : passo?.semAcao
            ? <span>Sai com a viagem às {horaCurta(new Date(passo.quandoMs).toISOString())}</span>
            : null}
      </div>
      {motivo && <p className={css.motivoDesabilitado}>{motivo}</p>}
      {quemLeva && (
        <p className={css.quemLeva}>
          <Icone nome="moto" tamanho={16} />
          {pedido.estado === 'entregue' ? 'Entregue por ' : 'Com '}<b>{quemLeva}</b>
        </p>
      )}

      {(semPagamento || complemento) && (
        <p className={css.marcas}>
          <Icone nome="alerta" tamanho={16} /> {semPagamento ? 'Sem pagamento' : 'Complemento a receber'}
        </p>
      )}

      {confirmando && (
        <div className={css.confirmacaoInline}>
          {semPagamento ? (
            <>
              <p>Cancelar a entrega de {conversa.nome}? Ela recebe aviso.</p>
              <div className={css.confirmacaoBotoes}>
                <Botao
                  variante="primario"
                  onClick={() => {
                    acoes.cancelarPedido(conversa.id, `Cancelei o pedido ${pedido.numero}. Qualquer coisa, é só chamar.`)
                    setConfirmando(null)
                  }}
                >
                  Cancelar entrega
                </Botao>
                <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
              </div>
            </>
          ) : (
            <>
              <p>Pedido pago. Marcar estorno também?</p>
              <div className={css.confirmacaoBotoes}>
                <Botao
                  variante="primario"
                  onClick={() => {
                    acoes.cancelarPedido(conversa.id, `Cancelei o pedido ${pedido.numero}. Qualquer coisa, é só chamar.`)
                    acoes.marcarEstorno(conversa.id)
                    setConfirmando(null)
                  }}
                >
                  Cancelar e estornar
                </Botao>
                <Botao
                  variante="secundario"
                  onClick={() => {
                    acoes.cancelarPedido(conversa.id, `Cancelei o pedido ${pedido.numero}. Qualquer coisa, é só chamar.`)
                    setConfirmando(null)
                  }}
                >
                  Só cancelar
                </Botao>
                <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
              </div>
            </>
          )}
        </div>
      )}

      {aberto && (
        <div className={css.corpoCartao}>
          <p>
            {itensDetalhados(pedido, cardapio).map((l) => `${l.qtd}× ${l.produto?.nome ?? l.sku}`).join(', ')}
          </p>
          <p>
            {conversa.cliente?.endereco ?? 'Sem endereço'}
            {conversa.cliente?.endereco && (
              <>
                {' · '}
                <a
                  className={css.linkMapa}
                  href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(conversa.cliente.endereco)}
                  target="_blank" rel="noopener noreferrer"
                >
                  Abrir no mapa
                </a>
              </>
            )}
          </p>
        </div>
      )}
    </li>
  )
}
