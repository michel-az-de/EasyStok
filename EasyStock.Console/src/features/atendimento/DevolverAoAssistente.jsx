import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { acoesDoModo } from '../../dominio/acoesDoModo'
import { primeiroNomeDaConversa } from '../../dominio/nomeDoContato'
import css from './atendimento.module.css'

// #2232 (A4): "Devolver ao Assistente" saiu da faixa "quem responde" e mora no ⋮ do cabeçalho, junto de
// Passar e Concluir: um menu só. Rodada 12 (issue #16): explica o efeito e pede confirmação antes de devolver.
export function DevolverAoAssistente({ conversa, pausado = false, bloqueada = false, aoDevolver }) {
  const [aberto, setAberto] = useState(false)
  if (!acoesDoModo(conversa, pausado, bloqueada).podeDevolver) return null

  const devolver = () => {
    setAberto(false)
    aoDevolver()
  }

  return (
    <>
      <Botao variante="texto" className={css.acaoModo} aria-haspopup="dialog"
        title="O Assistente volta a responder esta conversa" onClick={() => setAberto(true)}>
        Devolver ao Assistente
      </Botao>
      {aberto && (
        <Modal
          titulo="Devolver ao Assistente?"
          descricao={`O Assistente volta a responder as próximas mensagens de ${primeiroNomeDaConversa(conversa)}.`}
          aoFechar={() => setAberto(false)}
          rodape={(
            <>
              <Botao onClick={() => setAberto(false)}>Continuar comigo</Botao>
              <Botao variante="primario" onClick={devolver}>Devolver ao Assistente</Botao>
            </>
          )}
        >
          <ul className={css.listaEfeito}>
            <li>Ele responde sozinho dúvida de cardápio, horário, entrega e pagamento.</li>
            <li>Restrição alimentar, reclamação e prato sem saldo continuam vindo para você.</li>
            <li>Você pode assumir de novo a qualquer momento, pelo botão Assumir conversa.</li>
          </ul>
        </Modal>
      )}
    </>
  )
}
