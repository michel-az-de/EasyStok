import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoReais } from '../../../componentes/CampoReais'
import { DiferencaDaGaveta } from '../../../componentes/DiferencaDaGaveta'
import { Modal } from '../../../componentes/Modal'
import { useDevolucoesPendentes } from '../../../aplicacao/useDevolucoesPendentes'
import { diaMes } from '../../../dominio/aberturaDaLoja'
import {
  AVISO_AO_FECHAR_CAIXA, ROTULO_CONTADO_DO_ESQUECIDO, ROTULO_ENTROU_DEPOIS, ROTULO_FORA_DA_GAVETA, avisoDeDevolucoesPendentes,
  conferenciaDoFechamento, esperadoDoEsquecido,
} from '../../../dominio/caixa'
import {
  lerMoeda, moeda, moedaAltaDemais,
} from '../../../dominio/formato'
import css from './abaCaixa.module.css'

// Fechar o dia com conferência (aceite): ela digita o que contou na mão, o
// sistema mostra a diferença contra o esperado (SaldoInicial + entradas -
// saídas + pagamentos de pedido, igual ao FechamentoCaixa do EasyStok).
// Diferença não trava o fechamento: é achado para ela decidir, não erro do
// sistema (mesma filosofia de RN-06 "o sistema avisa, não trava a venda").
//
// #1474: com `gaveta` (modo API), o esperado é só o dinheiro da gaveta; Pix e cartão
// aparecem à parte e não entram na diferença do que ela contou. O campo nasce vazio (nascer
// com o esperado mostrava "bate certo" sem ninguém contar) e o botão espera a contagem.
// "Fechar o caixa", não "fechar o dia": fechar a loja é outro gesto.
// J2 (#1990): o valor contado se digita em reais ("58" é R$ 58,00) e, no modo API
// (`consultarDevolucoes`), o modal avisa o que falta devolver de pedidos cancelados, sem travar.
//
// #2078: o foco abre no campo do contado e, com valor digitado, Esc e clique fora não fecham (B11/E4).
// Três avisos diferentes (B12): `aviso` é da releitura antes de fechar e vai como veio; `erro` é a API
// que recusou; `erroSemResposta` é a rede que caiu, quando ninguém sabe se o fechamento gravou. Nenhum
// deles culpa o valor contado: a API nem recebe o contado como número.
export function ModalFecharCaixa({
  resumo, gaveta = null, aoFechar, aoConfirmar, enviando = false, erro = null, erroSemResposta = false, aviso = null, consultarDevolucoes = false,
}) {
  const esperado = gaveta ? gaveta.naGaveta : resumo.saldoEsperado
  const [centavosContado, setCentavosContado] = useState(null)
  // #2113 (D1 da #2084): no caixa esquecido, confere o esperado do dia da abertura; o que entrou depois fica à parte.
  const esquecido = gaveta ? esperadoDoEsquecido(resumo) : null
  const conferencia = conferenciaDoFechamento(centavosContado, esperado, esquecido)
  const devolucoes = useDevolucoesPendentes(consultarDevolucoes)
  const avisoDevolucoes = avisoDeDevolucoesPendentes(devolucoes)
  const falha = aviso ?? (erro && (erroSemResposta
    ? `${erro} Não deu para saber se o fechamento foi gravado. Ao tentar de novo, o caixa é relido antes e não fecha duas vezes.`
    : `O caixa não foi fechado. ${erro}`))

  return (
    <Modal
      titulo="Fechar o caixa"
      descricao={AVISO_AO_FECHAR_CAIXA}
      focarCampo
      segurarFechamento={centavosContado != null || enviando}
      aoFechar={() => { if (!enviando) aoFechar() }}
      rodape={(
        <>
          <Botao variante="texto" disabled={enviando} onClick={aoFechar}>Cancelar</Botao>
          <Botao
            variante="primario"
            icone="log-out"
            disabled={enviando || !conferencia.pronta || moedaAltaDemais(centavosContado)}
            onClick={() => aoConfirmar(centavosContado / 100)}
          >
            {enviando ? 'Fechando o caixa…' : 'Fechar o caixa'}
          </Botao>
        </>
      )}
    >
      {/* #2078 (B12): em grade, para o aviso de devolução não colar no rótulo do campo. */}
      <div className={css.corpoModal}>
        <dl className={css.resumoLista}>
          {esquecido ? (
            <>
              <div className={css.resumoForte}><dt>Esperado no fechamento de {diaMes(resumo.abertoDesde)}</dt><dd>{moeda(esquecido.esperado)}</dd></div>
              <div><dt>{ROTULO_ENTROU_DEPOIS}</dt><dd>{moeda(esquecido.entrouDepois)}</dd></div>
            </>
          ) : gaveta ? (
            <div className={css.resumoForte}><dt>Na gaveta (dinheiro)</dt><dd>{moeda(gaveta.naGaveta)}</dd></div>
          ) : (
            <div className={css.resumoForte}><dt>Saldo esperado</dt><dd>{moeda(resumo.saldoEsperado)}</dd></div>
          )}
          <div><dt>Saldo inicial</dt><dd>{moeda(resumo.saldoInicial)}</dd></div>
          {Boolean(resumo.totalVendas) && <div><dt>Vendas</dt><dd>{moeda(resumo.totalVendas)}</dd></div>}
          <div><dt>Outras entradas</dt><dd>{moeda(resumo.totalEntradasExtras)}</dd></div>
          <div><dt>Outras saídas</dt><dd>{moeda(resumo.totalSaidasExtras)}</dd></div>
          <div><dt>Pagamentos de pedidos</dt><dd>{moeda(resumo.totalPagamentosPedidos)}</dd></div>
          {gaveta && <div><dt>{ROTULO_FORA_DA_GAVETA}</dt><dd>{moeda(gaveta.pixECartao)}</dd></div>}
          {/* #1627: pode ser dinheiro que já está na gaveta (venda do pedido entregue pelo PWA, #1630). */}
          {Boolean(gaveta?.semForma) && <div><dt>Sem forma de pagamento (confira se é dinheiro)</dt><dd>{moeda(gaveta.semForma)}</dd></div>}
        </dl>

        {avisoDevolucoes && <output className={css.avisoAtencao}>{avisoDevolucoes}</output>}
        {devolucoes.estado === 'erro' && (
          <output className={css.avisoAtencao}>Não foi possível conferir se há devolução pendente de pedido cancelado.</output>
        )}

        <CampoReais
          disabled={enviando}
          rotulo={esquecido ? ROTULO_CONTADO_DO_ESQUECIDO : gaveta ? 'Quanto você contou na gaveta (só dinheiro)' : 'Quanto você contou no caixa'}
          placeholder="Digite o que contou"
          centavos={centavosContado}
          ler={lerMoeda}
          erro={moedaAltaDemais(centavosContado) ? 'Valor alto demais' : null}
          aoMudar={setCentavosContado}
          data-foco-inicial=""
        />

        {falha && <p className={css.avisoFechado} role="alert">{falha}</p>}

        <DiferencaDaGaveta conferencia={conferencia} className={css.corpoDiferenca} />
      </div>
    </Modal>
  )
}
