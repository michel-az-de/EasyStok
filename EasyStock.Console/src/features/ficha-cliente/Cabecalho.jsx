import { horaCurta } from '../../dominio/formato'
import { numeroCurto } from '../../dominio/pedido'
import css from './comanda.module.css'

// Cabeçalho da comanda, reusado pelo canhoto (mesmo desenho, seção 7): o
// título muda de contexto por fora (Modal "Canhoto NNNN"), aqui dentro é
// sempre "Comanda" + número.
export function Cabecalho({ numero, nomeCliente, faixa, enviadaEm }) {
  return (
    <header className={css.cabecalho}>
      <div className={css.linhaCabecalho}>
        <h2>Comanda</h2>
        <span className={css.numero}>Nº {numeroCurto(numero)}</span>
      </div>
      <p className={css.subCabecalho}>
        {nomeCliente} · Janela {faixa ?? 'não escolhida'}
        {enviadaEm != null && ` · Enviada ${horaCurta(enviadaEm)}`}
      </p>
    </header>
  )
}
