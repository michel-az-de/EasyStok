import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { useChegouNaTela } from '../../hooks/useChegouNaTela'
import { useMidiaDaMensagem } from '../../aplicacao/useMidiaDaMensagem'
import { diaMes, moeda, plural } from '../../dominio/formato'
import { resumoQuemE } from '../../dominio/quemE'
import { copiarTexto } from './apresentacaoDaFicha'
import css from './ficha.module.css'

// #3209 (S84): telefone, pedidos e mídias do cartão, para bater o olho e saber quem é.
function Telefone({ telefone }) {
  const [retorno, setRetorno] = useState(null) // 'copiado' | 'falhou'
  useEffect(() => {
    if (!retorno) return undefined
    const id = setTimeout(() => setRetorno(null), 2400)
    return () => clearTimeout(id)
  }, [retorno])
  const copiar = async () => setRetorno(await copiarTexto(telefone) ? 'copiado' : 'falhou')
  return (
    <div className={css.linhaQuemE}>
      <Icone nome="phone" tamanho={16} />
      <strong data-quem-e-telefone>{telefone}</strong>
      <Botao variante="texto" icone="copy" onClick={copiar} aria-label={`Copiar telefone ${telefone}`}>Copiar</Botao>
      <output className={css.retornoEndereco} aria-live="polite">
        {retorno === 'copiado' ? 'Copiado' : retorno === 'falhou' ? 'Não foi possível copiar' : ''}
      </output>
    </div>
  )
}

// Só baixa com o cartão na tela (#1577): no celular ele pode estar atrás da conversa.
function Miniatura({ midia, aoAbrir }) {
  const itemRef = useRef(null)
  const { url, erro } = useMidiaDaMensagem(midia, useChegouNaTela(itemRef))
  return (
    <li ref={itemRef}>
      <Botao variante="texto" className={css.miniatura} disabled={!url} onClick={(e) => aoAbrir(e.currentTarget)}
        aria-label={erro ? 'Foto que não abriu' : 'Abrir foto em tamanho inteiro'} data-miniatura>
        {url ? <img src={url} alt="" /> : <Icone nome={erro ? 'alerta' : 'imagem'} tamanho={16} />}
      </Botao>
    </li>
  )
}

function FotoInteira({ midia, aoFechar }) {
  const { url } = useMidiaDaMensagem(midia)
  return (
    <Modal titulo="Foto da conversa" aoFechar={aoFechar} largura="lg">
      {url && <img className={css.fotoInteira} src={url} alt={midia.texto || 'Foto da conversa'} />}
    </Modal>
  )
}

export function QuemEDoCliente({ conversa }) {
  const [aberta, setAberta] = useState(null)
  const { telefone, pedidos, midias } = resumoQuemE(conversa)
  const linhaPedidos = pedidos.quantidade > 0
    ? `${plural(pedidos.quantidade, 'pedido', 'pedidos')}${pedidos.ultimo ? ` · último em ${diaMes(pedidos.ultimo.em + (pedidos.ultimo.em.length === 10 ? 'T12:00:00' : ''))}, ${moeda(pedidos.ultimo.total)}` : ''}`
    : ''
  if (!telefone && !linhaPedidos && midias.itens.length === 0) return null
  return (
    <section className={css.quemE} aria-label="Quem é" data-quem-e>
      {telefone && <Telefone telefone={telefone} />}
      {linhaPedidos && <p className={css.linhaQuemE} data-quem-e-pedidos><Icone nome="shopping-cart" tamanho={16} />{linhaPedidos}</p>}
      {midias.itens.length > 0 && (
        <div className={css.midiasQuemE} data-quem-e-midias>
          <ul className={css.listaMiniaturas} aria-label="Fotos da conversa">
            {midias.itens.map((m) => <Miniatura key={m.mensagemId} midia={m} aoAbrir={(botao) => setAberta({ midia: m, botao })} />)}
            {midias.restantes > 0 && <li className={css.maisMidias}>+{midias.restantes}</li>}
          </ul>
          {conversa.temAnteriores && <small className={css.apoioQuemE}>Só as mensagens já carregadas.</small>}
        </div>
      )}
      {aberta && <FotoInteira midia={aberta.midia} aoFechar={() => { aberta.botao?.focus?.(); setAberta(null) }} />}
    </section>
  )
}
