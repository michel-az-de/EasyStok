import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { FolhaImpressao } from '../../componentes/FolhaImpressao'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcaoDisponivel } from '../../aplicacao/useAcaoDisponivel'
import { cancelarNasceMarcado, falhaAoCancelarNoEncerramento, pedidoAoEncerrar, rotuloCancelarAoEncerrar } from '../../aplicacao/api/encerramentoEMidia'
import { canalDaConversa } from '../../dominio/canal'
import { conversaEncerrada } from '../../dominio/conversa'
import { pedidoEncerrado } from '../../dominio/pedido'
import {
  PEDE_TEXTO_DA_DESPEDIDA, avisoDoPedidoAoEncerrar, faltaTextoDaDespedida, montarResumoAtendimento,
} from '../../dominio/resumoAtendimento'
import { GATILHOS, contextoDePrevia, regraDoGatilho, textoDaRegra } from '../../dominio/automacao'
import { FolhaResumo } from './FolhaResumo'
import css from './encerramento.module.css'

const RASCUNHO_PADRAO = { avaliacaoCliente: null, autoavaliacao: null, anotacao: '', guardarNota: true }

// Modal do resumo de encerramento (rodada 5, seção 5). Abre de três lugares
// (cabeçalho, menu do pedido, barra do Pedido em Entregue/Cancelado), todos já
// ligados no passo zero por `ui.encerrando` — este arquivo só constrói o
// CONTEÚDO que a decisão 31 (item 7) cobrou: "hoje o botão Encerrar abre modal
// vazia, é casca".
//
// A mesma `FolhaResumo` serve três situações: prévia ao vivo (antes de
// clicar Encerrar, os números seguem o relógio da tela), resumo definitivo
// (depois de clicar, lido de `conversa.atendimentos.at(-1)`, já congelado) e a
// cópia impressa (sempre somente leitura, com só a opção marcada, seção 5).
export function ModalEncerrar({ conversaId, numeroAtendimento = null, aoFechar }) {
  const { conversas, agora, regras, fonteApi } = useAtendimento()
  const { canais, cardapio } = useCatalogo()
  const {
    salvarAvaliacaoCliente, salvarAutoavaliacao, salvarAnotacaoFechamento,
    encerrarComResumo, cancelarPedido,
  } = useAcoes()

  const conversa = conversas.find((c) => c.id === conversaId) ?? null
  // Leitura de um resumo já fechado (ponta d, aba Atendimentos do Histórico):
  // mesma folha, sem Encerrar e sem nada editável.
  const leitura = numeroAtendimento != null
  const encerrada = leitura || (conversa ? conversaEncerrada(conversa) : false)
  const rascunho = conversa?.fechamentoRascunho ?? RASCUNHO_PADRAO

  // Confirmação de encerrar (decisão 46, "Encerrar" v2): escolha de mandar a
  // mensagem de despedida, com o texto pronto da regra de automação
  // (gatilho ENCERRAMENTO) já editável na hora. Reseta ao trocar de conversa,
  // mesmo padrão do formulário de cadastro rápido em BlocoCliente.jsx.
  const regraEncerramento = regraDoGatilho(regras, GATILHOS.ENCERRAMENTO)
  const [enviarMensagem, setEnviarMensagem] = useState(Boolean(regraEncerramento))
  const [textoMensagem, setTextoMensagem] = useState(() => (regraEncerramento
    ? textoDaRegra(regraEncerramento, contextoDePrevia(conversa, null, agora))
    : ''))
  // #1683: cancelar o pedido sem pagamento é escolha de quem atende. Nasce desmarcada, menos
  // quando o pedido não tem link em aberto e ficaria sem ninguém para fechá-lo.
  const [cancelarMarcado, setCancelarMarcado] = useState(() => cancelarNasceMarcado(conversa?.pedido, { fonteApi }))
  const pedidoJaCancelado = useRef(false)
  useEffect(() => {
    setCancelarMarcado(cancelarNasceMarcado(conversa?.pedido, { fonteApi }))
    pedidoJaCancelado.current = false
    setEnviarMensagem(Boolean(regraEncerramento))
    setTextoMensagem(regraEncerramento ? textoDaRegra(regraEncerramento, contextoDePrevia(conversa, null, agora)) : '')
    // eslint-disable-next-line react-hooks/exhaustive-deps -- só reseta ao trocar de conversa, não a cada tecla
  }, [conversaId])

  // eslint-disable-next-line react-hooks/exhaustive-deps -- resumo depende do conteudo de `conversa`, nao da referencia
  const resumo = useMemo(() => {
    if (!conversa) return null
    if (leitura) return (conversa.atendimentos ?? []).find((a) => a.numero === numeroAtendimento) ?? null
    // #1586: conversa da API não guarda `atendimentos`; encerrada por fora, não há resumo a ler.
    if (encerrada) return conversa.atendimentos?.at(-1) ?? null
    return montarResumoAtendimento(conversa, cardapio, agora, rascunho)
  }, [conversa, leitura, numeroAtendimento, encerrada, cardapio, agora, rascunho])

  // #1586: no modo API avaliação e anotação não gravam (só avisariam na faixa): não aparecem.
  const disponivel = useAcaoDisponivel()
  const guardaAvaliacaoEAnotacao = ['salvarAvaliacaoCliente', 'salvarAutoavaliacao', 'salvarAnotacaoFechamento'].every(disponivel)
  // #1586: Encerrar trava no primeiro toque e espera a resposta; o erro aparece aqui dentro,
  // porque a faixa do rodapé fica atrás da cortina.
  const emVoo = useRef(false)
  const despedidaJaSaiu = useRef(false)
  const [encerrandoAgora, setEncerrandoAgora] = useState(false)
  const [erroAoEncerrar, setErroAoEncerrar] = useState(null)
  // #1761: só cobra o texto depois do toque em Encerrar; o aviso fica junto do campo.
  const [cobrouDespedida, setCobrouDespedida] = useState(false)
  const campoDespedida = useRef(null)

  if (!conversaId || !conversa || !resumo) return null

  const pedido = conversa.pedido
  const pedidoAtivo = Boolean(pedido) && !pedidoEncerrado(pedido)
  const noPedido = pedidoAoEncerrar(pedidoAtivo ? pedido : null, { fonteApi, cancelarMarcado })
  const canal = canalDaConversa(canais, conversa)

  // Faixa da seção 5: pago e na esteira só fecha a conversa, o pedido segue em
  // Entregas por conta própria. #1683: aguardando pagamento só é cancelado se a
  // atendente marcar a caixa (antes a regra da rodada 4 cancelava sem perguntar).
  // Canais de aviso (decisão 46): ligados no cadastro (BlocoCliente.jsx),
  // aqui só leitura — a escolha por atendimento é só a da mensagem.
  const avisoEmailLigado = Boolean(conversa.cliente?.avisos?.email)
  const avisoSmsLigado = Boolean(conversa.cliente?.avisos?.sms)

  const semDespedida = faltaTextoDaDespedida(enviarMensagem && !despedidaJaSaiu.current, textoMensagem)

  const clicarEncerrar = async () => {
    if (emVoo.current) return
    if (semDespedida) {
      setCobrouDespedida(true)
      campoDespedida.current?.querySelector('textarea')?.focus()
      return
    }
    emVoo.current = true
    setEncerrandoAgora(true)
    setErroAoEncerrar(null)
    try {
      // Na demonstração as duas ações são síncronas e não devolvem nada: segue como sucesso.
      // #1683: cancelamento que falha não encerra; o erro fica aqui dentro.
      if (noPedido.cancelar && !pedidoJaCancelado.current) {
        const cancelamento = await cancelarPedido(conversa.id, `Cancelei o pedido ${pedido.numero}. Qualquer coisa, é só chamar.`)
        const falha = falhaAoCancelarNoEncerramento(cancelamento, pedido.numero)
        if (falha) { setErroAoEncerrar(falha); return }
        pedidoJaCancelado.current = true
      }
      const resultado = await encerrarComResumo(conversa.id, agora, {
        enviarMensagem: enviarMensagem && !despedidaJaSaiu.current,
        textoMensagem,
        avisoEmail: avisoEmailLigado,
        avisoSms: avisoSmsLigado,
        pedidoCancelado: pedidoJaCancelado.current,
      })
      if (resultado?.despedidaSaiu) despedidaJaSaiu.current = true
      if (resultado?.erro) {
        setErroAoEncerrar(despedidaJaSaiu.current
          ? `${resultado.erro} A despedida já foi enviada: tentar de novo não manda outra.`
          : resultado.erro)
      }
    } catch (erro) {
      setErroAoEncerrar(erro.message || 'Não foi possível encerrar. Tente de novo.')
    } finally {
      emVoo.current = false
      setEncerrandoAgora(false)
    }
  }

  const propsDaFolha = {
    nomeCliente: conversa.nome,
    canalNome: canal.nome,
    telefone: conversa.cliente?.telefone,
    resumo,
  }

  return (
    <>
      <Modal
        titulo={leitura ? `Resumo do atendimento ${resumo.numero}` : 'Encerrar atendimento'}
        aoFechar={aoFechar}
        rodape={(
          <>
            {erroAoEncerrar && <p className={css.erroAoEncerrar} role="alert">{erroAoEncerrar}</p>}
            <Botao variante="texto" onClick={aoFechar}>Voltar</Botao>
            <Botao variante="secundario" icone="printer" onClick={() => window.print()}>
              Exportar PDF
            </Botao>
            {!encerrada && (
              <Botao variante="primario" icone="log-out" disabled={encerrandoAgora} onClick={clicarEncerrar}>
                {encerrandoAgora ? 'Encerrando…' : 'Encerrar'}
              </Botao>
            )}
          </>
        )}
      >
        <div className={css.canhotoTela}>
          <div className={css.corpoModal}>
            {!encerrada && pedidoAtivo && !leitura && !noPedido.cancelar && (
              <p className={css.faixaAtencao}>
                {avisoDoPedidoAoEncerrar(conversa, { fonteApi })}
              </p>
            )}
            {!encerrada && !leitura && (
              <div className={css.confirmacaoEncerrar}>
                {noPedido.ofereceCancelar && (
                  <label>
                    <input
                      type="checkbox"
                      checked={cancelarMarcado}
                      disabled={encerrandoAgora}
                      onChange={(e) => setCancelarMarcado(e.target.checked)}
                    />
                    {rotuloCancelarAoEncerrar(pedido.numero)}
                  </label>
                )}
                <label>
                  <input
                    type="checkbox"
                    checked={enviarMensagem}
                    onChange={(e) => setEnviarMensagem(e.target.checked)}
                  />
                  Mandar mensagem de encerramento ao cliente
                </label>
                {enviarMensagem && (
                  <div ref={campoDespedida}>
                    <CampoArea
                      rotulo="Mensagem de encerramento"
                      rotuloOculto
                      placeholder="Ex.: Obrigada pela preferência! Qualquer coisa, é só chamar."
                      aria-invalid={cobrouDespedida && semDespedida ? true : undefined}
                      value={textoMensagem}
                      onChange={(e) => setTextoMensagem(e.target.value)}
                    />
                    {cobrouDespedida && semDespedida && (
                      <p className={css.erroAoEncerrar} role="alert">{PEDE_TEXTO_DA_DESPEDIDA}</p>
                    )}
                  </div>
                )}
                {(avisoEmailLigado || avisoSmsLigado) && (
                  <p className={css.avisosConfigurados}>
                    Também avisa por {[avisoEmailLigado && 'e-mail', avisoSmsLigado && 'SMS'].filter(Boolean).join(' e ')},
                    conforme o cadastro.
                  </p>
                )}
              </div>
            )}
            <FolhaResumo
              {...propsDaFolha}
              editavel={!encerrada}
              comAvaliacaoEAnotacao={guardaAvaliacaoEAnotacao}
              aoMarcarAvaliacaoCliente={(valor) => salvarAvaliacaoCliente(conversa.id, valor)}
              aoMarcarAutoavaliacao={(valor) => salvarAutoavaliacao(conversa.id, valor)}
              aoMudarAnotacao={(texto) => salvarAnotacaoFechamento(conversa.id, { texto })}
              aoMudarGuardarNota={(guardarNota) => salvarAnotacaoFechamento(conversa.id, { guardarNota })}
            />
          </div>
        </div>
      </Modal>
      <FolhaImpressao>
        <FolhaResumo {...propsDaFolha} editavel={false} impresso comAvaliacaoEAnotacao={guardaAvaliacaoEAnotacao} />
      </FolhaImpressao>
    </>
  )
}
