import { useCallback, useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { horaCurta } from '../../dominio/formato'
import {
  RECARGA_NAO_ENTREGUES_MS, assinaturaDeFalhas, falhaDeEnvio, falhaNovaNaTela, textoDaFalhaDeEnvio, TIPOS_DE_FALHA,
} from '../../dominio/falhaDeEnvio'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import css from './caixa.module.css'

// S59 (#1391): mensagens que não chegaram ao cliente, de todas as conversas. Só aparece no modo API
// (a massa local não envia de verdade) e só quando há alguma: a faixa abre a lista com Reenviar e
// Abrir conversa.
function Situacao({ mensagem }) {
  if (mensagem.porSms) return <Pilula tom="info" fina>saiu por SMS</Pilula>
  if (mensagem.aguardaCliente) return <Pilula tom="aviso" fina>aguardando o cliente</Pilula>
  return <Pilula tom="perigo" fina>não entregue</Pilula>
}

function Item({ item, aoReenviar, aoAbrir, ocupado, reenviando }) {
  const { mensagem } = item
  return (
    <li className={css.itemNaoEntregue}>
      <div className={css.topoNaoEntregue}>
        <h3>{item.contato}</h3>
      </div>
      <div className={css.metaNaoEntregue}>
        <Situacao mensagem={mensagem} />
        <time dateTime={mensagem.em}>{horaCurta(mensagem.em)}</time>
      </div>
      <p className={css.textoNaoEntregue}>{mensagem.texto}</p>
      {/* #1761: o erro cru do canal (em inglês) vira a frase do mapa do domínio. */}
      {mensagem.erro && <p className={css.erroNaoEntregue}>{textoDaFalhaDeEnvio(mensagem.erro)}</p>}
      <div className={css.acoesNaoEntregue}>
        {!mensagem.aguardaCliente && falhaDeEnvio(mensagem.erro).tipo !== TIPOS_DE_FALHA.FORA_DA_JANELA && (
          <Botao variante="secundario" disabled={ocupado} onClick={() => aoReenviar(item)} aria-label={`Reenviar mensagem para ${item.contato}`}>{reenviando ? 'Reenviando…' : 'Reenviar'}</Botao>
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
  const [carregando, setCarregando] = useState(false)
  const [resultado, setResultado] = useState(null)
  const atualizarRef = useRef(null)
  const envioRef = useRef(null)
  const restaurarRef = useRef(false)
  const focoFaixaRef = useRef(null)
  useEffect(() => {
    if (!aberto && restaurarRef.current) {
      document.querySelector('[data-inicio-tela]')?.focus()
      restaurarRef.current = false
    }
  }, [aberto])
  useEffect(() => {
    const faixa = focoFaixaRef.current
    if (!aberto && faixa && !faixa.isConnected) {
      if (document.activeElement === document.body && !document.querySelector('dialog[open]')) {
        document.querySelector('[data-inicio-tela]')?.focus()
      }
      focoFaixaRef.current = null
    }
  }, [aberto, itens, erro, carregando])

  // `silencioso`: a releitura de fundo não acende "Conferindo mensagens…" nem troca a faixa por erro.
  const carregar = useCallback(({ silencioso = false } = {}) => {
    if (!listarNaoEntregues) return Promise.resolve()
    if (!silencioso) setCarregando(true)
    return listarNaoEntregues()
      .then((lista) => {
        setItens(lista)
        setErro(null)
      })
      .catch((e) => { if (!silencioso) setErro(e.message) })
      .finally(() => { if (!silencioso) setCarregando(false) })
  }, [listarNaoEntregues])

  useEffect(() => { carregar() }, [carregar])
  // #1765 (item 10): a lista anda sozinha a cada minuto e quando uma falha nova aparece na tela.
  // Com a lista aberta, quem atualiza é ela (botão "Atualizar lista"): nada muda debaixo do dedo.
  const { conversas } = useAtendimento()
  const falhas = assinaturaDeFalhas(conversas)
  const falhasVistasRef = useRef(falhas)
  useEffect(() => {
    const antes = falhasVistasRef.current
    falhasVistasRef.current = falhas
    if (!aberto && falhaNovaNaTela(antes, falhas)) carregar({ silencioso: true })
  }, [falhas, aberto, carregar])
  useEffect(() => {
    if (!listarNaoEntregues || aberto) return undefined
    const id = setInterval(() => {
      if (globalThis.document?.visibilityState !== 'hidden') carregar({ silencioso: true })
    }, RECARGA_NAO_ENTREGUES_MS)
    return () => clearInterval(id)
  }, [listarNaoEntregues, aberto, carregar])
  useEffect(() => {
    if (!ocupado && !carregando && envioRef.current && !envioRef.current.isConnected) {
      atualizarRef.current?.focus()
      envioRef.current = null
    }
  }, [ocupado, carregando, itens])

  if (!listarNaoEntregues || (itens.length === 0 && !aberto && !erro && !carregando)) return null

  async function reenviarItem(item) {
    setOcupado(item.mensagem.id)
    envioRef.current = document.activeElement
    setResultado(null)
    try {
      const mensagem = await reenviar(item.conversaId, item.mensagem.id)
      const falhou = !mensagem || mensagem.status === 'falhou'
      setResultado({ erro: falhou, texto: falhou
        ? `Não foi possível reenviar para ${item.contato}. ${mensagem?.erro ? textoDaFalhaDeEnvio(mensagem.erro) : 'Confira a conversa antes de tentar novamente.'}`
        : `Reenvio solicitado para ${item.contato}. Confira o estado na conversa; a entrega ainda depende do canal.` })
      await carregar()
    } catch (e) {
      setResultado({ erro: true, texto: `Não foi possível reenviar. ${e.message}` })
    } finally {
      setOcupado(null)
    }
  }

  function abrirConversa(item) {
    setAberto(false)
    selecionar(item.conversaId)
    aoAbrir?.(item.conversaId)
  }

  function fechar() {
    // Ao esvaziar a lista, a faixa desmonta junto com o modal.
    restaurarRef.current = itens.length === 0 && !erro && !carregando
    setAberto(false)
  }

  return (
    <>
      <button type="button" className={css.faixaNaoEntregues} onFocus={(evento) => { focoFaixaRef.current = evento.currentTarget }} onClick={() => { setAberto(true); carregar() }}>
        <Icone nome="message-square-warning" tamanho={20} />
        {erro ? 'Mensagens sem atualizar' : carregando && itens.length === 0 ? 'Conferindo mensagens…' : itens.length === 1 ? '1 mensagem não entregue' : `${itens.length} mensagens não entregues`}
        <span className={css.verNaoEntregues}>Ver</span>
      </button>
      {aberto && (
        <Modal
          titulo="Não entregues"
          largura="min(740px, calc(100vw - 24px))"
          descricao="Mensagens que o WhatsApp não entregou. Falha temporária tenta de novo sozinha por até 6 h."
          aoFechar={fechar}
          rodape={<><Botao ref={atualizarRef} disabled={carregando || Boolean(ocupado)} onClick={() => carregar()}>Atualizar lista</Botao><Botao onClick={fechar}>Fechar</Botao></>}
        >
          {erro && <p className={css.erroNaoEntregue} role="alert">A lista não carregou: {erro}</p>}
          {carregando && <output className={`${css.resumoNaoEntregues} ${css.bloco}`}>Atualizando mensagens…</output>}
          {resultado && (resultado.erro
            ? <p role="alert" className={css.erroNaoEntregue}>{resultado.texto}</p>
            : <output className={`${css.resumoNaoEntregues} ${css.bloco}`}>{resultado.texto}</output>)}
          {itens.length === 0 && !erro && !carregando && (
            <Vazio titulo="Nenhuma mensagem nesta lista" icone="check" como="output">O estado de cada envio pode ser conferido na conversa.</Vazio>
          )}
          {itens.length > 0 && <p className={css.resumoNaoEntregues}><strong>{itens.length} {itens.length === 1 ? 'mensagem para conferir' : 'mensagens para conferir'}</strong></p>}
          <ul className={css.listaNaoEntregues} aria-label="Mensagens não entregues" aria-busy={carregando || Boolean(ocupado)}>
            {itens.map((item) => (
              <Item
                key={item.mensagem.id}
                item={item}
                ocupado={Boolean(ocupado) || carregando}
                reenviando={ocupado === item.mensagem.id}
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
