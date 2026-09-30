import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoMascarado } from '../../../componentes/CampoMascarado'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import {
  lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../../dominio/formato'
import css from './abaCaixa.module.css'

// Fechar o dia com conferência (aceite): ela digita o que contou na mão, o
// sistema mostra a diferença contra o esperado (SaldoInicial + entradas -
// saídas + pagamentos de pedido, igual ao FechamentoCaixa do EasyStok).
// Diferença não trava o fechamento: é achado para ela decidir, não erro do
// sistema (mesma filosofia de RN-06 "o sistema avisa, não trava a venda").
export function ModalFecharCaixa({ resumo, aoFechar, aoConfirmar }) {
  const [centavosContado, setCentavosContado] = useState(Math.round(resumo.saldoEsperado * 100))

  const contado = centavosContado / 100
  const diferenca = contado - resumo.saldoEsperado
  const tomDiferenca = diferenca === 0 ? 'ok' : Math.abs(diferenca) <= 5 ? 'aviso' : 'perigo'

  return (
    <Modal
      titulo="Fechar o dia"
      descricao="Depois de fechado, os lançamentos de hoje não podem mais ser estornados."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
          <Botao
            variante="primario"
            icone="log-out"
            disabled={moedaAltaDemais(centavosContado)}
            onClick={() => aoConfirmar(contado)}
          >
            Fechar o dia
          </Botao>
        </>
      )}
    >
      <dl className={css.resumoLista}>
        <div><dt>Saldo inicial</dt><dd>{moeda(resumo.saldoInicial)}</dd></div>
        <div><dt>Entradas extras</dt><dd>{moeda(resumo.totalEntradasExtras)}</dd></div>
        <div><dt>Saídas extras</dt><dd>{moeda(resumo.totalSaidasExtras)}</dd></div>
        <div><dt>Pagamentos de pedidos</dt><dd>{moeda(resumo.totalPagamentosPedidos)}</dd></div>
        <div className={css.resumoForte}><dt>Saldo esperado</dt><dd>{moeda(resumo.saldoEsperado)}</dd></div>
      </dl>

      <CampoMascarado
        tipo="moeda"
        rotulo="Quanto você contou no caixa"
        valor={mascaraMoeda(centavosContado)}
        erro={moedaAltaDemais(centavosContado) ? 'Valor alto demais' : null}
        aoMudarDigitos={(digitos) => setCentavosContado(Number(digitos || '0'))}
        aoColarTexto={(texto) => setCentavosContado(lerMoeda(texto))}
      />

      <p className={css.corpoDiferenca}>
        Diferença: <Pilula tom={tomDiferenca}>{diferenca === 0 ? 'bate certo' : moeda(diferenca)}</Pilula>
      </p>
    </Modal>
  )
}
