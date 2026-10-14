import { useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { conversaEncerrada, estaBloqueada } from '../../dominio/conversa'
import { faltaPagar } from '../../dominio/cobranca'
import { faixaDaJanela } from '../../dominio/entrega'
import { faixasDeDistancia, situacaoDoCep } from '../../dominio/areaEntrega'
import { numeroDaComanda } from '../../dominio/pedido'
import { motivoAntesDeCobrar } from '../../dominio/resumoPedido'
import { chavesDaFicha, fichaRecolhidaDePadrao } from '../../dominio/ficha'
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

export function PainelFicha({ aoAbrirCardapio, acaoCompartilhar }) {
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
  const { janelas, prefixosCepAtendidos } = useCatalogo()
  // #1683: conversa em que o cliente está escolhendo pelo link (quem sabe é a ComandaDoCliente).
  const [conversaEscolhendo, setConversaEscolhendo] = useState(null)

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
        Cliente, comanda, esteira e histórico aparecem aqui, prontos para marcar.
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
  const aoEncerrarAtendimento = () => abrirEncerramento(selecionada.id)
  const chaves = chavesDaFicha({
    conversaId: selecionada.id, pedidoId: pedido?.pedidoId ?? null,
    pagamentos: (pedido?.pagamentosApi ?? []).map((p) => p.pagamentoId),
  })
  // #1761 (L10): sem pedido, "Adicionar itens" é o próximo passo e mora no bloco da comanda.
  const acaoMontar = !pedido && !clienteEscolhendo
    ? <Botao largo variante="primario" disabled={!editavel} onClick={aoAbrirCardapio}>Adicionar itens</Botao> : null
  const comandaCompartilhada = <ComandaDoCliente key={chaves.comandaCompartilhada} conversaId={selecionada.id}
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
        {!pedido && comandaCompartilhada}
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

        {pedido ? (
          <>
            {/* #1442: a comanda recolhe e lembra, como as seções do cliente. */}
            <Bloco titulo="Pedido" chave="comanda" resumo={numero ? `Comanda ${numero}` : 'Rascunho'}>
              <BlocoPedido
                pedido={pedido}
                editavel={editavel}
                aoTrocarJanela={(janela, avisar) => escolherJanela(selecionada.id, janela, avisar)}
                aoForcarEncaixe={(janela, faixa, motivo) => forcarEncaixe(selecionada.id, janela, faixa, motivo)}
                aoAbrirCardapio={aoAbrirCardapio}
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
            </Bloco>
            {comandaCompartilhada}
            {/* #1510: formulário "Recebi" e confirmações não atravessam de uma conversa para outra. */}
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
              aoAbrirCardapio={aoAbrirCardapio}
              aoCancelarPedido={aoCancelarPedido}
              aoEncerrarAtendimento={aoEncerrarAtendimento}
              aoAlterarMeio={(meio) => alterarMeioPagamento(selecionada.id, meio)}
            />
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
            <BlocoEntrega conversa={selecionada} />
            {fonteApi && pedido.pedidoId && <BlocoFreteApi key={chaves.frete} pedido={pedido}
              cliente={selecionada.nome} numero={numero} endereco={cliente.endereco} />}
          </>
        ) : (clienteEscolhendo || !fonteApi) && (
          <Bloco titulo={clienteEscolhendo ? 'Revisar com o cliente' : 'Montar pelo atendimento'} chave="comanda">
            <p className={css.corpoBloco}>
              {clienteEscolhendo ? 'Ajuste os itens na seleção acima e salve para o cliente conferir. A entrega também pode ser combinada antes da confirmação.'
                : 'Nenhum pedido confirmado ainda. Adicione itens para montar pelo atendimento ou compartilhe o link de compra.'}
            </p>
            {!clienteEscolhendo && (
              <Botao largo disabled={!editavel} onClick={aoAbrirCardapio}>
                Adicionar itens
              </Botao>
            )}
          </Bloco>
        )}
        <BlocoCliente conversa={selecionada} recolhidas={fichaRecolhidaDePadrao({ pedido, cliente })} />

        {/* Histórico e Notas saíram da coluna no passe de integração (direção 19,
            seção 8): moram na modal Histórico da F2, botão no bloco Cliente. */}
      </div>

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
