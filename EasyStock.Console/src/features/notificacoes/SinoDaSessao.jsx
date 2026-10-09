import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import css from './notificacoes.module.css'

const gravidade = { Critica: 'Crítica', Alta: 'Alta', Media: 'Média', Informativa: 'Informativa' }

export function SinoDaSessao({ avisos }) {
  const [aberto, setAberto] = useState(false)
  const { total, recentes, erro, carregando, lendo, carregar, marcarLida } = avisos
  return <>
    <Botao variante="texto" icone="bell" aria-label={`Notificações${erro ? ', sem atualizar' : total === null ? '' : `, ${total} não lidas`}`}
      aria-haspopup="dialog" onClick={() => { setAberto(true); carregar() }}>
      <span>Avisos{erro ? ' !' : total ? ` (${total})` : ''}</span>
    </Botao>
    {aberto && <Modal titulo="Notificações" descricao="Avisos para você e para a equipe. A leitura de um aviso geral vale para toda a equipe."
      aoFechar={() => setAberto(false)} rodape={<><Botao onClick={carregar} disabled={carregando || Boolean(lendo)}>Atualizar avisos</Botao><Botao onClick={() => setAberto(false)}>Fechar</Botao></>}>
      {erro && <p role="alert" className={css.erro}>Sem atualizar: {erro}</p>}
      {carregando && total === null && <p role="status">Carregando avisos…</p>}
      {!erro && total === 0 && <p role="status">Nenhum aviso não lido.</p>}
      {total > 0 && <p className={css.resumo}>Mostrando {recentes.length} de {total} aviso(s) não lido(s). Os próximos aparecem conforme você marca como lidos.</p>}
      <ul className={css.lista}>
        {recentes.map((aviso) => <li key={aviso.id} className={css.aviso}>
          <div className={css.cabeca}><strong>{aviso.titulo || 'Aviso'}</strong><span>{gravidade[aviso.severidade] ?? aviso.severidade}</span></div>
          <p>{aviso.mensagem}</p>
          <Botao onClick={() => marcarLida(aviso.id)} disabled={Boolean(lendo)} aria-label={`Marcar como lido: ${aviso.titulo || 'Aviso'}`}>
            {lendo === aviso.id ? 'Salvando…' : 'Marcar como lido'}
          </Botao>
        </li>)}
      </ul>
    </Modal>}
  </>
}
