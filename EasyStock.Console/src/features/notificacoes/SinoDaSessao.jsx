import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { BarraDeAbas } from '../../componentes/BarraDeAbas'
import { Botao } from '../../componentes/Botao'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { agruparAvisos, destinoDoAviso, posicaoDoPainel } from '../../dominio/avisosDaSessao'
import { diaMesHora, idadeDoAviso } from '../../dominio/formato'
import { useEscape } from '../../hooks/useEscape'
import css from './notificacoes.module.css'

const gravidade = { Critica: 'Crítica', Alta: 'Alta', Media: 'Média', Informativa: 'Informativa' }
const tom = { Critica: 'perigo', Alta: 'aviso', Media: 'aviso', Informativa: 'info' }
const iconeDaGravidade = { Critica: 'alerta', Alta: 'alerta', Media: 'bell', Informativa: 'dica' }
const textoAparelho = {
  desligado: 'Receba avisos desta conta mesmo com o sistema fechado.',
  ativando: 'Aguarde a confirmação do navegador…',
  desativando: 'Desativando avisos neste aparelho…',
  ativo: 'Avisos desta conta ativados neste aparelho.',
  bloqueado: 'Avisos bloqueados neste navegador. Libere as notificações deste site nas configurações do navegador e ative de novo.',
  'sem-chave': 'Os avisos no aparelho ainda não estão configurados neste ambiente.',
  indisponivel: 'Este navegador não oferece avisos com o sistema fechado.',
}
// #3394: como o MenuDaConta (#1872), o painel sobe para a camada de topo do navegador: nenhum ancestral com
// transform ou overflow (o trilho rola, o menu do tablet é um <dialog>) o recorta ou desloca. Sem popover, fixed.
const COM_POPOVER = typeof HTMLElement !== 'undefined' && 'showPopover' in HTMLElement.prototype

// #3394: os Avisos abriam num Modal central, com fundo escuro, que tirava a pessoa do que fazia. Agora abrem
// num painel junto ao sino, como nas redes: abas Não lidas e Todas, grupos por dia, ponto de não lida, leitura
// na hora (com volta se a API recusar) e Marcar todas com confirmação. O painel é irmão do botão, sem caixa em
// volta, porque o CSS do topo, do trilho e do hall posiciona os pontos pelo `> button`.
export function SinoDaSessao({ avisos, aparelho }) {
  const [aberto, setAberto] = useState(false)
  const [aba, setAba] = useState('naoLidas')
  const [confirmandoTodas, setConfirmandoTodas] = useState(false)
  const [balancar, setBalancar] = useState(false)
  // A idade ("há 3 min") se mede na hora de abrir; o painel aberto é curto e não precisa de relógio próprio.
  const [agora, setAgora] = useState(() => Date.now())
  const { total, recentes, erro, carregando, falha, chegaram, todas, carregar, carregarTodas, marcarLida, marcarTodas, aoFecharPainel } = avisos
  const { permite, acoes } = useAcessoModulos()
  const botaoRef = useRef(null)
  const painelRef = useRef(null)
  const focoDepoisRef = useRef(null)
  const marcarTodasRef = useRef(null)
  const cancelarRef = useRef(null)
  const voltarParaMarcarTodasRef = useRef(false)
  const voltarDepoisDaCargaRef = useRef(null)
  const idPainel = useId()

  // O sino balança quando chega aviso novo de fato, não quando o total sobe porque uma leitura falhou e voltou.
  // Movimento reduzido: base.css encurta.
  useEffect(() => {
    if (chegaram.length > 0) setBalancar(true)
  }, [chegaram])

  const fechar = useCallback(({ devolverFoco = true } = {}) => {
    setAberto(false)
    setConfirmandoTodas(false)
    aoFecharPainel()
    if (devolverFoco) botaoRef.current?.focus()
  }, [aoFecharPainel])
  const abrir = () => {
    setAba('naoLidas')
    setAgora(Date.now())
    setAberto(true)
    carregar()
  }
  // Esc fecha só o painel: sem o preventDefault o <dialog> do menu do tablet também recebia o cancel (MenuDaConta).
  useEscape(aberto, useCallback((evento) => { evento.preventDefault(); fechar() }, [fechar]))
  useEffect(() => {
    if (!aberto) return undefined
    const aoTocarFora = (evento) => {
      if (!botaoRef.current?.contains(evento.target) && !painelRef.current?.contains(evento.target)) fechar({ devolverFoco: false })
    }
    document.addEventListener('pointerdown', aoTocarFora)
    return () => document.removeEventListener('pointerdown', aoTocarFora)
  }, [aberto, fechar])

  // Mede o sino e põe o painel no lugar dele (trilho, topo ou folha do celular); refaz quando a janela muda, a
  // página rola ou o conteúdo do painel cresce.
  useLayoutEffect(() => {
    const painel = painelRef.current
    if (!aberto || !painel) return undefined
    if (COM_POPOVER && !painel.matches(':popover-open')) painel.showPopover()
    let quadro = null
    const medir = () => {
      quadro = null
      const ancora = botaoRef.current?.getBoundingClientRect()
      if (!ancora) return
      Object.assign(painel.style, { maxHeight: '' })
      const lugar = posicaoDoPainel({ ancora, janela: { largura: window.innerWidth, altura: window.innerHeight }, alturaDoPainel: painel.offsetHeight })
      painel.dataset.lado = lugar.lado
      Object.assign(painel.style, {
        top: `${lugar.top}px`, maxHeight: `${lugar.maxHeight}px`,
        left: lugar.left === undefined ? 'auto' : `${lugar.left}px`, right: lugar.right === undefined ? 'auto' : `${lugar.right}px`,
        width: lugar.width === undefined ? 'auto' : `${lugar.width}px`,
      })
    }
    const agendar = (evento) => {
      if (evento?.type === 'scroll' && painel.contains(evento.target)) return
      if (quadro === null) quadro = requestAnimationFrame(medir)
    }
    medir()
    const observador = new ResizeObserver(() => agendar())
    observador.observe(painel)
    window.addEventListener('resize', agendar)
    document.addEventListener('scroll', agendar, true)
    return () => {
      observador.disconnect()
      window.removeEventListener('resize', agendar)
      document.removeEventListener('scroll', agendar, true)
      if (quadro !== null) cancelAnimationFrame(quadro)
    }
  }, [aberto])

  // Ao abrir, o foco vai para a aba ativa.
  useEffect(() => {
    if (aberto) painelRef.current?.querySelector('[role="tab"][aria-selected="true"]')?.focus()
  }, [aberto])
  // Depois de ler um aviso, o foco vai para o próximo não lido (o botão lido sumiu) ou para a aba. Só depois de
  // uma leitura: a consulta de 30 s muda a lista sem tirar o foco de onde a pessoa está.
  useEffect(() => {
    const alvo = focoDepoisRef.current
    if (!aberto || alvo === null) return
    focoDepoisRef.current = null
    const painel = painelRef.current
    const proximo = alvo ? painel?.querySelector(`[data-aviso="${CSS.escape(alvo)}"] button`) : null
    ;(proximo || painel?.querySelector('[role="tab"][aria-selected="true"]'))?.focus()
  }, [aberto, recentes, todas.itens])
  // Atualizar e Ver mais ficam desligados enquanto carregam, e o navegador tira o foco de botão desligado: terminada
  // a carga, o foco volta para quem foi clicado; se ele sumiu (Ver mais da última página), vai para a aba ativa.
  const carregandoAgora = carregando || todas.carregando
  useEffect(() => {
    const botao = voltarDepoisDaCargaRef.current
    if (carregandoAgora || !botao) return
    voltarDepoisDaCargaRef.current = null
    if (document.activeElement !== document.body && document.activeElement !== null) return
    ;(botao.isConnected ? botao : painelRef.current?.querySelector('[role="tab"][aria-selected="true"]'))?.focus()
  }, [carregandoAgora])
  // O botão clicado some com a confirmação: o foco vai para Cancelar e, cancelando, volta para Marcar todas.
  useEffect(() => {
    if (confirmandoTodas) cancelarRef.current?.focus()
    else if (voltarParaMarcarTodasRef.current) marcarTodasRef.current?.focus()
    voltarParaMarcarTodasRef.current = false
  }, [confirmandoTodas])

  const trocarAba = (id) => {
    setAba(id)
    setConfirmandoTodas(false)
    if (id === 'todas') carregarTodas()
  }
  const ler = (aviso, lista) => {
    const indice = lista.findIndex((a) => a.id === aviso.id)
    const vizinho = lista.slice(indice + 1).find((a) => !a.lida) ?? lista.slice(0, indice).reverse().find((a) => !a.lida)
    focoDepoisRef.current = vizinho?.id ?? ''
    marcarLida(aviso.id)
  }
  // #3402: tocar no aviso abre a tela da coisa avisada, como nas redes. Lê na hora (a leitura do hook volta se a API
  // recusar) sem passar por `ler`, que guardaria o foco para a próxima abertura, e fecha o painel sem devolver o foco ao
  // sino: na troca de tela, quem leva o foco é a NavegacaoDoConsole. Ctrl, Cmd, Shift ou Alt deixam o navegador abrir
  // outra aba, janela ou baixar: lê como o botão de ler (o link sai da lista e o foco vai ao próximo não lido) e esta aba
  // fica onde está. O botão do meio não dispara click: abre a outra aba sem ler.
  const destinoDe = (aviso) => destinoDoAviso(aviso, { permite, acoes })
  const abrirDestino = (evento, aviso, destino, lista) => {
    if (evento.ctrlKey || evento.metaKey || evento.shiftKey || evento.altKey) {
      if (!aviso.lida) ler(aviso, lista)
      return
    }
    if (!aviso.lida) marcarLida(aviso.id)
    fechar({ devolverFoco: false })
    // Mesmo hash não dispara hashchange e o foco cairia no body: vai ao início da tela, o mesmo alvo de
    // app/focoDaNavegacao.js (features não importa app).
    if (window.location.hash === destino.hash) document.querySelector('[data-inicio-tela]')?.focus({ preventScroll: true })
  }

  // #1989: quem ainda não ativou neste aparelho vê um ponto discreto no sino; o caminho fica dentro.
  const semAvisoNoAparelho = ['desligado', 'bloqueado'].includes(aparelho?.estado)
  // #2258 (auditoria A8, H11, A12): o caminho para ligar os avisos do aparelho vem ANTES da lista quando
  // eles estão desligados; ligado, vira uma linha discreta no pé do painel.
  const secaoDoAparelho = aparelho && (
    <section className={`${css.aparelho} ${semAvisoNoAparelho ? css.aparelhoDesligado : ''}`} aria-label="Avisos neste aparelho">
      <Icone nome={semAvisoNoAparelho ? 'bloqueio' : 'bell'} tamanho={20} className={css.iconeAparelho} />
      <p role={aparelho.estado === 'erro' ? 'alert' : 'status'}>{aparelho.mensagem ?? textoAparelho[aparelho.estado]}</p>
      {['desligado', 'bloqueado', 'erro', 'sem-chave', 'ativando'].includes(aparelho.estado) && <Botao icone="bell" onClick={aparelho.ativar} disabled={aparelho.estado === 'ativando'}>Ativar neste aparelho</Botao>}
      {['ativo', 'desativando'].includes(aparelho.estado) && <Botao variante="texto" onClick={aparelho.desligar} disabled={aparelho.estado === 'desativando'}>Desativar</Botao>}
    </section>
  )
  const naoLidas = total ?? recentes.length
  const lista = aba === 'naoLidas' ? recentes : todas.itens
  return <>
    <Botao ref={botaoRef} variante="texto" icone="bell" aria-label={`Avisos${erro ? ', sem atualizar' : total === null ? '' : `, ${total} não lidas`}${semAvisoNoAparelho ? ', avisos desligados neste aparelho' : ''}`}
      title={semAvisoNoAparelho ? 'Avisos desligados neste aparelho: abra e toque em Ativar' : undefined}
      className={balancar ? css.balanca : undefined} onAnimationEnd={() => setBalancar(false)}
      aria-haspopup="dialog" aria-expanded={aberto} aria-controls={aberto ? idPainel : undefined}
      onClick={() => (aberto ? fechar() : abrir())}>
      {/* #2232 (H3, L2): o número sai do texto e vai para o contador único do app. */}
      <span>Avisos</span>
      {/* #2762 (VF14): o erro vira ponto sobre o sino; o aria-label já diz "sem atualizar". */}
      {erro && <span className={css.pontoErro} data-aviso-erro aria-hidden="true" title="Avisos sem atualizar" />}
      {!erro && total > 0 && <Pilula contagem={total > 99 ? '99+' : total} tom="perigo" aria-hidden="true" data-contador-sino />}
      {semAvisoNoAparelho && <span className={css.pontoAparelho} data-aparelho-desligado aria-hidden="true" title="Avisos desligados neste aparelho" />}
    </Botao>
    {aberto && (
      // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- o Tab que sai do painel o fecha, como no Popover de diálogo (#3300).
      <section ref={painelRef} id={idPainel} className={css.painel} role="dialog" aria-label="Avisos" data-painel-avisos
        popover={COM_POPOVER ? 'manual' : undefined}
        onBlur={(evento) => {
          if (evento.relatedTarget && !evento.currentTarget.contains(evento.relatedTarget) && evento.relatedTarget !== botaoRef.current) fechar({ devolverFoco: false })
        }}>
        <header className={css.cabecalho}>
          <h2>Avisos</h2>
          <BotaoIcone icone="refresh-cw" rotulo="Atualizar avisos"
            onClick={(evento) => { voltarDepoisDaCargaRef.current = evento.currentTarget; setAgora(Date.now()); if (aba === 'todas') carregarTodas(); else carregar() }}
            disabled={carregandoAgora} />
          {aba === 'naoLidas' && naoLidas > 0 && !confirmandoTodas && (
            <Botao ref={marcarTodasRef} variante="texto" icone="circle-check" onClick={() => setConfirmandoTodas(true)}>Marcar todas como lidas</Botao>
          )}
        </header>
        {confirmandoTodas && (
          <div className={css.confirmacao}>
            <p>Marcar {naoLidas === 1 ? 'o aviso' : `os ${naoLidas} avisos`} como {naoLidas === 1 ? 'lido' : 'lidos'}? Os avisos gerais saem da lista da equipe toda.</p>
            <div>
              <Botao ref={cancelarRef} variante="texto" onClick={() => { voltarParaMarcarTodasRef.current = true; setConfirmandoTodas(false) }}>Cancelar</Botao>
              <Botao variante="primario" onClick={() => { setConfirmandoTodas(false); focoDepoisRef.current = ''; marcarTodas() }}>Sim, marcar todas</Botao>
            </div>
          </div>
        )}
        <BarraDeAbas className={css.abas} rotulo="Quais avisos" prefixoId={`${idPainel}-aba`} idPainel={`${idPainel}-lista`} ativa={aba} aoTrocar={trocarAba}
          abas={[
            { id: 'naoLidas', rotulo: 'Não lidas', nome: total === null ? 'Não lidas' : `Não lidas, ${naoLidas}`, contador: naoLidas > 0 ? naoLidas : null, tom: 'alerta' },
            { id: 'todas', rotulo: 'Todas' },
          ]} />
        <div className={css.corpo} id={`${idPainel}-lista`} role="tabpanel" aria-labelledby={`${idPainel}-aba-${aba}`}
          aria-busy={aba === 'todas' ? todas.carregando : carregando}>
          {falha && <p role="alert" className={css.erro}>{falha}</p>}
          {aba === 'naoLidas' && erro && <p role="alert" className={css.erro}>Sem atualizar: {erro}</p>}
          {aba === 'todas' && todas.erro && <p role="alert" className={css.erro}>Sem atualizar: {todas.erro}</p>}
          {semAvisoNoAparelho && secaoDoAparelho}
          {aba === 'naoLidas' && !erro && !carregando && naoLidas === 0 && recentes.length === 0 && (
            <Vazio titulo="Tudo em dia" icone="check" role="status">Nenhum aviso para ler. Os novos aparecem aqui e o sino balança.</Vazio>
          )}
          {aba === 'todas' && !todas.erro && !todas.carregando && todas.itens.length === 0 && (
            <Vazio titulo="Nenhum aviso ainda" icone="bell" role="status">Quando a loja tiver um aviso, ele fica guardado aqui.</Vazio>
          )}
          {lista.length === 0 && (aba === 'todas' ? todas.carregando : carregando) && <p role="status" className={css.resumo}>Carregando avisos…</p>}
          <ListaDeAvisos avisos={lista} agora={agora} chegaram={chegaram} aoLer={(aviso) => ler(aviso, lista)} destinoDe={destinoDe}
            aoAbrir={(evento, aviso, destino) => abrirDestino(evento, aviso, destino, lista)} />
          {aba === 'naoLidas' && naoLidas > recentes.length && recentes.length > 0 && (
            <p className={css.resumo}>Mostrando {recentes.length} de {naoLidas}. Os próximos aparecem conforme você lê.</p>
          )}
          {aba === 'todas' && todas.pagina < todas.paginas && (
            <Botao variante="texto" largo onClick={(evento) => { voltarDepoisDaCargaRef.current = evento.currentTarget; carregarTodas({ mais: true }) }} disabled={todas.carregando}>
              {todas.carregando ? 'Carregando…' : `Ver mais (${todas.total - todas.itens.length})`}
            </Botao>
          )}
        </div>
        {!semAvisoNoAparelho && secaoDoAparelho}
      </section>
    )}
  </>
}

// Uma linha por aviso, agrupada por dia. Não lido: fundo tingido, título forte, ponto e o botão de ler. Com destino
// (#3402), o título é o link e cobre a linha toda; o nome do link é o título, e a mensagem e a chamada o descrevem.
function ListaDeAvisos({ avisos, agora, chegaram, aoLer, destinoDe, aoAbrir }) {
  const base = useId()
  return agruparAvisos(avisos, agora).map((grupo) => (
    <section key={grupo.titulo} className={css.grupo} aria-label={grupo.titulo}>
      <h3>{grupo.titulo}</h3>
      <ul className={css.lista}>
        {grupo.avisos.map((aviso) => {
          const naoLida = !aviso.lida
          const idade = idadeDoAviso(aviso.createdAt, agora)
          const destino = destinoDe(aviso)
          const titulo = aviso.titulo || 'Aviso'
          const [idMensagem, idChamada] = [`${base}-${aviso.id}-mensagem`, `${base}-${aviso.id}-chamada`]
          return (
            <li key={aviso.id} data-aviso={aviso.id} data-tom={tom[aviso.severidade] ?? 'neutro'}
              className={`${css.aviso} ${naoLida ? css.naoLida : ''} ${chegaram.includes(aviso.id) ? css.chegou : ''}`}>
              <span className={css.selo} aria-hidden="true"><Icone nome={iconeDaGravidade[aviso.severidade] ?? 'bell'} tamanho={20} /></span>
              <div className={css.texto}>
                {naoLida && <span className="sr">Não lido.</span>}
                <h4>{destino
                  ? <a className={css.abrir} href={destino.hash} aria-describedby={`${aviso.mensagem ? idMensagem : ''} ${idChamada}`.trim()}
                    onClick={(evento) => aoAbrir(evento, aviso, destino)}>{titulo}</a>
                  : titulo}</h4>
                {aviso.mensagem && <p id={idMensagem}>{aviso.mensagem}</p>}
                <div className={css.meta}>
                  <Pilula tom={tom[aviso.severidade] ?? 'neutro'} fina>{gravidade[aviso.severidade] ?? aviso.severidade}</Pilula>
                  {/* #2303: o aviso diz quando nasceu; o de 14 dias não se passa por novo. */}
                  {idade && <time dateTime={aviso.createdAt} title={diaMesHora(aviso.createdAt)}>{idade}</time>}
                  {destino && <span id={idChamada} className={css.chamada}>{destino.chamada}</span>}
                </div>
              </div>
              {naoLida && <span className={css.ponto} data-nao-lida aria-hidden="true" />}
              {/* #2258 (A8): ler tem cara de botão e ícone; só de ícone, passa pelo BotaoIcone (gramática h). */}
              {naoLida && <BotaoIcone icone="check" tom="secundario" rotulo={`Marcar como lido: ${titulo}`} dica="Marcar como lido"
                onClick={() => aoLer(aviso)} />}
            </li>
          )
        })}
      </ul>
    </section>
  ))
}
