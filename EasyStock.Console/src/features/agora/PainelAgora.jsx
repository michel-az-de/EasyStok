// #1608: painel "Agora" da entrada do console. Três filas na ordem do trabalho
// (responder, cozinhar, despachar), cada uma com a contagem, os primeiros itens
// e um toque para a tela onde se resolve. Só leitura: quem age é o Balcão, a
// Cozinha e as Entregas. As filas chegam prontas de `dominio/agora.js`.
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { hashDoModulo } from '../../dominio/modulos'
import css from './agora.module.css'

const LIMITE = 4

// `aviso` (#1611): a fila ainda não sabe a contagem (lista carregando ou falhou).
// Sai o número e sai o "em dia": zero sem lista não é fila vazia.
function Fila({ titulo, apoio, itens, emDia, href, rotuloDoLink, renderizar, aviso = null }) {
  const resto = itens.length - LIMITE
  if (aviso) {
    return (
      <section className={css.fila} aria-label={titulo}>
        <header className={css.cabeca}>
          <span className={css.numero} data-vazio="true" aria-hidden="true">–</span>
          <div>
            <h2 className={css.titulo}>{titulo}</h2>
            <p className={css.apoio}>{apoio}</p>
          </div>
        </header>
        <p className={css.emDia} role="status">{aviso}</p>
        <a className={css.abrir} href={href}>Abrir {rotuloDoLink}<MarcaIcone nome="proximo" tamanho={16} /></a>
      </section>
    )
  }
  return (
    <section className={css.fila} aria-label={`${titulo}: ${itens.length}`}>
      <header className={css.cabeca}>
        <span className={css.numero} data-vazio={itens.length === 0}>{itens.length}</span>
        <div>
          <h2 className={css.titulo}>{titulo}</h2>
          <p className={css.apoio}>{apoio}</p>
        </div>
      </header>
      {itens.length === 0
        ? <p className={css.emDia}><MarcaIcone nome="confirmar" tamanho={16} /> {emDia}</p>
        : <ul className={css.itens}>{itens.slice(0, LIMITE).map((item) => <li key={item.id}>{renderizar(item)}</li>)}</ul>}
      <a className={css.abrir} href={href}>
        {resto > 0 ? `Ver mais ${resto} em ${rotuloDoLink}` : `Abrir ${rotuloDoLink}`}
        <MarcaIcone nome="proximo" tamanho={16} />
      </a>
    </section>
  )
}

const Pedido = (item) => (
  <div className={css.item} data-tom={item.atrasado ? 'perigo' : undefined}>
    <span className={css.quem}>{item.nome}</span>
    <span className={css.selo}>{item.situacao}</span>
    <span className={css.detalhe}>Pedido {item.numero}{item.faixa ? ` · ${item.faixa}` : ''}</span>
  </div>
)

export function PainelAgora({ filas, podeVer, aoAbrirConversa, avisoResponder = null }) {
  if (!podeVer.atendimento && !podeVer.cozinha && !podeVer.entregas) return null
  return (
    <section className={css.painel} aria-label="Agora">
      <p className={css.legenda}>Agora</p>
      <div className={css.filas}>
        {podeVer.atendimento && <Fila titulo="Responder" apoio="Só o que o automático não resolve"
          itens={filas.responder} emDia="Ninguém esperando você." aviso={avisoResponder}
          href={hashDoModulo('atendimento', 'balcao')} rotuloDoLink="Balcão"
          renderizar={(item) => (
            <button type="button" className={css.item} data-tom={item.tom} onClick={() => aoAbrirConversa(item.id)}>
              <span className={css.quem}>{item.nome}</span>
              <span className={css.selo}>{item.rotulo}</span>
              <span className={css.detalhe}>{item.texto}</span>
            </button>
          )} />}
        {podeVer.cozinha && <Fila titulo="Cozinhar" apoio="Pago e em preparo, o que sai antes primeiro"
          itens={filas.cozinhar} emDia="Nada na fila de preparo."
          href={hashDoModulo('cozinha', 'fila')} rotuloDoLink="Cozinha" renderizar={Pedido} />}
        {podeVer.entregas && <Fila titulo="Despachar" apoio="Pronto e a caminho"
          itens={filas.despachar} emDia="Nada esperando sair."
          href={hashDoModulo('entregas', 'painel')} rotuloDoLink="Entregas" renderizar={Pedido} />}
      </div>
    </section>
  )
}
