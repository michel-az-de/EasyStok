import { MarcaIcone } from '../../componentes/MarcaIcone'
import { usePulsoDoHall } from '../../aplicacao/usePulsoDoHall'
import { indicadoresDoHall } from '../../dominio/pulsoDoHall'
import { hashDoModulo } from '../../dominio/modulos'
import css from './hall.module.css'

const frentes = {
  atendimento: { nome: 'Atendimento pendente', icone: 'mensagem', acao: 'Abrir Atendimento', tela: 'balcao' },
  cozinha: { nome: 'Em preparo ou na fila', icone: 'producao', acao: 'Ver fila de preparo', tela: 'fila' },
  entregas: { nome: 'Prontos para sair', icone: 'entrega', acao: 'Organizar entregas', tela: 'painel' },
  financeiro: { nome: 'Saldo no caixa', icone: 'caixa', acao: 'Conferir caixa', tela: 'caixa' },
}
const hora = (em) => new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' }).format(em)
const dinheiro = (valor) => valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

// #1810: o hall mostra só o número e o caminho. A explicação de cada número fica no título do
// cartão (passar o mouse) e o frescor só aparece quando a leitura falhou ou está velha.
export function PulsoDoHall({ ids, foco }) {
  const { fontes } = usePulsoDoHall({ ativo: ids.length > 0, atendimento: ids.includes('atendimento'), pedidos: ids.includes('cozinha') || ids.includes('entregas'), financeiro: ids.includes('financeiro') })
  const indicadores = indicadoresDoHall(fontes, ids)
  if (!ids.length) return null
  return <section className={css.pulso} aria-labelledby="pulso-titulo">
    <h2 id="pulso-titulo" className={css.oculto}>Agora na casa · {foco}</h2>
    <ul className={css.indicadores}>{indicadores.map((item) => {
      const frente = frentes[item.id]
      const valor = item.valor === null ? '—' : item.id === 'financeiro' ? dinheiro(item.valor) : `${item.valor}${item.parcial ? '+' : ''}`
      // Só o que pede ação aparece por escrito: leitura que falhou e caixa de outro dia aberto.
      const aviso = item.estado === 'erro'
        ? `${item.em ? `Desatualizado · dados das ${hora(item.em)}` : 'Não foi possível atualizar'}${item.erro ? `: ${item.erro}` : ''}`
        : item.alerta
      return <li key={item.id}>
        <a data-indicador={item.id} data-estado={item.estado} className={css.indicador} href={hashDoModulo(item.id, frente.tela)} title={item.detalhe || undefined}>
          <span className={css.cabecaIndicador}><MarcaIcone nome={frente.icone} tamanho={20} />{frente.nome}</span>
          <strong className={css.valor}>{valor}</strong>
          {aviso && <span className={css.frescor}><MarcaIcone nome="atencao" tamanho={16} />{aviso}</span>}
          <span className={css.oculto}>{frente.acao}</span>
        </a>
      </li>
    })}</ul>
  </section>
}
