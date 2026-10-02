import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { primeiroNome } from '../../dominio/mensagem'
import css from './atendimento.module.css'

// Rodada 12 (issue #16), dúvida da Thatiane: "esse link do cardápio vem de
// onde?". Antes o botão mandava na hora, sem mostrar nada. Agora ela vê o
// texto, o endereço e de onde ele vem ANTES de enviar. O link e o texto
// continuam os mesmos de `dominio/cardapioLink.js` (quem chama monta).
export function ModalEnviarCardapio({
  nomeCliente, canalNome, link, daLoja = false, texto, aoEnviar, aoFechar,
}) {
  const [copiado, setCopiado] = useState(false)
  const endereco = (() => {
    try { return new URL(link).host } catch { return link }
  })()

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(link)
      setCopiado(true)
    } catch {
      setCopiado(false)
    }
  }

  return (
    <Modal
      titulo="Enviar o cardápio"
      descricao={`Vai para ${primeiroNome(nomeCliente)} pelo ${canalNome}, com o link para escolher os pratos.`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" icone="enviar" onClick={aoEnviar}>Enviar cardápio</Botao>
        </>
      )}
    >
      <div className={css.cardapioEnvio}>
        <p className={css.cardapioRotulo}>Mensagem</p>
        <p className={css.cardapioMensagem}>{texto}</p>

        <p className={css.cardapioRotulo}>De onde vem o link</p>
        {daLoja ? (
          // #1353: o mesmo link que o agente manda (S48). Vale 24 h e um pedido.
          <p>
            É o cardápio da Casa da Baba no site (<strong>{endereco}</strong>), o mesmo link que o
            atendimento automático manda. Mostra só o que está disponível hoje e vale por 24 horas.
          </p>
        ) : (
          <p>
            É a página de pedido da própria Casa da Baba, neste sistema
            (<strong>{endereco}</strong>). Mostra só o que está disponível hoje. O cliente escolhe,
            paga e o pedido volta sozinho para esta conversa.
          </p>
        )}
        <p className={css.cardapioLink}>
          <code>{link}</code>
          <Botao variante="texto" onClick={copiar}>{copiado ? 'Link copiado' : 'Copiar link'}</Botao>
        </p>
      </div>
    </Modal>
  )
}
