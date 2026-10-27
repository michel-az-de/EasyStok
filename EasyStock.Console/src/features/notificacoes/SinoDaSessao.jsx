import { useEffect, useRef, useState } from 'react'
import { TEXTO_DO_APARELHO, podeAtivarNoAparelho, podeDesligarNoAparelho } from '../../aplicacao/avisosNoAparelho'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import css from './notificacoes.module.css'

const gravidade = { Critica: 'Crítica', Alta: 'Alta', Media: 'Média', Informativa: 'Informativa' }
const tom = { Critica: 'perigo', Alta: 'aviso', Media: 'aviso', Informativa: 'info' }

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
  // #1989: quem ainda não ativou neste aparelho vê um ponto discreto no sino; o caminho fica dentro.
  const semAvisoNoAparelho = ['desligado', 'bloqueado'].includes(aparelho?.estado)
  return <>
    <Botao variante="texto" icone="bell" aria-label={`Avisos${erro ? ', sem atualizar' : total === null ? '' : `, ${total} não lidas`}${semAvisoNoAparelho ? ', avisos neste aparelho desligados' : ''}`}
      title={semAvisoNoAparelho ? 'Avisos neste aparelho desligados: abra para ativar' : undefined}
      aria-haspopup="dialog" onClick={() => { setAberto(true); carregar() }}>
      <span>Avisos{erro ? ' !' : total ? ` (${total})` : ''}</span>
      {semAvisoNoAparelho && <span className={css.pontoAparelho} data-aparelho-desligado aria-hidden="true" />}
    </Botao>
    {aberto && <Modal titulo="Avisos" largura="min(760px, calc(100vw - 24px))" descricao="Avisos para você e para a equipe. A leitura de um aviso geral vale para toda a equipe."
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
          <p role={aparelho.estado === 'erro' ? 'alert' : 'status'}>{aparelho.mensagem ?? TEXTO_DO_APARELHO[aparelho.estado]}</p>
        </div>
        {podeAtivarNoAparelho(aparelho.estado) && <Botao onClick={aparelho.ativar} disabled={aparelho.estado === 'ativando'}>Ativar avisos neste aparelho</Botao>}
        {podeDesligarNoAparelho(aparelho.estado) && <Botao onClick={aparelho.desligar} disabled={aparelho.estado === 'desativando'}>Desativar avisos neste aparelho</Botao>}
      </section>}
    </Modal>}
  </>
}
