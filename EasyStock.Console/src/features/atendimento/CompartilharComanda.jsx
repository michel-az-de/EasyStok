import { useState } from 'react'
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
  const [aberto, setAberto] = useState(false)
  const canal = canalDaConversa(canais, conversa)
  const podeEnviar = permissaoDeEscrita(conversa, agora, canal).pode
  async function gerar() {
    const resultado = await obterLinkCardapio(conversa.id)
    if (!resultado?.url) throw new Error('Não foi possível obter o link da comanda. Tente novamente.')
    const numero = resultado.numeroComanda || conversa.numeroComanda
    return { link: resultado.url, daLoja: resultado.daLoja, numeroComanda: numero,
      texto: textoConviteCardapio(conversa.nome, resultado.url, numero) }
  }
  if (conversaEncerrada(conversa) || estaBloqueada(conversa)) return null
  return <>
    <Botao icone="cardapio" variante="primario" aria-haspopup="dialog"
      onClick={() => setAberto(true)}>Link de compra</Botao>
    {aberto && <ModalEnviarCardapio nomeCliente={conversa.nome} canalNome={canal.nome}
      numeroComanda={conversa.numeroComanda} aoGerar={gerar}
      podeEnviar={podeEnviar} aoEnviar={texto => enviar(conversa.id, texto)} aoFechar={() => setAberto(false)} />}
  </>
}
