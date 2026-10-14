import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import css from './notificacoes.module.css'

const gravidade = { Critica: 'Crítica', Alta: 'Alta', Media: 'Média', Informativa: 'Informativa' }
const tom = { Critica: 'perigo', Alta: 'aviso', Media: 'aviso', Informativa: 'info' }
const textoAparelho = {
  desligado: 'Receba avisos desta conta mesmo com o Console fechado.',
  ativando: 'Aguarde a confirmação do navegador…',
  desativando: 'Desativando avisos neste aparelho…',
  ativo: 'Avisos desta conta ativados neste aparelho.',
  bloqueado: 'Avisos bloqueados nas configurações deste navegador.',
  'sem-chave': 'Os avisos no aparelho ainda não estão configurados neste ambiente.',
  indisponivel: 'Este navegador não oferece avisos com o Console fechado.',
}

export function SinoDaSessao({ avisos, aparelho }) {
  const [aberto, setAberto] = useState(false)
  const { total, recentes, erro, carregando, lendo, carregar, marcarLida } = avisos
  const atualizarRef = useRef(null)
  const leituraRef = useRef(null)
  useEffect(() => {
    if (!lendo && leituraRef.current && !leituraRef.current.isConnected) {
      atualizarRef.current?.focus()
      leituraRef.current = null
    }
  }, [lendo, recentes])
  return <>
    <Botao variante="texto" icone="bell" aria-label={`Notificações${erro ? ', sem atualizar' : total === null ? '' : `, ${total} não lidas`}`}
      aria-haspopup="dialog" onClick={() => { setAberto(true); carregar() }}>
      <span>Avisos{erro ? ' !' : total ? ` (${total})` : ''}</span>
    </Botao>
    {aberto && <Modal titulo="Notificações" largura="min(760px, calc(100vw - 24px))" descricao="Avisos para você e para a equipe. A leitura de um aviso geral vale para toda a equipe."
      aoFechar={() => setAberto(false)} rodape={<><Botao ref={atualizarRef} onClick={() => carregar()} disabled={carregando || Boolean(lendo)}>Atualizar avisos</Botao><Botao onClick={() => setAberto(false)}>Fechar</Botao></>}>
      {erro && <p role="alert" className={css.erro}>Sem atualizar: {erro}</p>}
      {carregando && <p role="status" className={css.resumo}>Atualizando avisos…</p>}
      {!erro && !carregando && total === 0 && <Vazio titulo="Nenhum aviso pendente" icone="check" role="status">Os novos avisos aparecem aqui.</Vazio>}
      {total > 0 && <div className={css.resumo}>
        <strong>{total} {total === 1 ? 'aviso não lido' : 'avisos não lidos'}</strong>
        <span>Mostrando {recentes.length} de {total}. Os próximos aparecem conforme você marca como lidos.</span>
      </div>}
      <ul className={css.lista} aria-label="Avisos não lidos" aria-busy={carregando || Boolean(lendo)}>
        {recentes.map((aviso) => <li key={aviso.id} className={css.aviso}>
          <div className={css.texto}>
            <Pilula tom={tom[aviso.severidade] ?? 'neutro'} fina>{gravidade[aviso.severidade] ?? aviso.severidade}</Pilula>
            <h3>{aviso.titulo || 'Aviso'}</h3>
            <p>{aviso.mensagem}</p>
          </div>
          <Botao variante="texto" onClick={(evento) => {
            leituraRef.current = evento.currentTarget
            marcarLida(aviso.id)
          }} disabled={carregando || Boolean(lendo)} aria-label={`Marcar como lido: ${aviso.titulo || 'Aviso'}`}>
            {lendo === aviso.id ? 'Salvando…' : 'Marcar como lido'}
          </Botao>
        </li>)}
      </ul>
      {aparelho && <section className={css.aparelho} aria-label="Avisos neste aparelho">
        <div>
          <h3>Avisos no celular ou computador</h3>
          <p role={aparelho.estado === 'erro' ? 'alert' : 'status'}>{aparelho.mensagem ?? textoAparelho[aparelho.estado]}</p>
        </div>
        {['desligado', 'erro', 'sem-chave', 'ativando'].includes(aparelho.estado) && <Botao onClick={aparelho.ativar} disabled={aparelho.estado === 'ativando'}>Ativar avisos neste aparelho</Botao>}
        {['ativo', 'desativando'].includes(aparelho.estado) && <Botao onClick={aparelho.desligar} disabled={aparelho.estado === 'desativando'}>Desativar avisos neste aparelho</Botao>}
      </section>}
    </Modal>}
  </>
}
