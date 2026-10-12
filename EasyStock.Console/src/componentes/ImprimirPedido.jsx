import { useRef, useState } from 'react'
import { Botao } from './Botao'
import { CampoSelecao } from './Campo'
import { Modal } from './Modal'
import { imprimirHtml } from './htmlNoNavegador'
import css from './ImprimirPedido.module.css'

const naoPropagar = (evento) => evento.stopPropagation()

// Ação "Imprimir" de um pedido pelos impressos em HTML da API (#1632): escolhe o documento e
// o papel, já no padrão salvo do aparelho, busca a página pronta e manda imprimir.
// O componente não conhece pedido nem API: quem chama entrega `documentos` e `papeis`
// (opções `{ valor, rotulo }`), `papelPadrao(documento)` e `buscarHtml(documento, papel)`.
// `aoImprimir(documento)` avisa quem chamou qual papel saiu (fila de canhotos, RN-27).
export function ImprimirPedido({
  documentos, papeis, papelPadrao, buscarHtml, documentoInicial, aoImprimir,
  rotulo = 'Imprimir', variante = 'secundario', titulo = 'Imprimir pedido', ...resto
}) {
  const [aberto, setAberto] = useState(false)
  const [documento, setDocumento] = useState(documentoInicial ?? documentos[0].valor)
  const [papel, setPapel] = useState(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })
  const ocupado = useRef(false)

  // Fechar solta a trava: um logo que não responde não pode prender a próxima impressão.
  const fechar = () => { ocupado.current = false; setAberto(false) }
  const abrir = () => {
    // O padrão é lido ao abrir: vale o que estiver salvo agora neste aparelho.
    setPapel(papelPadrao(documento))
    setSituacao({ estado: 'ocioso' })
    setAberto(true)
  }
  const trocarDocumento = (evento) => {
    setDocumento(evento.target.value)
    setPapel(papelPadrao(evento.target.value))
    setSituacao({ estado: 'ocioso' })
  }

  async function imprimir(evento) {
    evento.preventDefault()
    if (ocupado.current) return
    ocupado.current = true
    setSituacao({ estado: 'imprimindo' })
    try {
      await imprimirHtml(await buscarHtml(documento, papel))
      aoImprimir?.(documento)
      setAberto(false)
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro?.message || 'Tente de novo.' })
    } finally {
      ocupado.current = false
    }
  }

  const imprimindo = situacao.estado === 'imprimindo'
  return (
    // O cartão do KDS é arrastável: nada do que acontece aqui dentro pode virar arrasto.
    <span style={{ display: 'contents' }} onPointerDown={naoPropagar} onMouseDown={naoPropagar} onTouchStart={naoPropagar}>
      <Botao variante={variante} icone="printer" onClick={abrir} {...resto}>{rotulo}</Botao>
      {aberto && (
        <Modal
          titulo={titulo}
          aoFechar={fechar}
          largura="min(420px, calc(100vw - 24px))"
          rodape={(
            <>
              <Botao variante="texto" onClick={fechar}>Cancelar</Botao>
              <Botao variante="primario" icone="printer" disabled={imprimindo} onClick={imprimir}>
                {imprimindo ? 'Preparando…' : 'Imprimir'}
              </Botao>
            </>
          )}
        >
          <form className={css.campos} onSubmit={imprimir}>
            <CampoSelecao rotulo="Documento" opcoes={documentos} value={documento} disabled={imprimindo} onChange={trocarDocumento} />
            <CampoSelecao
              rotulo="Papel"
              dica="O padrão de cada documento neste aparelho fica em Configurações, Impressão."
              opcoes={papeis}
              value={papel ?? ''}
              disabled={imprimindo}
              onChange={(evento) => { setPapel(evento.target.value); setSituacao({ estado: 'ocioso' }) }}
            />
            {situacao.estado === 'erro' && (
              <p className={css.erro} role="alert">O impresso não saiu: {situacao.mensagem}</p>
            )}
          </form>
        </Modal>
      )}
    </span>
  )
}
