import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { ERRO_IDENTIDADE_DA_EDICAO, identidadeDaEdicao, mesmaIdentidadeDaEdicao } from '../../dominio/identidadeDaEdicao'
import css from './cliente.module.css'

export function ConferirDestinoDaEdicao({ identidade, conversa, aoConferir, idMensagem }) {
  const [revisando, revisar] = useState(null)
  if (!conversa || mesmaIdentidadeDaEdicao(identidade, conversa)) return null
  return <div>
    <p id={idMensagem} className={css.erroEmLinha} role="alert">{ERRO_IDENTIDADE_DA_EDICAO}</p>
    <Botao onMouseDown={e => e.preventDefault()} onClick={() => revisar(identidadeDaEdicao(conversa))}>Conferir destino da edição</Botao>
    {revisando && <Modal titulo="Conferir destino da edição" aoFechar={() => revisar(null)} rodape={<>
      <Botao onClick={() => revisar(null)}>Cancelar</Botao>
      <Botao variante="primario" disabled={!mesmaIdentidadeDaEdicao(revisando, conversa)} onClick={() => { aoConferir(revisando); revisar(null) }}>Manter texto para esta pessoa</Botao>
    </>}>
      <p>{revisando.clienteId ? 'O cadastro que receberá a edição é' : 'O novo cadastro será criado para a conversa de'} <strong>{revisando.nome}</strong>.</p>
      <p>Confira a pessoa e revise os dados digitados antes de salvar. Esta confirmação mantém o texto e não salva o cadastro.</p>
      {!mesmaIdentidadeDaEdicao(revisando, conversa) && <p role="alert">O vínculo mudou novamente. Feche e confira o destino atual.</p>}
    </Modal>}
  </div>
}
