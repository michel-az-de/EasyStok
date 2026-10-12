import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { primeiroNome } from '../../dominio/mensagem'
import css from './atendimento.module.css'

export function ModalEnviarCardapio({
  nomeCliente, canalNome, link, daLoja = false, texto, aoEnviar, aoFechar, numeroComanda, podeEnviar = true,
}) {
  const [copiado, setCopiado] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState('')
  const trava = useRef(false)
  const endereco = (() => {
    try { return new URL(link).host } catch { return link }
  })()

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(link)
      setCopiado(true)
    } catch {
      setCopiado(false)
      setErro('Não foi possível copiar. Abra "Como funciona esta comanda" e selecione o endereço para copiá-lo.')
    }
  }

  async function enviar() {
    if (trava.current) return
    trava.current = true
    setEnviando(true); setErro('')
    try {
      if (await aoEnviar() === false) throw new Error('A comanda não foi enviada. Confira o motivo na conversa. O link continua disponível para copiar ou tentar novamente.')
      aoFechar()
    } catch (e) { setErro(e.message) }
    finally { trava.current = false; setEnviando(false) }
  }

  return (
    <Modal
      titulo={numeroComanda ? `Compartilhar comanda ${numeroComanda}` : 'Compartilhar comanda'}
      descricao={`Com ${primeiroNome(nomeCliente)}. A seleção aparece na ficha enquanto ele escolhe.`}
      aoFechar={() => { if (!enviando) aoFechar() }}
      rodape={(
        <>
          <Botao onClick={copiar}>{copiado ? 'Link copiado' : 'Copiar link'}</Botao>
          <Botao variante="primario" icone="enviar" disabled={enviando || !podeEnviar} onClick={enviar}>
            {enviando ? 'Enviando…' : `Enviar pelo ${canalNome}`}
          </Botao>
        </>
      )}
    >
      <div className={css.cardapioEnvio}>
        {erro && <p role="alert">{erro}</p>}
        {!podeEnviar && <p>O canal não permite enviar texto agora. Você pode copiar o link ou usar um modelo aprovado na conversa.</p>}
        <p className={css.cardapioRotulo}>Mensagem</p>
        <p className={css.cardapioMensagem}>{texto}</p>

        <p className={css.cardapioLink}>
          <a href={link} target="_blank" rel="noreferrer">Abrir comanda {numeroComanda}</a>
        </p>
        <details>
        <summary>Como funciona esta comanda</summary>
        {daLoja ? (
          // #1353: o mesmo link que o agente manda (S48). Vale 24 h e um pedido.
          <p>
            É a comanda pessoal no site da Casa da Baba (<strong>{endereco}</strong>). Cliente e
            atendimento acompanham a mesma seleção. Cada link aceita uma confirmação; outro link permite
            acrescentar ou solicitar uma troca antes da entrega. O pagamento é por pedido ou diferença.
            Um novo link mantém o número e substitui a edição anterior ainda não confirmada.
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
        </p>
        </details>
      </div>
    </Modal>
  )
}
