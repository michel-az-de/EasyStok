import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoSelecao } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import css from './ficha.module.css'

// Bloquear é irreversível na cabeça de quem clica, então passa por duas etapas:
// primeiro o motivo, depois a consequência escrita por extenso. Cancelar fica
// separado da ação, nunca colado nela.

const OUTRO = 'Outro motivo'

export function ModalBloqueio({ nome, canais, quantasConversas, motivos, aoFechar, aoConfirmar }) {
  const [etapa, setEtapa] = useState('motivo')
  const [escolhido, setEscolhido] = useState('')
  const [detalhe, setDetalhe] = useState('')

  const exigeDetalhe = escolhido === OUTRO
  const motivoFinal = [escolhido === OUTRO ? '' : escolhido, detalhe.trim()]
    .filter(Boolean).join(' ')
  const completo = escolhido !== '' && (!exigeDetalhe || detalhe.trim().length >= 3)

  const opcoes = [
    { valor: '', rotulo: 'Escolha o motivo' },
    ...motivos.map((m) => ({ valor: m, rotulo: m })),
  ]

  if (etapa === 'motivo') {
    return (
      <Modal
        titulo={`Bloquear ${nome}`}
        descricao="O motivo é obrigatório e fica gravado na ficha."
        aoFechar={aoFechar}
        rodape={(
          <>
            <Botao onClick={aoFechar}>Cancelar</Botao>
            <Botao variante="primario" disabled={!completo} onClick={() => setEtapa('confirmar')}>
              Continuar
            </Botao>
          </>
        )}
      >
        <div className={css.formBloqueio}>
          <CampoSelecao
            rotulo="Motivo do bloqueio"
            opcoes={opcoes}
            value={escolhido}
            onChange={(e) => setEscolhido(e.target.value)}
          />
          <CampoArea
            rotulo={exigeDetalhe ? 'Escreva o motivo' : 'Detalhe (opcional)'}
            dica={exigeDetalhe
              ? 'Pelo menos três letras: você vai ler isso daqui a meses.'
              : 'O que aconteceu, com data e pedido se você lembrar.'}
            rows={3}
            value={detalhe}
            onChange={(e) => setDetalhe(e.target.value)}
          />
        </div>
      </Modal>
    )
  }

  return (
    <Modal
      titulo={`Bloquear ${nome} em todos os canais?`}
      descricao="Confira antes de confirmar. Dá para desbloquear depois, na mesma ficha."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={() => setEtapa('motivo')}>Voltar</Botao>
          <Botao variante="primario" onClick={() => aoConfirmar(motivoFinal)}>
            Bloquear cliente
          </Botao>
        </>
      )}
    >
      <p className={css.motivoEscolhido}>{motivoFinal}</p>
      <ul className={css.consequencias}>
        <li>Vale em {canais}: o bloqueio é do cadastro, não da conversa.</li>
        <li>{quantasConversas === 1 ? '1 conversa deste cadastro fica' : `${quantasConversas} conversas deste cadastro ficam`} com o envio travado.</li>
        <li>O atendimento automático desliga e não volta sozinho.</li>
        <li>O cliente não recebe aviso nenhum do bloqueio.</li>
      </ul>
    </Modal>
  )
}

export function ModalDesbloqueio({ nome, aoFechar, aoConfirmar }) {
  return (
    <Modal
      titulo={`Desbloquear ${nome}?`}
      descricao="O envio volta a funcionar em todos os canais deste cadastro."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" onClick={aoConfirmar}>Desbloquear</Botao>
        </>
      )}
    >
      <ul className={css.consequencias}>
        <li>O motivo do bloqueio continua gravado na ficha, como histórico.</li>
        <li>O atendimento automático segue desligado até você retomar.</li>
      </ul>
    </Modal>
  )
}
