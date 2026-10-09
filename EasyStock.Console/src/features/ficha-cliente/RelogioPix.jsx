import { useEffect, useState } from 'react'
import { Anel } from '../../componentes/Anel'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { faltaPagar, situacaoDaCobranca } from '../../dominio/cobranca'
import { horaCurta, moeda } from '../../dominio/formato'
import css from './RelogioPix.module.css'

// Secao 6 da direcao visual. Anel que esvazia no sentido horario a partir das
// 12h, numero grande no centro, cor em dois cortes. Mora dentro do bloco Pix,
// no lugar da pilula e da contagem de antes. O anel em si é o componente
// genérico de `componentes/Anel.jsx` (rodada 5, passo zero); aqui só o
// tamanho de sempre (128 px, traço 10) e as regras de cor e texto do Pix.

const DIAMETRO = 128
const TRACO = 10

const FRACAO_ATENCAO = 0.5
const FRACAO_PARADO = 0.2

const corPelaFracao = (fracao) => {
  if (fracao > FRACAO_ATENCAO) return 'ok'
  if (fracao > FRACAO_PARADO) return 'atencao'
  return 'parado'
}

const mmss = (restamMs) => {
  const segundosTotais = Math.max(Math.ceil(restamMs / 1000), 0)
  const minutos = Math.floor(segundosTotais / 60)
  const segundos = segundosTotais % 60
  return minutos + ':' + String(segundos).padStart(2, '0')
}

const clamp01 = (v) => Math.min(1, Math.max(0, v))

// Tique local de 1s por cima do relogio da tela, que anda de 30 em 30s. A
// contagem base vem de `situacaoDaCobranca` (o dominio continua sendo a
// fonte); aqui so interpolamos os segundos entre um tique grande e outro.
// `ancora` muda a cada tique grande OU a cada cobranca nova (reenvio), e e
// nesse instante que o tique local zera.
function useTiqueLocal(restamMsBase, ancora) {
  const [decorrido, setDecorrido] = useState(0)

  useEffect(() => {
    setDecorrido(0)
    const id = setInterval(() => setDecorrido((d) => d + 1000), 1000)
    return () => clearInterval(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ancora])

  return Math.max(restamMsBase - decorrido, 0)
}

export function RelogioPix({
  cobranca, agora, editavel, numeroPedido, nomeCliente,
  aoReenviar, aoCancelarPedido, aoEncerrarAtendimento, aoAceitarDivergencia, aoCobrarDiferenca,
}) {
  const situacao = situacaoDaCobranca(cobranca, agora)
  const total = Math.max(cobranca.expiraEm - cobranca.criadaEm, 1)
  // O relógio grande do app anda de 30 em 30s: entre um tique e outro, o
  // tique local do anel pode chegar a zero antes dele, ou o tique grande
  // pode alcançar "expirada" antes do anel. Os dois caminhos levam ao mesmo
  // "ao zerar", nunca a um reinício de contagem.
  const restam = useTiqueLocal(
    situacao.chave === 'aguardando' ? situacao.restamMs : 0,
    agora + '·' + cobranca.id,
  )
  const zerado = situacao.chave === 'expirada' || (situacao.chave === 'aguardando' && restam <= 0)

  const [confirmando, setConfirmando] = useState(null)
  // #1474 (R2): no modo API aceitar a diferença e cancelar o pedido não estão ligados.
  const disponivel = useAcaoDisponivel()
  const [recolhido, setRecolhido] = useState(false)

  // Recolhe a linha "Pago" 3s depois de cair. `cobranca` chega com `key` do
  // pai (BlocoCobranca), então uma cobrança nova remonta o componente e este
  // efeito só corre de novo dentro da MESMA cobrança, quando ela é paga.
  useEffect(() => {
    if (!cobranca.pagaEm) return undefined
    const id = setTimeout(() => setRecolhido(true), 3000)
    return () => clearTimeout(id)
  }, [cobranca.pagaEm])

  // Anúncio derivado na renderização, não guardado em estado: o texto muda
  // sozinho quando `pagaEm`/`zerado` mudam, e aria-live só fala quando o
  // conteúdo muda de verdade.
  const anuncio = cobranca.pagaEm
    ? 'Pago às ' + horaCurta(new Date(cobranca.pagaEm).toISOString()) + '.'
    : zerado ? 'A cobrança venceu.' : ''
  const anuncioRegiao = <output className="sr" aria-live="polite">{anuncio}</output>

  // Desfecho que fecha a cobrança sem pagamento: cancelamento ou estorno. O
  // relógio some de vez, sem número, sem trilho e sem botão de tentar de
  // novo (bug do dono, 24/09/2026: cancelar com a cobrança vencida deixava o
  // relógio "vencido" na tela, como se ainda valesse reenviar). Checado
  // depois dos Hooks (useTiqueLocal/useState/useEffect acima), nunca antes:
  // um `return` antes deles quebraria a ordem de Hooks entre renders.
  if (situacao.chave === 'cancelada' || situacao.chave === 'estornada') {
    return (
      <div className={css.relogio}>
        <output className="sr" aria-live="polite">{situacao.rotulo}.</output>
        <p className={css.recolhido}>
          <Icone nome="circle-x" tamanho={20} />
          {situacao.rotulo}
        </p>
      </div>
    )
  }

  // Pago (por qualquer via): conciliado sozinho, marcado à mão, ou com
  // diferença aceita. A cor fica sempre em --ok, o rótulo diz a via.
  // Divergente também tem `pagaEm` (a baixa chegou, só não bateu o valor),
  // então sai daqui e cai no ramo próprio logo abaixo.
  if (cobranca.pagaEm && situacao.chave !== 'divergente') {
    const rotuloPago = situacao.chave === 'conciliada'
      ? 'Pago'
      : cobranca.liberadaEm ? 'Pago com diferença' : 'Pago à mão'
    const horaPagamento = horaCurta(new Date(cobranca.pagaEm).toISOString())
    // Diferença aceita mostra o que ela realmente recebeu e liberou, não o
    // valor original cobrado; os outros dois casos são o mesmo número.
    const valorPago = cobranca.valorPago ?? cobranca.valor

    if (recolhido) {
      return (
        <div className={css.relogio}>
          {anuncioRegiao}
          <p className={css.recolhido}>
            <Icone nome="check" tamanho={20} />
            {rotuloPago} · {moeda(valorPago)} · {horaPagamento}
          </p>
        </div>
      )
    }

    return (
      <div className={css.relogio}>
        {anuncioRegiao}
        <Anel tamanho={DIAMETRO} traco={TRACO} fracao={1} tom="ok">
          <Icone nome="check" tamanho={40} rotulo="Pago" />
        </Anel>
        <p className={css.pago}>
          <span className={css.rotuloPago}>{rotuloPago}</span>
          <span className={css.horaPago}>{horaPagamento}</span>
        </p>
      </div>
    )
  }

  // Comprovante mandado, baixa do banco ainda não chegou. O anel congela na
  // fração de tempo que restava quando o print caiu, não em zero: zero
  // pareceria "venceu", e o código continua valendo.
  if (situacao.chave === 'em-conferencia') {
    const fracaoCongelada = clamp01(
      (cobranca.expiraEm - cobranca.comprovanteEm) / Math.max(cobranca.expiraEm - cobranca.criadaEm, 1),
    )
    return (
      <div className={css.relogio}>
        <Anel tamanho={DIAMETRO} traco={TRACO} fracao={fracaoCongelada} tom="atencao">
          <Icone nome="hourglass" tamanho={32} rotulo="Conferindo" />
          <span className={css.legenda}>Conferindo</span>
        </Anel>
      </div>
    )
  }

  // Caiu valor diferente do cobrado. A decisão é dela, os dois botões já são
  // a escolha (nunca só um primário aqui).
  if (situacao.chave === 'divergente') {
    const falta = faltaPagar(cobranca)
    return (
      <div className={css.relogio}>
        <Anel tamanho={DIAMETRO} traco={TRACO} fracao={1} tom="atencao">
          <span className={css.numeroDivergente}>
            {moeda(cobranca.valorPago)} de {moeda(cobranca.valor)}
          </span>
        </Anel>
        {editavel && (
          <div className={css.botoesZerado}>
            {disponivel('aceitarDivergencia') && (
              <Botao largo variante="primario" onClick={aoAceitarDivergencia}>Aceitar diferença</Botao>
            )}
            {falta > 0 && (
              <Botao largo variante="secundario" onClick={aoCobrarDiferenca}>Cobrar diferença</Botao>
            )}
          </div>
        )}
      </div>
    )
  }

  // Ao zerar: numero "0:00", trilho inteiro --parado, pisca e para. Os tres
  // botoes empilhados cobrem os tres caminhos dali: tentar de novo, desistir
  // com o pedido aberto, ou desistir fechando a conversa tambem.
  if (zerado) {
    return (
      <div className={`${css.relogio} ${css.zerado}`}>
        {anuncioRegiao}
        <Anel tamanho={DIAMETRO} traco={TRACO} fracao={0} tom="parado" trilhoParado>
          <span className={css.numero}>0:00</span>
          <span className={css.legenda}>Venceu</span>
        </Anel>
        {editavel && confirmando === null && (
          <div className={css.botoesZerado}>
            <Botao largo variante="primario" icone="refresh-cw" onClick={aoReenviar}>Reenviar cobrança</Botao>
            {disponivel('cancelarPedido') && (
              <Botao largo variante="secundario" icone="circle-x" onClick={() => setConfirmando('cancelar')}>
                Cancelar pedido
              </Botao>
            )}
            <Botao largo variante="secundario" icone="log-out" onClick={aoEncerrarAtendimento}>
              Encerrar atendimento
            </Botao>
          </div>
        )}
        {confirmando === 'cancelar' && (
          <div className={css.confirmacao}>
            <p className={css.legenda}>
              Cancelar o pedido {numeroPedido}? {nomeCliente} recebe aviso de cancelamento.
            </p>
            <div className={css.botoesZerado}>
              <Botao variante="primario" onClick={() => { aoCancelarPedido(); setConfirmando(null) }}>
                Cancelar pedido
              </Botao>
              <Botao variante="texto" onClick={() => setConfirmando(null)}>Manter</Botao>
            </div>
          </div>
        )}
      </div>
    )
  }

  // Contagem normal: verde acima de 50% do prazo, âmbar até 20%, vermelho
  // abaixo disso.
  const fracao = restam / total
  return (
    <div className={css.relogio}>
      <Anel tamanho={DIAMETRO} traco={TRACO} fracao={fracao} tom={corPelaFracao(fracao)}>
        <span
          className={css.numero}
          role="timer"
          aria-label={'Cobrança vence em ' + Math.ceil(restam / 60000) + ' minutos'}
        >
          {mmss(restam)}
        </span>
      </Anel>
      <p className={css.legenda}>vence {horaCurta(new Date(cobranca.expiraEm).toISOString())}</p>
    </div>
  )
}
