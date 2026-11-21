import { Botao } from './Botao'
import { Modal } from './Modal'

// #3011: a confirmação de ação destrutiva do Console, no lugar do window.confirm nativo. O botão leva o verbo
// da ação ("Desligar ferramenta", não "OK"); o foco inicial vai para o botão que não muda nada; Esc, o X e o
// clique no fundo cancelam (o Modal já liga os três ao `aoFechar`). Confirmar fecha e depois executa.
export function ModalConfirmar({ titulo, descricao, rotuloConfirmar, rotuloCancelar = 'Cancelar', aoConfirmar, aoFechar }) {
  return (
    <Modal
      titulo={titulo}
      descricao={descricao}
      aoFechar={aoFechar}
      focarCampo
      rodape={(
        <>
          <Botao data-foco-inicial onClick={aoFechar}>{rotuloCancelar}</Botao>
          <Botao variante="perigo" onClick={() => { aoFechar(); aoConfirmar() }}>{rotuloConfirmar}</Botao>
        </>
      )}
    />
  )
}
