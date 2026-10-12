import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { imprimirPdf } from '../../componentes/pdfNoNavegador'
import { Botao } from '../../componentes/Botao'
import { FolhaImpressao } from '../../componentes/FolhaImpressao'
import { Icone } from '../../componentes/Icone'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { faixaDaJanela } from '../../dominio/entrega'
import { pdfDoCanhoto } from '../../dominio/impressao'
import { itensDetalhados } from '../../dominio/pedido'
import { PapelCanhoto } from './Canhoto'
import css from './comanda.module.css'

// RN-27: entra na fila sozinho quando QUALQUER pedido passa a 'pago', sem
// depender do nome da ação que causou a mudança (o dev de Simular está
// construindo a dele agora; a confirmação de Pix já existente é outra).
// Comparação por DIFERENÇA de estado a cada renderização: guarda o último
// estado visto de cada pedido (por número) e só enfileira quando ele muda
// para 'pago' depois da primeira leitura, senão pedido histórico já pago no
// carregamento entraria na fila sozinho.
function pedidosRecemPagos(conversas, vistosRef, primeiraRodadaRef) {
  const vistos = vistosRef.current
  const recemPagos = []
  for (const conversa of conversas) {
    const pedido = conversa.pedido
    if (!pedido) continue
    const anterior = vistos.get(pedido.numero)
    if (!primeiraRodadaRef.current && anterior !== 'pago' && pedido.estado === 'pago') {
      recemPagos.push(pedido.numero)
    }
    vistos.set(pedido.numero, pedido.estado)
  }
  primeiraRodadaRef.current = false
  return recemPagos
}

// Global (montado em App.jsx, fora de qualquer ficha aberta): o pagamento de
// um pedido pode cair numa conversa que não é a selecionada no Balcão, e a
// gaveta precisa aparecer do mesmo jeito. Fila em memória, não no reducer —
// é operação da sessão, não dado que precise sobreviver a um F5.
//
// Decisão 31, item 4 (achado do gerente, 24/09): a fila abria a modal
// bloqueante sozinha a cada Pix pago — com dez pagamentos seguidos, dez
// modais na cara de quem cozinha. Agora é gaveta NÃO bloqueante: pílula fixa
// com contador e um botão "Imprimir" que já dispara a impressão (o portal
// `FolhaImpressao` fica sempre montado com o canhoto do topo da fila mesmo
// sem nenhuma modal aberta). Modal bloqueante só nasce do "Ver canhoto"
// manual, em `BlocoPedido.jsx` — este arquivo nunca abre uma.
export function FilaCanhotos() {
  const { conversas } = useAtendimento()
  const { marcarCanhotoImpresso } = useAcoes()
  const { cardapio, linhas, janelas } = useCatalogo()
  const [fila, setFila] = useState([])
  const vistosRef = useRef(new Map())
  const primeiraRodadaRef = useRef(true)

  useEffect(() => {
    const recemPagos = pedidosRecemPagos(conversas, vistosRef, primeiraRodadaRef)
    if (recemPagos.length > 0) {
      setFila((atual) => [...atual, ...recemPagos.filter((numero) => !atual.includes(numero))])
    }
  }, [conversas])

  const numeroAtual = fila[0] ?? null
  const conversaAtual = numeroAtual
    ? conversas.find((c) => c.pedido?.numero === numeroAtual) ?? null
    : null

  // Clique único imprime o primeiro da fila e passa pro próximo sozinho;
  // clique repetido esvazia a fila inteira (o "um por um ou todos" de
  // decisão 29, preservado aqui mesmo sem a modal).
  // Rodada 11 (issue #9): imprime o PDF de 80 mm, o mesmo do "Baixar PDF" da
  // modal; a folha do portal abaixo segue como plano B sem leitor de PDF.
  const dadosDoPapel = conversaAtual?.pedido && {
    pedido: conversaAtual.pedido,
    itens: itensDetalhados(conversaAtual.pedido, cardapio),
    linhas,
    nomeCliente: conversaAtual.nome,
    endereco: conversaAtual.cliente?.endereco ?? null,
    faixa: faixaDaJanela(janelas, conversaAtual.pedido.janela),
  }
  const imprimir = () => {
    if (!numeroAtual) return
    if (dadosDoPapel) imprimirPdf(pdfDoCanhoto(dadosDoPapel), { planoB: () => window.print() })
    marcarCanhotoImpresso(numeroAtual)
    setFila((atual) => atual.slice(1))
  }

  // Lugar reservado em cada arranjo, sem cobrir a navegação nem a Cobrança.
  // Redimensionar troca esse nó; a fila global continua montada e conserva os canhotos.
  const [peDaFicha, setPeDaFicha] = useState(null)
  useEffect(() => {
    const atualizar = () => {
      const destino = document.querySelector('[data-fila-canhotos]')
      setPeDaFicha((atual) => (atual === destino ? atual : destino))
    }
    const observador = new MutationObserver(atualizar)
    observador.observe(document.body, { childList: true, subtree: true })
    atualizar()
    return () => observador.disconnect()
  }, [])

  const gaveta = fila.length > 0 && (
    <output className={peDaFicha ? css.gavetaNoPe : css.gavetaCanhoto} aria-live="polite">
      <span className={css.gavetaTexto}>
        <Icone nome="printer" tamanho={20} />
        <span className={css.gavetaLabel}>
          {fila.length === 1 ? '1 canhoto na fila' : `${fila.length} canhotos na fila`}
        </span>
      </span>
      <Botao variante="primario" icone="printer" className={css.botaoImprimir} onClick={imprimir}>
        Imprimir
      </Botao>
    </output>
  )

  return (
    <>
      {gaveta && peDaFicha ? createPortal(gaveta, peDaFicha) : gaveta}

      {/* Portal de impressão do topo da fila, sempre montado enquanto ela
          existir: é o que deixa o botão "Imprimir" da gaveta sair sem abrir
          nenhuma modal. */}
      {dadosDoPapel && (
        <FolhaImpressao>
          <PapelCanhoto {...dadosDoPapel} />
        </FolhaImpressao>
      )}
    </>
  )
}
