import { MenuDeAcoes } from '../../componentes/MenuDeAcoes'
import { ROTULO_DO_MENU_DA_FICHA } from './apresentacaoDaFicha'
import css from './ficha.module.css'

// #2232 (F1 e F7): um menu só no cabeçalho da Ficha, com texto visível ("Ações"). Reúne o que eram
// dois botões só de ícone (editar e baixar) e o ⋮ que ficava ao lado de "Marcar pago". A confirmação
// em linha de cancelar, estornar e voltar etapa segue na barra do pedido (secao 4: nunca em modal).
// #2185: é o MenuDeAcoes, o menu único do Console, com o nome escrito no gatilho.
// `itens` vem de `itensDoMenuDaFicha`; `pdf` diz se o item de baixar está ligado e por quê não.
export function MenuDaFicha({ itens, aoEscolher, pdf = null }) {
  const semPdf = pdf?.motivo ?? null
  const baixando = Boolean(pdf?.baixando)
  const doMenu = itens.map((item) => ({
    ...item,
    aoEscolher: () => aoEscolher(item.chave),
    ...(item.chave === 'baixar' && {
      rotulo: baixando ? 'Preparando PDF' : item.rotulo,
      desabilitado: Boolean(semPdf) || baixando,
      motivo: semPdf ?? undefined,
    }),
  }))
  return (
    <span className={css.comMenu}>
      {/* O menu fecha ao escolher "Baixar": a espera do PDF aparece no gatilho, como no botão antigo. */}
      <MenuDeAcoes
        rotulo={baixando ? `${ROTULO_DO_MENU_DA_FICHA}: preparando PDF` : ROTULO_DO_MENU_DA_FICHA}
        texto={baixando ? 'Preparando PDF…' : 'Ações'} ocupado={baixando} itens={doMenu} className={css.gatilhoAcoes}
      />
      {pdf?.erro && <output role="alert" className={css.erroPdf}>{pdf.erro}</output>}
    </span>
  )
}
