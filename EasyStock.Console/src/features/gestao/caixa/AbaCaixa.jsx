import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { CampoMascarado } from '../../../componentes/CampoMascarado'
import { Chip } from '../../../componentes/Chip'
import { Pilula } from '../../../componentes/Pilula'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import {
  CATEGORIAS_ENTRADA_SUGERIDAS, CATEGORIAS_SAIDA_SUGERIDAS, METODOS_CAIXA, TIPOS_MOVIMENTO, caixaAberto, caixaAberturaDoDia,
  caixaFechamentoDoDia, movimentosDoDia, nomeDoMetodoCaixa, resumoCaixa, vendasDoDia,
} from '../../../dominio/caixa'
import {
  horaCurta, lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../../dominio/formato'
import { ModalFecharCaixa } from './ModalFecharCaixa'
import { ModalVendaAvulsa } from './ModalVendaAvulsa'
import css from './abaCaixa.module.css'

const OPCOES_METODO = METODOS_CAIXA.map((id) => ({ valor: id, rotulo: nomeDoMetodoCaixa(id) }))

// Prompt de estorno com motivo obrigatório, compartilhado entre lançamento e
// venda (aceite: "estorno em um toque... a partir do caixa e do pedido"; a
// versão "do pedido" já existe em `BlocoOcorrencia.jsx`/`PainelFicha.jsx`,
// reaproveitando a MESMA ação `marcarEstorno`). Mesmo padrão de campo e
// validação de `features/ficha-cliente/BlocoOcorrencia.jsx` ("Confirmar
// reembolso"), só que sem valor parcial: aqui é o toque único do caixa.
export function PromptEstorno({ rotulo, aoCancelar, aoConfirmar }) {
  const [motivo, setMotivo] = useState('')
  return (
    <div className={css.promptEstorno}>
      <p className={css.corpoBloco}>Estornar {rotulo}?</p>
      <CampoArea
        rotulo="Motivo do estorno"
        rotuloOculto
        dica="Obrigatório. Fica gravado no lançamento."
        rows={2}
        value={motivo}
        onChange={(e) => setMotivo(e.target.value)}
      />
      <p className={css.confirmarAcoes}>
        <Botao variante="texto" onClick={aoCancelar}>Manter</Botao>
        <Botao
          variante="primario"
          icone="undo-2"
          disabled={motivo.trim().length < 3}
          onClick={() => aoConfirmar(motivo.trim())}
        >
          Confirmar estorno
        </Botao>
      </p>
    </div>
  )
}

function LinhaLancamento({ movimento, aoPedirEstorno }) {
  const estornavel = movimento.tipo === TIPOS_MOVIMENTO.ENTRADA || movimento.tipo === TIPOS_MOVIMENTO.SAIDA
  const sinal = movimento.tipo === TIPOS_MOVIMENTO.SAIDA ? '−' : '+'
  return (
    <li className={`${css.linha} ${movimento.estornadoEm ? css.linhaEstornada : ''}`}>
      <span className={css.linhaInfo}>
        <strong>{movimento.categoria}</strong>
        <span className={css.dicaItem}>
          {horaCurta(new Date(movimento.criadoEm).toISOString())}
          {movimento.meio && ` · ${nomeDoMetodoCaixa(movimento.meio)}`}
          {movimento.descricao && ` · ${movimento.descricao}`}
        </span>
        {movimento.estornadoEm && (
          <span className={css.dicaItem}>
            Estornado por {movimento.estornadoPorNome} · {movimento.motivoEstorno}
          </span>
        )}
      </span>
      <span className={css.linhaValor}>
        {sinal} {moeda(movimento.valor)}
        {estornavel && !movimento.estornadoEm && (
          <Botao
            variante="texto"
            className={css.acaoEstorno}
            icone="undo-2"
            aria-label={`Estornar ${movimento.categoria}`}
            onClick={() => aoPedirEstorno({ tipo: 'movimento', id: movimento.id, rotulo: movimento.categoria })}
          >
            Estornar
          </Botao>
        )}
      </span>
    </li>
  )
}

export function AbaCaixa() {
  const { conversas, agora, caixa } = useAtendimento()
  const { cardapio } = useCatalogo()
  const {
    abrirCaixa, lancarMovimentoCaixa, estornarMovimentoCaixa, fecharCaixa, lancarVendaAvulsa, marcarEstorno,
  } = useAcoes()

  const [centavosAbertura, setCentavosAbertura] = useState(0)
  const [tipoLancamento, setTipoLancamento] = useState(TIPOS_MOVIMENTO.SAIDA)
  const [categoria, setCategoria] = useState('')
  const [centavosLancamento, setCentavosLancamento] = useState(0)
  const [metodoLancamento, setMetodoLancamento] = useState('dinheiro')
  const [descricao, setDescricao] = useState('')
  const [estornando, setEstornando] = useState(null)
  const [vendaAvulsaAberta, setVendaAvulsaAberta] = useState(false)
  const [fecharAberto, setFecharAberto] = useState(false)

  const movimentos = useMemo(() => caixa?.movimentos ?? [], [caixa])
  const abertura = caixaAberturaDoDia(movimentos, agora)
  const fechamento = caixaFechamentoDoDia(movimentos, agora)
  const aberto = caixaAberto(movimentos, agora)
  const resumo = useMemo(() => resumoCaixa(movimentos, conversas, cardapio, agora), [movimentos, conversas, cardapio, agora])
  const vendas = useMemo(() => vendasDoDia(conversas, agora), [conversas, agora])
  const lancamentos = useMemo(
    () => movimentosDoDia(movimentos, agora).filter((m) => m.tipo !== TIPOS_MOVIMENTO.FECHAMENTO),
    [movimentos, agora],
  )

  const limparFormLancamento = () => {
    setCategoria(''); setCentavosLancamento(0); setDescricao('')
  }

  const confirmarEstorno = (motivo) => {
    if (estornando.tipo === 'movimento') estornarMovimentoCaixa(agora, estornando.id, motivo)
    else marcarEstorno(estornando.id, motivo, estornando.valor)
    setEstornando(null)
  }

  // Nunca abriu hoje: só a abertura importa, o resto da tela nem existe ainda
  // (mesma lógica de "sem sessão de caixa" do mapa EasyStok, aplicada à UI).
  if (!abertura) {
    return (
      <div className={css.painel}>
        <p className={css.corpoBloco}>O caixa de hoje ainda não foi aberto.</p>
        <CampoMascarado
          tipo="moeda"
          rotulo="Saldo inicial"
          valor={mascaraMoeda(centavosAbertura)}
          erro={moedaAltaDemais(centavosAbertura) ? 'Valor alto demais' : null}
          dica="O dinheiro (ou nada) que já está na gaveta ao abrir o dia."
          aoMudarDigitos={(digitos) => setCentavosAbertura(Number(digitos || '0'))}
          aoColarTexto={(texto) => setCentavosAbertura(lerMoeda(texto))}
        />
        <Botao
          variante="primario"
          icone="dollar-sign"
          largo
          disabled={moedaAltaDemais(centavosAbertura)}
          onClick={() => abrirCaixa(agora, centavosAbertura / 100)}
        >
          Abrir o caixa
        </Botao>
      </div>
    )
  }

  return (
    <div className={css.painel}>
      <header className={css.cabecalho}>
        <div>
          <h4 className={css.tituloSecao}>Caixa de hoje</h4>
          <span className={css.dicaItem}>Aberto às {horaCurta(new Date(abertura.criadoEm).toISOString())} por {abertura.autorNome}</span>
        </div>
        <Pilula tom={aberto ? 'ok' : 'neutro'} icone={aberto ? undefined : 'lock'}>
          {aberto ? 'Aberto' : 'Fechado'}
        </Pilula>
      </header>

      {fechamento && (
        <p className={css.avisoFechado}>
          Dia fechado às {horaCurta(new Date(fechamento.criadoEm).toISOString())}. Diferença apurada: {' '}
          <strong>{fechamento.fechamento.diferenca === 0 ? 'bateu certo' : moeda(fechamento.fechamento.diferenca)}</strong>.
          {' '}Lançamentos e estornos de hoje ficam travados.
        </p>
      )}

      <dl className={css.resumoLista}>
        <div><dt>Saldo inicial</dt><dd>{moeda(resumo.saldoInicial)}</dd></div>
        <div><dt>Outras entradas</dt><dd>{moeda(resumo.totalEntradasExtras)}</dd></div>
        <div><dt>Outras saídas</dt><dd>{moeda(resumo.totalSaidasExtras)}</dd></div>
        <div><dt>Pagamentos de pedidos ({resumo.quantidadeVendas})</dt><dd>{moeda(resumo.totalPagamentosPedidos)}</dd></div>
        <div className={css.resumoForte}><dt>Saldo esperado</dt><dd>{moeda(resumo.saldoEsperado)}</dd></div>
      </dl>

      {resumo.porMetodo.length > 0 && (
        <ul className={css.porMetodo}>
          {resumo.porMetodo.map((linha) => (
            <li key={linha.metodo}>{linha.nome}: <strong>{moeda(linha.valor)}</strong></li>
          ))}
        </ul>
      )}

      {aberto && (
        <div className={css.acoesTopo}>
          <Botao variante="secundario" icone="shopping-cart" onClick={() => setVendaAvulsaAberta(true)}>
            Venda avulsa
          </Botao>
          <Botao variante="secundario" icone="log-out" onClick={() => setFecharAberto(true)}>
            Fechar o caixa
          </Botao>
        </div>
      )}

      {aberto && (
        <form
          className={css.formLancamento}
          onSubmit={(e) => {
            e.preventDefault()
            lancarMovimentoCaixa(agora, {
              tipoMovimento: tipoLancamento, categoria, valor: centavosLancamento / 100, meio: metodoLancamento, descricao,
            })
            limparFormLancamento()
          }}
        >
          <h4 className={css.tituloSecao}>Lançar entrada ou saída</h4>
          <div className={css.tiposLancamento} role="radiogroup" aria-label="Tipo de lançamento">
            <Chip papel="escolha" ativo={tipoLancamento === TIPOS_MOVIMENTO.ENTRADA} onClick={() => setTipoLancamento(TIPOS_MOVIMENTO.ENTRADA)}>
              Entrada
            </Chip>
            <Chip papel="escolha" ativo={tipoLancamento === TIPOS_MOVIMENTO.SAIDA} onClick={() => setTipoLancamento(TIPOS_MOVIMENTO.SAIDA)}>
              Saída
            </Chip>
          </div>

          <div className={css.categoriasSugeridas}>
            {(tipoLancamento === TIPOS_MOVIMENTO.SAIDA ? CATEGORIAS_SAIDA_SUGERIDAS : CATEGORIAS_ENTRADA_SUGERIDAS).map((sugestao) => (
              <Chip key={sugestao} papel="filtro" ativo={categoria === sugestao} onClick={() => setCategoria(sugestao)}>
                {sugestao}
              </Chip>
            ))}
          </div>

          <div className={css.formGrade}>
            <CampoTexto
              rotulo="Categoria"
              placeholder={tipoLancamento === TIPOS_MOVIMENTO.SAIDA ? 'Ex.: Sangria, Despesa' : 'Ex.: Suprimento (reforço de troco)'}
              value={categoria}
              onChange={(e) => setCategoria(e.target.value)}
            />
            <CampoSelecao
              rotulo="Método"
              opcoes={OPCOES_METODO}
              value={metodoLancamento}
              onChange={(e) => setMetodoLancamento(e.target.value)}
            />
          </div>
          <CampoMascarado
            tipo="moeda"
            rotulo="Valor"
            valor={mascaraMoeda(centavosLancamento)}
            erro={moedaAltaDemais(centavosLancamento) ? 'Valor alto demais' : null}
            aoMudarDigitos={(digitos) => setCentavosLancamento(Number(digitos || '0'))}
            aoColarTexto={(texto) => setCentavosLancamento(lerMoeda(texto))}
          />
          <CampoTexto
            rotulo="Descrição (opcional)"
            value={descricao}
            onChange={(e) => setDescricao(e.target.value)}
          />
          <Botao
            tipo="submit"
            variante="primario"
            icone={tipoLancamento === TIPOS_MOVIMENTO.SAIDA ? 'menos' : 'mais'}
            disabled={!categoria.trim() || centavosLancamento <= 0 || moedaAltaDemais(centavosLancamento)}
          >
            Lançar {tipoLancamento === TIPOS_MOVIMENTO.SAIDA ? 'saída' : 'entrada'}
          </Botao>
        </form>
      )}

      <h4 className={css.tituloSecao}>Lançamentos de hoje</h4>
      <ul className={css.lista}>
        {lancamentos.map((movimento) => (
          estornando?.tipo === 'movimento' && estornando.id === movimento.id
            ? (
              <li key={movimento.id}>
                <PromptEstorno rotulo={movimento.categoria} aoCancelar={() => setEstornando(null)} aoConfirmar={confirmarEstorno} />
              </li>
            )
            : <LinhaLancamento key={movimento.id} movimento={movimento} aoPedirEstorno={setEstornando} />
        ))}
      </ul>

      {vendas.length > 0 && (
        <>
          <h4 className={css.tituloSecao}>Pagamentos de pedidos hoje</h4>
          <ul className={css.lista}>
            {vendas.map((venda) => (
              estornando?.tipo === 'venda' && estornando.id === venda.conversaId
                ? (
                  <li key={venda.conversaId}>
                    <PromptEstorno rotulo={`o pedido ${venda.numero}`} aoCancelar={() => setEstornando(null)} aoConfirmar={confirmarEstorno} />
                  </li>
                )
                : (
                  <li key={venda.conversaId} className={`${css.linha} ${venda.estornado ? css.linhaEstornada : ''}`}>
                    <span className={css.linhaInfo}>
                      <strong>{venda.numero}</strong>
                      <span className={css.dicaItem}>{venda.nome} · {nomeDoMetodoCaixa(venda.meio)}</span>
                      {venda.estornado && <span className={css.dicaItem}>Estornado {moeda(venda.valorEstornado)}</span>}
                    </span>
                    <span className={css.linhaValor}>
                      {moeda(venda.valor)}
                      {aberto && !venda.estornado && (
                        <Botao
                          variante="texto"
                          className={css.acaoEstorno}
                          icone="undo-2"
                          aria-label={`Estornar pedido ${venda.numero}`}
                          onClick={() => setEstornando({
                            tipo: 'venda', id: venda.conversaId, rotulo: `o pedido ${venda.numero}`, valor: venda.valor,
                          })}
                        >
                          Estornar
                        </Botao>
                      )}
                    </span>
                  </li>
                )
            ))}
          </ul>
        </>
      )}

      {vendaAvulsaAberta && (
        <ModalVendaAvulsa
          cardapio={cardapio}
          aoFechar={() => setVendaAvulsaAberta(false)}
          aoLancar={(dados) => { lancarVendaAvulsa(agora, dados); setVendaAvulsaAberta(false) }}
        />
      )}

      {fecharAberto && (
        <ModalFecharCaixa
          resumo={resumo}
          aoFechar={() => setFecharAberto(false)}
          aoConfirmar={(contado) => { fecharCaixa(agora, contado); setFecharAberto(false) }}
        />
      )}
    </div>
  )
}
