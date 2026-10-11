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
      titulo="Enviar comanda ao cliente"
      descricao={`Vai para ${primeiroNome(nomeCliente)} pelo ${canalNome}. A seleção aparece nesta ficha enquanto ele escolhe.`}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao variante="primario" icone="enviar" onClick={aoEnviar}>Enviar comanda</Botao>
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
            É a comanda pessoal no site da Casa da Baba (<strong>{endereco}</strong>). Cliente e
            atendimento acompanham a mesma seleção. Cada link aceita uma confirmação; outro link permite
            acrescentar ou solicitar uma troca antes da entrega. O pagamento é por pedido ou diferença.
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
          <a href={link} target="_blank" rel="noreferrer">Abrir comanda</a>
          <Botao variante="texto" onClick={copiar}>{copiado ? 'Link copiado' : 'Copiar link'}</Botao>
        </p>
      </div>
    </Modal>
  )
}
