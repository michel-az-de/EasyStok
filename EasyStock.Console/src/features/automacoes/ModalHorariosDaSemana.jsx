import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { FormularioHorariosDaSemana } from './FormularioHorariosDaSemana'

// Casca do Balcão para os horários da semana: o corpo é o `FormularioHorariosDaSemana` (#2070).
// Enquanto a gravação vai, o modal não fecha (nem por Esc nem pelo fundo).
export function ModalHorariosDaSemana({ aoFechar }) {
  return <FormularioHorariosDaSemana moldura={({ corpo, acoes, ocupado, podeSair }) => {
    const fechar = () => { if (podeSair()) aoFechar() }
    return <Modal titulo="Horários da semana" largura="min(760px, calc(100vw - 24px))"
      descricao="As mudanças valem após salvar. Fechar mantém o rascunho nesta sessão."
      aoFechar={ocupado ? undefined : fechar}
      rodape={<>
        {acoes}
        <Botao disabled={ocupado} onClick={fechar}>Fechar</Botao>
      </>}>
      {corpo}
    </Modal>
  }} />
}
