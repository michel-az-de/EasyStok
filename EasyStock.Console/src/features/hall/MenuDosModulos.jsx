import { TrilhoDoConsole } from './TrilhoDoConsole'
import { pedirAjuda } from '../../aplicacao/pedirAjuda'
import { MODULOS, hashDoModulo, telasDoMenu } from '../../dominio/modulos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { IconeDoModulo } from './IconeDoModulo'
import css from './moldura.module.css'

export function MenuDosModulos({ moduloId, telaId, fonteApi, aoNavegar, semMenu = false }) {
  const { acoes } = useAcessoModulos()
  const modulo = MODULOS.find((item) => item.id === moduloId)
  return <>
    {/* #3004: o trilho é o mesmo do Início e do Balcão (`TrilhoDoConsole`); aqui só a rota atual. */}
    <TrilhoDoConsole onde={{ moduloId, telaId }} fonteApi={fonteApi} rotuloNav="Módulos da loja" aoNavegar={aoNavegar}
      aoAbrirAjuda={pedirAjuda} emModal={Boolean(aoNavegar)} />
    {!semMenu && <nav className={css.menu} aria-label={`Telas de ${modulo.nome}`}>
      <h2 className={css.nomeMenu}><IconeDoModulo modulo={modulo} />{modulo.nome}</h2>
      {telasDoMenu(modulo, { fonteApi, acoes }).map((item) => <a key={item.id} href={hashDoModulo(modulo.id, item.id)} onClick={aoNavegar} className={`${css.item} ${item.id === telaId ? css.itemAtivo : ''}`} aria-current={item.id === telaId ? 'page' : undefined}>
        <IconeDoModulo modulo={modulo} telaId={item.id} /><span>{item.rotulo}{fonteApi && item.indisponivelNaApi && <small className={css.preparacao}>Em preparação</small>}</span>
      </a>)}
    </nav>}
  </>
}
