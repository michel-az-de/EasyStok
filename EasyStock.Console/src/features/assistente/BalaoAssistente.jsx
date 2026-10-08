import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Botao } from '../../componentes/Botao'
import { CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useEscape } from '../../hooks/useEscape'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import {
  analisarFotoSuspeita, dicaCDC, padraoDeCompra, resumoCliente, revisarOrtografia, sentimentoDaConversa,
} from '../../dominio/assistente'
import { executarAcaoDoAssistente, respostaDoAssistente } from '../../dominio/acoesDoAssistente'
import {
  CartaoCDC, CartaoCliente, CartaoFotoSuspeita, CartaoOrtografia, CartaoPadraoDeCompra, CartaoSentimento,
} from './Cartoes'
import { AcoesPropostas } from './AcoesPropostas'
import css from './assistente.module.css'

// Balão flutuante do canto inferior direito (fala do dono, 24/09/2026 04h12):
// o que hoje fazia papel de assistente dentro do fio (`PainelAgente`, seção
// "Sugerir") mora agora aqui dentro, num cartão a mais, em vez de dividir a
// tela com a conversa. `sugestaoAgente` chega pronto de cima (mesma regra de
// `PainelAtendimento`: feature não monta feature; quem junta as duas
// features é `app/App.jsx`).
// #1445: `aoAbrirTela('cardapio' | 'comanda')` também vem do App, pelo mesmo motivo.
export function BalaoAssistente({ sugestaoAgente, aoAbrirTela }) {
  const { selecionada, agora, historico, rascunho } = useAtendimento()
  const { definirRascunho, perguntarAssistente, obterLinkCardapio, enviar, salvarNota } = useAcoes()

  const [aberto, setAberto] = useState(false)
  const [dispensados, setDispensados] = useState(() => new Set())
  const [pergunta, setPergunta] = useState('')
  const [trocas, setTrocas] = useState([])
  const [perguntando, setPerguntando] = useState(false)

  const botaoRef = useRef(null)
  const painelRef = useRef(null)
  const corpoRef = useRef(null)
  const proximoIdRef = useRef(1)

  // Pergunta nova ou resposta chegando: rola para o fim, senão a troca mais
  // recente nasce escondida embaixo da rolagem (o corpo já vem cheio de
  // cartão antes dela).
  useEffect(() => {
    const el = corpoRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [trocas])

  // Troca de conversa: cartão dispensado e histórico de pergunta são desta
  // conversa, não seguem para a próxima. Ajuste durante a renderização
  // (mesmo padrão de `PainelAgente.jsx`), não em efeito: dispara no mesmo
  // render em vez de um render extra depois.
  const [conversaAnterior, setConversaAnterior] = useState(selecionada?.id ?? null)
  if ((selecionada?.id ?? null) !== conversaAnterior) {
    setConversaAnterior(selecionada?.id ?? null)
    setDispensados(new Set())
    setTrocas([])
    setPergunta('')
  }

  function fechar() {
    setAberto(false)
    botaoRef.current?.focus()
  }
  useEscape(aberto, fechar)

  // Sem fechar ao clicar fora (diferente do Sininho): a Thatiane usa o
  // composer e a conversa COM o balão aberto ao lado (é o próprio ponto do
  // cartão de ortografia, que lê o rascunho enquanto ela digita). Fecha por
  // Esc ou pelo X, nunca por engano.
  const dispensar = (chave) => setDispensados((atual) => new Set(atual).add(chave))
  const aplicarNoRascunho = (texto) => { if (selecionada) definirRascunho(selecionada.id, texto) }

  const cliente = selecionada ? resumoCliente(selecionada, historico, agora) : null
  const sentimento = selecionada ? sentimentoDaConversa(selecionada.mensagens) : null
  const cdc = selecionada ? dicaCDC(selecionada) : null
  const foto = selecionada ? analisarFotoSuspeita(selecionada) : null
  const padrao = selecionada ? padraoDeCompra(historico, selecionada.nome) : null
  const ortografia = selecionada && rascunho?.trim() ? revisarOrtografia(rascunho) : null

  const sentimentoNotavel = sentimento && sentimento.classificacao !== 'neutro'
  const pedeAtencao = Boolean(cdc || foto || (sentimento && sentimento.classificacao === 'negativo'))

  async function aoPerguntar(evento) {
    evento.preventDefault()
    const texto = pergunta.trim()
    if (!texto || perguntando || !selecionada) return
    const id = proximoIdRef.current
    proximoIdRef.current += 1
    setPergunta('')
    setPerguntando(true)
    setTrocas((atual) => [...atual, { id, pergunta: texto, resposta: null, acoes: [], erro: null }])
    try {
      // #1445: com as ações propostas, o texto pode vir vazio; a troca fica pronta mesmo assim.
      const { texto: resposta, acoes } = respostaDoAssistente(await perguntarAssistente(texto, selecionada, historico))
      setTrocas((atual) => atual.map((t) => (t.id === id ? { ...t, resposta: resposta || (acoes.length ? '' : '(sem resposta)'), acoes } : t)))
    } catch (erro) {
      setTrocas((atual) => atual.map((t) => (t.id === id ? { ...t, erro: erro.message } : t)))
    } finally {
      setPerguntando(false)
    }
  }

  // #1445: só o clique da atendente executa a ação proposta. A conversa é a da troca
  // (troca de conversa já zera as trocas, então é sempre a selecionada).
  const marcarAcao = (trocaId, chave, mudanca) => setTrocas((atual) => atual.map((t) => (t.id !== trocaId ? t : {
    ...t, acoes: t.acoes.map((a) => (a.chave === chave ? { ...a, ...mudanca } : a)),
  })))
  async function aoExecutarAcao(trocaId, acaoProposta) {
    if (!selecionada) return
    marcarAcao(trocaId, acaoProposta.chave, { estado: 'executando', erro: null })
    try {
      await executarAcaoDoAssistente(acaoProposta, selecionada, {
        obterLinkCardapio, enviar, salvarNota, definirRascunho, abrirTela: (tela) => aoAbrirTela?.(tela),
      })
      marcarAcao(trocaId, acaoProposta.chave, { estado: 'feita' })
    } catch (erro) {
      marcarAcao(trocaId, acaoProposta.chave, { estado: 'falhou', erro: erro.message })
    }
  }

  // Integração 50: o gatilho fixo no canto de baixo cobria o "⋯" da esteira e
  // o botão largo do fim da Ficha. Com a conversa aberta ele mora no
  // cabeçalho do Atendimento, ao lado de Encerrar; o balão continua
  // flutuando. Sem esse cabeçalho (celular, gaveta), volta para o canto.
  // Integração do visual: no celular o painel da conversa é a aba
  // #painel-da-aba (sem aria-label "Atendimento"), então a busca inclui ela; e
  // um observador reacha o cabeçalho quando a aba ou a conversa troca, em vez
  // de decidir uma vez só na montagem. Sem cabeçalho, o gatilho fica no canto.
  const [cabecalhoDoAtendimento, setCabecalhoDoAtendimento] = useState(null)
  useEffect(() => {
    const achar = () => [...document.querySelectorAll('section[aria-label="Atendimento"] header, #painel-da-aba header')]
      .find((h) => h.querySelector('h2'))?.querySelector(':scope > div') ?? null
    setCabecalhoDoAtendimento(achar())
    const observador = new MutationObserver(() => {
      const novo = achar()
      setCabecalhoDoAtendimento((atual) => (atual === novo ? atual : novo))
    })
    observador.observe(document.body, { childList: true, subtree: true })
    return () => observador.disconnect()
  }, [])

  const gatilho = (
    <Botao
      ref={botaoRef}
      variante="primario"
      className={cabecalhoDoAtendimento ? css.gatilhoNoCabecalho : css.gatilho}
      aria-label={aberto ? 'Fechar assistente' : 'Abrir assistente'}
      aria-expanded={aberto}
      aria-haspopup="dialog"
      onClick={() => setAberto((v) => !v)}
    >
      <Icone nome="agente" tamanho={26} />
      {!aberto && pedeAtencao && <span className={css.pontoAtencao} aria-hidden="true" />}
    </Botao>
  )

  return (
    <>
      {cabecalhoDoAtendimento ? createPortal(gatilho, cabecalhoDoAtendimento) : gatilho}

      {aberto && (
        <section className={css.painel} aria-label="Assistente" ref={painelRef}>
          <div className={css.cabecalho}>
            <h2><Icone nome="agente" /> Assistente</h2>
            {/* "Fechar" sozinho, não "Fechar assistente": o gatilho já usa esse
                rótulo (alterna Abrir/Fechar assistente), e o landmark
                aria-label="Assistente" do painel já dá o contexto para quem
                usa leitor de tela, sem repetir a palavra em dois controles. */}
            <Botao variante="texto" aria-label="Fechar" onClick={fechar}>
              <Icone nome="fechar" tamanho={20} />
            </Botao>
          </div>

          <div className={css.corpo} ref={corpoRef}>
            {!selecionada && (
              <div className={css.vazio}>
                <Vazio titulo="Nenhuma conversa aberta">
                  Abra uma conversa no Balcão para ver o perfil do cliente, o sentimento e as sugestões.
                </Vazio>
              </div>
            )}

            {selecionada && (
              <>
                <div className={css.cartoes}>
                  {cliente && !dispensados.has('cliente') && (
                    <CartaoCliente dado={cliente} aoDispensar={() => dispensar('cliente')} />
                  )}
                  {sentimentoNotavel && !dispensados.has('sentimento') && (
                    <CartaoSentimento dado={sentimento} aoDispensar={() => dispensar('sentimento')} />
                  )}
                  {cdc && !dispensados.has('cdc') && (
                    <CartaoCDC dado={cdc} aoDispensar={() => dispensar('cdc')} />
                  )}
                  {foto && !dispensados.has('foto') && (
                    <CartaoFotoSuspeita dado={foto} aoDispensar={() => dispensar('foto')} />
                  )}
                  {padrao && !dispensados.has('padrao') && (
                    <CartaoPadraoDeCompra dado={padrao} aoAplicar={aplicarNoRascunho} aoDispensar={() => dispensar('padrao')} />
                  )}
                  {ortografia?.mudou && !dispensados.has('ortografia') && (
                    <CartaoOrtografia dado={ortografia} aoAplicar={aplicarNoRascunho} aoDispensar={() => dispensar('ortografia')} />
                  )}
                </div>

                {sugestaoAgente}

                <div className={css.perguntas}>
                  <p className={css.tituloPerguntas}>Pergunte ao assistente</p>
                  {trocas.length > 0 && (
                    <div className={css.trocas}>
                      {trocas.map((t) => (
                        <div key={t.id} className={css.troca}>
                          <p className={css.perguntaDaThatiane}>{t.pergunta}</p>
                          {t.resposta && <p className={css.respostaDoAssistente}>{t.resposta}</p>}
                          <AcoesPropostas
                            acoes={t.acoes}
                            nomeCliente={selecionada.nome}
                            aoExecutar={(acaoProposta) => aoExecutarAcao(t.id, acaoProposta)}
                          />
                          {t.erro && <p className={css.respostaErro}><Icone nome="alerta" tamanho={16} /> {t.erro}</p>}
                          {t.resposta === null && !t.erro && <p className={css.respostaPensando}>Pensando…</p>}
                        </div>
                      ))}
                    </div>
                  )}
                  <form className={css.formPergunta} onSubmit={aoPerguntar}>
                    <CampoTexto
                      rotulo="Pergunte ao assistente sobre esta conversa"
                      rotuloOculto
                      className={css.campoPergunta}
                      value={pergunta}
                      onChange={(e) => setPergunta(e.target.value)}
                      placeholder="Ex.: manda o cardápio, anota que prefere sem cebola"
                      disabled={perguntando}
                    />
                    <Botao
                      tipo="submit"
                      variante="primario"
                      className={css.botaoEnviar}
                      icone="enviar"
                      aria-label="Perguntar"
                      disabled={perguntando || !pergunta.trim()}
                    />
                  </form>
                </div>
              </>
            )}
          </div>
        </section>
      )}
    </>
  )
}
