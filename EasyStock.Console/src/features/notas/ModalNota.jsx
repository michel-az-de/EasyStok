import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'

export function ModalNota({ nomeCliente, aoSalvar, aoFechar }) {
  const [texto, setTexto] = useState('')
  const vazio = texto.trim().length === 0

  return (
    <Modal
      titulo="Nota interna"
      descricao={`Fica na ficha de ${nomeCliente}. O cliente nunca vê.`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" disabled={vazio} onClick={() => aoSalvar(texto.trim())}>
            Salvar nota
          </Botao>
        </>
      )}
    >
      <CampoArea
        rotulo="Texto da nota"
        rotuloOculto
        value={texto}
        onChange={(e) => setTexto(e.target.value)}
        placeholder="Ex.: prefere a massa mais al dente, reduzir 1 min de cozimento."
      />
    </Modal>
  )
}
