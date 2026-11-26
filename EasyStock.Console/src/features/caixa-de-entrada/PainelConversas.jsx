import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { EsqueletoDeLista } from '../../componentes/Esqueleto'
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { cargaDaLista } from '../../dominio/cargaDaLista'
import { useChamadaEstavel } from '../../hooks/useChamadaEstavel'
import { useAoAbrirConversa } from '../../aplicacao/aberturaDeConversa'
import { CELULAR, useTamanhoTela } from '../../hooks/useTamanhoTela'
import { CartaoConversa } from './CartaoConversa'
import { BarraBalcao } from './BarraBalcao'
import { NaoEntregues } from './NaoEntregues'
import { focaOCampoAoAbrir, focarCampoDeMensagem, temPonteiroFino } from './focoDoCampo'
import css from './caixa.module.css'

// Grupo recolhido do fim de "Todas" (seção 1): Encerradas e Bloqueados, cada
// um com sua própria lista e sua própria memória de aberto/fechado — abrir um
// não deve fechar o outro.
function GrupoRecolhido({
  titulo, conversas, aberto, aoAlternar, agora, automaticoPausado, pagamentosNaoVistos, aoAbrir, chave, busca, selecionadaId, usuario,
}) {
  if (conversas.length === 0) return null
  return (
    <div className={css.grupo}>
      <button
        type="button"
        className={css.tituloGrupo}
        aria-expanded={aberto}
        onClick={aoAlternar}
      >
        <Icone nome="chevron-right" tamanho={20} className={aberto ? css.chevronAberto : ''} />
        {titulo} · {conversas.length}
      </button>
      {aberto && conversas.map((conversa) => (
        <CartaoConversa
          key={conversa.id}
          conversa={conversa}
          selecionada={conversa.id === selecionadaId}
          agora={agora}
          pausado={automaticoPausado?.[conversa.id] ?? false}
          pagamentoNaoVisto={pagamentosNaoVistos?.has(conversa.id) ?? false}
          grupo={chave}
          usuario={usuario}
          busca={busca}
          aoAbrir={aoAbrir}
        />
      ))}
    </div>
  )
}

// Lista vazia em uma linha de texto e uma ação (#1442): o `Vazio` em linha (#2185, R13).
function VazioEmLinha({ texto, acao, aoAgir }) {
  return <Vazio variante="linha" titulo={texto} acao={acao && <Botao variante="texto" onClick={aoAgir}>{acao}</Botao>} />
}

// #1408: `buscaDeClientes` é o componente que a Moldura entrega para a busca achar cliente cadastrado sem conversa.
export function PainelConversas({ aoAbrir, buscaDeClientes: BuscaDeClientes = null, acoesCabecalho }) {
  const {
    conversas, visiveis, encerradasDoBalcao, bloqueadasDoBalcao, selecionadaId, filtros, agora,
    automaticoPausado, aberta, horarioDaLoja, pagamentosNaoVistos, fonteApi, sincronizacao, sessao,
  } = useAtendimento()
  const { mudarFiltro, selecionar } = useAcoes()
  const [gruposAbertos, setGruposAbertos] = useState({ encerradas: false, bloqueados: false })
  // Com busca, os dois grupos abrem sozinhos: achar a cliente encerrada ou
  // bloqueada e ela ficar escondida atrás de um recolhido parecia "não achou".
  // Ajuste de estado na troca de busca, no render (padrão do React para
  // estado que depende de prop), sem efeito.
  const [buscaVista, setBuscaVista] = useState(filtros.busca)
  if (buscaVista !== filtros.busca) {
    setBuscaVista(filtros.busca)
    const abrir = Boolean(filtros.busca.trim())
    setGruposAbertos({ encerradas: abrir, bloqueados: abrir })
  }

  // #2135: abrir a conversa leva o foco ao campo de mensagem, por clique ou por teclado, menos no
  // celular. O campo só existe na tela depois que a conversa nova monta, então o pedido espera o commit.
  const tamanho = useTamanhoTela()
  const [pedidosDeFoco, setPedidosDeFoco] = useState(0)
  useEffect(() => {
    if (pedidosDeFoco > 0) focarCampoDeMensagem()
  }, [pedidosDeFoco])

  // #2185 (F26): sino, aviso na tela e "Não entregues" abrem a conversa por fora da lista; o foco vai ao
  // campo igual (antes ficava no sino ou caía no corpo da página, e a digitação se perdia).
  useAoAbrirConversa(() => {
    if (focaOCampoAoAbrir({ celular: tamanho === CELULAR, ponteiroFino: temPonteiroFino() })) setPedidosDeFoco((n) => n + 1)
  })

  // #1577: identidade fixa, senão o `memo` do cartão não segura.
  const abrir = useChamadaEstavel((id) => {
    selecionar(id)
    aoAbrir?.(id)
    if (focaOCampoAoAbrir({ celular: tamanho === CELULAR, ponteiroFino: temPonteiroFino() })) {
      setPedidosDeFoco((n) => n + 1)
    }
  })

  function alternarGrupo(chave) {
    setGruposAbertos((atual) => ({ ...atual, [chave]: !atual[chave] }))
  }

  // A lista não pula debaixo do dedo (seção 1): enquanto o ponteiro está sobre
  // ela ou o foco está dentro dela, a reordenação espera; aplica 300 ms depois
  // que ele sai. Só a ORDEM congela — o conteúdo de cada cartão continua vindo
  // de `visiveis`, então mensagem nova ainda aparece na hora.
  // `ordemCongelada` mora em estado, não em ref: o valor entra na conta de
  // `lista` durante o render, e ref lido no render é o tipo de coisa que o
  // lint de refs (react/refs) reprova com razão — o componente não acorda
  // sozinho quando só o ref muda.
  const [ordemCongelada, setOrdemCongelada] = useState(null)
  const destravarRef = useRef(null)

  useEffect(() => () => clearTimeout(destravarRef.current), [])

  function travar() {
    clearTimeout(destravarRef.current)
    setOrdemCongelada((atual) => atual ?? visiveis.map((c) => c.id))
  }
  function destravarComAtraso() {
    clearTimeout(destravarRef.current)
    destravarRef.current = setTimeout(() => setOrdemCongelada(null), 300)
  }

  const listaBase = useMemo(() => {
    if (!ordemCongelada) return visiveis
    const porId = new Map(visiveis.map((c) => [c.id, c]))
    const congeladas = new Set(ordemCongelada)
    const ordenada = ordemCongelada.map((id) => porId.get(id)).filter(Boolean)
    const novas = visiveis.filter((c) => !congeladas.has(c.id))
    return [...ordenada, ...novas]
  }, [visiveis, ordemCongelada])

  // #3363 (S4): responder muda o motivo e a hora da conversa aberta e, sem isto, o cartão dela sai do lugar e os de baixo
  // sobem de uma vez (hoje 365 px e 6 × 93,2 px). Só o cartão da conversa aberta é preso: fica logo abaixo do vizinho que
  // tinha (ou no mesmo índice, se o vizinho saiu), atenuado se já não pertence a esta lista, até a troca de conversa. O resto
  // da lista ordena e recebe as novas como sempre. Mudar aba, canal, busca, ordem ou responsável recomeça a conta, e a âncora
  // é refeita se nasceu antes de a conversa estar na lista (primeira carga, aberta por fora). Estado no render, como `buscaVista`.
  const chaveDaLista = JSON.stringify([filtros.busca, filtros.aba, filtros.canais, filtros.ordenacao, filtros.responsavel])
  const [ancora, setAncora] = useState(null)
  const indiceNaBase = selecionadaId == null ? -1 : listaBase.findIndex((c) => c.id === selecionadaId)
  const ancoraValeu = ancora?.id === selecionadaId && ancora.chave === chaveDaLista && !(ancora.indice < 0 && indiceNaBase >= 0)
  if (!ancoraValeu && (selecionadaId != null || ancora != null)) {
    setAncora(selecionadaId == null ? null : { id: selecionadaId, chave: chaveDaLista, indice: indiceNaBase, acima: indiceNaBase > 0 ? listaBase[indiceNaBase - 1].id : null })
  }
  const lista = useMemo(() => {
    if (!ancoraValeu || ancora.indice < 0) return listaBase
    const aberta = listaBase.find((c) => c.id === selecionadaId) ?? conversas.find((c) => c.id === selecionadaId)
    if (!aberta) return listaBase
    const resto = listaBase.filter((c) => c.id !== selecionadaId)
    const abaixoDe = ancora.acima == null ? -1 : resto.findIndex((c) => c.id === ancora.acima)
    const posicao = ancora.acima == null ? 0 : abaixoDe >= 0 ? abaixoDe + 1 : Math.min(ancora.indice, resto.length)
    return [...resto.slice(0, posicao), aberta, ...resto.slice(posicao)]
  }, [listaBase, ancora, ancoraValeu, selecionadaId, conversas])
  const foraDaLista = ancoraValeu && ancora.indice >= 0 && indiceNaBase < 0 ? selecionadaId : null

  // #3363 (S1): a lista é uma parada de Tab só (foco itinerante). A parada é o cartão aberto; sem aberto, o último que teve
  // foco; sem ele, o primeiro. ↑ ↓ j k Home End andam entre os cartões. Estado, não ref: o id entra na conta do render.
  // Com o foco dentro da lista a parada é o cartão focado (senão o Tab de um cartão qualquer cairia no aberto, dentro da lista).
  const [focoId, setFocoId] = useState(null)
  const [focoNaLista, setFocoNaLista] = useState(false)
  const naLista = (id) => id != null && lista.some((c) => String(c.id) === String(id))
  const ativoId = String(focoNaLista && naLista(focoId) ? focoId : naLista(selecionadaId) ? selecionadaId : naLista(focoId) ? focoId : lista[0]?.id)
  function aoTeclarNaLista(e) {
    // Só com o foco num cartão e sem modificador (WCAG 2.1.4): j e k em campo de texto continuam escrevendo.
    if (!e.target.matches?.('button[data-conversa-id]') || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return
    const passo = { ArrowDown: 1, j: 1, ArrowUp: -1, k: -1 }[e.key]
    if (passo === undefined && e.key !== 'Home' && e.key !== 'End') return
    const cartoes = [...e.currentTarget.querySelectorAll('button[data-conversa-id]')]
    const alvo = e.key === 'Home' ? 0 : e.key === 'End' ? cartoes.length - 1 : cartoes.indexOf(e.target) + passo
    e.preventDefault()
    cartoes[Math.min(Math.max(alvo, 0), cartoes.length - 1)]?.focus()
  }

  const semCanal = filtros.canais.length === 0
  const semBusca = !filtros.busca.trim()
  // #2227: antes da primeira leitura a lista vazia não é "nenhuma conversa".
  const carga = cargaDaLista({ fonteApi, estado: sincronizacao?.estado, total: conversas.length })
  const vazia = lista.length === 0 && !carga
  // #2944 (E1-10): a entrada animada é só do cartão que chegou depois da primeira lista, não de todo remonte.
  const vistas = useRef(null)
  const novas = vistas.current ? new Set(lista.map((c) => c.id).filter((id) => !vistas.current.has(id))) : new Set()
  useEffect(() => { if (!carga) vistas.current = new Set(lista.map((c) => c.id)) })
  const rolagem = useRef(null)
  useLayoutEffect(() => {
    if (!carga && rolagem.current) rolagem.current.scrollTop = filtros.posicao ?? 0
  }, [carga, chaveDaLista])

  return (
    <>
      <BarraBalcao filtros={filtros} aoMudar={mudarFiltro} acoesCabecalho={acoesCabecalho} semContagem={Boolean(carga)} />
      <NaoEntregues aoAbrir={aoAbrir} />
      <div
        ref={rolagem}
        onScroll={e => mudarFiltro('posicao', e.currentTarget.scrollTop)}
        className={css.rolagem}
        onMouseEnter={travar}
        onMouseLeave={destravarComAtraso}
        onFocusCapture={travar}
        onBlurCapture={destravarComAtraso}
      >
        {/* #2762: esqueleto com a altura do cartão no lugar do texto solto; o texto segue no DOM (.sr). */}
        {carga === 'carregando' && (
          <EsqueletoDeLista linhas={6} altura="calc(var(--toque) * 1.8)">Carregando as conversas…</EsqueletoDeLista>
        )}
        {carga === 'erro' && (
          <VazioEmLinha texto="Não consegui carregar as conversas. Tento de novo em instantes." acao="Tentar de novo" aoAgir={() => globalThis.dispatchEvent(new Event('easystok:sincronizar-agora'))} />
        )}
        {/* #1442: vazio em uma linha, com o atalho certo. O bloco grande
            "Ninguém esperando você" ocupava a coluna e escondia o resto. */}
        {vazia && !semBusca
          && (filtros.aba === 'precisa' || encerradasDoBalcao.length + bloqueadasDoBalcao.length === 0) && (
          <VazioEmLinha texto={`Nenhuma conversa com "${filtros.busca.trim()}".`} acao="Limpar busca" aoAgir={() => mudarFiltro('busca', '')} />
        )}
        {vazia && semBusca && !semCanal && (
          <VazioEmLinha
            texto={`${filtros.aba === 'precisa' ? 'Ninguém esperando você' : 'Nada aberto'} no ${filtros.canais.join(', ')}.`}
            acao="Ver todos os canais"
            aoAgir={() => mudarFiltro('canais', [])}
          />
        )}
        {vazia && semBusca && semCanal && filtros.aba === 'precisa' && (
          <VazioEmLinha texto="Ninguém esperando você." acao="Ver todas" aoAgir={() => mudarFiltro('aba', 'todas')} />
        )}
        {vazia && semBusca && semCanal && filtros.aba !== 'precisa' && filtros.responsavel && filtros.responsavel !== 'todas' && (
          <VazioEmLinha texto={filtros.responsavel === 'minhas' ? 'Nenhuma conversa sua aberta agora.' : 'Nenhuma conversa sem dono aberta agora.'} acao="Ver todas" aoAgir={() => mudarFiltro('responsavel', 'todas')} />
        )}
        {vazia && semBusca && semCanal && filtros.aba !== 'precisa' && (!filtros.responsavel || filtros.responsavel === 'todas') && (
          <VazioEmLinha texto="Nenhuma conversa aberta agora." />
        )}
        {/* #3300 (F7): lista para o leitor de tela ("lista, 11 itens"); `display: contents` não muda o layout. */}
        {/* oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- #3363: as setas valem em qualquer cartão; a lista é o único ancestral comum. */}
        <div
          role="list" aria-label="Conversas" className={css.listaSemCaixa}
          onFocus={(e) => { const id = e.target.dataset?.conversaId; if (id) { setFocoId(id); setFocoNaLista(true) } }}
          onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget)) setFocoNaLista(false) }}
          onKeyDown={aoTeclarNaLista}
        >
        {lista.map((conversa) => (
          <div role="listitem" key={conversa.id} className={css.listaSemCaixa}>
          <CartaoConversa
            key={conversa.id}
            conversa={conversa}
            novo={novas.has(conversa.id)}
            selecionada={conversa.id === selecionadaId}
            agora={agora}
            pausado={automaticoPausado?.[conversa.id] ?? false}
            pagamentoNaoVisto={pagamentosNaoVistos?.has(conversa.id) ?? false}
            aberta={aberta}
            expediente={horarioDaLoja}
            busca={filtros.busca}
            aoAbrir={abrir}
            fonteApi={fonteApi}
            usuario={sessao?.usuario}
            tabIndex={String(conversa.id) === ativoId ? 0 : -1}
            atenuado={conversa.id === foraDaLista}
          />
          </div>
        ))}
        </div>
        {filtros.aba === 'todas' && (
          <>
            <GrupoRecolhido
              chave="encerradas"
              usuario={sessao?.usuario}
              titulo="Concluídas"
              conversas={encerradasDoBalcao}
              aberto={gruposAbertos.encerradas}
              aoAlternar={() => alternarGrupo('encerradas')}
              agora={agora}
              automaticoPausado={automaticoPausado}
              pagamentosNaoVistos={pagamentosNaoVistos}
              busca={filtros.busca}
              aoAbrir={abrir}
              selecionadaId={selecionadaId}
            />
            <GrupoRecolhido
              chave="bloqueados"
              usuario={sessao?.usuario}
              titulo="Bloqueados"
              conversas={bloqueadasDoBalcao}
              aberto={gruposAbertos.bloqueados}
              aoAlternar={() => alternarGrupo('bloqueados')}
              agora={agora}
              automaticoPausado={automaticoPausado}
              pagamentosNaoVistos={pagamentosNaoVistos}
              busca={filtros.busca}
              aoAbrir={abrir}
              selecionadaId={selecionadaId}
            />
          </>
        )}
        {fonteApi && BuscaDeClientes && <BuscaDeClientes busca={filtros.busca} conversas={conversas} aoAbrir={aoAbrir} />}
      </div>
    </>
  )
}
