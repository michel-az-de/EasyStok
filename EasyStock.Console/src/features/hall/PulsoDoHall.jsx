import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Botao } from '../../componentes/Botao'
import { usePulsoDoHall } from '../../aplicacao/usePulsoDoHall'
import { indicadoresDoHall } from '../../dominio/pulsoDoHall'
import { hashDoModulo } from '../../dominio/modulos'
import css from './hall.module.css'

const frentes = {
  atendimento: { nome: 'Atendimento pendente', icone: 'mensagem', acao: 'Abrir Atendimento', tela: 'balcao' },
  cozinha: { nome: 'Em preparo ou aguardando', icone: 'producao', acao: 'Ver fila de preparo', tela: 'fila' },
  entregas: { nome: 'Prontos para sair', icone: 'entrega', acao: 'Organizar entregas', tela: 'painel' },
  financeiro: { nome: 'Saldo esperado no caixa', icone: 'caixa', acao: 'Conferir caixa', tela: 'caixa' },
}
const hora = (em) => new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' }).format(em)
const dinheiro = (valor) => valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

export function PulsoDoHall({ ids, foco }) {
  const { fontes, atualizar } = usePulsoDoHall({ ativo: ids.length > 0, atendimento: ids.includes('atendimento'), pedidos: ids.includes('cozinha') || ids.includes('entregas'), financeiro: ids.includes('financeiro') })
  const indicadores = indicadoresDoHall(fontes, ids)
  const lendo = indicadores.some((item) => item.estado === 'carregando')
  if (!ids.length) return null
  return <section className={css.pulso} aria-labelledby="pulso-titulo">
    <div className={css.cabecaPulso}>
      <div><h2 id="pulso-titulo" className={css.subtitulo}>Agora na casa</h2><p>{foco} · atualização a cada minuto.</p></div>
      <Botao variante="texto" aria-label="Atualizar números" disabled={lendo} onClick={atualizar}><MarcaIcone nome="visivel" tamanho={20} />{lendo ? 'Conferindo…' : 'Atualizar'}</Botao>
    </div>
    <div className={css.indicadores}>{indicadores.map((item) => {
      const frente = frentes[item.id]
      return <article key={item.id} data-indicador={item.id} className={css.indicador}>
        <div className={css.cabecaIndicador}><MarcaIcone nome={frente.icone} tamanho={28} /><h3>{frente.nome}</h3></div>
        <strong className={css.valor}>{item.valor === null ? '—' : item.id === 'financeiro' ? dinheiro(item.valor) : `${item.valor}${item.parcial ? '+' : ''}`}</strong>
        <p>{item.valor === null && item.estado === 'carregando' ? 'Carregando números…' : item.detalhe}</p>
        <span className={css.frescor}>
          {item.estado === 'erro' ? <><MarcaIcone nome="atencao" tamanho={16} />{item.em ? `Desatualizado · dados das ${hora(item.em)}` : 'Não foi possível atualizar'}: {item.erro}</> : item.estado === 'carregando' ? (item.em ? `Atualizando · dados das ${hora(item.em)}` : '') : item.valor === null ? 'Valor indisponível' : `Atualizado às ${hora(item.em)}`}
          {item.parcial && ' · Resultado parcial. Confira a fila completa.'}
        </span>
        <a className={css.acaoIndicador} href={hashDoModulo(item.id, frente.tela)}>{frente.acao}<MarcaIcone nome="proximo" tamanho={18} /></a>
      </article>
    })}</div>
  </section>
}
