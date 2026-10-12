import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { primeiroNome } from '../../dominio/mensagem'
import css from './atendimento.module.css'

export function ModalEnviarCardapio({
  nomeCliente, canalNome, link, daLoja = false, texto, aoGerar, aoEnviar, aoFechar, numeroComanda, podeEnviar = true,
}) {
  const [conteudo, setConteudo] = useState(link ? { link, texto, daLoja, numeroComanda } : null)
  const [copiado, setCopiado] = useState(false)
  const [copiando, setCopiando] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState('')
  const trava = useRef(false)
  const ocupado = enviando || copiando
  const numero = conteudo?.numeroComanda || numeroComanda
  const endereco = (() => {
    try { return new URL(conteudo?.link).host } catch { return conteudo?.link }
  })()

  async function preparar() {
    if (conteudo) return conteudo
    const resultado = await aoGerar()
    if (!resultado?.link) throw new Error('Não foi possível obter o link da comanda. Tente novamente.')
    setConteudo(resultado)
    return resultado
  }

  const copiar = async () => {
    if (trava.current) return
    trava.current = true; setCopiando(true); setErro('')
    try {
      const dados = await preparar()
      try {
        await navigator.clipboard.writeText(dados.link)
        setCopiado(true)
      } catch {
        setCopiado(false)
        setErro('Não foi possível copiar. Abra "Como funciona esta comanda" e selecione o endereço para copiá-lo.')
      }
    } catch (e) { setErro(e.message) }
    finally { trava.current = false; setCopiando(false) }
  }

  async function enviar() {
    if (trava.current) return
    trava.current = true
    setEnviando(true); setErro('')
    try {
      const dados = await preparar()
      if (await aoEnviar(dados.texto) === false) throw new Error('A comanda não foi enviada. Confira o motivo na conversa. O link continua disponível para copiar ou tentar novamente.')
      aoFechar()
    } catch (e) { setErro(e.message) }
    finally { trava.current = false; setEnviando(false) }
  }

  return (
    <Modal
      titulo={numero ? `Compartilhar comanda ${numero}` : 'Compartilhar comanda'}
      descricao={`Com ${primeiroNome(nomeCliente)}. A seleção aparece na ficha enquanto ele escolhe.`}
      aoFechar={() => { if (!trava.current) aoFechar() }}
      rodape={(
        <>
          <Botao disabled={ocupado} onClick={copiar}>{copiando ? 'Copiando…' : copiado ? 'Link copiado' : conteudo ? 'Copiar link' : 'Gerar e copiar link'}</Botao>
          <Botao variante="primario" icone="enviar" disabled={ocupado || !podeEnviar} onClick={enviar}>
            {enviando ? 'Enviando…' : `${conteudo ? 'Enviar' : 'Gerar e enviar'} pelo ${canalNome}`}
          </Botao>
        </>
      )}
    >
      <div className={css.cardapioEnvio}>
        {erro && <p role="alert">{erro}</p>}
        {!podeEnviar && <p>O canal não permite enviar texto agora. Você pode copiar o link ou usar um modelo aprovado na conversa.</p>}
        {!conteudo && <p>Ao gerar uma nova edição, o link anterior ainda não confirmado, se houver, deixa de aceitar alterações.</p>}
        <p className={css.cardapioRotulo}>Mensagem</p>
        <p className={css.cardapioMensagem}>{conteudo?.texto || 'O link individual e a mensagem aparecerão aqui após a geração.'}</p>

        {conteudo && <><p className={css.cardapioLink}>
          <a href={conteudo.link} target="_blank" rel="noreferrer">Abrir comanda {numero}</a>
        </p>
        <details>
        <summary>Como funciona esta comanda</summary>
        {conteudo.daLoja ? (
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
          <code>{conteudo.link}</code>
        </p>
        </details></>}
      </div>
    </Modal>
  )
}
