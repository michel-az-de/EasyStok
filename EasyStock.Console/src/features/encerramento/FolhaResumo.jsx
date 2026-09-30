import { Chip } from '../../componentes/Chip'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { horaCurta, moeda } from '../../dominio/formato'
import { ROTULO_CONVERTEU } from '../../dominio/resumoAtendimento'
import css from './encerramento.module.css'

// Duas gramáticas de data (achado 6): numérica só no impresso (`impresso`
// true), humana na tela, igual à Ficha ("22 de set."). Antes as duas
// convivam na mesma folha (tela em "22/09/2026", Ficha em "22 de set.").
const dataCurta = (iso) => new Date(iso).toLocaleDateString('pt-BR')
const dataHumana = (iso) => new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })

const AVALIACOES_CLIENTE = [
  { valor: 'positiva', rotulo: 'Positiva', icone: 'thumbs-up' },
  { valor: 'negativa', rotulo: 'Negativa', icone: 'thumbs-down' },
]

const AUTOAVALIACOES = [
  { valor: 'bom', rotulo: 'Bom', icone: 'smile' },
  { valor: 'regular', rotulo: 'Regular', icone: 'meh' },
  { valor: 'ruim', rotulo: 'Ruim', icone: 'frown' },
]

// Grupo de escolha única (seção 5, "Botões de avaliação"): reaproveita o Chip
// papel="escolha" que a direção já usa para meio de pagamento e canal — mesmo
// contrato de acessibilidade (radiogroup/aria-checked), só troca o rótulo do
// grupo. `editavel` falso vira texto puro: "só a opção marcada aparece"
// (seção 5, o mesmo texto que a impressão usa).
function GrupoAvaliacao({ rotulo, opcoes, valor, editavel, aoEscolher }) {
  if (!editavel) {
    const escolhida = opcoes.find((o) => o.valor === valor)
    return (
      <div className={css.grupoAvaliacao}>
        <p className={css.tituloSecao}>{rotulo}</p>
        {escolhida
          ? (
            <p className={css.avaliacaoImpressa}>
              <Icone nome={escolhida.icone} tamanho={20} /> {escolhida.rotulo}
            </p>
          )
          : <p className={css.semAvaliacao}>Sem resposta ainda.</p>}
      </div>
    )
  }
  return (
    <div className={css.grupoAvaliacao}>
      <p className={css.tituloSecao}>{rotulo}</p>
      <div className={css.opcoesAvaliacao} role="radiogroup" aria-label={rotulo}>
        {opcoes.map((o) => (
          <Chip
            key={o.valor}
            papel="escolha"
            icone={o.icone}
            ativo={valor === o.valor}
            className={css.botaoAvaliacao}
            onClick={() => aoEscolher(o.valor)}
          >
            {o.rotulo}
          </Chip>
        ))}
      </div>
      {!valor && <p className={css.semAvaliacao}>Sem resposta ainda.</p>}
    </div>
  )
}

// A folha em si (seção 5): mesma casca em tela e em impressão
// (`ModalEncerrar.jsx` monta as duas cópias, igual ao canhoto). `editavel`
// falso é a impressão OU um atendimento já encerrado (ela não edita resumo
// congelado); nos dois casos os controles viram texto puro.
export function FolhaResumo({
  nomeCliente, canalNome, telefone, resumo, editavel, impresso = false,
  aoMarcarAvaliacaoCliente, aoMarcarAutoavaliacao, aoMudarAnotacao, aoMudarGuardarNota,
}) {
  const { mensagens, receita, vendas, marcos } = resumo
  const totalMensagens = mensagens.total || 1 // evita divisão por zero na largura da barra
  return (
    <div className={css.folha}>
      <header className={css.cabecalho}>
        <div>
          <p className={css.marca}>Casa da Baba</p>
          <p className={css.submarca}>massa artesanal</p>
        </div>
        <div>
          <p className={css.tituloResumo}>Resumo de atendimento</p>
          <p className={css.numeroResumo}>
            {resumo.numero ? `Nº ${resumo.numero}` : 'Prévia'}
            {' · '}
            {impresso ? dataCurta(resumo.encerradoEm ?? resumo.ate) : dataHumana(resumo.encerradoEm ?? resumo.ate)}
          </p>
        </div>
      </header>

      <div className={css.linhaCliente}>
        <p className={css.nomeCliente}>{nomeCliente}</p>
        <p className={css.subCliente}>{[canalNome, telefone].filter(Boolean).join(' · ')}</p>
        {resumo.converteu === 'sim' && (
          <p className={css.conversao}>Lead que virou cliente neste atendimento</p>
        )}
      </div>

      <div className={css.grade4}>
        <div className={css.estatistica}>
          <p className={css.rotuloEstatistica}>Duração</p>
          <p className={css.valorEstatistica}>{resumo.duracaoTexto}</p>
          <p className={css.apoioEstatistica}>{resumo.faixaHorario}</p>
        </div>
        <div className={css.estatistica}>
          <p className={css.rotuloEstatistica}>Mensagens</p>
          <p className={css.valorEstatistica}>{mensagens.total}</p>
        </div>
        <div className={css.estatistica}>
          <p className={css.rotuloEstatistica}>Receita</p>
          <p className={css.valorEstatistica}>{moeda(receita.liquido)}</p>
          {receita.estorno > 0 && <p className={css.apoioEstatistica}>estorno {moeda(-receita.estorno)}</p>}
          {receita.faltaReceber > 0 && (
            <p className={css.apoioEstatistica}>a receber {moeda(receita.faltaReceber)}</p>
          )}
        </div>
        <div className={css.estatistica}>
          <p className={css.rotuloEstatistica}>Converteu</p>
          <p className={css.valorEstatistica + ' ' + css.valorTexto}>{ROTULO_CONVERTEU[resumo.converteu]}</p>
        </div>
      </div>

      <div className={css.secao}>
        <p className={css.tituloSecao}>Mensagens</p>
        {mensagens.total === 0
          ? <p className={css.semMensagem}>Nenhuma mensagem neste atendimento.</p>
          : (
            <div className={css.barraMensagens}>
              {mensagens.cliente > 0 && (
                <span
                  className={css.parteMensagem + ' ' + css.parteCliente}
                  style={{ '--peso': mensagens.cliente / totalMensagens }}
                >
                  Cliente {mensagens.cliente}
                </span>
              )}
              {mensagens.voce > 0 && (
                <span
                  className={css.parteMensagem + ' ' + css.parteVoce}
                  style={{ '--peso': mensagens.voce / totalMensagens }}
                >
                  Você {mensagens.voce}
                </span>
              )}
              {mensagens.automatico > 0 && (
                <span
                  className={css.parteMensagem + ' ' + css.parteAutomatico}
                  style={{ '--peso': mensagens.automatico / totalMensagens }}
                >
                  Automático {mensagens.automatico}
                </span>
              )}
            </div>
          )}
      </div>

      <div className={css.secao}>
        <p className={css.tituloSecao}>Vendas</p>
        {vendas.length === 0
          ? <p className={css.semMensagem}>Sem pedido com movimento neste atendimento.</p>
          : (
            <table className={css.tabelaVendas}>
              <thead>
                <tr><th>Pedido</th><th>Itens</th><th>Meio</th><th>Valor</th><th>Situação</th></tr>
              </thead>
              <tbody>
                {vendas.map((v) => (
                  <tr key={v.numero}>
                    <td>{v.numero}</td>
                    <td>{v.itensTexto}</td>
                    <td>{v.meio}</td>
                    <td>{moeda(v.valor)}</td>
                    <td><Pilula tom={v.situacaoTom}>{v.situacao}</Pilula></td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr><td colSpan={3} aria-hidden="true" /><td className={css.totalVendas}>Total {moeda(resumo.vendasTotal)}</td><td aria-hidden="true" /></tr>
              </tfoot>
            </table>
          )}
      </div>

      {marcos.length > 0 && (
        <div className={css.secao}>
          <p className={css.tituloSecao}>Marcos</p>
          <ul className={css.marcos}>
            {marcos.map((m, indice) => (
              // eslint-disable-next-line react/no-array-index-key -- marco não tem id próprio
              <li key={indice} className={css.marco}>
                <span className={css.horaMarco}>
                  {horaCurta(m.em)}
                </span>
                <span>{m.rotulo}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className={css.avaliacoes}>
        <GrupoAvaliacao
          rotulo="Avaliação do cliente"
          opcoes={AVALIACOES_CLIENTE}
          valor={resumo.avaliacaoCliente}
          editavel={editavel}
          aoEscolher={aoMarcarAvaliacaoCliente}
        />
        <GrupoAvaliacao
          rotulo="Como foi para você"
          opcoes={AUTOAVALIACOES}
          valor={resumo.autoavaliacao}
          editavel={editavel}
          aoEscolher={aoMarcarAutoavaliacao}
        />
      </div>

      <div className={css.secao}>
        <p className={css.tituloSecao}>Anotação</p>
        {editavel
          ? (
            <>
              <CampoArea
                rotulo="Anotação do fechamento"
                rotuloOculto
                rows={3}
                value={resumo.anotacao}
                onChange={(evento) => aoMudarAnotacao(evento.target.value)}
              />
              <label className={css.checkboxNota}>
                <input
                  type="checkbox"
                  checked={resumo.guardarNota}
                  onChange={(evento) => aoMudarGuardarNota(evento.target.checked)}
                />
                Guardar também nas notas do cliente
              </label>
            </>
          )
          : (
            <p className={css.anotacaoImpressa}>
              {resumo.anotacao.trim() || 'Sem anotação.'}
            </p>
          )}
      </div>

      <footer className={css.rodape}>
        <span>
          {resumo.encerradoEm
            ? `Encerrado por ${resumo.encerradoPor} às ${horaCurta(resumo.encerradoEm)}`
            : 'Prévia, ainda não encerrado'}
        </span>
        <span>página 1/1</span>
      </footer>
    </div>
  )
}
