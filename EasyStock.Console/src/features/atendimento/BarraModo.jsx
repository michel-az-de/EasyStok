import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { MODOS, modoDoAtendimento, proximoPassoDepoisDeResponder } from '../../dominio/automatico'
import { primeiroNome } from '../../dominio/mensagem'
import css from './atendimento.module.css'

// Quem responde esta conversa. Um lugar só, no topo da thread, com os três
// estados e os dois toques: Assumir e Devolver ao automático (RN-04, D4).
// O log do automático saiu daqui (corte #39): cada mensagem automática já vem
// marcada como tal na própria conversa, contar de novo era ruído.
// Cadastro bloqueado não oferece toque nenhum: a conversa só volta a andar
// pelo desbloqueio na ficha.
//
// Rodada 12 (issue #16, dúvidas da Thatiane): "Devolver ao automático" agora
// explica o efeito e pede confirmação, e depois que ela responde a barra
// sugere o próximo passo (encerrar x aguardar o cliente).
export function BarraModo({
  conversa, pausado, bloqueada = false, aoAssumir, aoDevolver, aoEncerrar,
}) {
  const [confirmandoDevolver, setConfirmandoDevolver] = useState(false)
  const modo = modoDoAtendimento(conversa, pausado, bloqueada)
  const encerrada = modo.chave === MODOS.ENCERRADO
  // Assumir só faz sentido em conversa que ainda está no automático ou que
  // acabou de ser devolvida. Devolver só onde o automático está parado.
  const podeAssumir = modo.chave === MODOS.LIGADO || modo.chave === MODOS.COM_VOCE
  const podeDevolver = modo.chave === MODOS.PAUSADO || modo.chave === MODOS.COM_VOCE
  const proximo = !bloqueada && modo.chave === MODOS.PAUSADO
    ? proximoPassoDepoisDeResponder(conversa, pausado)
    : null
  const sugereEncerrar = proximo?.chave === 'encerrar-ou-aguardar' && Boolean(aoEncerrar)
  const nome = primeiroNome(conversa.nome)

  const devolver = () => {
    setConfirmandoDevolver(false)
    aoDevolver()
  }

  return (
    <section className={`${css.modo} ${css['modo_' + modo.chave]}`} aria-label="Quem responde esta conversa">
      <div className={css.modoLinha}>
        <Pilula tom={modo.tom}>{modo.rotulo}</Pilula>
        {!encerrada && !bloqueada && (
          <span className={css.modoAcoes}>
            {podeAssumir && (
              <Botao variante="primario" className={css.acaoToque} onClick={aoAssumir}>
                Assumir
              </Botao>
            )}
            {sugereEncerrar && (
              <Botao variante="padrao" icone="log-out" className={css.acaoToque} onClick={aoEncerrar}>
                Encerrar conversa
              </Botao>
            )}
            {podeDevolver && (
              <Botao
                variante={podeAssumir ? 'padrao' : 'primario'}
                className={css.acaoToque}
                title="O atendimento automático volta a responder esta conversa"
                aria-haspopup="dialog"
                onClick={() => setConfirmandoDevolver(true)}
              >
                Devolver ao automático
              </Botao>
            )}
          </span>
        )}
      </div>

      {modo.detalhe && <p className={css.modoDetalhe}>{modo.detalhe}</p>}
      {proximo && <p className={css.modoProximo}>{proximo.texto}</p>}

      {confirmandoDevolver && (
        <Modal
          titulo="Devolver ao atendimento automático?"
          descricao={`O atendimento automático volta a responder as próximas mensagens de ${nome}, sem você.`}
          aoFechar={() => setConfirmandoDevolver(false)}
          rodape={(
            <>
              <Botao onClick={() => setConfirmandoDevolver(false)}>Continuar comigo</Botao>
              <Botao variante="primario" onClick={devolver}>Devolver ao automático</Botao>
            </>
          )}
        >
          <ul className={css.listaEfeito}>
            <li>Ele responde sozinho dúvida de cardápio, horário, entrega e pagamento.</li>
            <li>Restrição alimentar, reclamação e prato sem saldo continuam vindo para você.</li>
            <li>Você pode assumir de novo a qualquer momento, pelo botão Assumir.</li>
          </ul>
        </Modal>
      )}
    </section>
  )
}
