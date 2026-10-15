import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { AlcaLargura } from '../../componentes/AlcaLargura'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useEscape } from '../../hooks/useEscape'
import { CELULAR, useTamanhoTela } from '../../hooks/useTamanhoTela'
import { CHAVE_ASSISTENTE, usePreferenciaDePainel } from '../../aplicacao/preferenciaDosPaineis'
import {
  ASSISTENTE, MARGEM_ASSISTENTE, larguraMaxima, limitar, normalizarAssistente, posicaoDentroDaTela,
} from '../../dominio/paineis'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { canalDaConversa } from '../../dominio/canal'
import { permissaoDeEscrita } from '../../dominio/janela'
import {
  analisarFotoSuspeita, dicaCDC, padraoDeCompra, resumoCliente, revisarOrtografia, sentimentoDaConversa,
} from '../../dominio/assistente'
import { executarAcaoDoAssistente } from '../../dominio/acoesDoAssistente'
import {
  CartaoCDC, CartaoCliente, CartaoFotoSuspeita, CartaoOrtografia, CartaoPadraoDeCompra, CartaoSentimento,
} from './Cartoes'
import { AcoesPropostas } from './AcoesPropostas'
import { useConversaAssistente } from './useConversaAssistente'
import css from './assistente.module.css'

// Balão flutuante do canto inferior direito (fala do dono, 24/09/2026 04h12):
// o que hoje fazia papel de assistente dentro do fio (`PainelAgente`, seção
// "Sugerir") mora agora aqui dentro, num cartão a mais, em vez de dividir a
// tela com a conversa. `sugestaoAgente` chega pronto de cima (mesma regra de
// `PainelAtendimento`: feature não monta feature; quem junta as duas
// features é `app/App.jsx`).
// #1445: `aoAbrirTela('cardapio' | 'comanda')` também vem do App, pelo mesmo motivo.
// #1806: encaixa à esquerda, à direita ou solto (arrastável pela barra de título), com largura
// pela alça; tudo lembrado por aparelho. `areaDoBalcao` (tela e colunas) vem do App para a
// largura máxima deixar a conversa legível (`dominio/paineis.js`).
const POSICOES = [
  { lado: 'esquerda', rotulo: 'Encaixar à esquerda', icone: 'encaixarEsquerda' },
  { lado: 'flutuante', rotulo: 'Flutuante', icone: 'solto' },
  { lado: 'direita', rotulo: 'Encaixar à direita', icone: 'encaixarDireita' },
]
const PASSO_MOVER = 16
const PASSO_MOVER_GRANDE = 64

export function BalaoAssistente({ sugestaoAgente, aoAbrirTela, areaDoBalcao }) {
  const { selecionada, agora, historico, rascunho, fonteApi } = useAtendimento()
  const { canais } = useCatalogo()
  const { definirRascunho, perguntarAssistente, obterLinkCardapio, enviar, salvarNota, devolverAutomatico } = useAcoes()

  const [aberto, setAberto] = useState(false)
  const [dispensados, setDispensados] = useState(() => new Set())
  const botaoRef = useRef(null)
  const painelRef = useRef(null)
  const corpoRef = useRef(null)
  const campoRef = useRef(null)
  const painelId = useId()
  const tituloId = `${painelId}-titulo`
  const [posicaoPainel, definirPosicaoPainel] = usePreferenciaDePainel(CHAVE_ASSISTENTE, normalizarAssistente)
  const celular = useTamanhoTela() === CELULAR
  const [alturaPainel, setAlturaPainel] = useState(0)
  const limparArrasto = useRef(null)
  useEffect(() => () => limparArrasto.current?.(), [])
  const { pergunta, setPergunta, trocas, perguntando, aoPerguntar, aoRepetir, aoExecutarAcao } = useConversaAssistente({
    conversa: selecionada,
    historico,
    perguntar: perguntarAssistente,
    executarAcao: (proposta, conversa, contextoAtual) => {
      const conferirContexto = () => {
        if (!contextoAtual()) throw new Error('A conversa mudou. Revise a ação no cliente correto.')
      }
      return executarAcaoDoAssistente(proposta, conversa, {
        obterLinkCardapio,
        devolverAutomatico: (...args) => { conferirContexto(); return devolverAutomatico(...args) },
        enviar: (...args) => { conferirContexto(); return enviar(...args) },
        salvarNota: (...args) => { conferirContexto(); return salvarNota(...args) },
        definirRascunho: (...args) => { conferirContexto(); return definirRascunho(...args) },
        abrirTela: (tela) => { conferirContexto(); return aoAbrirTela?.(tela) },
      })
    },
  })

  useEffect(() => {
    if (aberto) (campoRef.current ?? painelRef.current)?.focus()
  }, [aberto])

  // Pergunta nova ou resposta chegando: rola para o fim, senão a troca mais
  // recente nasce escondida embaixo da rolagem (o corpo já vem cheio de
  // cartão antes dela).
  const ultimaTroca = trocas.at(-1)
  useEffect(() => {
    const el = corpoRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [ultimaTroca?.id, ultimaTroca?.resposta, ultimaTroca?.erro])

  // Troca de conversa: cartão dispensado e histórico de pergunta são desta
  // conversa, não seguem para a próxima. Ajuste durante a renderização
  // (mesmo padrão de `PainelAgente.jsx`), não em efeito: dispara no mesmo
  // render em vez de um render extra depois.
  const [conversaAnterior, setConversaAnterior] = useState(selecionada?.id ?? null)
  if ((selecionada?.id ?? null) !== conversaAnterior) {
    setConversaAnterior(selecionada?.id ?? null)
    setDispensados(new Set())
  }

  function fechar() {
    setAberto(false)
    botaoRef.current?.focus()
  }
  useEscape(aberto, (evento) => {
    if (document.querySelector('dialog[open]')) return
    evento.preventDefault()
    fechar()
  })

  // Sem fechar ao clicar fora (diferente do Sininho): a Thatiane usa o
  // composer e a conversa COM o balão aberto ao lado (é o próprio ponto do
  // cartão de ortografia, que lê o rascunho enquanto ela digita). Fecha por
  // Esc ou pelo X, nunca por engano.
  const dispensar = (chave, evento) => {
    const proximo = evento?.currentTarget.closest('article')?.nextElementSibling?.querySelector('button')
    const destino = proximo ?? campoRef.current
    destino?.focus()
    setDispensados((atual) => new Set(atual).add(chave))
  }
  const aplicarNoRascunho = (texto) => { if (selecionada) definirRascunho(selecionada.id, texto) }

  const cliente = selecionada ? resumoCliente(selecionada, historico, agora) : null
  const sentimento = selecionada ? sentimentoDaConversa(selecionada.mensagens) : null
  const cdc = selecionada ? dicaCDC(selecionada) : null
  const foto = !fonteApi && selecionada ? analisarFotoSuspeita(selecionada) : null
  const padrao = selecionada ? padraoDeCompra(historico, selecionada.nome) : null
  const ortografia = selecionada && rascunho?.trim() ? revisarOrtografia(rascunho) : null
  const permissao = permissaoDeEscrita(selecionada, agora, canalDaConversa(canais, selecionada))
  const motivoDeEnvio = permissao.pode ? null : [permissao.motivo?.titulo, permissao.motivo?.detalhe].filter(Boolean).join(' ')

  const sentimentoNotavel = sentimento && sentimento.classificacao !== 'neutro'
  const pedeAtencao = Boolean(cdc || foto || (sentimento && sentimento.classificacao === 'negativo'))

  function enviarPergunta(evento) {
    evento.preventDefault()
    aoPerguntar()
  }

  // Âncora explícita no cabeçalho de cada painel: procurar qualquer
  // header com h2 inseria o assistente no papel da comanda. O slot mantém o botão
  // acessível sem cobrir as ações fixas da Cobrança.
  const [cabecalhoDoAtendimento, setCabecalhoDoAtendimento] = useState(null)
  useEffect(() => {
    const achar = () => document.querySelector('[data-ancora-assistente]')
    setCabecalhoDoAtendimento(achar())
    const observador = new MutationObserver(() => {
      const novo = achar()
      setCabecalhoDoAtendimento((atual) => (atual === novo ? atual : novo))
    })
    observador.observe(document.body, { childList: true, subtree: true })
    return () => observador.disconnect()
  }, [])

  // Largura dentro do que a tela permite agora; o solto usa a máxima do encaixe à direita.
  const area = areaDoBalcao ?? { tela: window.innerWidth, colunas: false }
  const { lado } = posicaoPainel
  const solto = lado === 'flutuante' && !celular
  const larguraMaximaPainel = larguraMaxima({
    ...area, lado: lado === 'esquerda' ? 'esquerda' : 'direita', margem: MARGEM_ASSISTENTE, faixa: ASSISTENTE,
  })
  const larguraPainel = limitar(posicaoPainel.largura, ASSISTENTE.min, larguraMaximaPainel)
  const dentroDaTela = (x, y) => posicaoDentroDaTela({
    x, y, largura: larguraPainel, altura: alturaPainel,
    tela: window.innerWidth, alturaTela: window.innerHeight, margem: MARGEM_ASSISTENTE,
  })
  const posicao = dentroDaTela(posicaoPainel.x, posicaoPainel.y)

  // Altura medida para o solto não passar da borda de baixo quando o conteúdo cresce. O
  // ResizeObserver entrega a primeira medida logo depois do layout, antes de pintar.
  useLayoutEffect(() => {
    const el = painelRef.current
    if (!aberto || !el || typeof ResizeObserver === 'undefined') return undefined
    const observador = new ResizeObserver(() => setAlturaPainel(el.offsetHeight))
    observador.observe(el)
    return () => observador.disconnect()
  }, [aberto])

  // Arrasto do solto pela barra de título: código próprio, só pointermove, como a AlcaLargura.
  // O dnd-kit do App fica com o arrasto de prato.
  const aoDescerNoCabecalho = (eventoDescida) => {
    if (!solto || eventoDescida.button !== 0) return
    if (eventoDescida.target.closest('button') && !eventoDescida.target.closest('[data-pega]')) return
    limparArrasto.current?.()
    eventoDescida.preventDefault()
    const inicio = { x: eventoDescida.clientX, y: eventoDescida.clientY, posicao }
    const ponteiro = eventoDescida.pointerId
    function mover(evento) {
      if (evento.pointerId !== ponteiro) return
      definirPosicaoPainel(dentroDaTela(
        inicio.posicao.x + evento.clientX - inicio.x,
        inicio.posicao.y + evento.clientY - inicio.y,
      ))
    }
    function limpar() {
      window.removeEventListener('pointermove', mover)
      window.removeEventListener('pointerup', soltar)
      window.removeEventListener('pointercancel', soltar)
      window.removeEventListener('blur', soltar)
      limparArrasto.current = null
    }
    function soltar(evento) {
      if (evento.pointerId !== undefined && evento.pointerId !== ponteiro) return
      limpar()
    }
    limparArrasto.current = limpar
    window.addEventListener('pointermove', mover)
    window.addEventListener('pointerup', soltar)
    window.addEventListener('pointercancel', soltar)
    window.addEventListener('blur', soltar)
  }

  // Teclado na pega: setas movem 16 px, Shift 64 px.
  const aoTeclarNaPega = (evento) => {
    const passo = evento.shiftKey ? PASSO_MOVER_GRANDE : PASSO_MOVER
    const delta = { ArrowLeft: [-passo, 0], ArrowRight: [passo, 0], ArrowUp: [0, -passo], ArrowDown: [0, passo] }[evento.key]
    if (!delta || evento.altKey || evento.ctrlKey || evento.metaKey) return
    evento.preventDefault()
    definirPosicaoPainel(dentroDaTela(posicao.x + delta[0], posicao.y + delta[1]))
  }

  const estiloPainel = celular ? undefined : {
    width: `${larguraPainel}px`,
    ...(solto ? { left: `${posicao.x}px`, top: `${posicao.y}px`, right: 'auto', bottom: 'auto' } : {}),
  }
  const classePainel = [css.painel, !celular && lado === 'esquerda' ? css.painelEsquerda : '', solto ? css.painelSolto : '']
    .filter(Boolean).join(' ')
  const alca = celular ? null : (
    <AlcaLargura
      rotulo="Redimensionar assistente"
      valor={larguraPainel}
      min={ASSISTENTE.min}
      max={larguraMaximaPainel}
      padrao={Math.min(ASSISTENTE.padrao, larguraMaximaPainel)}
      invertida={lado === 'direita'}
      aoMudar={(valor) => definirPosicaoPainel({ largura: valor })}
    />
  )

  const gatilho = (
    <Botao
      ref={botaoRef}
      // #1761 (L5): no cabeçalho da conversa é apoio, não o próximo passo: secundário.
      variante={cabecalhoDoAtendimento ? 'secundario' : 'primario'}
      className={cabecalhoDoAtendimento ? css.gatilhoNoCabecalho : css.gatilho}
      aria-label={aberto ? 'Fechar assistente' : 'Abrir assistente'}
      aria-expanded={aberto}
      aria-haspopup="dialog"
      aria-controls={aberto ? painelId : undefined}
      onClick={() => aberto ? fechar() : setAberto(true)}
    >
      <Icone nome="agente" tamanho={26} />
      {cabecalhoDoAtendimento && <span className={css.rotuloGatilho}>Assistente</span>}
      {!aberto && pedeAtencao && <span className={css.pontoAtencao} aria-hidden="true" />}
    </Botao>
  )

  return (
    <>
      {cabecalhoDoAtendimento ? createPortal(gatilho, cabecalhoDoAtendimento) : gatilho}

      {aberto && (
        <section className={classePainel} style={estiloPainel} id={painelId} role="dialog" aria-modal="false" aria-labelledby={tituloId} tabIndex={-1} ref={painelRef}>
          {lado === 'direita' && alca}
          <div className={css.colunaPainel}>
          <div className={`${css.cabecalho} ${solto ? css.cabecalhoArrastavel : ''}`} onPointerDown={aoDescerNoCabecalho}>
            <div className={css.identidade}>
              <h2 id={tituloId}><Icone nome="agente" /> Assistente</h2>
              {selecionada && <p className={css.clienteDoPainel}>{selecionada.nome}</p>}
            </div>
            {!celular && (
              <fieldset className={css.posicoes}>
                <legend className="sr">Posição do assistente</legend>
                {solto && (
                  <button
                    type="button"
                    data-pega=""
                    className={`${css.botaoPosicao} ${css.pega}`}
                    aria-label="Mover assistente"
                    aria-describedby={`${painelId}-mover`}
                    title="Arraste ou use as setas para mover"
                    onKeyDown={aoTeclarNaPega}
                  >
                    <Icone nome="pega" />
                    <span className="sr" id={`${painelId}-mover`}>Use as setas para mover. Shift move mais.</span>
                  </button>
                )}
                {POSICOES.map((p) => (
                  <button
                    type="button"
                    key={p.lado}
                    className={css.botaoPosicao}
                    aria-label={p.rotulo}
                    title={p.rotulo}
                    aria-pressed={lado === p.lado}
                    onClick={() => definirPosicaoPainel({ lado: p.lado })}
                  >
                    <Icone nome={p.icone} />
                  </button>
                ))}
              </fieldset>
            )}
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
                <p className={css.contexto}>Revise as sugestões. Cada ação só acontece quando você confirma.</p>
                <div className={css.cartoes}>
                  {cliente && !dispensados.has('cliente') && (
                    <CartaoCliente dado={cliente} aoDispensar={(e) => dispensar('cliente', e)} />
                  )}
                  {sentimentoNotavel && !dispensados.has('sentimento') && (
                    <CartaoSentimento dado={sentimento} aoDispensar={(e) => dispensar('sentimento', e)} />
                  )}
                  {cdc && !dispensados.has('cdc') && (
                    <CartaoCDC dado={cdc} aoDispensar={(e) => dispensar('cdc', e)} />
                  )}
                  {foto && !dispensados.has('foto') && (
                    <CartaoFotoSuspeita dado={foto} aoDispensar={(e) => dispensar('foto', e)} />
                  )}
                  {padrao && !dispensados.has('padrao') && (
                    <CartaoPadraoDeCompra dado={padrao} aoAplicar={fonteApi ? undefined : aplicarNoRascunho} aoDispensar={(e) => dispensar('padrao', e)} />
                  )}
                  {ortografia?.mudou && !dispensados.has('ortografia') && (
                    <CartaoOrtografia dado={ortografia} aoAplicar={aplicarNoRascunho} aoDispensar={(e) => dispensar('ortografia', e)} />
                  )}
                </div>

                {sugestaoAgente}

                <div className={css.perguntas}>
                  <p className={css.tituloPerguntas}>Pergunte ao assistente</p>
                  {trocas.length > 0 && (
                    <div className={css.trocas}>
                      {trocas.map((t) => (
                        <div key={t.id} className={css.troca}>
                          <div className={css.perguntaDaThatiane}><span className={css.autor}>Você</span><p>{t.pergunta}</p></div>
                          {t.resposta && <div className={css.respostaDoAssistente}><span className={css.autor}>Assistente</span><p>{t.resposta}</p></div>}
                          {t.acoes.length > 0 && <p className={css.tituloAcoes}>Ações para confirmar</p>}
                          <AcoesPropostas
                            acoes={t.acoes}
                            nomeCliente={selecionada.nome}
                            motivoDeEnvio={motivoDeEnvio}
                            semCadastro={fonteApi && !selecionada.clienteId}
                            aoExecutar={(acaoProposta) => aoExecutarAcao(t.id, acaoProposta)}
                          />
                          {t.erro && (
                            <div className={css.falhaConsulta}>
                              <p className={css.respostaErro} role="alert"><Icone nome="alerta" tamanho={16} /> {t.erro}</p>
                              <Botao variante="secundario" disabled={perguntando} onClick={() => {
                                campoRef.current?.focus()
                                aoRepetir(t)
                              }}>Tentar novamente</Botao>
                            </div>
                          )}
                          {t.resposta === null && !t.erro && <output className={css.respostaPensando}>Consultando o assistente…</output>}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
          {selecionada && (
            <form className={css.formPergunta} onSubmit={enviarPergunta} aria-busy={perguntando}>
              <CampoArea
                ref={campoRef}
                rotulo="Pergunte ao assistente sobre esta conversa"
                rotuloOculto
                className={css.campoPergunta}
                value={pergunta}
                onChange={(e) => setPergunta(e.target.value)}
                placeholder="Ex.: sugira uma resposta ou prepare uma nota"
                rows={2}
                maxLength={2000}
                dica="Ctrl+Enter para perguntar. Enter cria uma nova linha."
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey) && !e.nativeEvent?.isComposing) enviarPergunta(e)
                }}
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
          )}
          </div>
          {lado !== 'direita' && alca}
        </section>
      )}
    </>
  )
}
