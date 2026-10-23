// Gesto da loja no modo API (#1443, decisão do Felipe em 07/10). Abrir a loja é abrir o caixa: a
// tela mostra como ficou o caixa de ontem, pede a conferência do saldo inicial (e o motivo, se ela
// retificar) e só abre a loja depois que o caixa abriu. Fechar dentro do horário pede justificativa,
// que a API grava na auditoria e avisa os donos. Quem pede o gesto é `alternarLoja`
// (`aplicacao/api/expediente.js`); fora do horário, fechar segue direto, sem este modal.
import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { CampoReais } from '../../componentes/CampoReais'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useCaixaDoDiaApi } from '../../aplicacao/useCaixaDoDiaApi'
import {
  JUSTIFICATIVA_MINIMA, aberturaPedeConfirmacao, diaMes, gavetaDoFechamento, justificativaValida, observacaoDaAbertura,
  retificacaoDoSaldo, situacaoDoCaixaParaAbrirLoja, textoDaConfirmacaoDaAbertura,
} from '../../dominio/aberturaDaLoja'
import { conferenciaDaGaveta } from '../../dominio/caixa'
import {
  horaCurta, lerMoeda, moeda, moedaAltaDemais,
} from '../../dominio/formato'
import css from './loja.module.css'

const MOTIVO_MINIMO = 3

function useEnvio() {
  const [envio, setEnvio] = useState({ enviando: false, erro: null })
  const enviar = async (fn) => {
    setEnvio({ enviando: true, erro: null })
    try {
      await fn()
      setEnvio({ enviando: false, erro: null })
      return true
    } catch (erro) {
      setEnvio({ enviando: false, erro: erro.message })
      return false
    }
  }
  return [envio, enviar]
}

// J2 (#1990): dinheiro do caixa se digita em reais ("100" é R$ 100,00); campo vazio vale zero.
function CampoDinheiro({ rotulo, dica, centavos, aoMudar }) {
  return (
    <CampoReais
      rotulo={rotulo}
      dica={dica}
      centavos={centavos}
      ler={lerMoeda}
      erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null}
      aoMudar={(valor) => aoMudar(valor ?? 0)}
    />
  )
}

function Erro({ texto }) {
  return texto ? <p className={css.erro} role="alert">{texto}</p> : null
}

// O caixa de um dia anterior ficou aberto: confere a gaveta e fecha aquele dia antes de abrir hoje.
// #1474: confere só o dinheiro da gaveta (Pix e cartão não estão nela) e o campo nasce vazio: o
// botão só libera depois que ela digitar o que contou.
function FecharEsquecido({ situacao, aoFechou }) {
  const { agora } = useAtendimento()
  const { fecharCaixa } = useAcoes()
  const [contado, setContado] = useState(null)
  const [envio, enviar] = useEnvio()
  const conferencia = conferenciaDaGaveta(contado, situacao.naGaveta)

  return (
    <div className={css.bloco}>
      <p className={css.alerta}>
        O caixa de <strong>{diaMes(situacao.desde)}</strong> ficou aberto. Feche aquele dia antes de abrir o de hoje.
      </p>
      <dl className={css.numeros}>
        <div><dt>Na gaveta (dinheiro)</dt><dd>{moeda(situacao.naGaveta)}</dd></div>
      </dl>
      <CampoReais
        rotulo="Quanto você contou na gaveta (só dinheiro)"
        dica="Conte o dinheiro. A diferença fica registrada no fechamento."
        placeholder="Digite o que contou"
        centavos={contado}
        ler={lerMoeda}
        erro={moedaAltaDemais(contado) ? 'Valor alto demais' : null}
        aoMudar={setContado}
      />
      {conferencia.pronta && (
        <p className={css.apoio}>Diferença: <Pilula tom={conferencia.tom}>{conferencia.texto}</Pilula></p>
      )}
      <Erro texto={envio.erro} />
      <Botao
        variante="secundario"
        icone="log-out"
        disabled={envio.enviando || !conferencia.pronta || moedaAltaDemais(contado)}
        onClick={async () => {
          const ok = await enviar(() => fecharCaixa(agora, contado / 100, situacao.naGaveta, 'Fechado ao abrir a loja.'))
          if (ok) aoFechou()
        }}
      >
        Fechar o caixa de {diaMes(situacao.desde)}
      </Botao>
    </div>
  )
}

function UltimoFechamento({ ultimo }) {
  if (!ultimo) return <p className={css.apoio}>Nenhum fechamento anterior. Este é o primeiro caixa.</p>
  return (
    <div className={css.bloco}>
      <dl className={css.numeros}>
        {/* #1920: o que ficou na gaveta, o mesmo valor que o saldo inicial sugere; `saldoFinal` soma Pix e cartão. */}
        <div><dt>Na gaveta em {diaMes(ultimo.data)}</dt><dd>{moeda(gavetaDoFechamento(ultimo))}</dd></div>
        <div><dt>Entrou</dt><dd>{moeda(ultimo.totalPagamentosPedidos + ultimo.totalEntradasExtras)}</dd></div>
        <div><dt>Saiu</dt><dd>{moeda(ultimo.totalSaidasExtras)}</dd></div>
      </dl>
      <p className={css.apoio}>
        Fechado {ultimo.fechadoPorNome ? `por ${ultimo.fechadoPorNome} ` : ''}às {horaCurta(ultimo.fechadoEm)}.
      </p>
      {ultimo.observacoes && <p className={css.nota}>{ultimo.observacoes}</p>}
    </div>
  )
}

export function ModalAbrirLoja({ aoFechar }) {
  const { abrirLojaComCaixa } = useAcoes()
  const caixa = useCaixaDoDiaApi({ fechamentos: 1 })
  const [centavos, setCentavos] = useState(null)
  const [motivo, setMotivo] = useState('')
  const [envio, enviar] = useEnvio()

  const situacao = caixa.estado === 'ok' ? situacaoDoCaixaParaAbrirLoja(caixa.dia, caixa.fechamentos[0] ?? null) : null
  const saldoCentavos = centavos ?? Math.round((situacao?.saldoSugerido ?? 0) * 100)
  const saldoInicial = saldoCentavos / 100
  const retificacao = retificacaoDoSaldo(saldoInicial, situacao)
  const motivoFalta = retificacao.exigeMotivo && motivo.trim().length < MOTIVO_MINIMO
  // J2 (#1990): valor muito longe da sugestão pede um segundo toque; mudar o valor desfaz o primeiro.
  const [confirmando, setConfirmando] = useState(false)
  const pedeConfirmacao = aberturaPedeConfirmacao(saldoInicial, situacao)
  const mudarSaldo = (valor) => { setCentavos(valor); setConfirmando(false) }

  const abrir = async () => {
    if (pedeConfirmacao && !confirmando) { setConfirmando(true); return }
    const caixaJaAberto = situacao.tipo === 'aberto'
    const observacoes = caixaJaAberto ? undefined : observacaoDaAbertura({ saldoInformado: saldoInicial, situacao, motivo })
    const ok = await enviar(() => abrirLojaComCaixa({ saldoInicial, observacoes, caixaJaAberto }))
    // Caixa aberto e loja recusada: relê para o botão não tentar abrir o caixa de novo.
    if (!ok) caixa.recarregar()
  }

  const podeAbrir = situacao?.tipo === 'aberto' || situacao?.tipo === 'novo'
  const rotuloAbrir = situacao?.tipo === 'aberto' ? 'Abrir a loja' : 'Abrir caixa e loja'

  return (
    <Modal
      titulo="Abrir a loja"
      descricao="Abrir a loja é abrir o caixa. Confira a gaveta antes."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Cancelar</Botao>
          {podeAbrir && (
            <Botao
              variante="primario"
              icone="dollar-sign"
              disabled={envio.enviando || moedaAltaDemais(saldoCentavos) || motivoFalta}
              onClick={abrir}
            >
              {envio.enviando ? 'Abrindo…' : confirmando && pedeConfirmacao ? `Sim, abrir com ${moeda(saldoInicial)}` : rotuloAbrir}
            </Botao>
          )}
        </>
      )}
    >
      <div className={css.corpo}>
        {caixa.estado === 'carregando' && <p className={css.apoio}>Conferindo o caixa…</p>}
        {caixa.estado === 'erro' && (
          <div className={css.bloco}>
            <Erro texto={`O caixa não carregou: ${caixa.erro}`} />
            <Botao variante="texto" onClick={caixa.recarregar}>Tentar de novo</Botao>
          </div>
        )}

        {situacao?.tipo === 'esquecido' && <FecharEsquecido situacao={situacao} aoFechou={caixa.recarregar} />}

        {situacao?.tipo === 'fechadoHoje' && (
          <p className={css.alerta}>
            O caixa de hoje já foi fechado
            {situacao.fechamento?.fechadoEm ? ` às ${horaCurta(situacao.fechamento.fechadoEm)}` : ''} e não reabre.
            A loja só abre com o caixa aberto.
          </p>
        )}

        {situacao?.tipo === 'aberto' && (
          <dl className={css.numeros}>
            <div><dt>Caixa de hoje aberto com</dt><dd>{moeda(situacao.saldoInicial)}</dd></div>
            <div><dt>Na gaveta (dinheiro) agora</dt><dd>{moeda(situacao.naGaveta)}</dd></div>
          </dl>
        )}

        {situacao?.tipo === 'novo' && (
          <>
            <UltimoFechamento ultimo={situacao.ultimo} />
            <CampoDinheiro
              rotulo="Saldo inicial (o que está na gaveta agora)"
              dica={situacao.ultimo ? `Já vem com o saldo do fechamento de ${diaMes(situacao.ultimo.data)}.` : 'O dinheiro de troco que fica na gaveta.'}
              centavos={saldoCentavos}
              aoMudar={mudarSaldo}
            />
            {confirmando && pedeConfirmacao && (
              <p className={css.alerta} role="alert">{textoDaConfirmacaoDaAbertura(saldoInicial, situacao)}</p>
            )}
            {retificacao.exigeMotivo && (
              <>
                <p className={css.alerta}>
                  Diferença de <strong>{moeda(retificacao.diferenca)}</strong> contra o fechamento de {diaMes(situacao.ultimo.data)}.
                </p>
                <CampoArea
                  rotulo="Motivo da retificação"
                  dica="Obrigatório. Fica gravado na abertura do caixa."
                  rows={2}
                  value={motivo}
                  onChange={(e) => setMotivo(e.target.value)}
                />
              </>
            )}
          </>
        )}

        <Erro texto={envio.erro} />
      </div>
    </Modal>
  )
}

export function ModalFecharLoja({ aoFechar }) {
  const { fecharLojaComJustificativa } = useAcoes()
  const [justificativa, setJustificativa] = useState('')
  const [envio, enviar] = useEnvio()

  return (
    <Modal
      titulo="Fechar a loja no horário"
      descricao="A loja está dentro do horário de funcionamento. Só gerente ou dona fecha agora, com justificativa."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="texto" onClick={aoFechar}>Manter aberta</Botao>
          <Botao
            variante="primario"
            icone="lock"
            disabled={envio.enviando || !justificativaValida(justificativa)}
            onClick={() => enviar(() => fecharLojaComJustificativa(justificativa))}
          >
            {envio.enviando ? 'Fechando…' : 'Fechar a loja'}
          </Botao>
        </>
      )}
    >
      <div className={css.corpo}>
        <CampoArea
          rotulo="Justificativa"
          dica={`Pelo menos ${JUSTIFICATIVA_MINIMA} letras. Fica registrada com seu usuário e a hora, e os donos são avisados.`}
          rows={3}
          value={justificativa}
          onChange={(e) => setJustificativa(e.target.value)}
        />
        <p className={css.apoio}>O caixa continua aberto. Para fechar o dia, use Financeiro › Caixa do dia.</p>
        <Erro texto={envio.erro} />
      </div>
    </Modal>
  )
}

// Montado no topo da tela: abre o modal do gesto que `alternarLoja` pediu.
export function GestoLoja() {
  const { gestoLoja } = useAtendimento()
  const { fecharGestoLoja } = useAcoes()
  if (gestoLoja === 'abrir') return <ModalAbrirLoja aoFechar={fecharGestoLoja} />
  if (gestoLoja === 'fechar') return <ModalFecharLoja aoFechar={fecharGestoLoja} />
  return null
}
