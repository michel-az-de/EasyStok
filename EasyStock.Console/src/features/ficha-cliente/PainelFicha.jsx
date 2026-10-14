import { useState } from 'react'
import { useDroppable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { conversaEncerrada, estaBloqueada } from '../../dominio/conversa'
import { faltaPagar } from '../../dominio/cobranca'
import { faixaDaJanela } from '../../dominio/entrega'
import { faixasDeDistancia, situacaoDoCep } from '../../dominio/areaEntrega'
import { comandaTravada, numeroDaComanda, pedidoEncerrado, rotuloDoEstado, totalDoPedido } from '../../dominio/pedido'
import { faltaPagar as saldoDoPedido } from '../../dominio/pagamento'
import { moeda } from '../../dominio/formato'
import { motivoAntesDeCobrar } from '../../dominio/resumoPedido'
import { chavesDaFicha, primeiroPassoAntesDaVenda } from '../../dominio/ficha'
import { BarraProximoPasso } from './BarraProximoPasso'
import { BlocoCobranca } from './BlocoCobranca'
import { BlocoDevolucoes } from './BlocoDevolucoes'
import { BlocoCapturaAutomatica } from './BlocoCapturaAutomatica'
import { BlocoCliente } from './BlocoCliente'
import { BlocoEnderecoCapturado } from './BlocoEnderecoCapturado'
import { BlocoOcorrencia } from './BlocoOcorrencia'
import { BlocoOcorrenciasApi } from './BlocoOcorrenciasApi'
import { BlocoEntrega } from './BlocoEntrega'
import { BlocoFreteApi } from './BlocoFreteApi'
import { BlocoPedido } from './BlocoPedido'
import { ComandaDoCliente } from './ComandaDoCliente'
import css from './ficha.module.css'

export function PainelFicha(props) {
  const { selecionada } = useAtendimento()
  // Nenhum formulário ou modal do ciclo anterior atravessa a troca de contexto.
  const chave = `${selecionada?.id}:${conversaEncerrada(selecionada)}:${selecionada?.pedido?.pedidoId ?? selecionada?.pedido?.numero ?? ''}`
  return <FichaDoAtendimento key={chave} {...props} />
}

function AcessoDaFicha({ titulo, resumo, aoAbrir }) {
  return <button type="button" className={css.acessoFicha} onClick={aoAbrir} aria-haspopup="dialog">
    <span><strong>{titulo}</strong><small>{resumo}</small></span>
    <Icone nome="chevron-right" tamanho={18} />
  </button>
}

function FichaDoAtendimento({ aoAbrirCardapio, acaoCompartilhar }) {
  const {
    selecionada, visiveis, alertasEstoque, ultimoAvanco, agora, automaticoPausado, fonteApi,
  } = useAtendimento()
  const {
    avancarEsteira, cadastrarEndereco, fecharAlerta,
    ajustarQuantidade, ajustarObservacao, gerarPedido, desfazerEsteira, corrigirPasso, selecionar,
    gerarCobranca, reenviarCobranca, confirmarPagamento,
    marcarComprovante, aceitarDivergencia, escolherJanela, forcarEncaixe,
    cancelarPedido, marcarEstorno, abrirEncerramento, decidirAreaEntrega, escolherMeioPagamento,
    alterarMeioPagamento, desfazerPagamento, aprovarPedido,
  } = useAcoes()
  const { janelas, prefixosCepAtendidos, cardapio } = useCatalogo()
  const [secao, setSecao] = useState(null)
  // #1683: conversa em que o cliente está escolhendo pelo link (quem sabe é a ComandaDoCliente).
  const [conversaEscolhendo, setConversaEscolhendo] = useState(null)
  const { setNodeRef: receberPrato, isOver: recebendoPrato } = useDroppable({
    id: 'comanda-resumo', data: { type: 'comanda' },
    disabled: Boolean(secao) || !selecionada?.pedido || conversaEncerrada(selecionada) || pedidoEncerrado(selecionada?.pedido)
      || comandaTravada(selecionada?.pedido, { fonteApi }) || conversaEscolhendo === selecionada?.id,
  })

  if (!selecionada) {
    const primeira = visiveis[0]
    return (
      <Vazio
        titulo="Sem ficha na tela"
        acao={primeira && (
          <Botao variante="primario" onClick={() => selecionar(primeira.id)}>
            Abrir ficha de {primeira.nome.split(' ')[0]}
          </Botao>
        )}
      >
        Selecione uma conversa para ver o próximo passo do atendimento.
      </Vazio>
    )
  }

  const { cliente, pedido } = selecionada
  const editavel = !conversaEncerrada(selecionada)
  const faixa = faixaDaJanela(janelas, pedido?.janela)
  const avancoEm = ultimoAvanco?.conversaId === selecionada.id ? ultimoAvanco.em : null
  const primeiroNome = selecionada.nome.split(' ')[0]
  const clienteEscolhendo = conversaEscolhendo === selecionada.id
  const numero = selecionada.numeroComanda || (pedido ? numeroDaComanda(pedido, { fonteApi }) : null)
  const posVenda = pedido && pedidoEncerrado(pedido)
  const pedidoCriado = Boolean(pedido && (fonteApi ? pedido.pedidoId : pedido.cobranca || pedido.estado !== 'aguardando'))
  const tituloPedido = pedidoCriado ? 'Pedido e entrega' : 'Seleção do atendimento'
  const total = pedido?.totalApi ?? totalDoPedido(pedido, cardapio)
  const saldo = saldoDoPedido(pedido, cardapio)
  const itens = pedido?.itens?.reduce((soma, item) => soma + item.qtd, 0) ?? 0
  const resumoPagamento = pedido?.estado === 'cancelado' ? 'Confira recebimentos e devoluções'
    : saldo > 0 ? `${moeda(saldo)} a receber` : 'Pagamento completo'
  // #1474 (R3): o que falta antes de gerar a cobrança, na ordem em que se resolve. A barra
  // mostra o motivo e desabilita o botão em vez de deixar o EasyStok recusar depois.
  const prerequisito = pedido
    ? motivoAntesDeCobrar({ pedido, clienteId: selecionada.clienteId, endereco: cliente.endereco }, { fonteApi })
    : null

  // Texto pronto que o cliente lê ao cancelar (mesmo padrão de ENVIAR_MIDIA e
  // GERAR_PEDIDO: o domínio não escreve copy, quem monta é a tela). Reusado
  // pelo cancelamento direto e por "Encerrar atendimento" quando ele precisa
  // cancelar o pedido antes de fechar a conversa (secao 6, "ao zerar").
  const aoCancelarPedido = (motivo) => cancelarPedido(
    selecionada.id, fonteApi ? motivo : 'Cancelei o pedido ' + pedido.numero + '. Qualquer coisa, é só chamar.',
  )
  // Ponta (c) da integração: a barra do pedido (Entregue, Cancelado) e o
  // anel zerado abrem a MESMA modal do cabeçalho e do menu ⋯, com o resumo.
  // Quem cancela pedido sem pagamento é a própria modal (faixa da seção 5).
  const aoEncerrarAtendimento = () => { setSecao(null); abrirEncerramento(selecionada.id) }
  const abrirCardapio = () => { setSecao(null); aoAbrirCardapio() }
  const chaves = chavesDaFicha({
    conversaId: selecionada.id, pedidoId: pedido?.pedidoId ?? null,
    pagamentos: (pedido?.pagamentosApi ?? []).map((p) => p.pagamentoId),
  })
  // #1761 (L10): sem pedido, "Adicionar itens" é o próximo passo e mora no bloco da comanda.
  // #1806: lead sem cadastro cadastra primeiro, com o formulário já preenchido pela conversa.
  const cadastrarPrimeiro = primeiroPassoAntesDaVenda({ conversa: selecionada, pedido, fonteApi }) === 'cadastro'
  const acaoMontar = !pedido && !clienteEscolhendo
    ? (cadastrarPrimeiro
      ? <>
        <Botao largo variante="primario" disabled={!editavel} onClick={() => setSecao('cliente')}>Cadastrar cliente</Botao>
        <Botao largo disabled={!editavel} onClick={aoAbrirCardapio}>Adicionar itens</Botao>
      </>
      : <Botao largo variante="primario" disabled={!editavel} onClick={aoAbrirCardapio}>Adicionar itens</Botao>)
    : null
  const comandaCompartilhada = <ComandaDoCliente key={chaves.comandaCompartilhada} conversaId={selecionada.id} compacta
    nomeCliente={selecionada.nome}
    numeroComanda={numero} ativo={fonteApi} acaoCompartilhar={acaoCompartilhar} acaoMontar={acaoMontar}
    pedido={pedido} aoMudarEscolha={setConversaEscolhendo} />

  return (
    <div className={css.painel}>
      {/* Alerta no topo, em corpo de texto e como região viva. Estava nascendo
          a 1140 px de altura, em 13 px, invisível na prática. Lista, não campo
          único: um segundo desacerto de saldo não pode apagar o primeiro antes
          dela ver os dois (QA2-16). */}
      {alertasEstoque.map((item) => (
        <output key={item.id} className={css.alerta}>
          <Icone nome="alerta" />
          <p>{item.texto}</p>
          <Botao onClick={() => fecharAlerta(item.id)}>Entendi</Botao>
        </output>
      ))}

      <div className={css.conteudo}>
        <section ref={receberPrato} className={css.resumoAtual} data-recebendo-prato={recebendoPrato || undefined} aria-label="Resumo do atendimento">
          <p className={css.fase}>{!editavel ? 'Conversa encerrada' : posVenda ? 'Pós-venda' : pedidoCriado ? 'Pedido criado' : 'Antes da venda'}</p>
          <h3>{!editavel ? 'Atendimento encerrado' : pedidoCriado ? `Pedido ${numero ?? ''}`.trim() : pedido ? 'Seleção não confirmada' : 'Vamos atender ' + primeiroNome}</h3>
          <p>{!editavel ? 'O histórico está guardado. Reabra a conversa para retomar o atendimento.'
            : pedido ? `${pedidoCriado ? rotuloDoEstado(pedido.estado) : 'Em montagem'} · ${itens} ${itens === 1 ? 'item' : 'itens'} · ${moeda(total)}`
              : cadastrarPrimeiro ? 'Primeiro o cadastro: o pedido só nasce para cliente cadastrado. O que a conversa trouxe já vem preenchido.'
                : 'Monte o pedido por aqui ou acompanhe a escolha do cliente pelo link.'}</p>
        </section>

        {editavel && comandaCompartilhada}
        {editavel && !pedido && !fonteApi && acaoMontar && <div className={css.acaoMontar}>{acaoMontar}</div>}
        <nav className={css.acessosFicha} aria-label="Detalhes do atendimento">
          {pedido && <AcessoDaFicha titulo={editavel ? tituloPedido : 'Consultar pedido vinculado'}
            resumo={editavel ? faixa || 'Itens, horário e acompanhamento' : 'Consultar sem alterar o pedido'} aoAbrir={() => setSecao('pedido')} />}
          {editavel && pedidoCriado && <AcessoDaFicha titulo="Pagamento" resumo={resumoPagamento} aoAbrir={() => setSecao('pagamento')} />}
          <AcessoDaFicha titulo="Cliente e histórico"
            resumo={!cliente.endereco ? 'Conferir cadastro e endereço' : 'Cadastro, endereço e conversas anteriores'} aoAbrir={() => setSecao('cliente')} />
          {editavel && pedidoCriado && (fonteApi || posVenda || pedido.ocorrencia) && <AcessoDaFicha titulo="Pós-venda"
            resumo={pedido.ocorrencia ? 'Acompanhar problema relatado' : 'Ocorrências e devoluções'} aoAbrir={() => setSecao('pos-venda')} />}
        </nav>
      </div>

      {secao === 'cliente' && <Modal titulo={`Cliente e histórico · ${selecionada.nome}`} largura="760px" aoFechar={() => setSecao(null)}>
        {/* Rodada 11 (registro 92): onde o automático está no caminho do
            UC-01, enquanto ele monta o cadastro pela conversa. */}
        {selecionada.captura && selecionada.conta === 'lead' && (
          <BlocoCapturaAutomatica conversa={selecionada} pausado={Boolean(automaticoPausado[selecionada.id])} />
        )}

        {cliente.enderecoCapturado && !cliente.endereco && (
          <BlocoEnderecoCapturado
            endereco={cliente.enderecoCapturado}
            // #1474 (R9): no modo API os prefixos de CEP são da demonstração; quem diz se o
            // endereço está na área é o EasyStok, ao cadastrar (aviso "fora da área").
            situacaoArea={fonteApi ? null : situacaoDoCep(cliente.enderecoCapturado, prefixosCepAtendidos)}
            faixasDeDistancia={fonteApi ? null : faixasDeDistancia(cliente.enderecoCapturado, prefixosCepAtendidos)}
            aoConfirmar={() => cadastrarEndereco(selecionada.id)}
            aoDecidirArea={(decisao) => decidirAreaEntrega(selecionada.id, decisao, agora)}
          />
        )}

        <BlocoCliente key={selecionada.id} conversa={selecionada} />
      </Modal>}

      {secao === 'pedido' && pedido && <Modal titulo={`${editavel ? tituloPedido : 'Pedido vinculado'} · ${selecionada.nome}`} largura="760px" aoFechar={() => setSecao(null)}>
        {editavel && !pedidoCriado && <p>Estes itens ainda estão em montagem. O pedido será criado ao gerar a cobrança.</p>}
        {!editavel && <p>Consulta do atendimento encerrado. Reabra a conversa para fazer alterações.</p>}
        <BlocoPedido
          pedido={pedido}
          editavel={editavel}
          aoTrocarJanela={(janela, avisar) => escolherJanela(selecionada.id, janela, avisar)}
          aoForcarEncaixe={(janela, faixa, motivo) => forcarEncaixe(selecionada.id, janela, faixa, motivo)}
          aoAbrirCardapio={abrirCardapio}
          aoAjustar={(sku, delta) => ajustarQuantidade(selecionada.id, sku, delta)}
          aoAjustarObservacao={(sku, texto) => ajustarObservacao(selecionada.id, sku, texto)}
          aoGerarPedido={(meio) => { escolherMeioPagamento(selecionada.id, meio); return gerarPedido(selecionada.id, meio) }}
          nomeCliente={selecionada.nome}
          endereco={cliente.endereco}
          faixa={faixa}
          bloqueado={estaBloqueada(selecionada)}
          conversaId={selecionada.id}
          numeroComanda={numero}
          clienteEscolhendo={clienteEscolhendo}
        />
        {editavel && <>
            <BlocoEntrega conversa={selecionada} />
            {fonteApi && pedido.pedidoId && <BlocoFreteApi key={chaves.frete} pedido={pedido}
              cliente={selecionada.nome} numero={numero} endereco={cliente.endereco} />}
        </>}
      </Modal>}

      {secao === 'pagamento' && editavel && pedido && <Modal titulo={`Pagamento · ${selecionada.nome}`} largura="680px" aoFechar={() => setSecao(null)}>
        <BlocoCobranca
          key={chaves.cobranca}
          pedido={pedido}
          agora={agora}
          editavel={editavel}
          nomeCliente={primeiroNome}
          conversaId={selecionada.id}
          aoGerar={(meio) => gerarCobranca(selecionada.id, pedido, meio)}
          aoReenviar={(valorForcado, meio) => reenviarCobranca(selecionada.id, pedido, valorForcado ?? null, meio ?? null)}
          aoMarcarComprovante={() => marcarComprovante(selecionada.id)}
          aoAceitarDivergencia={() => aceitarDivergencia(selecionada.id)}
          aoAbrirCardapio={abrirCardapio}
          aoCancelarPedido={aoCancelarPedido}
          aoEncerrarAtendimento={aoEncerrarAtendimento}
          aoAlterarMeio={(meio) => alterarMeioPagamento(selecionada.id, meio)}
        />
      </Modal>}

      {secao === 'pos-venda' && editavel && pedido && <Modal titulo={`Pós-venda · ${selecionada.nome}`} largura="760px" aoFechar={() => setSecao(null)}>
            {fonteApi && pedido.pedidoId && <BlocoDevolucoes
              key={chaves.devolucoes}
              pedidoId={pedido.pedidoId}
              cancelado={pedido.estado === 'cancelado'}
            />}
            {fonteApi && pedido.pedidoId && <BlocoOcorrenciasApi key={chaves.ocorrenciasApi} pedidoId={pedido.pedidoId} conversaId={selecionada.id} />}
            {!fonteApi && pedido.ocorrencia && (
              <BlocoOcorrencia
                key={chaves.ocorrencia}
                ocorrencia={pedido.ocorrencia}
                conversaId={selecionada.id}
                nomeCliente={selecionada.nome}
                pedido={pedido}
              />
            )}
        {!fonteApi && !pedido.ocorrencia && <p>Nenhum problema registrado neste pedido.</p>}
      </Modal>}

      {pedido && editavel && (
        <BarraProximoPasso
          key={`${selecionada.id}:${avancoEm ?? 'sem-avanco'}`}
          emDesfazer={Boolean(avancoEm)}
          pedido={pedido}
          conversaId={selecionada.id}
          agora={agora}
          nomeCliente={primeiroNome}
          bloqueado={estaBloqueada(selecionada)}
          prerequisito={prerequisito}
          aoAvancar={(passo, entregador) => avancarEsteira(selecionada.id, passo, entregador)}
          aoDesfazer={() => desfazerEsteira(selecionada.id, ultimoAvanco.mensagemId, ultimoAvanco.posEntregaId)}
          aoGerarPix={(meio) => { escolherMeioPagamento(selecionada.id, meio); return gerarCobranca(selecionada.id, pedido, meio) }}
          aoReenviarPix={(valorForcado) => reenviarCobranca(selecionada.id, pedido, valorForcado ?? null)}
          aoAprovarPedido={aprovarPedido ? () => aprovarPedido(selecionada.id) : null}
          aoConfirmarPagamento={(valorPago, metodo) => confirmarPagamento(selecionada.id, valorPago ?? null, metodo)}
          aoAceitarDivergencia={() => aceitarDivergencia(selecionada.id)}
          aoCobrarDiferenca={() => reenviarCobranca(selecionada.id, pedido, faltaPagar(pedido.cobranca))}
          aoVoltarEtapa={() => corrigirPasso(selecionada.id)}
          aoCancelarPedido={aoCancelarPedido}
          aoMarcarEstorno={(motivo, valor) => marcarEstorno(selecionada.id, motivo, valor)}
          aoDesfazerPagamento={(motivo) => desfazerPagamento(selecionada.id, motivo)}
          aoEncerrarAtendimento={aoEncerrarAtendimento}
        />
      )}
    </div>
  )
}
