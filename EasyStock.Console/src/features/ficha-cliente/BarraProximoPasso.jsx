import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoSelecao } from '../../componentes/Campo'
import { CampoMascarado } from '../../componentes/CampoMascarado'
import { EscolhaDeEntregador } from '../../componentes/EscolhaDeEntregador'
import { Icone } from '../../componentes/Icone'
import { faltaPagar, podeDesfazerPagamento, situacaoDaCobranca } from '../../dominio/cobranca'
import {
  PASSOS, motivoParaNaoAvancar, passoAnterior, passoPorId, proximoPasso, situacaoDoPasso,
  SEGUNDOS_PARA_DESFAZER,
} from '../../dominio/esteira'
import {
  lerMoeda, mascaraMoeda, moeda, moedaAltaDemais,
} from '../../dominio/formato'
import { entregadorResolvido } from '../../dominio/viagem'
import { numeroParaEntregador, opcoesDoDespacho } from '../../dominio/despacho'
import { useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { BaixaAMao } from './BlocoCobranca'
import { MenuDoPedido } from './MenuDoPedido'
import { EscolhaDeMeio } from './EscolhaDeMeio'
import css from './ficha.module.css'

// Rótulo curto sob cada ícone da trilha (banca estética, item 3). O nome
// completo do passo continua no aria-label de cada item.
const ROTULO_CURTO = {
  aguardando: 'Cobrança', pago: 'Pago', preparo: 'Preparo',
  embalado: 'Embalado', entrega: 'Entrega', entregue: 'Entregue',
}

// Ícone de cada passo na trilha (secao 5 da direcao visual).
const ICONE_DA_TRILHA = {
  aguardando: 'dollar-sign', pago: 'circle-check', preparo: 'cooking-pot',
  embalado: 'package', entrega: 'moto', entregue: 'house',
}

// Rótulo e ícone do botão primário de cada passo, exceto "aguardando": esse
// depende da situação da cobrança (ver `acaoDoPix` abaixo).
const ACAO_DO_PASSO = {
  pago: { rotulo: 'Iniciar preparo', icone: 'cooking-pot' },
  preparo: { rotulo: 'Marcar embalado', icone: 'package' },
  embalado: { rotulo: 'Despachar pedido', icone: 'moto' },
  entrega: { rotulo: 'Marcar entregue', icone: 'house' },
}

const PROXIMO_DE_AGUARDANDO = proximoPasso('aguardando')?.rotulo ?? 'Pago'

// O passo "aguardando pagamento" não tem um botão único: o próximo passo
// muda com a situação da cobrança (secao 2, "Alerta do Pix"). Substitui a
// antiga trava, que só mostrava texto e travava a barra inteira.
function acaoDoPix(situacaoCob, { temItens, aoAbrirBaixa, aoGerarPix, aoReenviarPix }) {
  switch (situacaoCob.chave) {
    case 'nenhuma':
      return {
        rotuloEstado: 'Cobrança não gerada',
        motivo: temItens ? null : 'A comanda está vazia.',
        // Atalho da barra: gera no meio marcado nos chips do bloco Cobrança;
        // sem meio marcado, pergunta antes (ponta a da integração).
        botoes: [{ rotulo: 'Gerar cobrança', icone: 'dollar-sign', onClick: aoGerarPix, desabilitado: !temItens }],
      }
    case 'expirada':
      return {
        rotuloEstado: 'Cobrança venceu',
        botoes: [{ rotulo: 'Reenviar cobrança', icone: 'refresh-cw', onClick: aoReenviarPix }],
      }
    case 'em-conferencia':
      return {
        rotuloEstado: 'Conferindo comprovante',
        botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa }],
      }
    case 'divergente':
      return { rotuloEstado: 'Valor diferente', divergente: true }
    default:
      return {
        rotuloEstado: 'Aguardando pagamento',
        botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa }],
      }
  }
}

// Categorias da ocorrência na API (S27), na ordem em que a dona mais usa.
const CATEGORIAS_OCORRENCIA = [
  { valor: 'produto_improprio', rotulo: 'Produto com problema' },
  { valor: 'atraso', rotulo: 'Atraso' },
  { valor: 'preferencia', rotulo: 'Preferência do cliente' },
  { valor: 'outro', rotulo: 'Outro' },
]

// O bloco Pedido: trilha, onde está e ação (secao 5 da direção visual). Um
// alvo só, fixo no rodapé da coluna Ficha, no lugar de `BlocoEsteira` (que
// listava os seis passos soltos) e da barra antiga somados.
export function BarraProximoPasso({
  pedido, nomeCliente, agora, emDesfazer, bloqueado = false, aoAvancar, aoDesfazer,
  aoGerarPix, aoReenviarPix, aoConfirmarPagamento, aoAceitarDivergencia, aoCobrarDiferenca,
  aoVoltarEtapa, aoCancelarPedido, aoMarcarEstorno, aoEncerrarAtendimento, aoDesfazerPagamento,
  aoRegistrarOcorrencia = null,
}) {
  const [restam, setRestam] = useState(() => (emDesfazer ? SEGUNDOS_PARA_DESFAZER : 0))
  const [confirmando, setConfirmando] = useState(null) // 'cancelar' | 'estornar' | 'voltar' | 'desfazer'
  const [abrindoBaixa, setAbrindoBaixa] = useState(false)
  const [pedindoMeio, setPedindoMeio] = useState(false)
  const [pedindoEntregador, setPedindoEntregador] = useState(false)
  // Issue #17: entregador de um toque e os campos de veículo, placa e empresa.
  const { conversas } = useAtendimento()
  const { entregadores } = useCatalogo()
  // UC-06 passo 5: motivo é obrigatório, valor nasce com o que foi pago e dá
  // para editar para um estorno parcial (RN-36 não promete sempre o total).
  // Em centavos por dentro (seção 4 da direção visual, passo zero): a tela só
  // vê o texto já mascarado, "R$ X,XX".
  const [motivoEstorno, setMotivoEstorno] = useState('')
  const [centavosEstorno, setCentavosEstorno] = useState(0)
  // Rodada 12 (issue #13): motivo do "Desfazer pagamento", obrigatório.
  const [motivoDesfazer, setMotivoDesfazer] = useState('')
  // F09 (S27): ocorrência aberta à mão pela dona, só no modo API.
  const [ocorrenciaNova, setOcorrenciaNova] = useState({ categoria: 'produto_improprio', relato: '' })

  useEffect(() => {
    if (!emDesfazer) return undefined
    const id = setInterval(() => setRestam((r) => (r <= 1 ? 0 : r - 1)), 1000)
    return () => clearInterval(id)
  }, [emDesfazer])

  if (emDesfazer && restam > 0) {
    return (
      <div className={`${css.barraPasso} ${css.barraDesfazer}`}>
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
        <Botao largo variante="primario" icone="log-out" onClick={aoEncerrarAtendimento}>
          Encerrar atendimento
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

  if (pedido.estado === 'aguardando') {
    const situacaoCob = situacaoDaCobranca(pedido.cobranca, agora)
    const info = acaoDoPix(situacaoCob, {
      temItens: pedido.itens.length > 0,
      aoAbrirBaixa: () => setAbrindoBaixa(true),
      aoGerarPix: () => (pedido.meio ? aoGerarPix(pedido.meio) : setPedindoMeio(true)),
      aoReenviarPix: () => aoReenviarPix(),
    })
    rotuloEstado = info.rotuloEstado

    if (info.divergente) {
      const falta = faltaPagar(pedido.cobranca)
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          <Botao largo variante="primario" onClick={aoAceitarDivergencia}>Aceitar diferença</Botao>
          {falta > 0 && (
            <Botao largo variante="secundario" onClick={aoCobrarDiferenca}>Cobrar diferença</Botao>
          )}
        </div>
      )
    } else {
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {info.motivo && <p className={css.motivoDesabilitado}>{info.motivo}</p>}
          {info.botoes.map((b) => (
            <Botao
              key={b.rotulo} largo variante="primario" icone={b.icone}
              disabled={b.desabilitado} onClick={b.onClick}
            >
              {b.rotulo}
            </Botao>
          ))}
          {pedindoMeio && situacaoCob.chave === 'nenhuma' && !pedido.meio && (
            <EscolhaDeMeio
              escolhido={null}
              aoEscolher={(meio) => { setPedindoMeio(false); aoGerarPix(meio) }}
              pergunta="Como o cliente vai pagar?"
            />
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
    const botaoComplemento = complementoEmAberto && (
      <Botao largo variante="secundario" icone="dollar-sign" onClick={() => setAbrindoBaixa(true)}>
        Marcar pago (complemento {moeda(pedido.cobranca.valor)})
      </Botao>
    )

    if (!proximo) {
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          <Botao largo variante="primario" icone="log-out" onClick={aoEncerrarAtendimento}>
            Encerrar atendimento
          </Botao>
          {botaoComplemento}
        </div>
      )
    } else {
      const acao = ACAO_DO_PASSO[pedido.estado]
      // US-040: despachar sem saber quem leva é a mensagem fixa mentindo
      // pro cliente. Mesma forma de "sem meio marcado" acima: primeiro
      // toque pergunta em vez de avançar direto.
      const precisaDeEntregador = proximo.id === 'entrega' && !entregadorResolvido(pedido)
      // RN-14 / US-019: mesma resposta que o reducer usa para recusar.
      const motivo = motivoParaNaoAvancar(proximo.id, { bloqueado })
      corpoAcao = (
        <div className={css.botoesPrimarios}>
          {motivo && <p className={css.motivoDesabilitado}>{motivo}</p>}
          <Botao
            largo variante="primario" icone={acao.icone} disabled={Boolean(motivo)}
            onClick={() => (precisaDeEntregador ? setPedindoEntregador(true) : aoAvancar(proximo.id))}
          >
            {acao.rotulo}
          </Botao>
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

  const podeVoltarEtapa = Boolean(anterior)
  const podeCancelar = pedido.estado === 'aguardando'
  // Estornar é devolver dinheiro que entrou. Pedido sem cobrança paga (venda
  // antiga, massa de teste) não tem o que devolver, e oferecer o item aqui
  // levava a confirmar "você já devolveu R$ 0,00" (QA7, achado 1).
  const podeEstornar = pedido.estado !== 'aguardando' && Boolean(pedido.cobranca?.pagaEm)
  const valorPago = pedido.cobranca?.valorPago ?? pedido.cobranca?.valor ?? 0
  const podeDesfazer = podeDesfazerPagamento(pedido)

  // UC-06 passo 5: o valor nasce pré-preenchido com o que foi pago, editável
  // para um estorno parcial. Motivo some ao fechar, para não vazar de um
  // pedido para o próximo em telas que reusam este componente.
  const iniciarConfirmacao = (chave) => {
    if (chave === 'estornar') {
      setMotivoEstorno('')
      setCentavosEstorno(Math.round(valorPago * 100))
    }
    if (chave === 'desfazer') setMotivoDesfazer('')
    if (chave === 'ocorrencia') setOcorrenciaNova({ categoria: 'produto_improprio', relato: '' })
    setConfirmando(chave)
  }

  return (
    <div className={css.barraPasso}>
      <ol
        className={css.trilha}
        style={{ '--progresso': Math.max(0, PASSOS.findIndex((p) => situacaoDoPasso(p.id, pedido.estado).chave === 'atual')) / (PASSOS.length - 1) }}
      >
        {PASSOS.map((passo) => {
          const situacao = situacaoDoPasso(passo.id, pedido.estado)
          const concluido = situacao.chave === 'feito' || situacao.chave === 'atual'
          return (
            <li
              key={passo.id}
              className={`${css.trilhaItem} ${css[situacao.chave === 'proximo' ? 'futuro' : situacao.chave]} ${concluido ? css.concluido : ''}`}
              aria-label={`${passo.rotulo}, ${situacao.rotulo}`}
            >
              <span className={css.circuloPasso} aria-hidden="true">
                <Icone nome={ICONE_DA_TRILHA[passo.id]} tamanho={18} />
              </span>
              <span className={css.rotuloPasso} aria-hidden="true">{ROTULO_CURTO[passo.id] ?? passo.rotulo}</span>
            </li>
          )
        })}
      </ol>

      <div className={css.ondeEsta}>
        <Icone nome={iconeEstado} tamanho={32} />
        <div>
          <span className={css.rotuloEstado}>
            {rotuloEstado}
            {estornado && <span className={css.marcaEstorno}> · Estornado</span>}
          </span>
          {depoisTexto && confirmando === null && !abrindoBaixa && (
            <span className={css.depoisTexto}>{depoisTexto}</span>
          )}
        </div>
      </div>

      {confirmando === null && !abrindoBaixa && (
        <div className={css.linhaAcao}>
          {corpoAcao}
          <MenuDoPedido
            podeCancelar={podeCancelar}
            podeEstornar={podeEstornar}
            podeVoltarEtapa={podeVoltarEtapa}
            podeDesfazerPagamento={podeDesfazer}
            podeRegistrarOcorrencia={Boolean(aoRegistrarOcorrencia)}
            aoEscolher={iniciarConfirmacao}
          />
        </div>
      )}

      {abrindoBaixa && (
        <BaixaAMao
          valorCobrado={pedido.cobranca.valor}
          aoCancelar={() => setAbrindoBaixa(false)}
          aoConfirmar={(valor) => { setAbrindoBaixa(false); aoConfirmarPagamento(valor) }}
        />
      )}

      {confirmando === 'cancelar' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Cancelar o pedido {pedido.numero}? {nomeCliente} recebe aviso de cancelamento.
          </p>
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao variante="primario" onClick={() => { aoCancelarPedido(); setConfirmando(null) }}>
              Cancelar pedido
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'estornar' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Devolver para {nomeCliente}. Pago: {moeda(valorPago)}.
          </p>
          <CampoMascarado
            tipo="moeda"
            rotulo="Valor a devolver"
            valor={mascaraMoeda(centavosEstorno)}
            erro={moedaAltaDemais(centavosEstorno)
              ? 'Valor alto demais'
              : centavosEstorno > Math.round(valorPago * 100) ? 'Maior que o pago' : null}
            dica="Pode ser menor que o pago, para um estorno parcial."
            aoMudarDigitos={(digitos) => setCentavosEstorno(Number(digitos || '0'))}
            aoColarTexto={(texto) => setCentavosEstorno(lerMoeda(texto))}
          />
          <CampoArea
            rotulo="Motivo do estorno"
            dica="Obrigatório. Fica gravado na ocorrência e no cadastro do cliente."
            rows={2}
            value={motivoEstorno}
            onChange={(e) => setMotivoEstorno(e.target.value)}
          />
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao
              variante="primario"
              disabled={motivoEstorno.trim().length < 3
                || centavosEstorno <= 0 || moedaAltaDemais(centavosEstorno)
                || centavosEstorno > Math.round(valorPago * 100)}
              onClick={() => {
                aoMarcarEstorno(motivoEstorno.trim(), centavosEstorno / 100)
                setConfirmando(null)
              }}
            >
              Marcar estorno
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'desfazer' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Desfazer o pagamento de {moeda(valorPago)}? A cobrança volta a esperar
            {pedido.cobranca?.link ? ' e o pedido sai da cozinha' : ''}.
            {' '}{nomeCliente} não recebe mensagem nenhuma.
          </p>
          <CampoArea
            rotulo="Motivo"
            dica="Obrigatório. Fica na conversa e no cadastro do cliente."
            rows={2}
            value={motivoDesfazer}
            onChange={(e) => setMotivoDesfazer(e.target.value)}
          />
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao
              variante="primario"
              disabled={motivoDesfazer.trim().length < 3}
              onClick={() => { aoDesfazerPagamento(motivoDesfazer.trim()); setConfirmando(null) }}
            >
              Desfazer pagamento
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'ocorrencia' && (
        <div className={css.confirmarCorrecao}>
          <CampoSelecao
            rotulo="Tipo da ocorrência"
            opcoes={CATEGORIAS_OCORRENCIA}
            value={ocorrenciaNova.categoria}
            onChange={(e) => setOcorrenciaNova((o) => ({ ...o, categoria: e.target.value }))}
          />
          <CampoArea
            rotulo="O que aconteceu"
            dica="Obrigatório. Fica na ocorrência do pedido; o reembolso sai dela."
            rows={2}
            value={ocorrenciaNova.relato}
            onChange={(e) => setOcorrenciaNova((o) => ({ ...o, relato: e.target.value }))}
          />
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao
              variante="primario"
              disabled={ocorrenciaNova.relato.trim().length < 3}
              onClick={() => {
                aoRegistrarOcorrencia({ categoria: ocorrenciaNova.categoria, relato: ocorrenciaNova.relato.trim() })
                setConfirmando(null)
              }}
            >
              Registrar ocorrência
            </Botao>
          </p>
        </div>
      )}

      {confirmando === 'voltar' && (
        <div className={css.confirmarCorrecao}>
          <p className={css.corpoBloco}>
            Voltar para "{rotuloAnterior}"?
            {mensagemJaEnviada ? ' A mensagem já enviada não volta.' : ''}
          </p>
          <p className={css.confirmarAcoes}>
            <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            <Botao variante="primario" onClick={() => { aoVoltarEtapa(); setConfirmando(null) }}>
              Voltar
            </Botao>
          </p>
        </div>
      )}

    </div>
  )
}
