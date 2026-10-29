import { useState } from 'react'
import { abrirSecao } from '../../hooks/useRecolhido'
import { useDroppable } from '@dnd-kit/core'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useSimulacaoDoPedido } from '../../aplicacao/useSimulacaoDoPedido'
import { useADevolver } from '../../aplicacao/useADevolver'
import { conversaEncerrada, estaBloqueada } from '../../dominio/conversa'
import { faltaDaCobranca } from '../../dominio/cobranca'
import { faixaDoPedido } from '../../dominio/entrega'
import { faixasDeDistancia, situacaoDoCep } from '../../dominio/areaEntrega'
import { comandaTravada, numeroDaComanda, pedidoEncerrado, rotuloDoResumo, totalDoPedido } from '../../dominio/pedido'
import { faltaPagar, totalPago } from '../../dominio/pagamento'
import { moeda } from '../../dominio/formato'
import { ALVOS_DO_MOTIVO, pendenciaAntesDeCobrar } from '../../dominio/resumoPedido'
import { permissaoDeEscrita } from '../../dominio/janela'
import { canalDaConversa, fotoDoCliente } from '../../dominio/canal'
import { chavesDaFicha, primeiroPassoAntesDaVenda } from '../../dominio/ficha'
import { BarraProximoPasso } from './BarraProximoPasso'
import { focarCampoDaFicha } from './focarCampoDaFicha'
import { BlocoCobranca } from './BlocoCobranca'
import { BlocoDevolucoes } from './BlocoDevolucoes'
import { BlocoCapturaAutomatica } from './BlocoCapturaAutomatica'
import { ModalCliente } from './ModalCliente'
import { BlocoEnderecoCapturado } from './BlocoEnderecoCapturado'
import { BlocoOcorrencia } from './BlocoOcorrencia'
import { BlocoOcorrenciasApi } from './BlocoOcorrenciasApi'
import { BlocoEntrega } from './BlocoEntrega'
import { BlocoFreteApi } from './BlocoFreteApi'
import { BlocoPedido } from './BlocoPedido'
import { CartaoDoCliente } from './CartaoDoCliente'
import { ExtratoDoPedido } from './ExtratoDoPedido'
import { ComandaDoCliente } from './ComandaDoCliente'
import { resumoDoPagamento, saldoEmDestaque } from './apresentacaoDaFicha'
import css from './ficha.module.css'

export function PainelFicha(props) {
  const { selecionada } = useAtendimento()
  // Nenhum formulário ou modal do ciclo anterior atravessa a troca de contexto.
  const chave = `${selecionada?.id}:${conversaEncerrada(selecionada)}:${selecionada?.pedido?.pedidoId ?? selecionada?.pedido?.numero ?? ''}`
  return <FichaDoAtendimento key={chave} {...props} />
}

function AcessoDaFicha({ titulo, resumo, icone, aoAbrir }) {
  return <button type="button" className={css.acessoFicha} onClick={aoAbrir} aria-haspopup="dialog">
    {icone && <Icone nome={icone} tamanho={20} />}
    <span><strong>{titulo}</strong><small>{resumo}</small></span>
    <Icone nome="chevron-right" tamanho={18} />
  </button>
}

function FichaDoAtendimento({ aoAbrirCardapio, acaoCompartilhar }) {
  const {
    selecionada, alertasEstoque, ultimoAvanco, agora, automaticoPausado, fonteApi,
  } = useAtendimento()
  const {
    avancarEsteira, cadastrarEndereco, fecharAlerta,
    ajustarQuantidade, ajustarObservacao, gerarPedido, desfazerEsteira, corrigirPasso,
    gerarCobranca, reenviarCobranca, confirmarPagamento,
    marcarComprovante, aceitarDivergencia, escolherJanela, forcarEncaixe,
    cancelarPedido, marcarEstorno, abrirEncerramento, decidirAreaEntrega, escolherMeioPagamento,
    alterarMeioPagamento, desfazerPagamento, aprovarPedido, novaComanda, recarregarPedido,
  } = useAcoes()
  const { janelas, prefixosCepAtendidos, cardapio, canais } = useCatalogo()
  // #1884 ("Quanto fica?"): uma simulação por comanda, para a comanda e a barra mostrarem os mesmos números.
  const simulacao = useSimulacaoDoPedido({
    conversaId: selecionada?.id, clienteId: selecionada?.clienteId, endereco: selecionada?.cliente?.endereco,
    pedido: selecionada?.pedido, ativo: Boolean(fonteApi && selecionada && !conversaEncerrada(selecionada)),
  })
  // J2 (#1990): pedido pago cancelado diz no Resumo e no Pagamento quanto falta devolver.
  const aDevolver = useADevolver(fonteApi ? selecionada?.pedido : null)
  const [secao, setSecao] = useState(null)
  // #1686 (item 4): "Cancelar e refazer" na Seleção abre a confirmação de cancelar da barra.
  const [pedidoDeCancelar, setPedidoDeCancelar] = useState(0)
  // #1683: conversa em que o cliente está escolhendo pelo link (quem sabe é a ComandaDoCliente).
  const [conversaEscolhendo, setConversaEscolhendo] = useState(null)
  const { setNodeRef: receberPrato, isOver: recebendoPrato } = useDroppable({
    id: 'comanda-resumo', data: { type: 'comanda' },
    disabled: Boolean(secao) || !selecionada?.pedido || conversaEncerrada(selecionada) || pedidoEncerrado(selecionada?.pedido)
      || comandaTravada(selecionada?.pedido, { fonteApi }) || conversaEscolhendo === selecionada?.id,
  })

  if (!selecionada) {
    return (
      <Vazio titulo="Ficha">
        Escolha uma conversa para ver o cliente e o pedido.
      </Vazio>
    )
  }

  const { cliente, pedido } = selecionada
  const editavel = !conversaEncerrada(selecionada)
  const faixa = faixaDoPedido(janelas, pedido)
  const avancoEm = ultimoAvanco?.conversaId === selecionada.id ? ultimoAvanco.em : null
  const primeiroNome = selecionada.nome.split(' ')[0]
  const clienteEscolhendo = conversaEscolhendo === selecionada.id
  const numero = selecionada.numeroComanda || (pedido ? numeroDaComanda(pedido, { fonteApi }) : null)
  const posVenda = pedido && pedidoEncerrado(pedido)
  const pedidoCriado = Boolean(pedido && (fonteApi ? pedido.pedidoId : pedido.cobranca || pedido.estado !== 'aguardando'))
  const tituloPedido = 'Pedido e entrega'
  const total = pedido?.totalApi ?? totalDoPedido(pedido, cardapio)
  const saldo = faltaPagar(pedido, cardapio)
  const itens = pedido?.itens?.reduce((soma, item) => soma + item.qtd, 0) ?? 0
  // #1868: o saldo ganha linha própria no Resumo; o acesso Pagamento não repete o valor.
  const destacaSaldo = saldoEmDestaque({ editavel, pedidoCriado, saldo, cancelado: pedido?.estado === 'cancelado' })
  const resumoPagamento = resumoDoPagamento({ cancelado: pedido?.estado === 'cancelado', saldo, destaque: destacaSaldo, aDevolver })
  // #1474 (R3): o que falta antes de gerar a cobrança, na ordem em que se resolve. A barra
  // mostra o motivo e desabilita o botão em vez de deixar o EasyStok recusar depois.
  const gerarSemEnviar = fonteApi && permissaoDeEscrita(selecionada, agora, canalDaConversa(canais, selecionada)).motivo?.acao === 'janela'
  const pendencia = pedido
    ? pendenciaAntesDeCobrar({ pedido, clienteId: selecionada.clienteId, endereco: cliente.endereco }, {
      fonteApi,
    })
    : null
  const prerequisito = pendencia?.motivo ?? null

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
  // #1686 (item 9): o link do motivo abre onde ele se resolve e põe o foco no campo.
  const irParaMotivo = (alvo) => {
    if (alvo === ALVOS_DO_MOTIVO.CARDAPIO) { abrirCardapio(); return }
    if (alvo === ALVOS_DO_MOTIVO.JANELA) { setSecao('pedido'); focarCampoDaFicha('janela'); return }
    abrirSecao('contato')
    setSecao('cliente')
    focarCampoDaFicha(alvo)
  }
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
        <Botao icone="plus" aria-label="Adicionar itens" disabled={!editavel} onClick={aoAbrirCardapio}>Itens</Botao>
      </>
      : <Botao variante="primario" icone="plus" aria-label="Adicionar itens" disabled={!editavel} onClick={aoAbrirCardapio}>Itens</Botao>)
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
          <h3>{!editavel ? 'Conversa concluída' : pedidoCriado ? `${fonteApi ? 'Comanda' : 'Pedido'} ${numero ?? ''} · ${selecionada.nome}`.trim() : pedido ? 'Pedido em montagem' : 'Pedido de ' + primeiroNome}</h3>
          <p>{!editavel ? 'O histórico está guardado. Reabra a conversa para retomar o atendimento.'
            : pedido ? `${pedidoCriado ? rotuloDoResumo(pedido.estado, saldo, Array.isArray(pedido.pagamentosApi) ? totalPago(pedido) : null) : 'Em montagem'} · ${itens} ${itens === 1 ? 'item' : 'itens'} · ${moeda(total)}`
              : cadastrarPrimeiro ? 'Cadastre o cliente para montar o pedido. Os dados da conversa já estão preenchidos.'
                : 'Adicione itens ou envie o cardápio ao cliente.'}</p>
          {destacaSaldo && <p className={css.saldoFicha}>{moeda(saldo)} a receber</p>}
          {aDevolver > 0 && <p className={css.saldoFicha}>{moeda(aDevolver)} a devolver</p>}
        </section>

        {/* #1839: o cliente sempre à vista (resumo da IA, total de pedidos, tags e a última nota). */}
        <CartaoDoCliente conversa={selecionada} fonteApi={fonteApi} foto={fotoDoCliente(canalDaConversa(canais, selecionada), cliente)} aoAbrir={() => setSecao('cliente')}
          aoVerEntrega={pedidoCriado ? () => setSecao('pedido') : undefined} />

        {editavel && comandaCompartilhada}
        {/* #1686 (item 3): pedido entregue ou cancelado, a mesma conversa começa outra compra. */}
        {editavel && fonteApi && posVenda && novaComanda && (
          <div className={css.acaoMontar}>
            <Botao largo variante="secundario" icone="plus" onClick={() => novaComanda(selecionada.id)}>Nova comanda</Botao>
          </div>
        )}
        {editavel && !pedido && !fonteApi && acaoMontar && <div className={css.acaoMontar}>{acaoMontar}</div>}
        <nav className={css.acessosFicha} aria-label="Detalhes do atendimento">
          {pedido && <AcessoDaFicha icone="shopping-cart" titulo={editavel ? tituloPedido : 'Consultar pedido vinculado'}
            resumo={editavel ? faixa || 'Itens, horário e acompanhamento' : 'Consultar sem alterar o pedido'} aoAbrir={() => setSecao('pedido')} />}
          {editavel && pedidoCriado && <AcessoDaFicha icone="credit-card" titulo="Pagamento" resumo={resumoPagamento} aoAbrir={() => setSecao('pagamento')} />}
          <AcessoDaFicha icone="ficha" titulo="Ficha"
            resumo={!cliente.endereco ? 'Conferir cadastro e endereço' : 'Cadastro, endereço e conversas anteriores'} aoAbrir={() => setSecao('cliente')} />
          {editavel && pedidoCriado && (fonteApi || posVenda || pedido.ocorrencia) && <AcessoDaFicha icone="undo-2" titulo="Pós-venda"
            resumo={pedido.ocorrencia ? 'Acompanhar problema relatado' : 'Ocorrências e devoluções'} aoAbrir={() => setSecao('pos-venda')} />}
        </nav>
      </div>

      {secao === 'cliente' && <ModalCliente conversa={selecionada} aoFechar={() => setSecao(null)}>
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

      </ModalCliente>}

      {secao === 'pedido' && pedido && <Modal titulo={`${editavel ? tituloPedido : 'Pedido vinculado'} · ${selecionada.nome}`} largura="760px" aoFechar={() => setSecao(null)}>
        {editavel && !pedidoCriado && <p>Estes itens ainda estão em montagem. O pedido será criado ao gerar a cobrança.</p>}
        {!editavel && <p>Consulta do atendimento encerrado. Reabra a conversa para fazer alterações.</p>}
        <BlocoPedido
          pedido={pedido}
          editavel={editavel}
          aoTrocarJanela={(janela, avisar) => escolherJanela(selecionada.id, janela, avisar)}
          aoForcarEncaixe={(janela, faixa, motivo) => forcarEncaixe(selecionada.id, janela, faixa, motivo)}
          acaoAlterarPeloLink={fonteApi ? acaoCompartilhar : null}
          aoCancelarParaRefazer={() => { setSecao(null); setPedidoDeCancelar((n) => n + 1) }}
          aoAbrirCardapio={abrirCardapio}
          aoAjustar={(sku, delta) => ajustarQuantidade(selecionada.id, sku, delta)}
          aoAjustarObservacao={(sku, texto) => ajustarObservacao(selecionada.id, sku, texto)}
          gerarSemEnviar={gerarSemEnviar}
          aoGerarPedido={(meio) => { escolherMeioPagamento(selecionada.id, meio); return gerarPedido(selecionada.id, meio, !gerarSemEnviar) }}
          nomeCliente={selecionada.nome}
          endereco={cliente.endereco}
          faixa={faixa}
          bloqueado={estaBloqueada(selecionada)}
          conversaId={selecionada.id}
          numeroComanda={pedidoCriado ? numero : null}
          clienteEscolhendo={clienteEscolhendo}
          simulacao={simulacao}
        />
        {editavel && <>
            <BlocoEntrega conversa={selecionada} />
            {fonteApi && pedido.pedidoId && <BlocoFreteApi key={chaves.frete} pedido={pedido}
              cliente={selecionada.nome} numero={numero} endereco={cliente.endereco} />}
        </>}
      </Modal>}

      {secao === 'pagamento' && editavel && pedido && <Modal titulo={`Pagamento · ${selecionada.nome}`} largura="680px" aoFechar={() => setSecao(null)}>
        {fonteApi && <ExtratoDoPedido pedido={pedido} aDevolver={aDevolver} />}
        <BlocoCobranca
          key={chaves.cobranca}
          pedido={pedido}
          agora={agora}
          editavel={editavel && !pedido.statusDesconhecido}
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
              aoAlterado={() => recarregarPedido?.(selecionada.id)}
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
          simulacao={simulacao}
          pedido={pedido}
          conversaId={selecionada.id}
          agora={agora}
          nomeCliente={primeiroNome}
          bloqueado={estaBloqueada(selecionada)}
          prerequisito={prerequisito}
          alvoDoPrerequisito={pendencia?.alvo ?? null}
          aoIrParaMotivo={irParaMotivo}
          pedidoDeCancelar={pedidoDeCancelar}
          aoAvancar={(passo, entregador, opcoes) => avancarEsteira(selecionada.id, passo, entregador, opcoes)}
          aoDesfazer={() => desfazerEsteira(selecionada.id, ultimoAvanco.mensagemId, ultimoAvanco.posEntregaId)}
          gerarSemEnviar={gerarSemEnviar}
          aoGerarPix={(meio) => { escolherMeioPagamento(selecionada.id, meio); return gerarCobranca(selecionada.id, pedido, meio, !gerarSemEnviar) }}
          aoReenviarPix={(valorForcado) => reenviarCobranca(selecionada.id, pedido, valorForcado ?? null)}
          aoAprovarPedido={aprovarPedido ? () => aprovarPedido(selecionada.id) : null}
          aoConfirmarPagamento={(valorPago, metodo) => confirmarPagamento(selecionada.id, valorPago ?? null, metodo)}
          aoAceitarDivergencia={() => aceitarDivergencia(selecionada.id)}
          aoCobrarDiferenca={() => reenviarCobranca(selecionada.id, pedido, faltaDaCobranca(pedido.cobranca))}
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
