// Aba Caixa no modo API (#1443): o caixa de verdade do EasyStok (`api/caixa`). O resumo e o saldo
// esperado vêm da API (a mesma conta do card do painel); a tela só mostra, lança e relê. A versão
// de demonstração (`AbaCaixa.jsx`) continua com a massa local.
import { useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { Chip } from '../../../componentes/Chip'
import { Pilula } from '../../../componentes/Pilula'
import { Modal } from '../../../componentes/Modal'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { useCaixaDoDiaApi } from '../../../aplicacao/useCaixaDoDiaApi'
import { useConfirmacaoDaAbertura } from '../../../aplicacao/useConfirmacaoDaAbertura'
import { useFluxoDeCaixaApi } from '../../../aplicacao/useFluxoDeCaixaApi'
import { relerParaFechar } from '../../../aplicacao/fechamentoDoCaixa'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import {
  diaMes, dicaDoSaldoInicial, gavetaDoFechamento, observacaoDaAbertura, retificacaoDoSaldo, situacaoDoCaixaParaAbrirLoja,
} from '../../../dominio/aberturaDaLoja'
import {
  CATEGORIAS_ENTRADA_SUGERIDAS, CATEGORIAS_SAIDA_SUGERIDAS, METODOS_CAIXA, ROTULO_FORA_DA_GAVETA, ROTULO_FORA_DO_DINHEIRO, TIPOS_MOVIMENTO, categoriaAoTrocarTipo, gavetaDoDia, lancamentosEmOrdem, linhasDoFluxo,
  nomeDoMetodoCaixa, temMovimentoNoDia, totaisDoFluxo,
} from '../../../dominio/caixa'
import {
  horaCurta, lerMoeda, moeda, moedaAltaDemais, plural,
} from '../../../dominio/formato'
import { PromptEstorno } from './PromptEstorno'
import { ModalFecharCaixa } from './ModalFecharCaixa'
import css from './abaCaixa.module.css'

const OPCOES_METODO = METODOS_CAIXA.map((id) => ({ valor: id, rotulo: nomeDoMetodoCaixa(id) }))

// Uma ação por vez, com o erro da API ao lado do botão; depois de gravar, relê o dia.
// #1627: `origem` diz quem pediu a ação ('abertura', 'estorno'). Sem ela, estornar um lançamento no
// dia sem abertura fazia o botão dizer "Abrindo o caixa…" e punha o erro no bloco de abrir.
// #2078 (B12): `semResposta` é a rede que caiu (status 0 do cliente da API): ninguém sabe se gravou.
function useAcaoComRecarga(recarregar) {
  const [situacao, setSituacao] = useState({ enviando: false, erro: null, origem: null, semResposta: false })
  const emCurso = useRef(false)
  const executar = async (fn, origem = null) => {
    if (emCurso.current) return false
    emCurso.current = true
    setSituacao({ enviando: true, erro: null, origem, semResposta: false })
    try {
      await fn()
      await recarregar()
      setSituacao({ enviando: false, erro: null, origem: null, semResposta: false })
      return true
    } catch (erro) {
      setSituacao({ enviando: false, erro: erro.message, origem, semResposta: erro?.status === 0 })
      return false
    } finally {
      emCurso.current = false
    }
  }
  return [situacao, executar, () => setSituacao((atual) => ({ ...atual, erro: null, semResposta: false }))]
}

// J2 (#1990): dinheiro do caixa se digita em reais ("100" é R$ 100,00); campo vazio vale zero.
function CampoDinheiro({ rotulo, dica, centavos, aoMudar, disabled = false, ...resto }) {
  return (
    <CampoReais
      {...resto}
      disabled={disabled}
      rotulo={rotulo}
      dica={dica}
      centavos={centavos}
      ler={lerMoeda}
      erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null}
      aoMudar={(valor) => aoMudar(valor ?? 0)}
    />
  )
}

function AbrirCaixa({ dia, ultimo, executar, enviando, abrindo }) {
  const { agora } = useAtendimento()
  const { abrirCaixa } = useAcoes()
  const situacao = situacaoDoCaixaParaAbrirLoja(dia, ultimo)
  const [centavos, setCentavos] = useState(Math.round((situacao.saldoSugerido ?? 0) * 100))
  const [motivo, setMotivo] = useState('')
  const saldoInicial = centavos / 100
  const retificacao = retificacaoDoSaldo(saldoInicial, situacao)
  // J2 (#1990): valor muito longe da sugestão pede um segundo toque; mudar o valor desfaz o primeiro.
  const confirmacao = useConfirmacaoDaAbertura(saldoInicial, situacao)
  const abrir = () => {
    if (confirmacao.segurar()) return
    executar(() => abrirCaixa(agora, saldoInicial, observacaoDaAbertura({ saldoInformado: saldoInicial, situacao, motivo })), 'abertura')
  }

  return (
    <div className={css.painel}>
      <p className={css.corpoBloco}>
        O caixa de hoje ainda não foi aberto.
        {ultimo ? ` O fechamento de ${diaMes(ultimo.data)} deixou ${moeda(gavetaDoFechamento(ultimo))} na gaveta.` : ''}
      </p>
      <CampoDinheiro
        rotulo="Saldo inicial"
        dica={dicaDoSaldoInicial(dia)}
        centavos={centavos}
        disabled={enviando}
        aoMudar={(valor) => { setCentavos(valor); confirmacao.desfazer() }}
      />
      {confirmacao.aviso && <p className={css.avisoAtencao} role="alert">{confirmacao.aviso}</p>}
      {retificacao.exigeMotivo && (
        <CampoArea
          rotulo={`Motivo da diferença de ${moeda(retificacao.diferenca)}`}
          dica="Obrigatório quando o saldo não bate com o último fechamento."
          rows={2}
          disabled={enviando}
          value={motivo}
          onChange={(e) => setMotivo(e.target.value)}
        />
      )}
      <Botao
        variante="primario"
        icone="dollar-sign"
        largo
        disabled={enviando || moedaAltaDemais(centavos) || (retificacao.exigeMotivo && motivo.trim().length < 3)}
        onClick={abrir}
      >
        {abrindo ? 'Abrindo o caixa…' : confirmacao.rotulo('Abrir o caixa')}
      </Botao>
    </div>
  )
}

// #2078 (B11/E4): o modal mora aqui, junto do que ela digitou. O foco abre no valor e, com algo
// digitado, Esc e clique fora não fecham; o X fecha.
function ModalLancamento({ executar, enviando, erro, aoFechar }) {
  const { agora } = useAtendimento()
  const { lancarMovimentoCaixa } = useAcoes()
  const [tipo, setTipo] = useState(TIPOS_MOVIMENTO.SAIDA)
  const [categoria, setCategoria] = useState('')
  const [centavos, setCentavos] = useState(0)
  const [metodo, setMetodo] = useState('dinheiro')
  const [descricao, setDescricao] = useState('')
  const saida = tipo === TIPOS_MOVIMENTO.SAIDA
  // #1584: a categoria sugerida do tipo anterior não acompanha a troca.
  const trocarTipo = (novo) => { setTipo(novo); setCategoria((atual) => categoriaAoTrocarTipo(atual, novo)) }
  // Saída pela web exige descrição (FIN-003): é o rastro da auditoria.
  const incompleto = !categoria.trim() || centavos <= 0 || moedaAltaDemais(centavos) || (saida && !descricao.trim())
  const digitado = Boolean(categoria.trim() || centavos > 0 || descricao.trim())

  return (
    <Modal titulo="Lançar entrada ou saída" aoFechar={enviando ? undefined : aoFechar} focarCampo segurarFechamento={digitado || enviando}>
      {erro}
      <form
        className={css.formLancamento}
        onSubmit={async (e) => {
          e.preventDefault()
          if (enviando || incompleto) return
          const ok = await executar(() => lancarMovimentoCaixa(agora, {
            tipoMovimento: tipo, categoria, valor: centavos / 100, meio: metodo, descricao,
          }))
          if (ok) { setCategoria(''); setCentavos(0); setDescricao(''); aoFechar() }
        }}
      >
        <div className={css.tiposLancamento} role="radiogroup" aria-label="Tipo de lançamento">
          <Chip papel="escolha" disabled={enviando} ativo={!saida} onClick={() => trocarTipo(TIPOS_MOVIMENTO.ENTRADA)}>Entrada</Chip>
          <Chip papel="escolha" disabled={enviando} ativo={saida} onClick={() => trocarTipo(TIPOS_MOVIMENTO.SAIDA)}>Saída</Chip>
        </div>
        {/* #1474: suprimento (reforço de troco) entra na gaveta; sangria e despesa saem. */}
        <div className={css.categoriasSugeridas}>
          {(saida ? CATEGORIAS_SAIDA_SUGERIDAS : CATEGORIAS_ENTRADA_SUGERIDAS).map((sugestao) => (
            <Chip key={sugestao} papel="filtro" disabled={enviando} ativo={categoria === sugestao} onClick={() => setCategoria(sugestao)}>{sugestao}</Chip>
          ))}
        </div>
        <div className={css.formGrade}>
          <CampoTexto disabled={enviando} rotulo="Categoria" value={categoria} onChange={(e) => setCategoria(e.target.value)} />
          <CampoSelecao disabled={enviando} rotulo="Forma de pagamento" opcoes={OPCOES_METODO} value={metodo} onChange={(e) => setMetodo(e.target.value)} />
        </div>
        <CampoDinheiro disabled={enviando} rotulo="Valor" centavos={centavos} aoMudar={setCentavos} data-foco-inicial="" />
        <CampoTexto
          rotulo={saida ? 'Descrição (obrigatória na saída)' : 'Descrição (opcional)'}
          value={descricao}
          disabled={enviando}
          onChange={(e) => setDescricao(e.target.value)}
        />
        <Botao tipo="submit" variante="primario" icone={saida ? 'menos' : 'mais'} disabled={enviando || incompleto}>
          {enviando ? 'Registrando…' : `Lançar ${saida ? 'saída' : 'entrada'}`}
        </Botao>
      </form>
    </Modal>
  )
}

function Lancamentos({ dia, executar, podeGerenciar, enviando }) {
  const { agora } = useAtendimento()
  const { estornarMovimentoCaixa } = useAcoes()
  const [estornando, setEstornando] = useState(null)
  // #1474: movimentos e pagamentos numa lista só, na ordem da hora.
  const lancamentos = lancamentosEmOrdem(dia)
  const podeEstornar = (m) => podeGerenciar && !dia.fechado && !m.estornadoEm && !m.devolucaoPedido && (m.tipo === TIPOS_MOVIMENTO.ENTRADA || m.tipo === TIPOS_MOVIMENTO.SAIDA)

  if (lancamentos.length === 0) return <p className={css.corpoBloco}>Nenhum lançamento neste caixa.</p>
  return (
    <>
      <h3 className={css.tituloSecao}>Lançamentos</h3>
      <ul className={css.lista}>
        {lancamentos.map((m, indice) => (m.origemLinha === 'extra'
          ? (
            <li key={`${m.origem}-${m.tipo}-${m.em}-${m.valor}-${indice}`} className={css.linha}>
              <span className={css.linhaInfo}>
                <strong>{m.tipo === 'estorno' ? 'Estorno de venda' : m.origem === 'Pedido' ? 'Pagamento de pedido' : 'Venda'}</strong>
                <span className={css.dicaItem}>{horaCurta(m.em)}{m.meio && ` · ${nomeDoMetodoCaixa(m.meio)}`}{m.tipo !== 'estorno' && m.descricao && ` · ${m.descricao}`}</span>
              </span>
              <span className={m.valor < 0 ? `${css.linhaValor} ${css.valorSaida}` : css.linhaValor}>{m.valor < 0 ? '−' : '+'} {moeda(Math.abs(m.valor))}</span>
            </li>
          )
          : estornando === m.id && podeEstornar(m)
          ? (
            <li key={m.id}>
              <PromptEstorno
                rotulo={m.categoria}
                enviando={enviando}
                aoCancelar={() => setEstornando(null)}
                aoConfirmar={async (motivo) => {
                  if (await executar(() => estornarMovimentoCaixa(agora, m.id, motivo), 'estorno')) setEstornando(null)
                }}
              />
            </li>
          )
          : (
            <li key={m.id} className={`${css.linha} ${m.estornadoEm ? css.linhaEstornada : ''}`}>
              <span className={css.linhaInfo}>
                <strong>{m.categoria}</strong>
                <span className={css.dicaItem}>
                  {horaCurta(m.em)}
                  {m.meio && ` · ${nomeDoMetodoCaixa(m.meio)}`}
                  {m.descricao && ` · ${m.descricao}`}
                </span>
                {m.estornadoEm && <span className={css.dicaItem}>Estornado{m.estornadoPorNome ? ` por ${m.estornadoPorNome}` : ''} · {m.motivoEstorno}</span>}
              </span>
              <span className={css.linhaValor}>
                <span className={m.tipo === TIPOS_MOVIMENTO.SAIDA ? css.valorSaida : undefined}>{m.tipo === TIPOS_MOVIMENTO.SAIDA ? '−' : '+'} {moeda(m.valor)}</span>
                {podeEstornar(m) && (
                  <Botao variante="texto" className={css.acaoEstorno} icone="undo-2" disabled={enviando} aria-label={`Estornar ${m.categoria}`} onClick={() => setEstornando(m.id)}>
                    Estornar
                  </Botao>
                )}
              </span>
            </li>
          )))}
      </ul>
    </>
  )
}

function UltimosFechamentos({ fechamentos }) {
  if (fechamentos.length === 0) return <p className={css.corpoBloco}>Nenhum fechamento anterior registrado.</p>
  return (
    <>
      <h3 className={css.tituloSecao}>Últimos fechamentos</h3>
      <ul className={css.lista}>
        {fechamentos.map((f) => (
          <li key={f.id} className={css.linha}>
            <span className={css.linhaInfo}>
              <strong>{diaMes(f.data)}</strong>
              <span className={css.dicaItem}>{f.fechadoPorNome ? `${f.fechadoPorNome} · ` : ''}{horaCurta(f.fechadoEm)}</span>
              {f.observacoes && <span className={css.dicaItem}>{f.observacoes}</span>}
            </span>
            {/* #1920: o valor da linha é o dinheiro que ficou na gaveta, o mesmo que a abertura sugere;
                `saldoFinal` soma Pix e cartão e desmentia o "Contado na gaveta" da observação. */}
            <span className={css.linhaValor} title={`${ROTULO_TOTAL_DO_CAIXA}: ${moeda(f.saldoFinal)}`}>{moeda(gavetaDoFechamento(f))}</span>
          </li>
        ))}
      </ul>
    </>
  )
}

// #2078 (D7/B17): um nome só para o total da API (troco, dinheiro, Pix e cartão). Eram quatro ("Total do
// caixa" no Hall, "Total esperado do dia", "Total do dia", "Total do dia até agora"), e o "do dia" se
// confundia com o resultado do dia da tabela dos 7 dias.
const ROTULO_TOTAL_DO_CAIXA = 'Total do caixa (com Pix e cartão)'

// #1474: a gaveta primeiro, porque é o número que ela confere na mão. #1624: a linha de vendas
// entra quando há venda; sem ela as linhas não somavam o total.
// #1627: sem abertura ninguém disse quanto havia na gaveta, então a tela não afirma "na gaveta" nem
// "saldo inicial": mostra o que entrou e saiu, por onde. O que veio sem forma de pagamento (venda
// do pedido entregue pelo PWA, #1630) tem linha própria em vez de engordar "Pix e cartão".
function ResumoDoDia({ dia, gaveta, semAbertura = false }) {
  const linhas = [
    !semAbertura && { forte: true, rotulo: 'Na gaveta (dinheiro)', valor: gaveta.naGaveta },
    !semAbertura && { rotulo: 'Saldo inicial', valor: dia.saldoInicial },
    dia.totalVendas !== 0 && { rotulo: 'Vendas', valor: dia.totalVendas },
    { rotulo: 'Outras entradas', valor: dia.totalEntradasExtras },
    { rotulo: 'Outras saídas', valor: dia.totalSaidasExtras },
    { rotulo: 'Pagamentos de pedidos', valor: dia.totalPagamentosPedidos },
    semAbertura && { rotulo: 'Em dinheiro', valor: gaveta.naGaveta },
    { rotulo: semAbertura ? ROTULO_FORA_DO_DINHEIRO : ROTULO_FORA_DA_GAVETA, valor: gaveta.pixECartao },
    gaveta.semForma !== 0 && { rotulo: 'Sem forma de pagamento', valor: gaveta.semForma },
  ].filter(Boolean)
  return (
    <>
      {/* Sete itens cabem numa linha só; com outro número, quatro colunas não deixam o total sozinho. */}
      <dl className={`${css.resumoLista} ${linhas.length === 6 ? '' : css.resumoEmQuatro}`}>
        {linhas.map((l) => (
          <div key={l.rotulo} className={l.forte ? css.resumoForte : undefined}><dt>{l.rotulo}</dt><dd>{moeda(l.valor)}</dd></div>
        ))}
        <div className={css.resumoTotal}><dt>{ROTULO_TOTAL_DO_CAIXA}</dt><dd>{moeda(dia.saldoEsperado)}</dd></div>
      </dl>
      <p className={css.notaGaveta}>
        {semAbertura
          ? 'Sem abrir o caixa, o sistema não sabe quanto havia na gaveta: aqui está só o que entrou e saiu hoje.'
          : 'Pix e cartão não compõem o dinheiro da gaveta.'}
        {/* #2078 (B20): com o caixa fechado não há mais o que conferir antes de fechar. */}
        {gaveta.semForma !== 0 && (dia.fechado
          ? ' O que veio sem forma de pagamento pode ter sido dinheiro.'
          : ' O que veio sem forma de pagamento pode ser dinheiro: confira antes de fechar o caixa.')}
      </p>
    </>
  )
}

const SITUACAO_DO_DIA = {
  fechado: { tom: 'neutro', rotulo: 'Fechado' },
  aberto: { tom: 'ok', rotulo: 'Aberto' },
  'sem-abertura': { tom: 'aviso', rotulo: 'Sem abertura' },
  'sem-movimento': { tom: 'neutro', rotulo: 'Sem movimento' },
}
const diaDaSemana = (data) => new Date(`${data}T12:00:00Z`).toLocaleDateString('pt-BR', { weekday: 'short', timeZone: 'UTC' })

// #1624: o fluxo de caixa que faltava no Financeiro. Hoje vem do caixa do dia (relido a cada
// lançamento); os seis dias anteriores, da mesma conta, lida dia a dia.
// #1627: cada célula leva o rótulo da coluna, para a tabela virar cartões em tela estreita.
function FluxoDaSemana({ hoje }) {
  const anteriores = useFluxoDeCaixaApi(hoje.data, 6, `${hoje.esquecidoAberto}|${hoje.abertoDesde ?? ''}`)
  const linhas = linhasDoFluxo(hoje, anteriores.dias)
  const total = totaisDoFluxo(linhas)
  const desde = linhas[0].desde
  return (
    <section className={css.grupoRegistros} aria-label="Fluxo de caixa dos últimos 7 dias">
      <h3 className={css.tituloSecao}>Últimos 7 dias</h3>
      {anteriores.estado === 'carregando' && <output className={css.corpoBloco}>Carregando os dias anteriores…</output>}
      {anteriores.estado === 'erro' && (
        <p className={css.avisoFechado} role="alert">
          Os dias anteriores não carregaram. {anteriores.erro}
          {' '}<Botao variante="texto" icone="refresh-cw" onClick={anteriores.recarregar}>Tentar de novo</Botao>
        </p>
      )}
      {desde && (
        <p className={css.corpoBloco}>
          O caixa está aberto desde {diaMes(desde)}: a primeira linha soma tudo desde a abertura. O que entrou em {diaMes(desde)} antes de abrir o caixa não aparece nesta tabela.
        </p>
      )}
      <div className={css.rolagemFluxo}>
        <table className={css.fluxo}>
          <caption>Entrou e saiu não contam suprimento e sangria, que só trocam o dinheiro de lugar.</caption>
          <thead>
            <tr><th scope="col">Dia</th><th scope="col">Entrou</th><th scope="col">Saiu</th><th scope="col">Resultado do dia</th><th scope="col">Caixa</th></tr>
          </thead>
          <tbody>
            {linhas.map((l, indice) => (
              <tr key={l.data}>
                <th scope="row">{l.desde ? `Desde ${diaMes(l.desde)}` : indice === 0 ? `Hoje, ${diaMes(l.data)}` : `${diaDaSemana(l.data)}, ${diaMes(l.data)}`}</th>
                <td data-rotulo="Entrou">{moeda(l.entrou)}</td>
                <td data-rotulo="Saiu">{moeda(l.saiu)}</td>
                <td data-rotulo="Resultado do dia" className={l.saldo < 0 ? css.valorSaida : undefined}>{moeda(l.saldo)}</td>
                <td data-rotulo="Caixa">
                  <Pilula tom={SITUACAO_DO_DIA[l.situacao].tom} fina>{SITUACAO_DO_DIA[l.situacao].rotulo}</Pilula>
                  {/* O dia fechado vem com os números do fechamento; o que entrou depois vem à parte (#1630). */}
                  {l.depoisDoFechamento !== 0 && (
                    <span className={css.dicaItem}>{moeda(Math.abs(l.depoisDoFechamento))} {l.depoisDoFechamento > 0 ? 'depois de fechar' : 'a menos depois de fechar'}</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
          {anteriores.estado === 'ok' && (
            <tfoot>
              <tr>
                <th scope="row">{desde ? 'Total' : '7 dias'}</th>
                <td data-rotulo="Entrou">{moeda(total.entrou)}</td>
                <td data-rotulo="Saiu">{moeda(total.saiu)}</td>
                <td data-rotulo="Resultado" className={total.saldo < 0 ? css.valorSaida : undefined}>{moeda(total.saldo)}</td>
                <td data-rotulo="Caixa">{plural(linhas.filter((l) => l.situacao !== 'sem-movimento').length, 'dia com movimento', 'dias com movimento')}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </section>
  )
}

export function AbaCaixaApi() {
  const { acoes } = useAcessoModulos()
  const podeGerenciar = acoes.gerenciarCaixa === true
  const { agora } = useAtendimento()
  const { fecharCaixa } = useAcoes()
  const caixa = useCaixaDoDiaApi({ fechamentos: 7 })
  const [acao, executar, limparErro] = useAcaoComRecarga(caixa.recarregar)
  const [fecharAberto, setFecharAberto] = useState(false)
  const [lancamentoAberto, setLancamentoAberto] = useState(false)
  // #2005 (console-02): o fechamento só abre, e só confirma, com o dia relido.
  const [conferindo, setConferindo] = useState(false)
  const [avisoDoFechamento, setAvisoDoFechamento] = useState(null)

  if (caixa.estado === 'carregando') return <output className={css.corpoBloco}>Carregando o caixa…</output>
  if (caixa.estado === 'erro' && !caixa.dia) {
    return (
      <Vazio icone="alerta" role="alert" titulo="O caixa não carregou"
        acao={<Botao icone="refresh-cw" onClick={caixa.recarregar}>Tentar de novo</Botao>}>
        {caixa.erro}
      </Vazio>
    )
  }

  const { dia, fechamentos } = caixa
  const aviso = acao.erro && <p className={css.avisoFechado} role="alert">{acao.erro}</p>
  // O erro aparece junto de quem pediu: o do estorno, na lista; os outros, no bloco de cima.
  const erroDoEstorno = acao.origem === 'estorno' && aviso
  const erro = acao.origem !== 'estorno' && aviso
  const erroDaConsulta = caixa.estado === 'erro' && <p className={css.avisoFechado} role="alert">
    O caixa não foi atualizado. Os valores abaixo podem estar desatualizados. {caixa.erro}
    {' '}<Botao variante="texto" icone="refresh-cw" disabled={acao.enviando} onClick={caixa.recarregar}>Recarregar caixa</Botao>
  </p>
  const avisoDePermissao = !podeGerenciar && <p className={css.corpoBloco}>O fechamento e o estorno de lançamentos ficam com a dona ou gerente.</p>

  // #1474: a dona confere a gaveta; Pix e cartão não estão nela.
  const gaveta = gavetaDoDia(dia)
  // #1624: sem abertura o dia já pode ter dinheiro (Pix de pedido, venda, lançamento do PWA, que não
  // abre caixa). A tela mostrava só o campo do saldo inicial; agora o movimento aparece junto.
  const semAbertura = !dia.aberto && !dia.fechado
  const comMovimento = temMovimentoNoDia(dia)
  const titulo = dia.esquecidoAberto ? `Aberto desde ${diaMes(dia.abertoDesde)}` : 'Hoje'
  const podeLancar = dia.aberto && !dia.fechado && !dia.esquecidoAberto
  const fecharLancamento = () => { limparErro(); setLancamentoAberto(false) }
  const abrirFechamento = async () => {
    limparErro()
    setAvisoDoFechamento(null)
    setConferindo(true)
    try {
      const lido = await relerParaFechar(caixa.recarregar)
      if (lido.erro) setAvisoDoFechamento(lido.erro)
      else setFecharAberto(true)
    } finally { setConferindo(false) }
  }
  // #2078 (B12): cada tentativa começa sem o erro da anterior (o erro velho da API vencia o aviso
  // novo da releitura). Se a tentativa anterior gravou sem resposta, a releitura já vem fechada.
  const confirmarFechamento = async (contado) => {
    limparErro()
    setAvisoDoFechamento(null)
    setConferindo(true)
    try {
      const lido = await relerParaFechar(caixa.recarregar, { esperadoVisto: gaveta.naGaveta })
      if (lido.dia?.fechado) { setFecharAberto(false); return }
      if (lido.erro) { setAvisoDoFechamento(lido.erro); return }
      if (await executar(() => fecharCaixa(agora, contado, lido.gaveta.naGaveta))) setFecharAberto(false)
    } finally { setConferindo(false) }
  }

  // #1627: um retorno só. Com dois, a tabela dos 7 dias mudava de lugar na árvore ao abrir o caixa,
  // desmontava e refazia as seis leituras, piscando "Carregando".
  return (
    <div className={`${css.painel} ${css.caixaApi}`}>
      {erroDaConsulta}
      {semAbertura ? (
        <>
          <div className={`${css.painel} ${css.abertura}`}>
            <AbrirCaixa dia={dia} ultimo={fechamentos[0] ?? null} executar={executar} enviando={acao.enviando} abrindo={acao.enviando && acao.origem === 'abertura'} />
            {avisoDePermissao}
            {erro}
          </div>
          <h3 className={css.tituloSecao}>Movimento de hoje</h3>
          {comMovimento
            ? <p className={css.corpoBloco}>Já tem dinheiro no dia, mesmo sem abrir o caixa. Ao abrir, informe só o troco: o que entrou hoje já está na conta.</p>
            : <p className={css.corpoBloco}>Nada entrou nem saiu hoje ainda.</p>}
          {comMovimento && <ResumoDoDia dia={dia} gaveta={gaveta} semAbertura />}
        </>
      ) : (
        <>
          <header className={css.cabecalho}>
            <div className={css.identificacao}>
              <p className={css.tituloSecao}>{titulo}</p>
              <Pilula tom={dia.fechado ? 'neutro' : dia.esquecidoAberto ? 'aviso' : 'ok'} icone={dia.fechado ? 'lock' : undefined}>
                {dia.fechado ? 'Fechado' : 'Aberto'}
              </Pilula>
            </div>
            <div className={css.acoesTopo}>
              {podeGerenciar && dia.aberto && !dia.fechado && (
                <Botao variante="texto" icone="log-out" disabled={acao.enviando || conferindo} onClick={abrirFechamento}>
                  {dia.esquecidoAberto ? `Fechar o caixa de ${diaMes(dia.abertoDesde)}` : 'Fechar o caixa'}
                </Botao>
              )}
              {podeLancar && (
                <Botao variante="primario" icone="mais" disabled={acao.enviando} onClick={() => { limparErro(); setLancamentoAberto(true) }}>
                  Lançamento
                </Botao>
              )}
            </div>
          </header>

          {dia.esquecidoAberto && (
            <p className={css.avisoFechado}>
              Este caixa ficou aberto desde {diaMes(dia.abertoDesde)}. {podeGerenciar ? 'Feche aquele dia (conferindo a gaveta) para abrir o de hoje.' : 'Peça à dona ou gerente para fechar aquele dia antes de abrir o de hoje.'}
            </p>
          )}
          {dia.fechado && dia.fechamento && (
            <p className={css.corpoBloco}>
              Fechado às {horaCurta(dia.fechamento.fechadoEm)}{dia.fechamento.fechadoPorNome ? ` por ${dia.fechamento.fechadoPorNome}` : ''}.
              {dia.fechamento.observacoes ? ` ${dia.fechamento.observacoes}` : ''}
              {dia.posFechamento !== 0 && ` ${dia.posFechamento > 0 ? 'Entrou' : 'Saiu'} ${moeda(Math.abs(dia.posFechamento))} depois de fechar, fora do total do dia.`}
            </p>
          )}

          <ResumoDoDia dia={dia} gaveta={gaveta} />
          {!fecharAberto && avisoDoFechamento && <p className={css.avisoFechado} role="alert">{avisoDoFechamento}</p>}
          {!(podeLancar && lancamentoAberto) && erro}
          {avisoDePermissao}
        </>
      )}

      <section className={css.extrato} aria-label="Movimentação do caixa">
        {(!semAbertura || comMovimento) && (
          <div className={css.grupoRegistros}>
            {erroDoEstorno}
            <Lancamentos dia={dia} executar={executar} podeGerenciar={podeGerenciar} enviando={acao.enviando} />
          </div>
        )}
        <FluxoDaSemana hoje={dia} />
        <div className={css.grupoRegistros}>
          <UltimosFechamentos fechamentos={fechamentos} />
        </div>
      </section>

      {podeLancar && lancamentoAberto && (
        <ModalLancamento executar={executar} enviando={acao.enviando} erro={erro} aoFechar={fecharLancamento} />
      )}

      {podeGerenciar && fecharAberto && (
        <ModalFecharCaixa
          consultarDevolucoes
          resumo={dia}
          gaveta={gaveta}
          enviando={acao.enviando || conferindo}
          erro={acao.erro}
          erroSemResposta={acao.semResposta}
          aviso={avisoDoFechamento}
          aoFechar={() => setFecharAberto(false)}
          aoConfirmar={confirmarFechamento}
        />
      )}
    </div>
  )
}
