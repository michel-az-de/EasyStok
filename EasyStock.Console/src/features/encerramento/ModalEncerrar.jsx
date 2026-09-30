import { useEffect, useMemo, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { FolhaImpressao } from '../../componentes/FolhaImpressao'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { canalDaConversa } from '../../dominio/canal'
import { conversaEncerrada } from '../../dominio/conversa'
import { pedidoEncerrado } from '../../dominio/pedido'
import { montarResumoAtendimento } from '../../dominio/resumoAtendimento'
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
  const { conversas, agora, regras } = useAtendimento()
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
  useEffect(() => {
    setEnviarMensagem(Boolean(regraEncerramento))
    setTextoMensagem(regraEncerramento ? textoDaRegra(regraEncerramento, contextoDePrevia(conversa, null, agora)) : '')
    // eslint-disable-next-line react-hooks/exhaustive-deps -- só reseta ao trocar de conversa, não a cada tecla
  }, [conversaId])

  // eslint-disable-next-line react-hooks/exhaustive-deps -- resumo depende do conteudo de `conversa`, nao da referencia
  const resumo = useMemo(() => {
    if (!conversa) return null
    if (leitura) return (conversa.atendimentos ?? []).find((a) => a.numero === numeroAtendimento) ?? null
    if (encerrada) return conversa.atendimentos.at(-1) ?? null
    return montarResumoAtendimento(conversa, cardapio, agora, rascunho)
  }, [conversa, leitura, numeroAtendimento, encerrada, cardapio, agora, rascunho])

  if (!conversaId || !conversa || !resumo) return null

  const pedido = conversa.pedido
  const pedidoAtivo = Boolean(pedido) && !pedidoEncerrado(pedido)
  const aguardandoPagamento = pedidoAtivo && pedido.estado === 'aguardando'
  const canal = canalDaConversa(canais, conversa)

  // Faixa da seção 5: aguardando pagamento cancela e encerra (regra da
  // rodada 4, reaproveitada); pago e na esteira só fecha a conversa, o pedido
  // segue em Entregas por conta própria.
  // Canais de aviso (decisão 46): ligados no cadastro (BlocoCliente.jsx),
  // aqui só leitura — a escolha por atendimento é só a da mensagem.
  const avisoEmailLigado = Boolean(conversa.cliente?.avisos?.email)
  const avisoSmsLigado = Boolean(conversa.cliente?.avisos?.sms)

  const clicarEncerrar = () => {
    if (aguardandoPagamento) {
      cancelarPedido(conversa.id, `Cancelei o pedido ${pedido.numero}. Qualquer coisa, é só chamar.`)
    }
    encerrarComResumo(conversa.id, agora, {
      enviarMensagem,
      textoMensagem,
      avisoEmail: avisoEmailLigado,
      avisoSms: avisoSmsLigado,
    })
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
            <Botao variante="texto" onClick={aoFechar}>Voltar</Botao>
            <Botao variante="secundario" icone="printer" onClick={() => window.print()}>
              Exportar PDF
            </Botao>
            {!encerrada && (
              <Botao variante="primario" icone="log-out" onClick={clicarEncerrar}>Encerrar</Botao>
            )}
          </>
        )}
      >
        <div className={css.canhotoTela}>
          <div className={css.corpoModal}>
            {!encerrada && pedidoAtivo && !leitura && (
              <p className={css.faixaAtencao}>
                {aguardandoPagamento
                  ? `O pedido ${pedido.numero} sem pagamento será cancelado.`
                  : `O pedido ${pedido.numero} segue na esteira até a entrega.`}
              </p>
            )}
            {!encerrada && !leitura && (
              <div className={css.confirmacaoEncerrar}>
                <label>
                  <input
                    type="checkbox"
                    checked={enviarMensagem}
                    onChange={(e) => setEnviarMensagem(e.target.checked)}
                  />
                  Mandar mensagem de encerramento ao cliente
                </label>
                {enviarMensagem && (
                  <CampoArea
                    rotulo="Mensagem de encerramento"
                    rotuloOculto
                    value={textoMensagem}
                    onChange={(e) => setTextoMensagem(e.target.value)}
                  />
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
              aoMarcarAvaliacaoCliente={(valor) => salvarAvaliacaoCliente(conversa.id, valor)}
              aoMarcarAutoavaliacao={(valor) => salvarAutoavaliacao(conversa.id, valor)}
              aoMudarAnotacao={(texto) => salvarAnotacaoFechamento(conversa.id, { texto })}
              aoMudarGuardarNota={(guardarNota) => salvarAnotacaoFechamento(conversa.id, { guardarNota })}
            />
          </div>
        </div>
      </Modal>
      <FolhaImpressao>
        <FolhaResumo {...propsDaFolha} editavel={false} impresso />
      </FolhaImpressao>
    </>
  )
}
