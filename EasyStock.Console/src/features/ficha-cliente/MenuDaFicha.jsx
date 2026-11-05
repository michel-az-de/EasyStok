import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Popover } from '../../componentes/Popover'
import { ROTULO_DO_MENU_DA_FICHA } from './apresentacaoDaFicha'
import css from './ficha.module.css'

// #2232 (F1 e F7): um menu só no cabeçalho da Ficha, com texto visível ("Ações"). Reúne o que eram
// dois botões só de ícone (editar e baixar) e o ⋮ que ficava ao lado de "Marcar pago". A confirmação
// em linha de cancelar, estornar e voltar etapa segue na barra do pedido (secao 4: nunca em modal).
// `itens` vem de `itensDoMenuDaFicha`; `pdf` diz se o item de baixar está ligado e por quê não.
export function MenuDaFicha({ itens, aoEscolher, pdf = null }) {
  const [aberto, setAberto] = useState(false)
  const fechar = () => setAberto(false)
  const escolher = (chave) => { fechar(); aoEscolher(chave) }
  const semPdf = pdf?.motivo ?? null
  return (
    <span className={css.comMenu}>
      <Botao
        variante="secundario"
        className={css.gatilhoAcoes}
        icone="ellipsis-vertical"
        aria-haspopup="menu"
        aria-expanded={aberto}
        aria-label={ROTULO_DO_MENU_DA_FICHA}
        onClick={() => setAberto((v) => !v)}
      >
        Ações
      </Botao>
      {pdf?.erro && <output role="alert" className={css.erroPdf}>{pdf.erro}</output>}
      {aberto && (
        <Popover rotulo={ROTULO_DO_MENU_DA_FICHA} posicao="abaixo" aoFechar={fechar} portal>
          <div className={css.menuAcoes}>
            {itens.map((item) => {
              const desligado = item.chave === 'baixar' && Boolean(semPdf || pdf?.baixando)
              return (
                <button key={item.chave} type="button" role="menuitem" className={css.itemMenu}
                  aria-disabled={desligado || undefined} title={item.chave === 'baixar' ? semPdf ?? undefined : undefined}
                  onClick={() => { if (!desligado) escolher(item.chave) }}>
                  <Icone nome={item.icone} />
                  {item.chave === 'baixar' && pdf?.baixando ? 'Preparando PDF' : item.rotulo}
                  {item.chave === 'baixar' && semPdf && <small className={css.motivoItemMenu}>{semPdf}</small>}
                </button>
              )
            })}
          </div>
        </Popover>
      )}
    </span>
  )
}
