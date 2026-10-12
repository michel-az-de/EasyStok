import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { horaCurta } from '../../dominio/formato'
import { passoPorId } from '../../dominio/esteira'
import { pedidoEncerrado } from '../../dominio/pedido'
import { conversasDoLote, PASSOS_PAPEL, validarSelecao } from '../../dominio/loteDePapel'
import css from './ModalLoteDePapel.module.css'

// Global (montado em App.jsx, fora de qualquer ficha, mesmo padrão de
// FilaCanhotos.jsx): a queda de conexão não é de uma conversa só, é da loja
// inteira. UC-04 E1 + US-042 ("ao voltar a conexão, os status marcados no
// papel são lançados na tela em lote"). "Nada de tela nova se couber numa
// modal" — cabe: banner enquanto está caída, pílula + modal quando volta.
export function ModalLoteDePapel() {
  const { conversas, conexao, agora, fonteApi } = useAtendimento()
  const { conexaoVoltou, lancarLotePapel } = useAcoes()
  const [modalAberta, setModalAberta] = useState(false)
  const [selecoes, setSelecoes] = useState({})
  // #1241 (modo API): o EasyStok responde o que recusou; a modal mostra antes de fechar.
  const [recusas, setRecusas] = useState([])
  const [lancando, setLancando] = useState(false)
  const eraOnlineRef = useRef(conexao.online)
  const faixaRef = useRef(null)

  const pendentes = conexao.pedidosAbertos.length > 0
  const candidatos = conversasDoLote(conversas, conexao.pedidosAbertos)

  // Integração 72: a faixa é fixa no topo e cobria o cabeçalho da conversa
  // (nome, Encerrar, assistente) e, no celular, a barra de cima inteira.
  // Enquanto ela existe, a tela desce a altura dela (`--faixa-offline`). A
  // pendência depois de "Depois" usa a mesma faixa: flutuando embaixo, ela
  // cobria as abas do celular e o fim da lista do Balcão no tablet.
  useLayoutEffect(() => {
    const faixa = faixaRef.current
    if (!faixa) return undefined
    const raiz = document.documentElement
    const medir = () => raiz.style.setProperty('--faixa-offline', `${faixa.offsetHeight}px`)
    medir()
    const observador = new ResizeObserver(medir)
    observador.observe(faixa)
    return () => {
      observador.disconnect()
      raiz.style.removeProperty('--faixa-offline')
    }
  }, [conexao.online, pendentes, modalAberta])

  // Abre sozinha só na TRANSIÇÃO de volta (não em todo render offline→online
  // já visto), e só quando sobrou pedido para lançar.
  useEffect(() => {
    if (eraOnlineRef.current === false && conexao.online === true && conexao.pedidosAbertos.length > 0) {
      setSelecoes({})
      setModalAberta(true)
    }
    eraOnlineRef.current = conexao.online
  }, [conexao.online, conexao.pedidosAbertos.length])

  const marcar = (conversaId, passoId) => setSelecoes((atual) => ({ ...atual, [conversaId]: passoId }))

  const fechar = () => {
    setRecusas([])
    setModalAberta(false)
  }

  // No modo demonstração o lote é aplicado na hora. No modo API a ação devolve uma promessa:
  // `null` quando nem chegou ao EasyStok (a modal fica para tentar de novo), ou o resultado
  // com as recusas, que ficam na tela até ela fechar.
  const confirmar = async () => {
    setLancando(true)
    const resultado = await lancarLotePapel(selecoes, agora)
    setLancando(false)
    if (resultado === null) return
    setSelecoes({})
    if (resultado?.rejeitados?.length) setRecusas(resultado.rejeitados)
    else fechar()
  }

  const algumAvanco = candidatos.some((c) => {
    const alvo = selecoes[c.id]
    return alvo && alvo !== c.pedido.estado
  })

  return (
    <>
      {!conexao.online && (
        <output ref={faixaRef} className={css.bannerOffline} aria-live="polite">
          <Icone nome="alerta" tamanho={20} />
          <span>
            Sem conexão desde {horaCurta(conexao.offlineDesde)}. Nenhuma mensagem está saindo pro
            cliente agora — continue pelos canhotos no quadro.
          </span>
          <Botao variante="primario" onClick={() => conexaoVoltou(agora)}>Conexão voltou</Botao>
        </output>
      )}

      {conexao.online && pendentes && !modalAberta && (
        <output ref={faixaRef} className={css.faixaPendente} aria-live="polite">
          <Icone nome="printer" tamanho={20} />
          <span>
            {candidatos.length === 1 ? '1 pedido para lançar do papel' : `${candidatos.length} pedidos para lançar do papel`}
          </span>
          <Botao variante="primario" onClick={() => setModalAberta(true)}>Lançar</Botao>
        </output>
      )}

      {modalAberta && (
        <Modal
          titulo="Lançar o que foi feito no papel"
          descricao={`Sem conexão de ${horaCurta(conexao.offlineDesde)} até ${horaCurta(conexao.voltouEm)}. Marque até onde cada pedido andou no canhoto: confirmar lança tudo de uma vez, na ordem da esteira, ${fonteApi
            ? 'no EasyStok. O cliente não recebe aviso do que já passou.'
            : 'e cada cliente recebe um aviso só, com o passo final.'}`}
          aoFechar={fechar}
          rodape={recusas.length > 0 ? (
            <Botao variante="primario" onClick={fechar}>Entendi</Botao>
          ) : (
            <>
              <Botao variante="texto" onClick={fechar}>Depois</Botao>
              <Botao variante="primario" disabled={!algumAvanco || lancando} onClick={confirmar}>
                {lancando ? 'Lançando…' : 'Confirmar'}
              </Botao>
            </>
          )}
        >
          {recusas.length > 0 && (
            <div role="alert">
              <p>O resto foi lançado. Estes ficaram de fora, ajuste no pedido:</p>
              <ul className={css.lista}>
                {recusas.map((r) => <li key={r.numero} className={css.avisoCancelado}>{r.numero}: {r.motivo}</li>)}
              </ul>
            </div>
          )}
          {candidatos.length === 0 && <p>Nenhum pedido estava aberto quando a conexão caiu.</p>}
          <ul className={css.lista}>
            {candidatos.map((c) => {
              const parado = pedidoEncerrado(c.pedido)
              // P0 (banca3/e4-e7, achado 1): o alvo só existe quando ELA
              // clicou. Cair no estado atual do pedido pintava o chip como já
              // escolhido sem toque nenhum; quem via "já marcado" não tocava
              // e o avanço do papel se perdia em silêncio.
              const escolha = selecoes[c.id] ?? null
              return (
                <li key={c.id} className={css.linha}>
                  <div className={css.linhaTopo}>
                    <strong>{c.nome}</strong>
                    <span className={css.numero}>{c.pedido.numero}</span>
                  </div>
                  {parado ? (
                    <p className={css.avisoCancelado}>
                      {c.pedido.estado === 'cancelado' ? 'Cancelado durante a queda, não lança.' : 'Já entregue.'}
                    </p>
                  ) : (
                    <>
                      <p className={css.estadoAtual}>Hoje: {passoPorId(c.pedido.estado)?.rotulo}</p>
                      <div className={css.opcoes} role="radiogroup" aria-label={`Até onde ${c.nome} andou no papel`}>
                        {PASSOS_PAPEL.map((passoId) => (
                          <Chip
                            key={passoId}
                            papel="escolha"
                            ativo={escolha === passoId}
                            emBreve={!validarSelecao(c.pedido, passoId).ok}
                            onClick={() => marcar(c.id, passoId)}
                          >
                            {passoPorId(passoId).rotulo}
                          </Chip>
                        ))}
                      </div>
                    </>
                  )}
                </li>
              )
            })}
          </ul>
        </Modal>
      )}
    </>
  )
}
