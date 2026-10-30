import { useState } from 'react'
import { Modal } from '../../componentes/Modal'
import { Botao } from '../../componentes/Botao'
import { aplicarFocoDoHall, FOCOS_DO_HALL, normalizarPreferenciasHall, ordenarPaineisDoHall } from '../../dominio/preferenciasHall'
import { IconeDoModulo } from './IconeDoModulo'
import css from './hall.module.css'

export function PersonalizarHall({ preferencia, modulos, aoSalvar, aoFechar, aoOrganizar }) {
  const [rascunho, setRascunho] = useState(preferencia)
  const mudar = (campo, valor) => setRascunho((atual) => ({ ...atual, [campo]: valor }))
  const paineis = ordenarPaineisDoHall(rascunho, modulos.map((modulo) => modulo.id))
  return <Modal titulo="Ajustar tela inicial" descricao="Ajustes salvos neste navegador para sua conta e empresa." aoFechar={aoFechar} largura="720px"
    rodape={<>{aoOrganizar && <Botao variante="texto" onClick={aoOrganizar}>Organizar áreas e favoritos</Botao>}<Botao variante="texto" onClick={aoFechar}>Cancelar</Botao><Botao variante="primario" onClick={() => aoSalvar(normalizarPreferenciasHall(rascunho))}>Aplicar ajustes</Botao></>}>
    <fieldset className={css.configGrupo}><legend>Modelo do painel</legend><p>Escolha a ordem das áreas e os indicadores. Seus favoritos são mantidos.</p>
      <div className={css.modelos}>{FOCOS_DO_HALL.map((foco) => <label key={foco.id} className={css.modelo}>
        <input type="radio" name="foco-hall" value={foco.id} checked={rascunho.foco === foco.id} onChange={() => setRascunho(aplicarFocoDoHall(rascunho, foco.id))} onClick={() => setRascunho(aplicarFocoDoHall(rascunho, foco.id))} />
        <span>{foco.nome}</span>
      </label>)}</div>
      <p className={css.previaModelo} role="status">{paineis.length ? `No painel: ${paineis.map((id) => modulos.find((modulo) => modulo.id === id).nome).join(' → ')}.` : 'Nenhum indicador selecionado.'}</p>
    </fieldset>
    <fieldset className={css.configGrupo}><legend>Indicadores no painel</legend>
      {modulos.filter((modulo) => ['atendimento', 'cozinha', 'entregas', 'financeiro'].includes(modulo.id)).map((modulo) => <label key={modulo.id} className={css.configOpcao}>
        <input type="checkbox" checked={rascunho.paineis.includes(modulo.id)} onChange={(evento) => mudar('paineis', evento.target.checked ? [...rascunho.paineis, modulo.id] : rascunho.paineis.filter((id) => id !== modulo.id))} />
        <IconeDoModulo modulo={modulo} tamanho={24} /><span>{modulo.nome}</span>
      </label>)}
    </fieldset>
    <fieldset className={css.configGrupo}><legend>Exibição</legend>
      <label className={css.configOpcao}><input type="checkbox" checked={rascunho.movimentos} onChange={(evento) => mudar('movimentos', evento.target.checked)} /><span>Animações suaves</span></label>
      <p>A preferência de reduzir movimentos do seu aparelho tem prioridade.</p>
    </fieldset>
  </Modal>
}
