// Gesto da loja no modo API (#1443, decisão do Felipe em 07/10). Abrir a loja é abrir o caixa: a
// tela mostra como ficou o caixa de ontem, pede a conferência do saldo inicial (e o motivo, se ela
// retificar) e só abre a loja depois que o caixa abriu. Fechar dentro do horário pede justificativa,
// que a API grava na auditoria e avisa os donos. Quem pede o gesto é `alternarLoja`
// (`aplicacao/api/expediente.js`); fora do horário, fechar segue direto, sem este modal.
import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { CampoReais } from '../../componentes/CampoReais'
import { DiferencaDaGaveta } from '../../componentes/DiferencaDaGaveta'
import { Modal } from '../../componentes/Modal'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useCaixaDoDiaApi } from '../../aplicacao/useCaixaDoDiaApi'
import { useConfirmacaoDaAbertura } from '../../aplicacao/useConfirmacaoDaAbertura'
import {
  JUSTIFICATIVA_MINIMA, ROTULO_GAVETA_CONTADA_AGORA, avisoDaAberturaAConfirmar, caixaAoFecharLoja, diaMes, dicaDoSaldoInicial,
  gavetaDoFechamento, justificativaValida, observacaoDaAbertura, observacaoDaConfirmacao, retificacaoDoSaldo, situacaoDoCaixaParaAbrirLoja,
} from '../../dominio/aberturaDaLoja'
import {
  ROTULO_CONTADO_DO_ESQUECIDO, ROTULO_ENTROU_DEPOIS, conferenciaDaGaveta, conferenciaDoFechamento,
} from '../../dominio/caixa'
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
  // #2113 (D1 da #2084): o esperado do dia da abertura e, à parte, o que entrou depois e fica no caixa de hoje.
  const { esquecido } = situacao
  const conferencia = conferenciaDoFechamento(contado, situacao.naGaveta, esquecido)

  return (
    <div className={css.bloco}>
      <p className={css.alerta}>
        O caixa de <strong>{diaMes(situacao.desde)}</strong> ficou aberto. Feche aquele dia antes de abrir o de hoje.
      </p>
      <dl className={css.numeros}>
        {esquecido ? (
          <>
            <div><dt>Esperado no fechamento de {diaMes(situacao.desde)}</dt><dd>{moeda(esquecido.esperado)}</dd></div>
            <div><dt>{ROTULO_ENTROU_DEPOIS}</dt><dd>{moeda(esquecido.entrouDepois)}</dd></div>
          </>
        ) : <div><dt>Na gaveta (dinheiro)</dt><dd>{moeda(situacao.naGaveta)}</dd></div>}
      </dl>
      <CampoReais
        rotulo={esquecido ? ROTULO_CONTADO_DO_ESQUECIDO : 'Quanto você contou na gaveta (só dinheiro)'}
        dica="Conte o dinheiro. A diferença fica registrada no fechamento."
        placeholder="Digite o que contou"
        centavos={contado}
        ler={lerMoeda}
        erro={moedaAltaDemais(contado) ? 'Valor alto demais' : null}
        aoMudar={setContado}
      />
      <DiferencaDaGaveta conferencia={conferencia} className={css.diferenca} />
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
        {/* #2078 (D5/B8): "Entrou" e "Saiu" do fechamento não fechavam com a gaveta (o Entrou levava
            Pix e suprimento e deixava as vendas de fora; o Saiu, a sangria) nem com a tabela dos 7 dias.
            O fechamento não traz a conta só do dinheiro: fica o número que ela confere. */}
        <div><dt>Na gaveta em {diaMes(ultimo.data)}</dt><dd>{moeda(gavetaDoFechamento(ultimo))}</dd></div>
      </dl>
      <p className={css.apoio}>
        Fechado {ultimo.fechadoPorNome ? `por ${ultimo.fechadoPorNome} ` : ''}às {horaCurta(ultimo.fechadoEm)}.
      </p>
      {ultimo.observacoes && <p className={css.nota}>{ultimo.observacoes}</p>}
    </div>
  )
}

// #2078 (revisão R1): simétrico ao Fechar. Sem `controlarLoja` o controle da loja responde 403 depois
// que o caixa já abriu (a política do caixa deixa a operadora abrir): nada sai e a tela diz quem abre.
export function ModalAbrirLoja({ aoFechar }) {
  const { acoes } = useAcessoModulos()
  if (acoes?.controlarLoja !== true) {
    return (
      <Modal
        titulo="Abrir a loja"
        descricao="Só gerente ou dona abre a loja: peça a uma delas."
        aoFechar={aoFechar}
        rodape={<Botao variante="primario" onClick={aoFechar}>Entendi</Botao>}
      >
        <p className={css.apoio}>Abrir a loja também abre o caixa do dia, com a conferência da gaveta.</p>
      </Modal>
    )
  }
  return <AbrirLojaComCaixa aoFechar={aoFechar} />
}

function AbrirLojaComCaixa({ aoFechar }) {
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
  const confirmacao = useConfirmacaoDaAbertura(saldoInicial, situacao)
  const mudarSaldo = (valor) => { setCentavos(valor); confirmacao.desfazer() }

  // #2113 (B1 da #2084): o caixa abriu sozinho; abrir a loja confirma a abertura com a gaveta contada agora.
  const aConfirmar = situacao?.tipo === 'aberto' && situacao.aConfirmar
  const conferenciaDaConfirmacao = conferenciaDaGaveta(centavos, situacao?.naGaveta)

  const abrir = async () => {
    if (confirmacao.segurar()) return
    if (aConfirmar) {
      const contado = centavos / 100
      const ok = await enviar(() => abrirLojaComCaixa({ saldoInicial: contado, observacoes: observacaoDaConfirmacao(contado, situacao.naGaveta), caixaJaAberto: false }))
      if (!ok) caixa.recarregar()
      return
    }
    const caixaJaAberto = situacao.tipo === 'aberto'
    const observacoes = caixaJaAberto ? undefined : observacaoDaAbertura({ saldoInformado: saldoInicial, situacao, motivo })
    const ok = await enviar(() => abrirLojaComCaixa({ saldoInicial, observacoes, caixaJaAberto }))
    // Caixa aberto e loja recusada: relê para o botão não tentar abrir o caixa de novo.
    if (!ok) caixa.recarregar()
  }

  const podeAbrir = situacao?.tipo === 'aberto' || situacao?.tipo === 'novo'
  const rotuloAbrir = aConfirmar ? 'Confirmar abertura e abrir a loja' : situacao?.tipo === 'aberto' ? 'Abrir a loja' : 'Abrir caixa e loja'

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
              disabled={envio.enviando || moedaAltaDemais(saldoCentavos) || motivoFalta || (aConfirmar && centavos == null)}
              onClick={abrir}
            >
              {envio.enviando ? 'Abrindo…' : confirmacao.rotulo(rotuloAbrir)}
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
            {/* #2078 (B6): o modal ficava sem caminho. Só a dona devolve a loja ao horário. */}
            {' '}Para a loja voltar a seguir o horário da semana, a dona usa Atendimento › Horários e mensagens › Voltar a seguir o horário.
          </p>
        )}

        {aConfirmar && (
          <div className={css.bloco}>
            <p className={css.alerta}>
              {avisoDaAberturaAConfirmar(situacao.saldoInicial, situacao.ultimo)} Conte a gaveta inteira para confirmar a abertura.
            </p>
            <dl className={css.numeros}>
              <div><dt>Na gaveta (dinheiro) agora</dt><dd>{moeda(situacao.naGaveta)}</dd></div>
            </dl>
            <CampoReais
              rotulo={ROTULO_GAVETA_CONTADA_AGORA}
              dica="O troco que veio do fechamento e o dinheiro que já entrou hoje."
              placeholder="Digite o que contou"
              centavos={centavos}
              ler={lerMoeda}
              erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null}
              aoMudar={setCentavos}
            />
            <DiferencaDaGaveta conferencia={conferenciaDaConfirmacao} className={css.diferenca} />
          </div>
        )}

        {situacao?.tipo === 'aberto' && !aConfirmar && (
          <dl className={css.numeros}>
            <div><dt>Caixa de hoje aberto com</dt><dd>{moeda(situacao.saldoInicial)}</dd></div>
            <div><dt>Na gaveta (dinheiro) agora</dt><dd>{moeda(situacao.naGaveta)}</dd></div>
          </dl>
        )}

        {situacao?.tipo === 'novo' && (
          <>
            <UltimoFechamento ultimo={situacao.ultimo} />
            {/* #2078 (D6/B18): "o que está na gaveta agora" contava duas vezes o dinheiro que já entrou hoje. */}
            <CampoDinheiro
              rotulo="Saldo inicial (o troco da gaveta)"
              dica={`${dicaDoSaldoInicial(caixa.dia)}${situacao.ultimo ? ` Já vem com o que o fechamento de ${diaMes(situacao.ultimo.data)} deixou.` : ''}`}
              centavos={saldoCentavos}
              aoMudar={mudarSaldo}
            />
            {confirmacao.aviso && <p className={css.alerta} role="alert">{confirmacao.aviso}</p>}
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

// #2078: o texto do caixa segue o caixa de verdade (B9: dizia "continua aberto" com ele fechado ou sem
// abrir). Quem não pode fechar a loja (B10: a operadora escrevia a justificativa e ouvia "sem
// permissão") vê o porquê e quem fecha, sem campo nem botão. A permissão continua a da API.
export function ModalFecharLoja({ aoFechar }) {
  const { fecharLojaComJustificativa } = useAcoes()
  const { acoes } = useAcessoModulos()
  const podeFechar = acoes?.controlarLoja === true
  const caixa = useCaixaDoDiaApi({ fechamentos: 1 })
  const [justificativa, setJustificativa] = useState('')
  const [envio, enviar] = useEnvio()

  return (
    <Modal
      titulo="Fechar a loja no horário"
      descricao={podeFechar
        ? 'A loja está dentro do horário de funcionamento. Fechar agora pede justificativa.'
        : 'A loja está dentro do horário de funcionamento. Só gerente ou dona fecha a loja agora: peça a uma delas.'}
      aoFechar={aoFechar}
      rodape={podeFechar ? (
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
      ) : <Botao variante="primario" onClick={aoFechar}>Entendi</Botao>}
    >
      <div className={css.corpo}>
        {podeFechar && (
          <CampoArea
            rotulo="Justificativa"
            dica={`Pelo menos ${JUSTIFICATIVA_MINIMA} letras. Fica registrada com seu usuário e a hora, e os donos são avisados.`}
            rows={3}
            value={justificativa}
            onChange={(e) => setJustificativa(e.target.value)}
          />
        )}
        <p className={css.apoio}>{caixaAoFecharLoja(caixa.estado === 'ok' ? caixa.dia : null)}</p>
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
