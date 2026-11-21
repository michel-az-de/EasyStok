import { IconeDoModulo } from './IconeDoModulo'
import css from './atalhosCelular.module.css'

// #3013: no celular os módulos ficavam depois do painel do dia. Esta faixa fica logo abaixo da busca e repete o
// destino e a linha de estado do cartão (`estadoDe` é o `estadoDoCartao` do Início, mesma leitura, sem chamada nova).
// Só aparece até 640 px; o cartão da grade segue sendo o caminho no resto das telas.
export function FaixaDeAtalhos({ modulos, estadoDe }) {
  const atalhos = modulos.filter((modulo) => modulo.disponivel)
  if (atalhos.length === 0) return null
  return (
    <nav className={css.faixa} aria-label="Atalhos dos módulos">
      <ul className={css.lista}>
        {atalhos.map((modulo) => {
          const estado = estadoDe(modulo.id)
          return (
            <li key={modulo.id} className={css.item}>
              <a className={css.atalho} href={estado?.href ?? modulo.href}>
                <span className={css.icone} aria-hidden="true"><IconeDoModulo modulo={modulo} tamanho={20} /></span>
                <span className={css.nome}>{modulo.nome}</span>
                {estado && <span className={css.estado} data-atencao={estado.atencao || undefined}>{estado.texto}</span>}
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
