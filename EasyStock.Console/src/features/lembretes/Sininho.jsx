import { useEffect, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useEscape } from '../../hooks/useEscape'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { faixaDaJanela } from '../../dominio/entrega'
import {
  ORIGENS, chaveLembrete, descreverAtraso, descreverPrazo, novoLembrete, vencido,
} from '../../dominio/lembrete'
import { primeiroNome } from '../../dominio/mensagem'
import { EVENTOS_DE_SOM, ROTULO_DO_SOM } from '../../dominio/automatico'
import { horaCurta } from '../../dominio/formato'
import { ModalProgramarLembrete } from './ModalProgramarLembrete'
import css from './lembretes.module.css'

const ICONE_ORIGEM = {
  [ORIGENS.MANUAL]: 'relogio',
  [ORIGENS.PASSAGEM]: 'conversa',
  [ORIGENS.PAGAMENTO]: 'dollar-sign',
  [ORIGENS.AVISO]: 'bell',
}

// Avisos no aparelho (#1426): Web Push com o console fechado. Só no modo API,
// onde existe servidor para mandar; a permissão sai só deste botão.
const TEXTO_AVISO_NO_APARELHO = {
  desligado: 'Receba o aviso mesmo com o console fechado.',
  ativando: 'Ativando os avisos neste aparelho...',
  ativo: 'Avisos ativados neste aparelho.',
  bloqueado: 'Avisos bloqueados nas configurações do navegador deste aparelho.',
  'sem-chave': 'O EasyStok ainda não está configurado para mandar avisos ao aparelho.',
  indisponivel: 'Este navegador não recebe avisos com o console fechado. No iPhone, só com o console na tela de início.',
}

function AvisosNoAparelho({ estado, mensagem, ativar }) {
  const podeAtivar = estado === 'desligado' || estado === 'erro'
  return (
    <div className={`${css.secao} ${css.avisosAparelho}`}>
      <p className={css.tituloSecao}>Avisos no celular ou computador</p>
      <output className={css.textoAparelho}>
        {estado === 'erro' ? `Não deu para ativar: ${mensagem ?? 'tente de novo.'}` : TEXTO_AVISO_NO_APARELHO[estado]}
      </output>
      {(podeAtivar || estado === 'ativando') && (
        <Botao icone="bell" largo disabled={estado === 'ativando'} onClick={ativar}>
          {estado === 'erro' ? 'Tentar de novo' : 'Ativar avisos no celular/computador'}
        </Botao>
      )}
    </div>
  )
}

// Item 7 (rodada 10): reaproveita ícone que já tem sentido em outro lugar da
// tela para cada som, em vez de inventar glifo novo.
const ICONE_DO_EVENTO_SOM = {
  [EVENTOS_DE_SOM.NOVO_ATENDIMENTO]: 'conversa',
  [EVENTOS_DE_SOM.PAGAMENTO_CONFIRMADO]: 'dollar-sign',
  [EVENTOS_DE_SOM.PASSOU_PARA_VOCE]: 'hand',
}

// "Últimas horas" (achado 7): janela generosa, ela pode ter saído da tela por
// um bom tempo. `eventosSonoros` já vem podado a 30 no Provider.
const JANELA_SONS_RECENTES_MS = 6 * 60 * 60 * 1000

// "Vanessa · há 12 min": só quando o lembrete não tem detalhe próprio (é o
// caso do manual, que ela mesma escreveu). O automático guarda o detalhe
// curto do glossário ("Agente passou para você", "Pedido 0184 sem
// pagamento"), que diz mais que nome e tempo sozinhos.
function subtituloDoItem(lembrete, agora, conversas) {
  if (lembrete.detalhe) return lembrete.detalhe
  const conversa = lembrete.conversaId ? conversas.find((c) => c.id === lembrete.conversaId) : null
  const tempo = descreverAtraso(lembrete.quando, agora)
  return conversa ? `${primeiroNome(conversa.nome)} · ${tempo}` : tempo
}

// Novos e Vistos usam o mesmo item, com Concluir. "Adiar 10 min" saiu (corte
// #6): repetia o que Programar lembrete já faz, e o ponto à esquerda (seção
// 8) só aparece em Novos, quem chama decide com `comPonto`.
function ItemAtivo({ lembrete, agora, conversas, comPonto, aoAbrir, aoConcluir }) {
  return (
    <li className={css.item}>
      {/* Rodada 12 (issue #16): o ponto é "novo, ainda não visto". */}
      {comPonto && <span className={css.ponto} title="Novo, ainda não visto" aria-hidden="true" />}
      <Icone nome={ICONE_ORIGEM[lembrete.origem] ?? 'relogio'} tamanho={24} />
      <button
        type="button"
        className={css.alvo}
        disabled={!lembrete.conversaId}
        onClick={() => aoAbrir(lembrete.conversaId)}
      >
        <strong>{lembrete.titulo}</strong>
        <em>{subtituloDoItem(lembrete, agora, conversas)}</em>
      </button>
      <Botao
        variante="texto" className={css.acaoItem} title="Concluir" aria-label="Concluir"
        onClick={() => aoConcluir(lembrete)}
      >
        <Icone nome="check" tamanho={24} />
      </Botao>
    </li>
  )
}

// Programados não tem ação de concluir: só a hora à direita (seção 8).
function ItemProgramado({ lembrete, agora, realcado, aoAbrir }) {
  return (
    <li className={`${css.itemProgramado} ${realcado ? css.destaque : ''}`}>
      <Icone nome={ICONE_ORIGEM[lembrete.origem] ?? 'relogio'} tamanho={24} />
      <button
        type="button"
        className={css.alvo}
        disabled={!lembrete.conversaId}
        onClick={() => aoAbrir(lembrete.conversaId)}
      >
        <strong>{lembrete.titulo}</strong>
        {/* Rodada 12 (issue #16): o programado também diz de onde veio. */}
        {lembrete.detalhe && <em>{lembrete.detalhe}</em>}
      </button>
      <span className={css.hora}>{descreverPrazo(lembrete.quando, agora)}</span>
    </li>
  )
}

// Sino do topo (seção 8): badge do que venceu e não foi visto, dropdown
// ancorado com Novos / Vistos / Programados, e o "+" que abre a modal de
// programar. Visto é estilo Instagram: some da lista quando o painel fecha,
// não quando ela olha.
export function Sininho() {
  const {
    lembretes, agora, selecionada, vistos, conversas, eventosSonoros, fonteApi, avisosNoAparelho,
  } = useAtendimento()
  const {
    concluirLembrete, criarLembrete, marcarLembretesVistos, selecionar,
  } = useAcoes()
  const { janelas } = useCatalogo()

  const [aberto, setAberto] = useState(false)
  const [modalAberta, setModalAberta] = useState(false)
  const [congelados, setCongelados] = useState(null)
  const [vistosExpandido, setVistosExpandido] = useState(false)
  const [balancar, setBalancar] = useState(false)
  const [realceId, setRealceId] = useState(null)

  const sinoRef = useRef(null)
  const wrapperRef = useRef(null)
  const painelRef = useRef(null)
  const contagemAnteriorRef = useRef(0)
  const idsConhecidosRef = useRef(null)
  const aguardandoRealceRef = useRef(false)

  // Init preguiçoso: só a primeira renderização grava o ponto de partida.
  if (idsConhecidosRef.current === null) idsConhecidosRef.current = new Set(lembretes.map((l) => l.id))

  const naoVistos = lembretes.filter((l) => vencido(l, agora) && !vistos[chaveLembrete(l)])
  const contagem = naoVistos.length

  function fecharPainel() {
    setAberto(false)
    setCongelados(null)
    setVistosExpandido(false)
    sinoRef.current?.focus()
  }

  // Sino balança quando a contagem de novos SOBE (seção 8). Reduzido some
  // pela regra global de base.css, não precisa checar aqui.
  useEffect(() => {
    if (contagem > contagemAnteriorRef.current) setBalancar(true)
    contagemAnteriorRef.current = contagem
  }, [contagem])

  // Realce do item recém-criado: só dispara quando a modal acabou de
  // programar (aguardandoRealceRef), nunca num lembrete automático que chega
  // sozinho pela sincronização.
  useEffect(() => {
    const atuais = new Set(lembretes.map((l) => l.id))
    if (aguardandoRealceRef.current) {
      const novo = lembretes.find((l) => !idsConhecidosRef.current.has(l.id))
      if (novo) {
        aguardandoRealceRef.current = false
        setRealceId(novo.id)
        idsConhecidosRef.current = atuais
        const t = setTimeout(() => setRealceId(null), 600)
        return () => clearTimeout(t)
      }
    }
    idsConhecidosRef.current = atuais
    return undefined
  }, [lembretes])

  useEscape(aberto, fecharPainel)

  useEffect(() => {
    if (!aberto) return undefined
    const aoClicarFora = (evento) => {
      if (!wrapperRef.current?.contains(evento.target)) fecharPainel()
    }
    document.addEventListener('pointerdown', aoClicarFora)
    return () => document.removeEventListener('pointerdown', aoClicarFora)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto])

  useEffect(() => {
    if (aberto) painelRef.current?.querySelector('button')?.focus()
  }, [aberto])

  const alternarPainel = () => {
    if (aberto) { fecharPainel(); return }
    if (naoVistos.length > 0) marcarLembretesVistos(naoVistos.map(chaveLembrete))
    setCongelados(new Set(naoVistos.map(chaveLembrete)))
    setAberto(true)
  }

  const abrirConversa = (conversaId) => {
    if (!conversaId) return
    selecionar(conversaId)
    fecharPainel()
  }

  // Enquanto o painel está aberto, quem já era Novo na hora de abrir continua
  // em Novos mesmo depois de marcado visto (seção 8: "estilo Instagram").
  const chavesCongeladas = congelados ?? new Set()
  const ehNovo = (l) => chavesCongeladas.has(chaveLembrete(l)) || (vencido(l, agora) && !vistos[chaveLembrete(l)])
  const novos = lembretes.filter(ehNovo)
  const vistosLista = lembretes.filter((l) => vencido(l, agora) && vistos[chaveLembrete(l)] && !ehNovo(l))
  const programados = lembretes.filter((l) => !vencido(l, agora))
  const faixa = faixaDaJanela(janelas, selecionada?.pedido?.janela)
  // Item 7 (rodada 10): o que soou enquanto ela estava fora, mais recente
  // primeiro. Não entra na conta de "semNada" nem tem Concluir: é revisão,
  // não tarefa.
  const sonsRecentes = (eventosSonoros ?? [])
    .filter((s) => agora - s.quando <= JANELA_SONS_RECENTES_MS)
    .slice()
    .sort((a, b) => b.quando - a.quando)
  const semNada = novos.length === 0 && vistosLista.length === 0 && programados.length === 0

  const aoProgramar = ({ texto, quando, conversaId }) => {
    aguardandoRealceRef.current = true
    criarLembrete(novoLembrete({ titulo: texto, detalhe: null, quando, conversaId }))
    setModalAberta(false)
  }

  const rotuloBadge = contagem > 9 ? '9+' : String(contagem)
  const rotuloSino = contagem > 0 ? `Lembretes, ${contagem} ${contagem === 1 ? 'novo' : 'novos'}` : 'Lembretes'

  return (
    <div className={css.sininho} ref={wrapperRef}>
      <Botao
        ref={sinoRef}
        variante="texto"
        className={`${css.sino} ${balancar ? css.balanca : ''}`}
        aria-label={rotuloSino}
        title={rotuloSino}
        aria-expanded={aberto}
        aria-haspopup="menu"
        onAnimationEnd={() => setBalancar(false)}
        onClick={alternarPainel}
      >
        <Icone nome="bell" tamanho={24} />
        {contagem > 0 && <span className={css.badge} aria-hidden="true">{rotuloBadge}</span>}
      </Botao>

      {aberto && (
        <section className={css.painel} aria-label="Lembretes" ref={painelRef}>
          <div className={css.cabecalho}>
            <h2>Lembretes</h2>
            <Botao
              variante="texto" className={css.mais} aria-label="Programar lembrete" title="Programar lembrete"
              onClick={() => setModalAberta(true)}
            >
              <Icone nome="mais" tamanho={24} />
            </Botao>
          </div>

          {semNada && <p className={css.vazio}>Nada por agora</p>}

          {novos.length > 0 && (
            <div className={css.secao}>
              {/* Rodada 12 (issue #16): legenda do ponto ao lado de cada
                  cartão, o "alertazinho" que não se explicava. */}
              <p className={css.tituloSecao}>
                Novos
                <span className={css.legendaPonto}>
                  <span className={css.ponto} aria-hidden="true" /> ponto = ainda não visto
                </span>
              </p>
              <ul className={css.lista}>
                {novos.map((lembrete) => (
                  <ItemAtivo
                    key={lembrete.id}
                    lembrete={lembrete}
                    agora={agora}
                    conversas={conversas}
                    comPonto
                    aoAbrir={abrirConversa}
                    aoConcluir={concluirLembrete}
                  />
                ))}
              </ul>
            </div>
          )}

          {vistosLista.length > 0 && (
            <div className={css.secao}>
              <Botao
                variante="texto" className={css.botaoVistos} aria-expanded={vistosExpandido}
                onClick={() => setVistosExpandido((v) => !v)}
              >
                Vistos · {vistosLista.length}
              </Botao>
              {vistosExpandido && (
                <ul className={css.lista}>
                  {vistosLista.map((lembrete) => (
                    <ItemAtivo
                      key={lembrete.id}
                      lembrete={lembrete}
                      agora={agora}
                      conversas={conversas}
                      comPonto={false}
                      aoAbrir={abrirConversa}
                      aoConcluir={concluirLembrete}
                    />
                  ))}
                </ul>
              )}
            </div>
          )}

          {programados.length > 0 && (
            <div className={css.secao}>
              <p className={css.tituloSecao}>Programados</p>
              <ul className={css.lista}>
                {programados.map((lembrete) => (
                  <ItemProgramado
                    key={lembrete.id}
                    lembrete={lembrete}
                    agora={agora}
                    realcado={lembrete.id === realceId}
                    aoAbrir={abrirConversa}
                  />
                ))}
              </ul>
            </div>
          )}

          {sonsRecentes.length > 0 && (
            <div className={css.secao}>
              <p className={css.tituloSecao}>Sons de agora há pouco</p>
              <ul className={css.lista}>
                {sonsRecentes.map((som) => {
                  const conversa = conversas.find((c) => c.id === som.conversaId)
                  return (
                    <li key={som.id} className={css.item}>
                      <Icone nome={ICONE_DO_EVENTO_SOM[som.evento] ?? 'relogio'} tamanho={24} />
                      <span className={css.alvo}>
                        <strong>{ROTULO_DO_SOM[som.evento] ?? som.evento}</strong>
                        <em>
                          {conversa ? `${primeiroNome(conversa.nome)} · ` : ''}
                          {horaCurta(new Date(som.quando).toISOString())}
                        </em>
                      </span>
                    </li>
                  )
                })}
              </ul>
            </div>
          )}

          {fonteApi && avisosNoAparelho && <AvisosNoAparelho {...avisosNoAparelho} />}
        </section>
      )}

      {modalAberta && (
        <ModalProgramarLembrete
          conversa={selecionada}
          faixa={faixa}
          agora={agora}
          aoProgramar={aoProgramar}
          aoFechar={() => setModalAberta(false)}
        />
      )}
    </div>
  )
}
