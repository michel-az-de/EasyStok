import { useEffect, useId, useRef, useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { CampoReais } from '../../componentes/CampoReais'
import { EscolhaDeEntregador } from '../../componentes/EscolhaDeEntregador'
import { Icone } from '../../componentes/Icone'
import {
  aguardaAprovacao, aguardaSinalManual, avisoDeLinkVencidoApi, esperaLinkNovoApi, faltaDaCobranca, podeDesfazerPagamento, situacaoDaCobranca, valorSugeridoParaBaixa,
} from '../../dominio/cobranca'
import { saldoApi, ultimoPagamentoApi } from '../../dominio/pagamento'
import { ROTULO_GERAR_E_ENVIAR, itensDoResumo, resumoDaCobrancaEscolhida } from '../../dominio/resumoPedido'
import { totalDoPedido } from '../../dominio/pedido'
import {
  PASSOS, motivoParaNaoAvancar, passoAnterior, passoPorId, proximoPasso, situacaoDoPasso,
  SEGUNDOS_PARA_DESFAZER,
} from '../../dominio/esteira'
import {
  lerMoeda, moeda, moedaAltaDemais,
} from '../../dominio/formato'
import { entregadorResolvido } from '../../dominio/viagem'
import { numeroParaEntregador, opcoesDoDespacho } from '../../dominio/despacho'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { BaixaAMao } from './BlocoCobranca'
import { MenuDoPedido } from './MenuDoPedido'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import { acaoDoPix } from './acaoDoPix'
import { criarExecucaoDaEtapa, houveAvancoConfirmado, marcoDaComanda } from './progressoDaComanda'
import css from './ficha.module.css'

// Rótulo curto sob cada ícone da trilha (banca estética, item 3). O nome
// completo do passo continua no aria-label de cada item.
const ROTULO_CURTO = {
  aguardando: 'Cobrança', pago: 'Pago', preparo: 'Preparo',
  embalado: 'Pronto', entrega: 'Entrega', entregue: 'Entregue',
}

// Ícone de cada passo na trilha (secao 5 da direcao visual).
const ICONE_DA_TRILHA = {
  aguardando: 'dollar-sign', pago: 'circle-check', preparo: 'cooking-pot',
  embalado: 'package', entrega: 'moto', entregue: 'house',
}

// Rótulo e ícone do botão primário de cada passo, exceto "aguardando": esse
// depende da situação da cobrança (ver `acaoDoPix.js`).
// #1686 (item 8): os mesmos toques da Cozinha (`COLUNAS_KDS`).
const ACAO_DO_PASSO = {
  pago: { rotulo: 'Começar preparo', icone: 'cooking-pot' },
  preparo: { rotulo: 'Marcar pronto', icone: 'package' },
  embalado: { rotulo: 'Saiu para entrega', icone: 'moto' },
  entrega: { rotulo: 'Marcar entregue', icone: 'house' },
}

const PROXIMO_DE_AGUARDANDO = proximoPasso('aguardando')?.rotulo ?? 'Pago'

// O passo "aguardando pagamento" não tem um botão único: o próximo passo muda com a situação da
// cobrança (`acaoDoPix.js`, secao 2, "Alerta do Pix").
const GERANDO_COBRANCA = 'Gerando cobrança…'

// O bloco Pedido: trilha, onde está e ação (secao 5 da direção visual). Um
// alvo só, fixo no rodapé da coluna Ficha, no lugar de `BlocoEsteira` (que
// listava os seis passos soltos) e da barra antiga somados.
export function BarraProximoPasso({
  pedido, conversaId, nomeCliente, agora, emDesfazer, simulacao = null, bloqueado = false, prerequisito = null, gerarSemEnviar = false, aoAvancar, aoDesfazer,
  alvoDoPrerequisito = null, aoIrParaMotivo = null, pedidoDeCancelar = 0,
  aoGerarPix, aoReenviarPix, aoConfirmarPagamento, aoAceitarDivergencia, aoCobrarDiferenca,
  aoVoltarEtapa, aoCancelarPedido, aoMarcarEstorno, aoEncerrarAtendimento, aoDesfazerPagamento, aoAprovarPedido,
}) {
  const [restam, setRestam] = useState(() => (emDesfazer ? SEGUNDOS_PARA_DESFAZER : 0))
  const [confirmando, setConfirmando] = useState(null) // 'cancelar' | 'estornar' | 'voltar' | 'desfazer'
  const [motivoCancelar, setMotivoCancelar] = useState('')
  const [erroCancelar, setErroCancelar] = useState(null)
  const cancelando = useRef(false)
  // #1686 (item 4): "Cancelar e refazer" da Seleção abre aqui a mesma confirmação do menu ⋯.
  const [cancelarVisto, setCancelarVisto] = useState(pedidoDeCancelar)
  if (pedidoDeCancelar !== cancelarVisto) {
    setCancelarVisto(pedidoDeCancelar)
    if (pedidoDeCancelar) { setMotivoCancelar(''); setErroCancelar(null); setConfirmando('cancelar') }
  }
  const { acoes: capacidades } = useAcessoModulos()
  const [abrindoBaixa, setAbrindoBaixa] = useState(false)
  const [pedindoMeio, setPedindoMeio] = useState(false)
  // #1875: o meio tocado na pergunta só fica marcado; criar o pedido e cobrar é o botão de confirmação.
  const [meioNaBarra, setMeioNaBarra] = useState(null)
  // #1989: a forma trocada na Cobrança com o resumo aberto passa para o resumo; nada sai com a antiga.
  const [meioDoPedidoVisto, setMeioDoPedidoVisto] = useState(pedido.meio ?? null)
  if ((pedido.meio ?? null) !== meioDoPedidoVisto) {
    setMeioDoPedidoVisto(pedido.meio ?? null)
    if (pedindoMeio) setMeioNaBarra(pedido.meio ?? null)
  }
  const [pedindoEntregador, setPedindoEntregador] = useState(false)
  // Issue #17: entregador de um toque e os campos de veículo, placa e empresa.
  const { conversas, fonteApi } = useAtendimento()
  const { entregadores, cardapio, meiosDePagamento } = useCatalogo()
  // No modo API, só ações ligadas aparecem. O erro fica ao lado do botão.
  const disponivel = useAcaoDisponivel()
  const [erroAcao, setErroAcao] = useState(null)
  const [marcoEmVoo, setMarcoEmVoo] = useState(null)
  // #1761: "Confirmando alteração…" aparecia ao gerar cobrança, que não é alteração.
  const [esperaEmVoo, setEsperaEmVoo] = useState(null)
  const emVoo = Boolean(marcoEmVoo)
  const [executarUmaVez] = useState(criarExecucaoDaEtapa)
  const [trilhaAberta, setTrilhaAberta] = useState(false)
  const idTrilha = useId()
  const marco = marcoDaComanda(pedido, conversaId)
  const [marcoVisto, setMarcoVisto] = useState(marco)
  const [confirmacao, setConfirmacao] = useState(null)
  if (marco.chave !== marcoVisto.chave || marco.estado !== marcoVisto.estado) {
    setMarcoVisto(marco)
    setConfirmacao(houveAvancoConfirmado(marcoVisto, marco) ? marco.estado : null)
  }
  useEffect(() => {
    if (!confirmacao) return undefined
    const id = setTimeout(() => setConfirmacao(null), 2400)
    return () => clearTimeout(id)
  }, [confirmacao])
  // UC-06 passo 5: motivo é obrigatório, valor nasce com o que foi pago e dá
  // para editar para um estorno parcial (RN-36 não promete sempre o total).
  // Em centavos por dentro (seção 4 da direção visual, passo zero): a tela só
  // vê o texto já mascarado, "R$ X,XX".
  const [motivoEstorno, setMotivoEstorno] = useState('')
  const [centavosEstorno, setCentavosEstorno] = useState(0)
  // Rodada 12 (issue #13): motivo do "Desfazer pagamento", obrigatório.
  const [motivoDesfazer, setMotivoDesfazer] = useState('')
  const [erroDesfazer, setErroDesfazer] = useState(null)
  const desfazendo = useRef(false)
  // #1683: estorno e "Voltar etapa" seguem o mesmo padrão: esperam, mostram a recusa, fecham no sucesso.
  const [erroCorrecao, setErroCorrecao] = useState(null)
  const corrigindo = useRef(false)

  // O retorno pertence ao pedido e à etapa que iniciaram a ação. A espera só termina
  // com a Promise, inclusive quando o rascunho ganha pedidoId durante a recarga.
  const executar = (fazer, espera = null) => {
    if (emVoo) return
    setErroAcao(null)
    const marcoNaHora = marco
    return executarUmaVez(fazer, {
      aoOcupar: () => { setMarcoEmVoo(marcoNaHora); setEsperaEmVoo(espera) },
      aoFalhar: texto => setErroAcao({ ...marcoNaHora, texto }),
      aoLiberar: () => setMarcoEmVoo(null),
    })
  }
  const erroVisivel = erroAcao?.chave === marco.chave && erroAcao.estado === marco.estado ? erroAcao.texto : null
  // #1776 (item 19): a API recusou "entregue" por saldo a receber; a barra oferece confirmar nesta etapa.
  const [saldoAConfirmar, setSaldoAConfirmar] = useState(null)
  const pedeSaldo = Boolean(erroVisivel) && saldoAConfirmar?.chave === marco.chave && saldoAConfirmar.estado === marco.estado
  const avancarPasso = (passo, opcoes) => {
    const marcoNaHora = marco
    return executar(() => aoAvancar(passo, undefined, opcoes))
      ?.then?.((r) => setSaldoAConfirmar(r?.saldoAReceber ? marcoNaHora : null))
  }
  const textoDaEspera = marcoEmVoo?.chave === marco.chave
    ? (esperaEmVoo ?? 'Confirmando alteração…') : 'Concluindo a ação iniciada nesta conversa…'

  useEffect(() => {
    if (!emDesfazer) return undefined
    const id = setInterval(() => setRestam((r) => (r <= 1 ? 0 : r - 1)), 1000)
    return () => clearInterval(id)
  }, [emDesfazer])

  if (emDesfazer && restam > 0) {
    return (
      <div className={`${css.barraPasso} ${css.barraDesfazer}`} data-etapa-confirmada>
        <Icone nome="check" />
        <span className={css.barraTexto}>{nomeCliente} avisada</span>
        <Botao onClick={aoDesfazer}>Desfazer · {restam}</Botao>
      </div>
    )
  }

  // Fora da trilha: cancelado. Um círculo só, sem os seis passos.
  if (pedido.estado === 'cancelado') {
    const estornado = Boolean(pedido.cobranca?.estornadaEm)
    return (
      <div className={css.barraPasso}>
        <div className={css.ondeEsta}>
          <Icone nome="circle-x" tamanho={32} />
          <span className={css.rotuloEstado}>{estornado ? 'Cancelado com estorno' : 'Cancelado'}</span>
        </div>
        <Botao largo variante="primario" icone="circle-check" onClick={aoEncerrarAtendimento}>
          Concluir conversa
        </Botao>
      </div>
    )
  }

  const anterior = passoAnterior(pedido.estado)
  const rotuloAnterior = passoPorId(anterior)?.rotulo
  const mensagemJaEnviada = Boolean(passoPorId(pedido.estado)?.mensagem)
  const estornado = Boolean(pedido.cobranca?.estornadaEm)

  let rotuloEstado
  let depoisTexto = 'Próximo: ' + PROXIMO_DE_AGUARDANDO
  let iconeEstado = 'dollar-sign'
  let corpoAcao
  // #1868: o botão "Marcar pago (complemento R$ X)" já diz quanto falta.
  let saldoNoBotao = false

  if (pedido.statusDesconhecido) {
    // #2005 (console-22): sem cobrança nem baixa para um status que o console não conhece.
    rotuloEstado = 'Status desconhecido'
    depoisTexto = null
    corpoAcao = (
      <p className={css.motivoDesabilitado} role="note">
        O pedido está em &quot;{pedido.statusApi}&quot;, um status que este console não conhece. Cobrança e baixa ficam
        bloqueadas: confira o pedido no EasyStok.
      </p>
    )
  } else if (pedido.estado === 'aguardando') {
    const situacaoCob = situacaoDaCobranca(pedido.cobranca, agora)
    const info = acaoDoPix(situacaoCob, {
      prerequisito: prerequisito ?? (pedido.itens.length > 0 ? null : 'A comanda está vazia.'),
      // #1474 (R4): no modo API este é o único botão que cria o pedido (cobra e manda a comanda).
      rotuloGerar: fonteApi && !pedido.pedidoId && !gerarSemEnviar ? ROTULO_GERAR_E_ENVIAR : 'Gerar cobrança',
      linkVencidoApi: fonteApi && pedido.pedidoId ? avisoDeLinkVencidoApi(pedido.cobranca, pedido.statusApi) : null,
      linkPendenteApi: fonteApi && Boolean(pedido.cobranca?.link),
      aoAbrirBaixa: () => setAbrindoBaixa(true),
      // #1989 (#1765-3): com a forma já escolhida também abre o resumo; quem gera é o "Confirmar".
      aoGerarPix: () => { if (!pedindoMeio) setMeioNaBarra(pedido.meio ?? null); setPedindoMeio(true) },
      aoReenviarPix: () => executar(() => aoReenviarPix()),
      aoMandarLinkNovo: fonteApi && pedido.pedidoId && esperaLinkNovoApi(pedido.cobranca, pedido.statusApi)
        ? () => executar(() => aoReenviarPix()) : null,
    })
    rotuloEstado = fonteApi && aguardaSinalManual(pedido) ? 'Aguardando sinal' : info.rotuloEstado
    if (fonteApi && aguardaSinalManual(pedido)) depoisTexto = pedido.requerAprovacao ? 'Próximo: Aprovação da loja' : 'Próximo: Fila de preparo'

    if (info.divergente) {
      const falta = faltaDaCobranca(pedido.cobranca)
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {disponivel('aceitarDivergencia') && (
            <Botao largo variante="primario" onClick={aoAceitarDivergencia}>Aceitar diferença</Botao>
          )}
          {falta > 0 && disponivel('cobrarDiferenca') && (
            <Botao largo variante="secundario" onClick={aoCobrarDiferenca}>Cobrar diferença</Botao>
          )}
        </div>
      )
    } else {
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {gerarSemEnviar && !pedido.pedidoId && <p className={css.motivoDesabilitado}>Janela fechada: gere sem enviar. Depois copie o link em Pagamento ou use um modelo aprovado.</p>}
          {/* #1686 (item 9): o motivo que segura o botão leva ao campo que resolve. */}
          {info.motivo && (info.motivo === prerequisito && alvoDoPrerequisito && aoIrParaMotivo
            ? <p className={css.motivoDesabilitado}><button type="button" className={css.linkMotivo} data-alvo-motivo={alvoDoPrerequisito}
              onClick={() => aoIrParaMotivo(alvoDoPrerequisito)}>{info.motivo}</button></p>
            : <p className={css.motivoDesabilitado}>{info.motivo}</p>)}
          {info.nota && <p className={css.motivoDesabilitado}>{info.nota}</p>}
          {info.botoes.map((b) => (
            <Botao
              key={b.rotulo} largo variante={b.secundario ? 'secundario' : 'primario'} icone={b.icone}
              disabled={b.desabilitado || emVoo} onClick={b.onClick}
            >
              {fonteApi && aguardaSinalManual(pedido) && b.rotulo === 'Marcar pago' ? 'Registrar sinal' : b.rotulo}
            </Botao>
          ))}
          {pedindoMeio && situacaoCob.chave === 'nenhuma' && (
            <>
              <EscolhaDeMeio
                escolhido={meioNaBarra}
                pedido={pedido}
                geraAoEscolher
                aoEscolher={setMeioNaBarra}
                pergunta="Como o cliente vai pagar?"
              />
              {meioNaBarra && (
                <p className={css.motivoDesabilitado} data-resumo-itens>{itensDoResumo(pedido, cardapio)}</p>
              )}
              {meioNaBarra && (
                <p className={css.motivoDesabilitado} data-resumo-cobranca>
                  {resumoDaCobrancaEscolhida(pedido, {
                    meio: meioNaBarra, rotuloMeio: meiosDePagamento.find((m) => m.id === meioNaBarra)?.rotulo ?? meioNaBarra,
                    total: pedido.totalApi ?? totalDoPedido(pedido, cardapio), fonteApi, agora, simulacao,
                  })}
                </p>
              )}
              <Botao
                largo variante="primario" icone="check" data-confirmar-cobranca
                disabled={!meioNaBarra || emVoo}
                onClick={() => { const meio = meioNaBarra; setPedindoMeio(false); setMeioNaBarra(null); return executar(() => aoGerarPix(meio), GERANDO_COBRANCA) }}
              >
                {fonteApi && !pedido.pedidoId && !gerarSemEnviar ? 'Confirmar e enviar' : 'Confirmar e gerar'}
              </Botao>
              <Botao largo variante="secundario" disabled={emVoo} onClick={() => { setPedindoMeio(false); setMeioNaBarra(null) }}>
                Voltar
              </Botao>
            </>
          )}
        </div>
      )
    }
  } else {
    const passoAtual = passoPorId(pedido.estado)
    const proximo = proximoPasso(pedido.estado)
    rotuloEstado = passoAtual.rotulo
    iconeEstado = ICONE_DA_TRILHA[pedido.estado]
    depoisTexto = proximo ? 'Próximo: ' + proximo.rotulo : null

    // Seção 3.2 (acréscimo): item novo num pedido que já passou de
    // "aguardando" pode gerar cobrança complementar (`cobranca.diferenca`,
    // `dominio/cobranca.js`). O pedido não volta pra trás na esteira (D7: o
    // sistema avisa, não trava, quando é a dona vendendo) — mas ela ainda
    // precisa de um jeito de marcar esse Pix extra como pago, e a barra só
    // oferecia "Marcar pago" dentro do passo "aguardando". Botão secundário
    // aqui, ao lado do avanço normal, sem mexer em BlocoCobranca.jsx (F4).
    const situacaoComplemento = situacaoDaCobranca(pedido.cobranca, agora)
    const complementoEmAberto = Boolean(pedido.cobranca?.diferenca)
      && !pedido.cobranca?.pagaEm
      && situacaoComplemento.chave !== 'nenhuma'
    saldoNoBotao = complementoEmAberto
    const botaoComplemento = complementoEmAberto && (
      <Botao largo variante="secundario" icone="dollar-sign" onClick={() => setAbrindoBaixa(true)}>
        {/* #2007 (item 9): depois do sinal da encomenda (coberta até a metade), o que falta é o restante; acréscimo
            numa encomenda já quitada continua complemento. */}
        {pedido.sinalNaReserva && (pedido.alocadoApi ?? pedido.totalPagoApi ?? 0) * 2 <= (pedido.totalApi ?? 0) + 0.01
          ? <>Marcar pago (restante {moeda(pedido.cobranca.valor)})</>
          : <>Marcar pago (complemento {moeda(pedido.cobranca.valor)})</>}
      </Botao>
    )

    if (aguardaAprovacao(pedido)) {
      // #1474 (R8): pedido fora da área liberado espera a dona aprovar antes da fila e da baixa
      // à mão. "Iniciar preparo" aqui só voltaria erro da máquina de estados.
      rotuloEstado = 'Esperando sua aprovação'
      depoisTexto = 'Próximo: Em preparo'
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          <Botao largo variante="primario" icone="circle-check" disabled={emVoo || !aoAprovarPedido}
            onClick={() => executar(aoAprovarPedido)}>
            Aprovar pedido
          </Botao>
        </div>
      )
    } else if (!proximo) {
      // #2079 (C2): entregue com o dinheiro da porta não lançado não está "concluído, nada pendente": a barra
      // diz quanto falta e oferece a baixa à frente do encerrar.
      const aReceberDoEntregue = fonteApi && Boolean(pedido.pedidoId) && pedido.estado === 'entregue' && !complementoEmAberto
        ? saldoApi(pedido) : 0
      if (aReceberDoEntregue > 0) saldoNoBotao = true
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {aReceberDoEntregue > 0 && (
            <>
              <p className={css.motivoDesabilitado}>
                Entregue com {moeda(aReceberDoEntregue)} a receber. Marque pago quando o dinheiro entrar.
              </p>
              <Botao largo variante="primario" icone="dollar-sign" onClick={() => setAbrindoBaixa(true)}>
                Marcar pago ({moeda(aReceberDoEntregue)})
              </Botao>
            </>
          )}
          <Botao largo variante={aReceberDoEntregue > 0 ? 'secundario' : 'primario'} icone="circle-check" onClick={aoEncerrarAtendimento}>
            Concluir conversa
          </Botao>
          {botaoComplemento}
        </div>
      )
    } else {
      const acao = ACAO_DO_PASSO[pedido.estado]
      // US-040: despachar sem saber quem leva é a mensagem fixa mentindo
      // pro cliente. Mesma forma de "sem meio marcado" acima: primeiro
      // toque pergunta em vez de avançar direto.
      // No modo API o aviso de saída é do EasyStok e não leva o nome: não pergunta quem leva.
      const precisaDeEntregador = !fonteApi && proximo.id === 'entrega' && !entregadorResolvido(pedido)
      // RN-14 / US-019: mesma resposta que o reducer usa para recusar.
      const motivo = motivoParaNaoAvancar(proximo.id, { bloqueado })
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {motivo && <p className={css.motivoDesabilitado}>{motivo}</p>}
          <Botao
            largo variante="primario" icone={acao.icone} disabled={Boolean(motivo) || emVoo}
            onClick={() => (precisaDeEntregador ? setPedindoEntregador(true) : avancarPasso(proximo.id))}
          >
            {acao.rotulo}
          </Botao>
          {pedeSaldo && (
            <Botao largo variante="secundario" disabled={emVoo} onClick={() => avancarPasso(proximo.id, { confirmarSaldoAReceber: true })}>
              Marcar entregue mesmo assim
            </Botao>
          )}
          {botaoComplemento}
          {pedindoEntregador && precisaDeEntregador && (
            <EscolhaDeEntregador
              {...opcoesDoDespacho(conversas, entregadores)}
              pergunta={`Quem leva o pedido ${numeroParaEntregador(pedido)}? O cliente é avisado com o nome.`}
              aoEscolher={(entregador) => { setPedindoEntregador(false); aoAvancar(proximo.id, entregador) }}
            />
          )}
        </div>
      )
    }
  }

  const alocadoApi = pedido.alocadoApi ?? pedido.totalPagoApi
  const naFilaSemQuitar = Boolean(pedido.pedidoId) && saldoApi(pedido) > 0
  if (naFilaSemQuitar && pedido.estado === 'pago' && !aguardaAprovacao(pedido)) rotuloEstado = 'Na fila de preparo'
  const podeVoltarEtapa = Boolean(anterior) && disponivel('corrigirPasso')
  const temPagamento = alocadoApi > 0 || pedido.totalPagoApi > 0 || Boolean(pedido.cobranca?.pagaEm)
  const podeCancelar = disponivel('cancelarPedido') && (fonteApi
    ? Boolean(pedido.pedidoId) && (!temPagamento || capacidades?.cancelarPedidoPago === true)
    : pedido.estado === 'aguardando')
  // Estornar é devolver dinheiro que entrou. Pedido sem cobrança paga (venda
  // antiga, massa de teste) não tem o que devolver, e oferecer o item aqui
  // levava a confirmar "você já devolveu R$ 0,00" (QA7, achado 1).
  const podeEstornar = pedido.estado !== 'aguardando' && Boolean(pedido.cobranca?.pagaEm) && disponivel('marcarEstorno')
  const valorPago = pedido.cobranca?.valorPago ?? pedido.cobranca?.valor ?? 0
  const podeDesfazer = podeDesfazerPagamento(pedido)
  const valorParaDesfazer = ultimoPagamentoApi(pedido)?.valor ?? valorPago
  const estornoValido = motivoEstorno.trim().length >= 3
    && centavosEstorno > 0 && !moedaAltaDemais(centavosEstorno)
    && centavosEstorno <= Math.round(valorPago * 100)

  // UC-06 passo 5: o valor nasce pré-preenchido com o que foi pago, editável
  // para um estorno parcial. Motivo some ao fechar, para não vazar de um
  // pedido para o próximo em telas que reusam este componente.
  const iniciarConfirmacao = (chave) => {
    if (chave === 'estornar') {
      setMotivoEstorno('')
      setCentavosEstorno(Math.round(valorPago * 100))
    }
    if (chave === 'desfazer') { setMotivoDesfazer(''); setErroDesfazer(null) }
    if (chave === 'cancelar') { setMotivoCancelar(''); setErroCancelar(null) }
    setErroCorrecao(null)
    setConfirmando(chave)
  }

  const confirmarCancelamento = async () => {
    if (cancelando.current) return
    cancelando.current = true
    setMarcoEmVoo(marco)
    setErroCancelar(null)
    try {
      const resultado = await aoCancelarPedido(motivoCancelar.trim())
      if (resultado?.erro) setErroCancelar(resultado.erro)
      else setConfirmando(null)
    } catch (erro) { setErroCancelar(erro.message || 'Não foi possível cancelar. Tente de novo.') }
    finally { cancelando.current = false; setMarcoEmVoo(null) }
  }

  // #1586: dinheiro segue o padrão do cancelamento. "Desfazer pagamento" espera a resposta e só
  // fecha no sucesso; na recusa o motivo digitado fica e o erro aparece ao lado.
  const confirmarDesfazer = async () => {
    if (desfazendo.current) return
    desfazendo.current = true
    setMarcoEmVoo(marco)
    setErroDesfazer(null)
    try {
      const resultado = await aoDesfazerPagamento(motivoDesfazer.trim())
      if (resultado?.erro) setErroDesfazer(resultado.erro)
      else setConfirmando(null)
    } catch (erro) { setErroDesfazer(erro.message || 'Não foi possível desfazer o pagamento. Tente de novo.') }
    finally { desfazendo.current = false; setMarcoEmVoo(null) }
  }

  const confirmarCorrecao = async (fazer, falha) => {
    if (corrigindo.current) return
    corrigindo.current = true
    setMarcoEmVoo(marco)
    setErroCorrecao(null)
    try {
      const resultado = await fazer()
      if (resultado?.erro) setErroCorrecao(resultado.erro)
      else setConfirmando(null)
    } catch (erro) { setErroCorrecao(erro.message || falha) }
    finally { corrigindo.current = false; setMarcoEmVoo(null) }
  }

  // #1683: confirmação em linha pelo teclado. Esc volta; no motivo, Enter quebra a linha e
  // Ctrl+Enter confirma, para um Enter distraído não cancelar pedido. Em andamento, nada fecha.
  const teclaDaConfirmacao = (evento) => {
    if (evento.key === 'Escape' && !emVoo) { evento.preventDefault(); evento.stopPropagation(); setConfirmando(null) }
    if (evento.key === 'Enter' && (evento.ctrlKey || evento.metaKey) && evento.target.tagName === 'TEXTAREA') {
      evento.preventDefault()
      evento.currentTarget.requestSubmit()
    }
  }

  // "Marcar pago": o formulário (`BaixaAMao`) fica aberto até a resposta e mostra a recusa,
  // com o valor e o método que ela digitou. Ele mesmo trava o segundo toque.
  const confirmarBaixa = async (valor, metodo) => {
    setMarcoEmVoo(marco)
    try {
      const resultado = await aoConfirmarPagamento(valor, metodo)
      if (!resultado?.erro) setAbrindoBaixa(false)
      return resultado
    } finally { setMarcoEmVoo(null) }
  }

  return (
    <div className={css.barraPasso} data-etapa-confirmada={confirmacao ? '' : undefined}>
      <button type="button" className={css.resumoAndamento} aria-expanded={trilhaAberta}
        aria-controls={idTrilha} onClick={() => setTrilhaAberta(aberta => !aberta)}>
        <span className="sr">Etapa {marco.indice + 1} de {marco.total}</span>
        <span className={css.abrirEtapas}>{trilhaAberta ? 'Recolher' : 'Ver etapas'}<Icone nome={trilhaAberta ? 'chevron-up' : 'chevron-down'} tamanho={16} /></span>
      </button>
      <div id={idTrilha} hidden={!trilhaAberta}>
      <ol
        className={css.trilha}
        aria-label="Etapas do pedido"
        style={{ '--progresso': Math.max(0, marco.indice) / (marco.total - 1) }}
      >
        {PASSOS.map((passo) => {
          const situacao = situacaoDoPasso(passo.id, pedido.estado)
          const concluido = situacao.chave === 'feito' || situacao.chave === 'atual'
          return (
            <li
              key={passo.id}
              aria-current={situacao.chave === 'atual' ? 'step' : undefined}
              className={`${css.trilhaItem} ${css[situacao.chave === 'proximo' ? 'futuro' : situacao.chave]} ${concluido ? css.concluido : ''}`}
              aria-label={`${naFilaSemQuitar && passo.id === 'pago' ? 'Fila' : passo.rotulo}, ${situacao.rotulo}`}
            >
              <span className={css.circuloPasso} aria-hidden="true">
                <Icone nome={ICONE_DA_TRILHA[passo.id]} tamanho={18} />
              </span>
              <span className={css.rotuloPasso} aria-hidden="true">{naFilaSemQuitar && passo.id === 'pago' ? 'Fila' : ROTULO_CURTO[passo.id] ?? passo.rotulo}</span>
            </li>
          )
        })}
      </ol>
      </div>

      <div className={css.ondeEsta}>
        <Icone nome={iconeEstado} tamanho={32} />
        <div>
          <span className={`${css.rotuloEstado} ${rotuloEstado === 'Cobrança pendente' ? css.cobrancaPendente : ''}`}>
            {rotuloEstado}
            {estornado && <span className={css.marcaEstorno}> · Estornado</span>}
          </span>
          {depoisTexto && confirmando === null && !abrindoBaixa && (
            <span className={css.depoisTexto}>{depoisTexto}</span>
          )}
        </div>
      </div>

      {(emVoo || confirmacao) && <output className={css.retornoEtapa} aria-live="polite">
        <Icone nome={emVoo ? 'relogio' : 'check'} tamanho={16} />
        {emVoo ? textoDaEspera : 'Etapa atualizada.'}
      </output>}

      {confirmando === null && !abrindoBaixa && (
        <div className={css.linhaAcao} aria-busy={emVoo}>
          {corpoAcao}
          <MenuDoPedido
            podeCancelar={podeCancelar}
            podeEstornar={podeEstornar}
            podeVoltarEtapa={podeVoltarEtapa}
            podeDesfazerPagamento={podeDesfazer}
            aoEscolher={iniciarConfirmacao}
          />
        </div>
      )}

      {erroVisivel && confirmando === null && !abrindoBaixa && (
        <p className={css.motivoDesabilitado} role="alert">{erroVisivel}</p>
      )}

      {/* #1868: com o "Marcar pago (complemento R$ X)" na barra, o "Já coberto/Falta" era a terceira
          vez do saldo na coluna (o Resumo o destaca no topo) e custava a barra em 720 px. Sem o
          botão, a linha fica: é o rótulo financeiro honesto do pedido na fila sem quitação. */}
      {!saldoNoBotao && alocadoApi > 0 && saldoApi(pedido) > 0 && (
        <p className={css.corpoBloco}>Já coberto: {moeda(alocadoApi)}. Falta: {moeda(saldoApi(pedido))}.</p>
      )}
      {abrindoBaixa && (
        <BaixaAMao
          valorCobrado={valorSugeridoParaBaixa(pedido)}
          pedido={pedido}
          aoAprovar={aoAprovarPedido}
          aoCancelar={() => setAbrindoBaixa(false)}
          aoConfirmar={confirmarBaixa}
        />
      )}

      {confirmando === 'cancelar' && (
        // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- Escape em qualquer campo fecha a confirmação; o form é o único ancestral comum.
        <form className={css.confirmarCorrecao} onKeyDown={teclaDaConfirmacao} onSubmit={(evento) => {
          evento.preventDefault()
          if (!emVoo && !(fonteApi && motivoCancelar.trim().length < 3)) confirmarCancelamento()
        }}>
          <p className={css.corpoBloco}>
            Cancelar o pedido {pedido.numero}? {fonteApi ? 'Avise o cliente pela conversa após confirmar.' : `${nomeCliente} recebe aviso de cancelamento.`}
          </p>
          {fonteApi && <CampoArea rotulo="Motivo do cancelamento" value={motivoCancelar}
            onChange={(e) => setMotivoCancelar(e.target.value)} maxLength={500} disabled={emVoo} autoFocus />}
          {fonteApi && temPagamento && <p className={css.corpoBloco}>
            O cancelamento não devolve o pagamento automaticamente. A devolução deve ser tratada separadamente.
          </p>}
          {erroCancelar && <p className={css.motivoDesabilitado} role="alert">{erroCancelar}</p>}
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" disabled={emVoo} autoFocus={!fonteApi} onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao variante="perigo" tipo="submit" disabled={emVoo || (fonteApi && motivoCancelar.trim().length < 3)}>
              {emVoo ? 'Cancelando…' : 'Cancelar pedido'}
            </Botao>
          </p>
        </form>
      )}

      {confirmando === 'estornar' && (
        // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- idem
        <form className={css.confirmarCorrecao} onKeyDown={teclaDaConfirmacao} onSubmit={(evento) => {
          evento.preventDefault()
          if (emVoo || !estornoValido) return
          confirmarCorrecao(() => aoMarcarEstorno(motivoEstorno.trim(), centavosEstorno / 100),
            'Não foi possível registrar o estorno. Tente de novo.')
        }}>
          <p className={css.corpoBloco}>
            Devolver para {nomeCliente}. Pago: {moeda(valorPago)}.
          </p>
          <CampoReais
            rotulo="Valor a devolver"
            centavos={centavosEstorno}
            ler={lerMoeda}
            erro={moedaAltaDemais(centavosEstorno)
              ? 'Valor alto demais'
              : centavosEstorno > Math.round(valorPago * 100) ? 'Maior que o pago' : null}
            dica="Pode ser menor que o pago, para um estorno parcial."
            aoMudar={(valor) => setCentavosEstorno(valor ?? 0)}
          />
          <CampoArea
            rotulo="Motivo do estorno"
            dica="Obrigatório. Fica gravado na ocorrência e no cadastro do cliente."
            rows={2}
            value={motivoEstorno}
            onChange={(e) => setMotivoEstorno(e.target.value)}
            disabled={emVoo}
            autoFocus
          />
          {erroCorrecao && <p className={css.motivoDesabilitado} role="alert">{erroCorrecao}</p>}
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" disabled={emVoo} onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao variante="perigo" tipo="submit" disabled={emVoo || !estornoValido}>
              {emVoo ? 'Registrando…' : 'Marcar estorno'}
            </Botao>
          </p>
        </form>
      )}

      {confirmando === 'desfazer' && (
        // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- idem
        <form className={css.confirmarCorrecao} onKeyDown={teclaDaConfirmacao} onSubmit={(evento) => {
          evento.preventDefault()
          if (!emVoo && motivoDesfazer.trim().length >= 3) confirmarDesfazer()
        }}>
          <p className={css.corpoBloco}>
            Desfazer o último pagamento de {moeda(valorParaDesfazer)}? A cobrança volta a esperar
            {pedido.cobranca?.link ? ' e o pedido sai da cozinha' : ''}.
            {' '}{nomeCliente} não recebe mensagem nenhuma.
          </p>
          <CampoArea
            rotulo="Motivo"
            dica="Obrigatório. Fica na conversa e no cadastro do cliente."
            rows={2}
            value={motivoDesfazer}
            onChange={(e) => setMotivoDesfazer(e.target.value)}
            disabled={emVoo}
            autoFocus
          />
          {erroDesfazer && <p className={css.motivoDesabilitado} role="alert">{erroDesfazer}</p>}
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" disabled={emVoo} onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao
              variante="perigo"
              tipo="submit"
              disabled={emVoo || motivoDesfazer.trim().length < 3}
            >
              {emVoo ? 'Desfazendo…' : 'Desfazer pagamento'}
            </Botao>
          </p>
        </form>
      )}

      {confirmando === 'voltar' && (
        // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- idem
        <form className={css.confirmarCorrecao} onKeyDown={teclaDaConfirmacao} onSubmit={(evento) => {
          evento.preventDefault()
          if (!emVoo) confirmarCorrecao(() => aoVoltarEtapa(), 'Não foi possível voltar a etapa. Tente de novo.')
        }}>
          <p className={css.corpoBloco}>
            Voltar para "{rotuloAnterior}"?
            {mensagemJaEnviada ? ' A mensagem já enviada não volta.' : ''}
          </p>
          {erroCorrecao && <p className={css.motivoDesabilitado} role="alert">{erroCorrecao}</p>}
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" disabled={emVoo} autoFocus onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao variante="primario" tipo="submit" disabled={emVoo}>
              {emVoo ? 'Voltando…' : 'Voltar'}
            </Botao>
          </p>
        </form>
      )}

    </div>
  )
}
