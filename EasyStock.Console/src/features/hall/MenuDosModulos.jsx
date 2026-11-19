import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { MenuDaConta } from '../../componentes/MenuDaConta'
import { Marca } from '../../componentes/Marca'
import { HASH_HALL } from '../../dominio/rota'
import { MODULOS, NOME_INICIO, NOME_RESPOSTAS_AUTOMATICAS, atalhosDiretos, ehAtalhoDireto, emOrdemDosModulos, hashDoModulo, moduloPorId, telasDoMenu } from '../../dominio/modulos'
import { IconeDoModulo } from './IconeDoModulo'
import css from './moldura.module.css'

export function MenuDosModulos({ moduloId, telaId, fonteApi, aoNavegar, semMenu = false }) {
  const { permite, aoSair, sessaoPersistente, conta, acoes } = useAcessoModulos()
  const modulo = MODULOS.find((item) => item.id === moduloId)
  // #2258 (auditoria A2): a ordem dos módulos é a mesma do Início, da Ajuda e da Busca.
  // Em Respostas automáticas o atual é o próprio atalho, não o módulo Atendimento.
  const naRespostas = moduloId === 'atendimento' && telaId === 'respostas'
  // #2870: em Clientes e em Pedidos o atual é o atalho, como em Respostas automáticas.
  const noAtalho = naRespostas || ehAtalhoDireto(moduloId, telaId)
  const areas = emOrdemDosModulos(MODULOS).filter((item) => permite(item.id) && telasDoMenu(item, { fonteApi, acoes }).length)
  const atalhos = atalhosDiretos({ fonteApi, acoes, permite })
  return <>
    <nav className={css.trilho} aria-label="Módulos da loja">
      <a className={css.inicioTrilho} href={HASH_HALL} onClick={aoNavegar} aria-label={`Ir para o ${NOME_INICIO}`}><Marca compacta /><span>{NOME_INICIO}</span></a>
      {areas.map((item) => <a key={item.id} href={hashDoModulo(item.id)} onClick={aoNavegar} className={`${css.itemTrilho} ${item.id === moduloId && !noAtalho ? css.moduloAtivo : ''}`} aria-current={item.id === moduloId && !noAtalho ? 'true' : undefined} title={item.nome}>
        <IconeDoModulo modulo={item} /><span>{item.nome}</span>
      </a>)}
      {atalhos.map((atalho) => {
        const atual = atalho.moduloId === moduloId && atalho.telaId === telaId
        return <a key={`atalho-${atalho.id}`} href={atalho.href} onClick={aoNavegar} className={`${css.itemTrilho} ${atual ? css.moduloAtivo : ''}`} aria-current={atual ? 'true' : undefined} title={atalho.nome}>
          <IconeDoModulo modulo={atalho.modulo} telaId={atalho.telaId} /><span>{atalho.nome}</span>
        </a>
      })}
      {/* #1839 (F1): as automáticas só abriam pelo trilho do Balcão. O atalho leva à tela que as liga e escreve. */}
      {permite('atendimento') && <a href={hashDoModulo('atendimento', 'respostas')} onClick={aoNavegar}
        className={`${css.itemTrilho} ${naRespostas ? css.moduloAtivo : ''}`} aria-current={naRespostas ? 'true' : undefined} title={NOME_RESPOSTAS_AUTOMATICAS}>
        <IconeDoModulo modulo={moduloPorId('atendimento')} telaId="respostas" /><span>{NOME_RESPOSTAS_AUTOMATICAS}</span>
      </a>}
      {/* #1870: o Sair mora atrás do avatar, no pé do trilho, como no Balcão. */}
      <span className={css.espacoTrilho} />
      <div className={css.avatarTrilho}>
        <MenuDaConta nome={conta?.nome} detalhe={conta?.casa} aoSair={aoSair}
          rotuloSair={sessaoPersistente ? 'Sair deste aparelho' : 'Sair'}
          tituloSair={sessaoPersistente ? 'A Cozinha permanece conectada ao fechar o navegador. Saia ao trocar de pessoa.' : undefined}>
          <InterruptorTema />
        </MenuDaConta>
      </div>
    </nav>
    {!semMenu && <nav className={css.menu} aria-label={`Telas de ${modulo.nome}`}>
      <h2 className={css.nomeMenu}><IconeDoModulo modulo={modulo} />{modulo.nome}</h2>
      {telasDoMenu(modulo, { fonteApi, acoes }).map((item) => <a key={item.id} href={hashDoModulo(modulo.id, item.id)} onClick={aoNavegar} className={`${css.item} ${item.id === telaId ? css.itemAtivo : ''}`} aria-current={item.id === telaId ? 'page' : undefined}>
        <IconeDoModulo modulo={modulo} telaId={item.id} /><span>{item.rotulo}{fonteApi && item.indisponivelNaApi && <small className={css.preparacao}>Em preparação</small>}</span>
      </a>)}
    </nav>}
  </>
}
