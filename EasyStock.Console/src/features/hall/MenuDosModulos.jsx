import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Marca } from '../../componentes/Marca'
import { HASH_HALL } from '../../dominio/rota'
import { MODULOS, hashDoModulo, telasDoMenu } from '../../dominio/modulos'
import { IconeDoModulo } from './IconeDoModulo'
import css from './moldura.module.css'

export function MenuDosModulos({ moduloId, telaId, fonteApi, aoNavegar }) {
  const { permite } = useAcessoModulos()
  const modulo = MODULOS.find((item) => item.id === moduloId)
  const areas = MODULOS.filter((item) => permite(item.id) && telasDoMenu(item, { fonteApi }).length)
  return <>
    <nav className={css.trilho} aria-label="Módulos da loja">
      <a className={css.inicioTrilho} href={HASH_HALL} onClick={aoNavegar} aria-label="Ir para os módulos"><Marca compacta /><span>Módulos</span></a>
      {areas.map((item) => <a key={item.id} href={hashDoModulo(item.id)} onClick={aoNavegar} className={`${css.itemTrilho} ${item.id === moduloId ? css.moduloAtivo : ''}`} aria-current={item.id === moduloId ? 'true' : undefined}>
        <IconeDoModulo modulo={item} /><span>{item.nome}</span>
      </a>)}
    </nav>
    <nav className={css.menu} aria-label={`Telas de ${modulo.nome}`}>
      <h2 className={css.nomeMenu}><IconeDoModulo modulo={modulo} />{modulo.nome}</h2>
      {telasDoMenu(modulo, { fonteApi }).map((item) => <a key={item.id} href={hashDoModulo(modulo.id, item.id)} onClick={aoNavegar} className={`${css.item} ${item.id === telaId ? css.itemAtivo : ''}`} aria-current={item.id === telaId ? 'page' : undefined}>
        <IconeDoModulo modulo={modulo} telaId={item.id} /><span>{item.rotulo}</span>
      </a>)}
    </nav>
  </>
}
