import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { primeiroNome } from '../../dominio/mensagem'
import css from './atendimento.module.css'

// Rodada 12 (issue #16, dúvidas da Thatiane): devolver explica o efeito e pede confirmação.
export function ModalDevolverAssistente({ conversa, aoConfirmar, aoFechar }) {
  return (
    <Modal
      titulo="Devolver ao Assistente?"
      descricao={`O Assistente volta a responder as próximas mensagens de ${primeiroNome(conversa.nome)}.`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Continuar comigo</Botao>
          <Botao variante="primario" onClick={() => { aoFechar(); aoConfirmar() }}>Devolver ao Assistente</Botao>
        </>
      )}
    >
      <ul className={css.listaEfeito}>
        <li>Ele responde sozinho dúvida de cardápio, horário, entrega e pagamento.</li>
        <li>Restrição alimentar, reclamação e prato sem saldo continuam vindo para você.</li>
        <li>Você pode assumir de novo a qualquer momento, pelo botão Assumir.</li>
      </ul>
    </Modal>
  )
}
