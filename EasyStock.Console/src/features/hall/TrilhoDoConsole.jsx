// Trilho lateral do desktop (#3004): o mesmo no Início, no Balcão e nos módulos. A lista e a ordem vêm
// de `dominio/trilhoDoConsole.js`; aqui só o desenho. O que o Balcão desenha por conta própria (estado
// da loja, Entregas com resumo, Lembretes, Disposição) chega em `balcao.itens`, por id; fora do Balcão
// esses itens são links para o Balcão ("<rótulo>, abre no Balcão"). A conta (Tema, Ajuda e Sair) fecha o
// trilho, fora da rolagem, para ficar sempre à vista.
import { Fragment } from 'react'
import { Icone } from '../../componentes/Icone'
import { Marca } from '../../componentes/Marca'
import { MenuDaConta } from '../../componentes/MenuDaConta'
import { rotuloDoSair } from '../../componentes/contaDoMenu'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { simularEscondido } from '../../aplicacao/simularEscondido'
import { NOME_INICIO } from '../../dominio/modulos'
import { itensDoTrilho } from '../../dominio/trilhoDoConsole'
import { IconeDoModulo } from './IconeDoModulo'
import css from './trilhoDoConsole.module.css'

const ICONE_DO_ITEM = { loja: 'house', gestao: 'painel', lembretes: 'relogio', disposicao: 'modelo', simular: 'flask-conical' }

function ItemParaOBalcao({ item, aoNavegar }) {
  return (
    <a href={item.href} onClick={aoNavegar} className={css.item} aria-label={item.nome} title={item.nome}>
      <Icone nome={ICONE_DO_ITEM[item.id]} tamanho={24} /><span>{item.rotulo}</span>
    </a>
  )
}

function ItemDeLink({ item, aoNavegar }) {
  const classe = `${css.item} ${item.atual ? css.atual : ''}`
  const icone = <IconeDoModulo modulo={item.modulo} telaId={item.telaId} tamanho={24} />
  if (item.atual && item.noBalcao) {
    return <span className={classe} aria-current="page" title={item.nome}>{icone}<span>{item.rotulo}</span></span>
  }
  return (
    <a href={item.href} onClick={aoNavegar} className={classe} aria-current={item.atual ? 'page' : undefined} title={item.nome}>
      {icone}<span>{item.rotulo}</span>
    </a>
  )
}

export function TrilhoDoConsole({
  onde, fonteApi = false, rotuloNav = 'Navegação', aoNavegar, aoAbrirAjuda, balcao = null, atendente = null, emModal = false,
}) {
  const { permite, acoes, aoSair, sessaoPersistente, conta } = useAcessoModulos()
  const itens = itensDoTrilho({ permite, acoes, fonteApi, simulacoes: !fonteApi && !simularEscondido(), onde })
  const desenhado = balcao?.itens ?? {}
  return (
    <nav className={`${css.trilho} ${emModal ? css.emModal : ''}`} aria-label={rotuloNav}>
      <div className={css.itens}>
        {itens.map((item) => {
          if (item.id === 'inicio') {
            return <a key={item.id} className={css.inicio} href={item.href} onClick={aoNavegar} aria-current={item.atual ? 'page' : undefined} aria-label={`Ir para o ${NOME_INICIO}`} title={NOME_INICIO}>
              <Marca compacta /><span>{NOME_INICIO}</span>
            </a>
          }
          if (balcao && desenhado[item.id]) return <Fragment key={item.id}>{desenhado[item.id]}</Fragment>
          if (item.soNoBalcao) return <ItemParaOBalcao key={item.id} item={item} aoNavegar={aoNavegar} />
          return <ItemDeLink key={item.id} item={{ ...item, noBalcao: onde === 'balcao' }} aoNavegar={aoNavegar} />
        })}
      </div>
      <div className={css.conta}>
        <MenuDaConta nome={atendente ?? conta?.nome} detalhe={conta?.casa} aoSair={aoSair} aoAbrirAjuda={aoAbrirAjuda}
          rotuloSair={rotuloDoSair(sessaoPersistente)}
          tituloSair={sessaoPersistente ? 'A Cozinha permanece conectada ao fechar o navegador. Saia ao trocar de pessoa.' : undefined} />
      </div>
    </nav>
  )
}
