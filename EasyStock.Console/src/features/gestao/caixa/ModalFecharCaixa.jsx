import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoMascarado } from '../../../componentes/CampoMascarado'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { conferenciaDaGaveta } from '../../../dominio/caixa'
import {
  lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
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
export function ModalFecharCaixa({ resumo, gaveta = null, aoFechar, aoConfirmar }) {
  const esperado = gaveta ? gaveta.naGaveta : resumo.saldoEsperado
  const [centavosContado, setCentavosContado] = useState(null)
  const conferencia = conferenciaDaGaveta(centavosContado, esperado)

  return (
    <Modal
      titulo="Fechar o caixa"
      descricao="Depois de fechado, os lançamentos de hoje não podem mais ser estornados."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
          <Botao
            variante="primario"
            icone="log-out"
            disabled={!conferencia.pronta || moedaAltaDemais(centavosContado)}
            onClick={() => aoConfirmar(centavosContado / 100)}
          >
            Fechar o caixa
          </Botao>
        </>
      )}
    >
      <dl className={css.resumoLista}>
        {gaveta ? (
          <div className={css.resumoForte}><dt>Na gaveta (dinheiro)</dt><dd>{moeda(gaveta.naGaveta)}</dd></div>
        ) : (
          <div className={css.resumoForte}><dt>Saldo esperado</dt><dd>{moeda(resumo.saldoEsperado)}</dd></div>
        )}
        <div><dt>Saldo inicial</dt><dd>{moeda(resumo.saldoInicial)}</dd></div>
        <div><dt>Outras entradas</dt><dd>{moeda(resumo.totalEntradasExtras)}</dd></div>
        <div><dt>Outras saídas</dt><dd>{moeda(resumo.totalSaidasExtras)}</dd></div>
        <div><dt>Pagamentos de pedidos</dt><dd>{moeda(resumo.totalPagamentosPedidos)}</dd></div>
        {gaveta && <div><dt>Pix e cartão (fora da gaveta)</dt><dd>{moeda(gaveta.pixECartao)}</dd></div>}
      </dl>

      <CampoMascarado
        tipo="moeda"
        rotulo={gaveta ? 'Quanto você contou na gaveta (só dinheiro)' : 'Quanto você contou no caixa'}
        placeholder="Digite o que contou"
        valor={centavosContado == null ? '' : mascaraMoeda(centavosContado)}
        erro={moedaAltaDemais(centavosContado) ? 'Valor alto demais' : null}
        aoMudarDigitos={(digitos) => setCentavosContado(digitos ? Number(digitos) : null)}
        aoColarTexto={(texto) => setCentavosContado(lerMoeda(texto))}
      />

      {conferencia.pronta && (
        <p className={css.corpoDiferenca}>
          Diferença: <Pilula tom={conferencia.tom}>{conferencia.texto}</Pilula>
        </p>
      )}
    </Modal>
  )
}
