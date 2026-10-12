import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import css from './ModalNota.module.css'

export function ModalNota({ nomeCliente, aoSalvar, aoFechar }) {
  const [texto, setTexto] = useState('')
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState(null)
  const salvando = useRef(false)
  const campo = useRef(null)
  const vazio = texto.trim().length === 0

  async function salvar() {
    if (vazio || salvando.current) return
    salvando.current = true
    campo.current?.focus()
    setOcupado(true)
    setErro(null)
    try {
      if (await aoSalvar(texto.trim()) === false) {
        setErro('Sem confirmação da nota. Seu texto continua aqui. Confira a ficha antes de tentar de novo.')
      } else aoFechar()
    } catch (falha) {
      setErro(falha.message || 'Não foi possível salvar a nota. Seu texto continua aqui.')
    } finally {
      salvando.current = false
      setOcupado(false)
    }
  }

  return (
    <Modal
      titulo="Nota interna"
      descricao={`Fica na ficha de ${nomeCliente}. O cliente nunca vê.`}
      aoFechar={ocupado ? undefined : aoFechar}
      rodape={(
        <>
          <Botao disabled={ocupado} onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" disabled={vazio || ocupado} onClick={salvar}>
            {ocupado ? 'Salvando…' : 'Salvar nota'}
          </Botao>
        </>
      )}
    >
      <CampoArea
        rotulo="Texto da nota"
        rotuloOculto
        value={texto}
        ref={campo}
        readOnly={ocupado}
        aria-busy={ocupado}
        onChange={(e) => setTexto(e.target.value)}
        placeholder="Ex.: prefere a massa mais al dente, reduzir 1 min de cozimento."
      />
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>
  )
}
