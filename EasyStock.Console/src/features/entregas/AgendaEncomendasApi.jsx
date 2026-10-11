import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Pilula } from '../../componentes/Pilula'
import { rotuloDaData } from '../../dominio/roteiroDoDia'
import { useAgendaEncomendasApi } from '../../aplicacao/useAgendaEncomendasApi'
import css from './entregasApi.module.css'

const hora = (valor) => String(valor).slice(0, 5)

export function AgendaEncomendasApi() {
  const { hoje, inicio, fim, agenda, erro, voltar, avancar, mudarInicio, atualizar } = useAgendaEncomendasApi()

  return (
    <div className={css.conteudo}>
      <section className={css.secao} aria-label="Período da agenda">
        <div className={css.linha}>
          <Botao variante="texto" disabled={inicio <= hoje} onClick={voltar}>Semana anterior</Botao>
          <CampoTexto rotulo="Primeiro dia" tipo="date" value={inicio} min={hoje} onChange={(e) => { if (e.target.value) mudarInicio(e.target.value) }} />
          <Botao variante="texto" onClick={avancar}>Próximos 7 dias</Botao>
          <Botao variante="secundario" onClick={atualizar}>Atualizar agenda</Botao>
        </div>
        <p className={css.apoio}>De {rotuloDaData(inicio)} a {rotuloDaData(fim)}. Vagas ocupadas vêm das reservas ativas.</p>
      </section>

      {erro && <p role="alert" className={css.faixa}>{erro}</p>}
      {!agenda && !erro && <p className={css.vazio}>Carregando a agenda…</p>}
      {agenda && !agenda.lojaDisponivel && <p className={css.vazio}>Sem vitrine de entrega ativa.</p>}
      {agenda?.dias.map((dia) => {
        const ocupadas = dia.janelas.reduce((total, janela) => total + janela.ocupadas, 0)
        return (
          <section key={dia.data} className={css.secao} aria-label={rotuloDaData(dia.data)}>
            <h2>{rotuloDaData(dia.data)}</h2>
            <p className={css.apoio}>{ocupadas} {ocupadas === 1 ? 'vaga ocupada' : 'vagas ocupadas'}</p>
            {dia.bloqueio && <p><Pilula tom="aviso">Dia fechado</Pilula> {dia.bloqueio}</p>}
            {dia.janelas.length === 0 && <p className={css.vazio}>Sem janela cadastrada neste dia.</p>}
            {dia.janelas.map((janela) => (
              <div key={janela.janelaId} className={css.janelaDoDia}>
                <div className={css.linha}>
                  <h3 className={css.cresce}>{hora(janela.inicio)}–{hora(janela.fim)} · {janela.label}</h3>
                  <span className={css.numero}>{janela.ocupadas} de {janela.capacidade} vagas</span>
                </div>
                {!janela.ativa && <p><Pilula tom="aviso">Janela inativa</Pilula></p>}
                {janela.bloqueio && !dia.bloqueio && <p><Pilula tom="aviso">Bloqueada</Pilula> {janela.bloqueio}</p>}
                {janela.pedidos.length === 0 && <p className={css.vazio}>Nenhum pedido nesta janela.</p>}
                {janela.pedidos.length > 0 && <ul className={css.lista}>
                  {janela.pedidos.map((pedido) => <li key={pedido.pedidoId} className={css.cartao}>
                    <strong>{pedido.numeroCurto}</strong> · {pedido.clienteNome || 'Cliente sem nome'} · {pedido.status.replaceAll('_', ' ')}
                  </li>)}
                </ul>}
              </div>
            ))}
          </section>
        )
      })}
    </div>
  )
}
