import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { textoConviteCardapio } from '../../dominio/cardapioLink'
import { canalDaConversa } from '../../dominio/canal'
import { permissaoDeEscrita } from '../../dominio/janela'
import { conversaEncerrada, estaBloqueada } from '../../dominio/conversa'
import { ModalEnviarCardapio } from './ModalEnviarCardapio'

export function CompartilharComanda({ conversa }) {
  const { obterLinkCardapio, enviar } = useAcoes()
  const { agora } = useAtendimento()
  const { canais } = useCatalogo()
  const [link, setLink] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const [erro, setErro] = useState('')
  const trava = useRef(false)
  const canal = canalDaConversa(canais, conversa)
  const podeEnviar = permissaoDeEscrita(conversa, agora, canal).pode
  async function abrir() {
    if (trava.current) return
    trava.current = true; setOcupado(true); setErro('')
    try {
      const resultado = await obterLinkCardapio(conversa.id)
      if (!resultado) throw new Error('Não foi possível abrir a comanda. Tente novamente.')
      setLink(resultado)
    } catch (e) { setErro(e.message) }
    finally { trava.current = false; setOcupado(false) }
  }
  const numero = link?.numeroComanda || conversa.numeroComanda
  const texto = link ? textoConviteCardapio(conversa.nome, link.url, numero) : ''
  if (conversaEncerrada(conversa) || estaBloqueada(conversa)) return null
  return <>
    <Botao icone="cardapio" variante="primario" aria-haspopup="dialog" disabled={ocupado}
      onClick={abrir}>{ocupado ? 'Abrindo comanda…' : 'Compartilhar comanda'}</Botao>
    {erro && <p role="alert">{erro}</p>}
    {link && <ModalEnviarCardapio nomeCliente={conversa.nome} canalNome={canal.nome}
      link={link.url} daLoja={link.daLoja} numeroComanda={numero} texto={texto}
      podeEnviar={podeEnviar} aoEnviar={() => enviar(conversa.id, texto)} aoFechar={() => setLink(null)} />}
  </>
}
