import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Pilula } from '../../componentes/Pilula'
import { rotuloDaData } from '../../dominio/roteiroDoDia'
import { useAgendaEncomendasApi } from '../../aplicacao/useAgendaEncomendasApi'
import css from './entregasApi.module.css'

const hora = (valor) => String(valor).slice(0, 5)
const diaDaSemana = (data) => new Date(`${data}T12:00:00Z`).toLocaleDateString('pt-BR', { weekday: 'short', timeZone: 'UTC' })
const diaDoMes = (data) => new Date(`${data}T12:00:00Z`).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'UTC' })

export function AgendaEncomendasApi() {
  const { hoje, inicio, fim, agenda, erro, voltar, avancar, mudarInicio, atualizar } = useAgendaEncomendasApi()

  return (
    <div className={`${css.conteudo} ${css.agenda}`}>
      <section className={css.periodoAgenda} aria-label="Período da agenda">
        <div className={css.controlesAgenda}>
          <Botao variante="texto" className={css.passoSemana} icone="chevron-right" aria-label="Semana anterior" title="Semana anterior" disabled={inicio <= hoje} onClick={voltar} />
          <CampoTexto rotulo="Primeiro dia" rotuloOculto tipo="date" value={inicio} min={hoje} onChange={(e) => { if (e.target.value) mudarInicio(e.target.value) }} />
          <Botao variante="texto" className={css.passoSemana} icone="chevron-right" aria-label="Próximos 7 dias" title="Próximos 7 dias" onClick={avancar} />
        </div>
        <p className={css.intervaloAgenda}>De <time dateTime={inicio}>{diaDoMes(inicio)}</time> a <time dateTime={fim}>{diaDoMes(fim)}/{fim.slice(0, 4)}</time></p>
        <Botao variante="texto" onClick={atualizar}>Atualizar agenda</Botao>
      </section>
      <p className={`${css.apoio} ${css.notaAgenda}`}>Vagas ocupadas vêm das reservas ativas.</p>

      {erro && <p role="alert" className={css.faixa}>{erro}</p>}
      {!agenda && !erro && <p className={css.vazio}>Carregando a agenda…</p>}
      {agenda && !agenda.lojaDisponivel && <p className={css.vazio}>Sem vitrine de entrega ativa.</p>}
      <div className={css.semanaAgenda}>
      {agenda?.dias.map((dia) => {
        const ocupadas = dia.janelas.reduce((total, janela) => total + janela.ocupadas, 0)
        return (
          <section key={dia.data} className={css.diaAgenda} aria-label={rotuloDaData(dia.data)} aria-current={dia.data === hoje ? 'date' : undefined}>
            <header className={css.cabecalhoAgenda}>
              <h2><span>{diaDaSemana(dia.data)}</span><time dateTime={dia.data}>{diaDoMes(dia.data)}</time></h2>
              <p className={css.apoio}>{ocupadas} {ocupadas === 1 ? 'vaga ocupada' : 'vagas ocupadas'}</p>
            </header>
            <div className={css.janelasAgenda}>
            {dia.bloqueio && <p className={css.estadoAgenda}><Pilula tom="aviso">Dia fechado</Pilula><span>{dia.bloqueio}</span></p>}
            {dia.janelas.length === 0 && <p className={css.vazioAgenda}>Sem janela cadastrada.</p>}
            {dia.janelas.map((janela) => (
              <div key={janela.janelaId} className={css.janelaAgenda}>
                <div className={css.tituloJanelaAgenda}>
                  <h3>{hora(janela.inicio)}–{hora(janela.fim)}</h3>
                  <span className={css.rotuloJanelaAgenda}>{janela.label}</span>
                  <span className={css.ocupacaoAgenda}>{janela.ocupadas} de {janela.capacidade} vagas</span>
                </div>
                {!janela.ativa && <p className={css.estadoAgenda}><Pilula tom="aviso">Janela inativa</Pilula></p>}
                {janela.bloqueio && !dia.bloqueio && <p className={css.estadoAgenda}><Pilula tom="aviso">Bloqueada</Pilula><span>{janela.bloqueio}</span></p>}
                {janela.pedidos.length === 0 && <p className={css.vazioAgenda}>Sem reservas nesta janela.</p>}
                {janela.pedidos.length > 0 && <ul className={css.pedidosAgenda}>
                  {janela.pedidos.map((pedido) => <li key={pedido.pedidoId}>
                    <strong>{pedido.numeroCurto}</strong>
                    <span>{pedido.clienteNome || 'Cliente sem nome'}</span>
                    <span className={css.apoio}>{pedido.status.replaceAll('_', ' ')}</span>
                  </li>)}
                </ul>}
              </div>
            ))}
            </div>
          </section>
        )
      })}
      </div>
    </div>
  )
}
