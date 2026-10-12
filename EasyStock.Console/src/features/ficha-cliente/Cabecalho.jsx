import { horaCurta } from '../../dominio/formato'
import { numeroCurto } from '../../dominio/pedido'
import css from './comanda.module.css'

// Cabeçalho da comanda, reusado pelo canhoto (mesmo desenho, seção 7): o
// título muda de contexto por fora (Modal "Canhoto NNNN"), aqui dentro é
// sempre "Comanda" + número.
//
// #1474: `numero` nulo é a comanda do modo API antes de o pedido existir (sem número
// inventado); `semJanela` avisa no cabeçalho que a janela de entrega ainda falta.
export function Cabecalho({ numero, nomeCliente, faixa, enviadaEm, semJanela = false }) {
  return (
    <header className={css.cabecalho}>
      <div className={css.linhaCabecalho}>
        <h2>Comanda</h2>
        <span className={css.numero}>{numero == null ? 'Rascunho' : `Nº ${numeroCurto(numero)}`}</span>
      </div>
      <p className={css.subCabecalho}>
        {/* #1474: só a janela escolhida; sem escolha, o aviso "Sem janela". */}
        {nomeCliente}{faixa && ` · Janela ${faixa}`}
        {!faixa && semJanela && <span className={css.semJanela}> · Sem janela</span>}
        {enviadaEm != null && ` · Enviada ${horaCurta(enviadaEm)}`}
      </p>
    </header>
  )
}
