// Aba Caixa no modo API (#1443): o caixa de verdade do EasyStok (`api/caixa`). O resumo e o saldo
// esperado vêm da API (a mesma conta do card do painel); a tela só mostra, lança e relê. A versão
// de demonstração (`AbaCaixa.jsx`) continua com a massa local.
import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { CampoMascarado } from '../../../componentes/CampoMascarado'
import { Chip } from '../../../componentes/Chip'
import { Pilula } from '../../../componentes/Pilula'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { useCaixaDoDiaApi } from '../../../aplicacao/useCaixaDoDiaApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import {
  diaMes, observacaoDaAbertura, retificacaoDoSaldo, situacaoDoCaixaParaAbrirLoja,
} from '../../../dominio/aberturaDaLoja'
import {
  CATEGORIAS_ENTRADA_SUGERIDAS, CATEGORIAS_SAIDA_SUGERIDAS, METODOS_CAIXA, TIPOS_MOVIMENTO, gavetaDoDia, lancamentosEmOrdem, nomeDoMetodoCaixa,
} from '../../../dominio/caixa'
import {
  horaCurta, lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../../dominio/formato'
import { PromptEstorno } from './AbaCaixa'
import { ModalFecharCaixa } from './ModalFecharCaixa'
import css from './abaCaixa.module.css'

const OPCOES_METODO = METODOS_CAIXA.map((id) => ({ valor: id, rotulo: nomeDoMetodoCaixa(id) }))

// Uma ação por vez, com o erro da API ao lado do botão; depois de gravar, relê o dia.
function useAcaoComRecarga(recarregar) {
  const [situacao, setSituacao] = useState({ enviando: false, erro: null })
  const executar = async (fn) => {
    setSituacao({ enviando: true, erro: null })
    try {
      await fn()
      setSituacao({ enviando: false, erro: null })
      await recarregar()
      return true
    } catch (erro) {
      setSituacao({ enviando: false, erro: erro.message })
      return false
    }
  }
  return [situacao, executar]
}

function CampoDinheiro({ rotulo, dica, centavos, aoMudar }) {
  return (
    <CampoMascarado
      tipo="moeda"
      rotulo={rotulo}
      dica={dica}
      valor={mascaraMoeda(centavos)}
      erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null}
      aoMudarDigitos={(digitos) => aoMudar(Number(digitos || '0'))}
      aoColarTexto={(texto) => aoMudar(lerMoeda(texto))}
    />
  )
}

function AbrirCaixa({ dia, ultimo, executar, enviando }) {
  const { agora } = useAtendimento()
  const { abrirCaixa } = useAcoes()
  const situacao = situacaoDoCaixaParaAbrirLoja(dia, ultimo)
  const [centavos, setCentavos] = useState(Math.round((situacao.saldoSugerido ?? 0) * 100))
  const [motivo, setMotivo] = useState('')
  const saldoInicial = centavos / 100
  const retificacao = retificacaoDoSaldo(saldoInicial, situacao)

  return (
    <div className={css.painel}>
      <p className={css.corpoBloco}>
        O caixa de hoje ainda não foi aberto.
        {ultimo ? ` O fechamento de ${diaMes(ultimo.data)} deixou ${moeda(ultimo.saldoFinal)} na gaveta.` : ''}
      </p>
      <CampoDinheiro
        rotulo="Saldo inicial"
        dica="O dinheiro que está na gaveta ao abrir o dia."
        centavos={centavos}
        aoMudar={setCentavos}
      />
      {retificacao.exigeMotivo && (
        <CampoArea
          rotulo={`Motivo da diferença de ${moeda(retificacao.diferenca)}`}
          dica="Obrigatório quando o saldo não bate com o último fechamento."
          rows={2}
          value={motivo}
          onChange={(e) => setMotivo(e.target.value)}
        />
      )}
      <Botao
        variante="primario"
        icone="dollar-sign"
        largo
        disabled={enviando || moedaAltaDemais(centavos) || (retificacao.exigeMotivo && motivo.trim().length < 3)}
        onClick={() => executar(() => abrirCaixa(agora, saldoInicial, observacaoDaAbertura({ saldoInformado: saldoInicial, situacao, motivo })))}
      >
        Abrir o caixa
      </Botao>
    </div>
  )
}

function FormLancamento({ executar, enviando }) {
  const { agora } = useAtendimento()
  const { lancarMovimentoCaixa } = useAcoes()
  const [tipo, setTipo] = useState(TIPOS_MOVIMENTO.SAIDA)
  const [categoria, setCategoria] = useState('')
  const [centavos, setCentavos] = useState(0)
  const [metodo, setMetodo] = useState('dinheiro')
  const [descricao, setDescricao] = useState('')
  const saida = tipo === TIPOS_MOVIMENTO.SAIDA
  // Saída pela web exige descrição (FIN-003): é o rastro da auditoria.
  const incompleto = !categoria.trim() || centavos <= 0 || moedaAltaDemais(centavos) || (saida && !descricao.trim())

  return (
    <form
      className={css.formLancamento}
      onSubmit={async (e) => {
        e.preventDefault()
        const ok = await executar(() => lancarMovimentoCaixa(agora, {
          tipoMovimento: tipo, categoria, valor: centavos / 100, meio: metodo, descricao,
        }))
        if (ok) { setCategoria(''); setCentavos(0); setDescricao('') }
      }}
    >
      <h4 className={css.tituloSecao}>Lançar entrada ou saída</h4>
      <div className={css.tiposLancamento} role="radiogroup" aria-label="Tipo de lançamento">
        <Chip papel="escolha" ativo={!saida} onClick={() => setTipo(TIPOS_MOVIMENTO.ENTRADA)}>Entrada</Chip>
        <Chip papel="escolha" ativo={saida} onClick={() => setTipo(TIPOS_MOVIMENTO.SAIDA)}>Saída</Chip>
      </div>
      {/* #1474: suprimento (reforço de troco) entra na gaveta; sangria e despesa saem. */}
      <div className={css.categoriasSugeridas}>
        {(saida ? CATEGORIAS_SAIDA_SUGERIDAS : CATEGORIAS_ENTRADA_SUGERIDAS).map((sugestao) => (
          <Chip key={sugestao} papel="filtro" ativo={categoria === sugestao} onClick={() => setCategoria(sugestao)}>{sugestao}</Chip>
        ))}
      </div>
      <div className={css.formGrade}>
        <CampoTexto rotulo="Categoria" value={categoria} onChange={(e) => setCategoria(e.target.value)} />
        <CampoSelecao rotulo="Método" opcoes={OPCOES_METODO} value={metodo} onChange={(e) => setMetodo(e.target.value)} />
      </div>
      <CampoDinheiro rotulo="Valor" centavos={centavos} aoMudar={setCentavos} />
      <CampoTexto
        rotulo={saida ? 'Descrição (obrigatória na saída)' : 'Descrição (opcional)'}
        value={descricao}
        onChange={(e) => setDescricao(e.target.value)}
      />
      <Botao tipo="submit" variante="primario" icone={saida ? 'menos' : 'mais'} disabled={enviando || incompleto}>
        Lançar {saida ? 'saída' : 'entrada'}
      </Botao>
    </form>
  )
}

function Lancamentos({ dia, executar, podeGerenciar }) {
  const { agora } = useAtendimento()
  const { estornarMovimentoCaixa } = useAcoes()
  const [estornando, setEstornando] = useState(null)
  // #1474: movimentos e pagamentos numa lista só, na ordem da hora.
  const lancamentos = lancamentosEmOrdem(dia)
  const podeEstornar = (m) => podeGerenciar && !dia.fechado && !m.estornadoEm && !m.devolucaoPedido && (m.tipo === TIPOS_MOVIMENTO.ENTRADA || m.tipo === TIPOS_MOVIMENTO.SAIDA)

  if (lancamentos.length === 0) return <p className={css.corpoBloco}>Nenhum lançamento neste caixa.</p>
  return (
    <>
      <h4 className={css.tituloSecao}>Lançamentos</h4>
      <ul className={css.lista}>
        {lancamentos.map((m) => (m.origemLinha === 'extra'
          ? (
            <li key={`${m.origem}-${m.em}-${m.valor}`} className={css.linha}>
              <span className={css.linhaInfo}>
                <strong>{m.origem === 'Pedido' ? 'Pagamento de pedido' : 'Venda'}</strong>
                <span className={css.dicaItem}>{horaCurta(m.em)}{m.meio && ` · ${nomeDoMetodoCaixa(m.meio)}`}{m.descricao && ` · ${m.descricao}`}</span>
              </span>
              <span className={css.linhaValor}>+ {moeda(m.valor)}</span>
            </li>
          )
          : estornando === m.id && podeEstornar(m)
          ? (
            <li key={m.id}>
              <PromptEstorno
                rotulo={m.categoria}
                aoCancelar={() => setEstornando(null)}
                aoConfirmar={async (motivo) => {
                  if (await executar(() => estornarMovimentoCaixa(agora, m.id, motivo))) setEstornando(null)
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
                {m.tipo === TIPOS_MOVIMENTO.SAIDA ? '−' : '+'} {moeda(m.valor)}
                {podeEstornar(m) && (
                  <Botao variante="texto" className={css.acaoEstorno} icone="undo-2" aria-label={`Estornar ${m.categoria}`} onClick={() => setEstornando(m.id)}>
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
      <h4 className={css.tituloSecao}>Últimos fechamentos</h4>
      <ul className={css.lista}>
        {fechamentos.map((f) => (
          <li key={f.id} className={css.linha}>
            <span className={css.linhaInfo}>
              <strong>{diaMes(f.data)}</strong>
              <span className={css.dicaItem}>{f.fechadoPorNome ? `${f.fechadoPorNome} · ` : ''}{horaCurta(f.fechadoEm)}</span>
              {f.observacoes && <span className={css.dicaItem}>{f.observacoes}</span>}
            </span>
            <span className={css.linhaValor}>{moeda(f.saldoFinal)}</span>
          </li>
        ))}
      </ul>
    </>
  )
}

export function AbaCaixaApi() {
  const { acoes } = useAcessoModulos()
  const podeGerenciar = acoes.gerenciarCaixa === true
  const { agora } = useAtendimento()
  const { fecharCaixa } = useAcoes()
  const caixa = useCaixaDoDiaApi({ fechamentos: 7 })
  const [acao, executar] = useAcaoComRecarga(caixa.recarregar)
  const [fecharAberto, setFecharAberto] = useState(false)

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
  const erro = acao.erro && <p className={css.avisoFechado} role="alert">{acao.erro}</p>
  const avisoDePermissao = !podeGerenciar && <p className={css.corpoBloco}>O fechamento e o estorno de lançamentos ficam com a dona ou gerente.</p>

  if (!dia.aberto && !dia.fechado) {
    return (
      <div className={css.painel}>
        <AbrirCaixa dia={dia} ultimo={fechamentos[0] ?? null} executar={executar} enviando={acao.enviando} />
        {avisoDePermissao}
        {erro}
        <UltimosFechamentos fechamentos={fechamentos} />
      </div>
    )
  }

  // #1474: a dona confere a gaveta; Pix e cartão não estão nela.
  const gaveta = gavetaDoDia(dia)
  const titulo = dia.esquecidoAberto ? `Caixa de ${diaMes(dia.abertoDesde)} (ainda aberto)` : 'Caixa de hoje'
  return (
    <div className={css.painel}>
      <header className={css.cabecalho}>
        <h4 className={css.tituloSecao}>{titulo}</h4>
        <Pilula tom={dia.fechado ? 'neutro' : dia.esquecidoAberto ? 'aviso' : 'ok'} icone={dia.fechado ? 'lock' : undefined}>
          {dia.fechado ? 'Fechado' : 'Aberto'}
        </Pilula>
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
        </p>
      )}

      {/* #1474: a gaveta primeiro, porque é o número que ela confere na mão. */}
      <dl className={css.resumoLista}>
        <div className={css.resumoForte}><dt>Na gaveta (dinheiro)</dt><dd>{moeda(gaveta.naGaveta)}</dd></div>
        <div><dt>Saldo inicial</dt><dd>{moeda(dia.saldoInicial)}</dd></div>
        <div><dt>Outras entradas</dt><dd>{moeda(dia.totalEntradasExtras)}</dd></div>
        <div><dt>Outras saídas</dt><dd>{moeda(dia.totalSaidasExtras)}</dd></div>
        <div><dt>Pagamentos de pedidos</dt><dd>{moeda(dia.totalPagamentosPedidos)}</dd></div>
        <div><dt>Pix e cartão (fora da gaveta)</dt><dd>{moeda(gaveta.pixECartao)}</dd></div>
        <div><dt>{dia.fechado ? 'Total do dia' : 'Total esperado do dia'}</dt><dd>{moeda(dia.saldoEsperado)}</dd></div>
      </dl>

      {podeGerenciar && dia.aberto && !dia.fechado && (
        <div className={css.acoesTopo}>
          <Botao variante="secundario" icone="log-out" onClick={() => setFecharAberto(true)}>
            {dia.esquecidoAberto ? `Fechar o caixa de ${diaMes(dia.abertoDesde)}` : 'Fechar o caixa'}
          </Botao>
        </div>
      )}
      {erro}
      {avisoDePermissao}

      {dia.aberto && !dia.fechado && !dia.esquecidoAberto && <FormLancamento executar={executar} enviando={acao.enviando} />}

      <Lancamentos dia={dia} executar={executar} podeGerenciar={podeGerenciar} />
      <UltimosFechamentos fechamentos={fechamentos} />

      {podeGerenciar && fecharAberto && (
        <ModalFecharCaixa
          resumo={dia}
          gaveta={gaveta}
          aoFechar={() => setFecharAberto(false)}
          aoConfirmar={async (contado) => {
            setFecharAberto(false)
            await executar(() => fecharCaixa(agora, contado, gaveta.naGaveta))
          }}
        />
      )}
    </div>
  )
}
