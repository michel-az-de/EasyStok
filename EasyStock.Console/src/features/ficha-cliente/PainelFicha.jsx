import { Bloco } from '../../componentes/Bloco'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { conversaEncerrada, estaBloqueada } from '../../dominio/conversa'
import { faltaPagar } from '../../dominio/cobranca'
import { faixaDaJanela } from '../../dominio/entrega'
import { faixasDeDistancia, situacaoDoCep } from '../../dominio/areaEntrega'
import { BarraProximoPasso } from './BarraProximoPasso'
import { BlocoCobranca } from './BlocoCobranca'
import { BlocoCapturaAutomatica } from './BlocoCapturaAutomatica'
import { BlocoCliente } from './BlocoCliente'
import { BlocoEnderecoCapturado } from './BlocoEnderecoCapturado'
import { BlocoOcorrencia } from './BlocoOcorrencia'
import { BlocoEntrega } from './BlocoEntrega'
import { BlocoPedido } from './BlocoPedido'
import css from './ficha.module.css'

export function PainelFicha({ aoAbrirCardapio }) {
  const {
    selecionada, visiveis, alertasEstoque, ultimoAvanco, agora, automaticoPausado,
  } = useAtendimento()
  const {
    avancarEsteira, cadastrarEndereco, fecharAlerta,
    ajustarQuantidade, ajustarObservacao, gerarPedido, desfazerEsteira, corrigirPasso, selecionar,
    gerarCobranca, reenviarCobranca, confirmarPagamento,
    marcarComprovante, aceitarDivergencia, escolherJanela, forcarEncaixe,
    cancelarPedido, marcarEstorno, abrirEncerramento, decidirAreaEntrega, escolherMeioPagamento,
    alterarMeioPagamento, desfazerPagamento,
  } = useAcoes()
  const { janelas, prefixosCepAtendidos } = useCatalogo()

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

  // Texto pronto que o cliente lê ao cancelar (mesmo padrão de ENVIAR_MIDIA e
  // GERAR_PEDIDO: o domínio não escreve copy, quem monta é a tela). Reusado
  // pelo cancelamento direto e por "Encerrar atendimento" quando ele precisa
  // cancelar o pedido antes de fechar a conversa (secao 6, "ao zerar").
  const aoCancelarPedido = () => cancelarPedido(
    selecionada.id, 'Cancelei o pedido ' + pedido.numero + '. Qualquer coisa, é só chamar.',
  )
  // Ponta (c) da integração: a barra do pedido (Entregue, Cancelado) e o
  // anel zerado abrem a MESMA modal do cabeçalho e do menu ⋯, com o resumo.
  // Quem cancela pedido sem pagamento é a própria modal (faixa da seção 5).
  const aoEncerrarAtendimento = () => abrirEncerramento(selecionada.id)

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
        {/* Rodada 11 (registro 92): onde o automático está no caminho do
            UC-01, enquanto ele monta o cadastro pela conversa. */}
        {selecionada.captura && selecionada.conta === 'lead' && (
          <BlocoCapturaAutomatica conversa={selecionada} pausado={Boolean(automaticoPausado[selecionada.id])} />
        )}

        {cliente.enderecoCapturado && !cliente.endereco && (
          <BlocoEnderecoCapturado
            endereco={cliente.enderecoCapturado}
            situacaoArea={situacaoDoCep(cliente.enderecoCapturado, prefixosCepAtendidos)}
            faixasDeDistancia={faixasDeDistancia(cliente.enderecoCapturado, prefixosCepAtendidos)}
            aoConfirmar={() => cadastrarEndereco(selecionada.id)}
            aoDecidirArea={(decisao) => decidirAreaEntrega(selecionada.id, decisao, agora)}
          />
        )}

        <BlocoCliente conversa={selecionada} />

        {pedido ? (
          <>
            <BlocoPedido
              pedido={pedido}
              editavel={editavel}
              aoTrocarJanela={(janela) => escolherJanela(selecionada.id, janela)}
              aoForcarEncaixe={(janela, faixa, motivo) => forcarEncaixe(selecionada.id, janela, faixa, motivo)}
              aoAbrirCardapio={aoAbrirCardapio}
              aoAjustar={(sku, delta) => ajustarQuantidade(selecionada.id, sku, delta)}
              aoAjustarObservacao={(sku, texto) => ajustarObservacao(selecionada.id, sku, texto)}
              aoGerarPedido={(meio) => { escolherMeioPagamento(selecionada.id, meio); return gerarPedido(selecionada.id, meio) }}
              nomeCliente={selecionada.nome}
              endereco={cliente.endereco}
              faixa={faixa}
              bloqueado={estaBloqueada(selecionada)}
            />
            <BlocoCobranca
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
            {pedido.ocorrencia && (
              <BlocoOcorrencia
                ocorrencia={pedido.ocorrencia}
                conversaId={selecionada.id}
                nomeCliente={selecionada.nome}
                pedido={pedido}
              />
            )}
            <BlocoEntrega conversa={selecionada} />
          </>
        ) : (
          <Bloco titulo="Comanda">
            <p className={css.corpoBloco}>
              Nenhuma comanda aberta. O resumo e a cobrança nascem do pedido.
            </p>
            <Botao largo disabled={!editavel} onClick={aoAbrirCardapio}>
              Abrir cardápio
            </Botao>
          </Bloco>
        )}

        {/* Histórico e Notas saíram da coluna no passe de integração (direção 19,
            seção 8): moram na modal Histórico da F2, botão no bloco Cliente. */}
      </div>

      {pedido && editavel && (
        <BarraProximoPasso
          key={avancoEm ?? 'sem-avanco'}
          emDesfazer={Boolean(avancoEm)}
          pedido={pedido}
          agora={agora}
          nomeCliente={primeiroNome}
          bloqueado={estaBloqueada(selecionada)}
          aoAvancar={(passo, entregador) => avancarEsteira(selecionada.id, passo, entregador)}
          aoDesfazer={() => desfazerEsteira(selecionada.id, ultimoAvanco.mensagemId, ultimoAvanco.posEntregaId)}
          aoGerarPix={(meio) => { escolherMeioPagamento(selecionada.id, meio); gerarCobranca(selecionada.id, pedido, meio) }}
          aoReenviarPix={(valorForcado) => reenviarCobranca(selecionada.id, pedido, valorForcado ?? null)}
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
