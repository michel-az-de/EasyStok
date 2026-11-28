// #3417: abaixo de 1180 px o trilho some em toda rota; este botão abre o mesmo trilho num modal, no Início e no topo do
// Atendimento (nos módulos o Menu da MolduraDoModulo faz o mesmo, junto com as telas do módulo).
import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { TrilhoDoConsole } from './TrilhoDoConsole'
import css from './moldura.module.css'

export function MenuDoTrilho({ onde, fonteApi = false, aoAbrirAjuda, className = '' }) {
  const [aberto, setAberto] = useState(false)
  return <>
    <Botao className={className} icone="painel" aria-haspopup="dialog" aria-expanded={aberto} onClick={() => setAberto(true)}>Menu</Botao>
    {aberto && <Modal titulo="Menu" aoFechar={() => setAberto(false)} largura="min(600px, calc(100vw - 24px))">
      <div className={css.menuModal}>
        <TrilhoDoConsole onde={onde} fonteApi={fonteApi} rotuloNav="Navegação" aoAbrirAjuda={aoAbrirAjuda} emModal
          aoNavegar={(evento) => { if (evento.target.closest('a')) setAberto(false) }} />
      </div>
    </Modal>}
  </>
}
