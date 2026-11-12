import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { diaMesHora, idadeDoAviso } from '../../dominio/formato'
import css from './notificacoes.module.css'

const gravidade = { Critica: 'Crítica', Alta: 'Alta', Media: 'Média', Informativa: 'Informativa' }
const tom = { Critica: 'perigo', Alta: 'aviso', Media: 'aviso', Informativa: 'info' }
const textoAparelho = {
  desligado: 'Receba avisos desta conta mesmo com o sistema fechado.',
  ativando: 'Aguarde a confirmação do navegador…',
  desativando: 'Desativando avisos neste aparelho…',
  ativo: 'Avisos desta conta ativados neste aparelho.',
  bloqueado: 'Avisos bloqueados neste navegador. Libere as notificações deste site nas configurações do navegador e ative de novo.',
  'sem-chave': 'Os avisos no aparelho ainda não estão configurados neste ambiente.',
  indisponivel: 'Este navegador não oferece avisos com o sistema fechado.',
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
  // #1989: quem ainda não ativou neste aparelho vê um ponto discreto no sino; o caminho fica dentro.
  const semAvisoNoAparelho = ['desligado', 'bloqueado'].includes(aparelho?.estado)
  // #2258 (auditoria A8, H11, A12): o caminho para ligar os avisos do aparelho vem ANTES da lista quando
  // eles estão desligados, e os dois botões ganham ícone e cara de botão.
  const secaoDoAparelho = aparelho && (
      <section className={css.aparelho} aria-label="Avisos neste aparelho">
        <div>
          <h3>Avisos neste aparelho</h3>
          <p role={aparelho.estado === 'erro' ? 'alert' : 'status'}>{aparelho.mensagem ?? textoAparelho[aparelho.estado]}</p>
        </div>
        {['desligado', 'bloqueado', 'erro', 'sem-chave', 'ativando'].includes(aparelho.estado) && <Botao icone="bell" onClick={aparelho.ativar} disabled={aparelho.estado === 'ativando'}>Ativar avisos neste aparelho</Botao>}
        {['ativo', 'desativando'].includes(aparelho.estado) && <Botao onClick={aparelho.desligar} disabled={aparelho.estado === 'desativando'}>Desativar avisos neste aparelho</Botao>}
      </section>
  )
  return <>
    <Botao variante="texto" icone="bell" aria-label={`Avisos${erro ? ', sem atualizar' : total === null ? '' : `, ${total} não lidas`}${semAvisoNoAparelho ? ', avisos desligados neste aparelho' : ''}`}
      title={semAvisoNoAparelho ? 'Avisos desligados neste aparelho: abra e toque em Ativar' : undefined}
      aria-haspopup="dialog" onClick={() => { setAberto(true); carregar() }}>
      {/* #2232 (H3, L2): o número sai do texto e vai para o contador único do app. */}
      <span>Avisos{erro ? ' !' : ''}</span>
      {!erro && total > 0 && <Pilula contagem={total > 99 ? '99+' : total} tom="perigo" aria-hidden="true" data-contador-sino />}
      {semAvisoNoAparelho && <span className={css.pontoAparelho} data-aparelho-desligado aria-hidden="true" title="Avisos desligados neste aparelho" />}
    </Botao>
    {aberto && <Modal titulo="Avisos" largura="md" descricao="Avisos para você e para a equipe. A leitura de um aviso geral vale para toda a equipe."
      aoFechar={() => setAberto(false)} rodape={<><Botao ref={atualizarRef} onClick={() => carregar()} disabled={carregando || Boolean(lendo)}>Atualizar avisos</Botao><Botao onClick={() => setAberto(false)}>Fechar</Botao></>}>
      {erro && <p role="alert" className={css.erro}>Sem atualizar: {erro}</p>}
      {carregando && <p role="status" className={css.resumo}>Atualizando avisos…</p>}
      {!erro && !carregando && total === 0 && <Vazio titulo="Nenhum aviso pendente" icone="check" role="status">Os novos avisos aparecem aqui.</Vazio>}
      {total > 0 && <div className={css.resumo}>
        <strong>{total} {total === 1 ? 'aviso não lido' : 'avisos não lidos'}</strong>
        <span>Mostrando {recentes.length} de {total}. Os próximos aparecem conforme você marca como lidos.</span>
      </div>}
      {semAvisoNoAparelho && secaoDoAparelho}
      <ul className={css.lista} aria-label="Avisos não lidos" aria-busy={carregando || Boolean(lendo)}>
        {recentes.map((aviso) => { const idade = idadeDoAviso(aviso.createdAt, Date.now()); return <li key={aviso.id} className={css.aviso}>
          <div className={css.texto}>
            <div className={css.cabecaAviso}>
              <Pilula tom={tom[aviso.severidade] ?? 'neutro'} fina>{gravidade[aviso.severidade] ?? aviso.severidade}</Pilula>
              {/* #2303: o aviso diz quando nasceu; o de 14 dias não se passa por novo. */}
              {idade && <time className={css.quando} dateTime={aviso.createdAt} title={diaMesHora(aviso.createdAt)}>{idade}</time>}
            </div>
            <h3>{aviso.titulo || 'Aviso'}</h3>
            <p>{aviso.mensagem}</p>
          </div>
          <Botao icone="check" onClick={(evento) => {
            leituraRef.current = evento.currentTarget
            marcarLida(aviso.id)
          }} disabled={carregando || Boolean(lendo)} aria-label={`Marcar como lido: ${aviso.titulo || 'Aviso'}`}>
            {lendo === aviso.id ? 'Salvando…' : 'Marcar como lido'}
          </Botao>
        </li> })}
      </ul>
      {!semAvisoNoAparelho && secaoDoAparelho}
    </Modal>}
  </>
}
