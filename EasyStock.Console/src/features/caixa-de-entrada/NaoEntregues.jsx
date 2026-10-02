import { useCallback, useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { horaCurta } from '../../dominio/formato'
import { useAcoes } from '../../aplicacao/contextos'
import css from './caixa.module.css'

// S59 (#1391): mensagens que não chegaram ao cliente, de todas as conversas. Só aparece no modo API
// (a massa local não envia de verdade) e só quando há alguma: a faixa abre a lista com Reenviar e
// Abrir conversa.
function Situacao({ mensagem }) {
  if (mensagem.porSms) return <Pilula tom="info" fina>saiu por SMS</Pilula>
  if (mensagem.aguardaCliente) return <Pilula tom="aviso" fina>aguardando o cliente</Pilula>
  return <Pilula tom="perigo" fina>não entregue</Pilula>
}

function Item({ item, aoReenviar, aoAbrir, ocupado }) {
  const { mensagem } = item
  return (
    <li className={css.itemNaoEntregue}>
      <div className={css.topoNaoEntregue}>
        <strong>{item.contato}</strong>
        <Situacao mensagem={mensagem} />
        <time dateTime={mensagem.em}>{horaCurta(mensagem.em)}</time>
      </div>
      <p className={css.textoNaoEntregue}>{mensagem.texto}</p>
      {mensagem.erro && <p className={css.erroNaoEntregue}>{mensagem.erro}</p>}
      <div className={css.acoesNaoEntregue}>
        {!mensagem.aguardaCliente && (
          <Botao variante="secundario" disabled={ocupado} onClick={() => aoReenviar(item)}>Reenviar</Botao>
        )}
        <Botao variante="texto" onClick={() => aoAbrir(item)}>Abrir conversa</Botao>
      </div>
    </li>
  )
}

export function NaoEntregues({ aoAbrir }) {
  const { listarNaoEntregues, reenviar, selecionar } = useAcoes()
  const [itens, setItens] = useState([])
  const [aberto, setAberto] = useState(false)
  const [ocupado, setOcupado] = useState(null)
  const [erro, setErro] = useState(null)

  const carregar = useCallback(() => {
    if (!listarNaoEntregues) return Promise.resolve()
    return listarNaoEntregues()
      .then((lista) => {
        setItens(lista)
        setErro(null)
      })
      .catch((e) => setErro(e.message))
  }, [listarNaoEntregues])

  useEffect(() => { carregar() }, [carregar])

  if (!listarNaoEntregues || (itens.length === 0 && !aberto)) return null

  async function reenviarItem(item) {
    setOcupado(item.mensagem.id)
    await reenviar(item.conversaId, item.mensagem.id)
    await carregar()
    setOcupado(null)
  }

  function abrirConversa(item) {
    setAberto(false)
    selecionar(item.conversaId)
    aoAbrir?.(item.conversaId)
  }

  return (
    <>
      <button type="button" className={css.faixaNaoEntregues} onClick={() => { setAberto(true); carregar() }}>
        <Icone nome="message-square-warning" tamanho={20} />
        {itens.length === 1 ? '1 mensagem não entregue' : `${itens.length} mensagens não entregues`}
        <span className={css.verNaoEntregues}>Ver</span>
      </button>
      {aberto && (
        <Modal
          titulo="Não entregues"
          descricao="Mensagens que o WhatsApp não entregou. Falha temporária tenta de novo sozinha por até 6 h."
          aoFechar={() => setAberto(false)}
        >
          {erro && <p className={css.erroNaoEntregue} role="alert">A lista não carregou: {erro}</p>}
          {itens.length === 0 && !erro && (
            <Vazio titulo="Tudo entregue">Nenhuma mensagem ficou para trás.</Vazio>
          )}
          <ul className={css.listaNaoEntregues}>
            {itens.map((item) => (
              <Item
                key={item.mensagem.id}
                item={item}
                ocupado={ocupado === item.mensagem.id}
                aoReenviar={reenviarItem}
                aoAbrir={abrirConversa}
              />
            ))}
          </ul>
        </Modal>
      )}
    </>
  )
}
