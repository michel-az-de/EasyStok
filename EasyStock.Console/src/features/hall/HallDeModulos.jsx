// Hall de módulos (#1447, homologação de 07/10): a tela inicial depois do login,
// com os módulos do ERP da Casa da Baba numerados como no plano (ADR-0056). O
// módulo ligado abre pela rota; o que ainda não tem tela aparece "Em breve" e
// não navega. Os atalhos embaixo do nome são o menu do módulo (as outras telas).
import { Icone } from '../../componentes/Icone'
import { useAtendimento } from '../../aplicacao/contextos'
import { hashDoModulo, modulosDoHall, saudacaoDoHall } from '../../dominio/modulos'
import css from './hall.module.css'

const doisDigitos = (n) => String(n).padStart(2, '0')

function CartaoModulo({ modulo }) {
  const atalhos = modulo.telas.slice(1)
  if (!modulo.disponivel) {
    return (
      <li className={`${css.cartao} ${css.emBreve}`}>
        <span className={css.numero}>{doisDigitos(modulo.numero)}</span>
        <span className={css.icone}><Icone nome={modulo.icone} tamanho={28} /></span>
        <span className={css.nome}>{modulo.nome}</span>
        <span className={css.resumo}>{modulo.resumo}</span>
        <span className={css.selo}><Icone nome="lock" tamanho={16} /> Em breve</span>
      </li>
    )
  }
  return (
    <li className={css.cartao}>
      <span className={css.numero}>{doisDigitos(modulo.numero)}</span>
      <span className={css.icone}><Icone nome={modulo.icone} tamanho={28} /></span>
      <a className={css.abrir} href={modulo.href}>
        <span className={css.nome}>{modulo.nome}</span>
      </a>
      <span className={css.resumo}>{modulo.resumo}</span>
      {atalhos.length > 0 && (
        <ul className={css.atalhos} aria-label={`Outras telas de ${modulo.nome}`}>
          {atalhos.map((tela) => (
            <li key={tela.id}>
              <a className={css.atalho} href={hashDoModulo(modulo.id, tela.id)}>{tela.rotulo}</a>
            </li>
          ))}
        </ul>
      )}
    </li>
  )
}

export function HallDeModulos() {
  const { fonteApi, sessao, agora } = useAtendimento()
  const modulos = modulosDoHall({ fonteApi })
  return (
    <div className={css.hall}>
      <header className={css.topo}>
        <p className={css.marca}>
          <span className={css.casa}>Casa da Baba</span>
          <span className={css.produto}>EasyStok</span>
        </p>
      </header>
      <main className={css.conteudo}>
        <div className={css.cabeca}>
          <h1 className={css.titulo}>{saudacaoDoHall(agora, sessao?.usuario?.nome)}</h1>
          <p className={css.apoio}>Escolha o módulo para começar.</p>
        </div>
        <ul className={css.grade} aria-label="Módulos">
          {modulos.map((modulo) => <CartaoModulo key={modulo.id} modulo={modulo} />)}
        </ul>
      </main>
    </div>
  )
}
