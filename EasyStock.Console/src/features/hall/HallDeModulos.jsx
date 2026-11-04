import { useEffect, useRef, useState } from 'react'
import { DndContext, DragOverlay } from '@dnd-kit/core'
import { rectSortingStrategy, SortableContext, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Icone } from '../../componentes/Icone'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Marca } from '../../componentes/Marca'
import { CampoTexto } from '../../componentes/Campo'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { chaveDasPreferenciasHall, gravarPreferenciasHall, lerPreferenciasHall } from '../../aplicacao/preferenciasHall'
import { Botao } from '../../componentes/Botao'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { MenuDaConta } from '../../componentes/MenuDaConta'
import { Vazio } from '../../componentes/Vazio'
import { buscarDestinosDoConsole, filtrarModulos, hashDoModulo, modulosDoHall, saudacaoDoHall } from '../../dominio/modulos'
import {
  alternarFavoritoDoHall, favoritosDoHall, favoritosPrimeiroNoHall, moverModuloDoHall, moverPainelDoHall,
  ordenarModulosDoHall, ordenarPaineisDoHall, restaurarOrdemDoHall, FOCOS_DO_HALL,
} from '../../dominio/preferenciasHall'
import { pendenciaDoModulo } from '../../dominio/pulsoDoHall'
import { usePulsoDoHall } from '../../aplicacao/usePulsoDoHall'
import { IconeDoModulo } from './IconeDoModulo'
import { IlustracaoDoModulo } from './IlustracaoDoModulo'
import { alcaDoLink, useCliqueAposArrasto, useSensoresDoHall } from './arrastoDoHall'
import { colisoesDoHall } from './colisoesDoHall'
import { PulsoDoHall } from './PulsoDoHall'
import { PersonalizarHall } from './PersonalizarHall'
import css from './hall.module.css'

function CartaoModulo({ modulo, favorito, organizar, arrastavel, movimentos, indice, total, pendencia, aoFavoritar, aoMover }) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: modulo.id, disabled: !arrastavel,
    transition: movimentos ? { duration: 180, easing: 'cubic-bezier(0.2, 0, 0, 1)' } : null,
  })
  // #1839 D: fora do modo Organizar o próprio link é a alça; dentro dele, o botão Mover.
  const alcaNoLink = arrastavel && !organizar ? { ref: setActivatorNodeRef, ...alcaDoLink(attributes, listeners) } : { draggable: false }
  const mover = (direcao, evento) => {
    aoMover(modulo.id, direcao)
    // O botão pode ficar desabilitado depois de chegar à ponta da grade.
    evento.currentTarget.closest('li')?.querySelector('[data-alca-modulo]')?.focus()
  }
  return (
    <li ref={setNodeRef} data-modulo={modulo.id}
      className={[css.cartao, !modulo.disponivel && css.emBreve, isDragging && css.arrastando, organizar && css.organizando].filter(Boolean).join(' ')}
      style={{ transform: CSS.Transform.toString(transform), transition: arrastavel ? transition : undefined }}>
      <IlustracaoDoModulo id={modulo.id} className={css.ilustracaoCartao} />
      {organizar && <div className={css.cabecaCartao}>
        <div className={css.controlesCartao}>
          {organizar && modulo.disponivel && <button type="button" className={css.favoritar} aria-pressed={favorito}
            aria-label={`${favorito ? 'Remover' : 'Adicionar'} ${modulo.nome} ${favorito ? 'dos' : 'aos'} favoritos`}
            onClick={() => aoFavoritar(modulo.id)}>
            <MarcaIcone nome={favorito ? 'confirmar' : 'adicionar'} tamanho={16} />
            <span>{favorito ? 'Favorito' : 'Favoritar'}</span>
          </button>}
        </div>
      </div>}
      {modulo.disponivel ? <>
        <a className={css.abrir} href={modulo.href} {...alcaNoLink}>
          <span className={css.nome}>{modulo.nome}</span>
          <MarcaIcone nome="proximo" tamanho={20} />
        </a>
        {pendencia && <span className={css.pendencia} data-atencao={pendencia.atencao || undefined}>{pendencia.texto}</span>}
        {/* #1810: as telas de cada área ficam na busca e no menu do módulo; o cartão só leva à área. */}
      </> : <>
        <span className={css.nome}>{modulo.nome}</span>
        <span className={css.resumo}>{modulo.resumo}</span>
        <span className={css.selo}><MarcaIcone nome="bloqueado" tamanho={20} /> Em breve</span>
      </>}
      {organizar && <div className={css.movimento}>
        <button ref={setActivatorNodeRef} type="button" data-alca-modulo={modulo.id}
          className={css.alca} {...attributes} {...listeners} aria-label={`Mover ${modulo.nome}`}>
          <MarcaIcone nome="ordenar" tamanho={20} /> Mover
        </button>
        <span className={css.posicao}>{indice + 1} de {total}</span>
        <button type="button" className={css.mover} aria-label={`Subir ${modulo.nome}`}
          disabled={indice === 0} onClick={(evento) => mover(-1, evento)}><MarcaIcone nome="acima" tamanho={20} /></button>
        <button type="button" className={css.mover} aria-label={`Descer ${modulo.nome}`}
          disabled={indice === total - 1} onClick={(evento) => mover(1, evento)}><MarcaIcone nome="abaixo" tamanho={20} /></button>
      </div>}
    </li>
  )
}

export function HallDeModulos(props) {
  const chavePreferencia = chaveDasPreferenciasHall(props.sessao)
  return <HallPersonalizado key={chavePreferencia ?? 'visita'} {...props} chavePreferencia={chavePreferencia} />
}

function HallPersonalizado({ fonteApi = false, sessao = null, agora, ajuda, painel = null, chavePreferencia }) {
  const { permite, aoSair, notificacoes, conta, acoes } = useAcessoModulos()
  const [busca, setBusca] = useState('')
  const [organizar, setOrganizar] = useState(false)
  const [preferencia, setPreferencia] = useState(() => lerPreferenciasHall(chavePreferencia))
  const [anterior, setAnterior] = useState(null)
  const [mensagem, setMensagem] = useState('')
  const [arrastando, setArrastando] = useState(null)
  const [personalizando, setPersonalizando] = useState(false)
  const buscaRef = useRef(null)
  const organizarRef = useRef(null)
  const ajustarRef = useRef(null)
  // #1810: entrar e sair da organização troca os botões de lugar; o foco segue para o novo.
  const focoAposOrganizar = useRef(null)
  useEffect(() => {
    const alvo = focoAposOrganizar.current
    focoAposOrganizar.current = null
    if (alvo === 'ajustar') ajustarRef.current?.focus()
    else if (alvo === 'organizar') document.querySelector('[data-alca-modulo]')?.focus()
  }, [organizar])
  const modulosPermitidos = modulosDoHall({ fonteApi, acoes }).filter((modulo) => permite(modulo.id))
  const todos = ordenarModulosDoHall(modulosPermitidos, preferencia)
  const modulos = filtrarModulos(todos, busca)
  const moduloArrastando = modulos.find((modulo) => modulo.id === arrastando)
  const favoritos = favoritosDoHall(modulosPermitidos, preferencia)
  const buscando = Boolean(busca.trim())
  const destinos = buscarDestinosDoConsole(todos, busca)
  const foco = FOCOS_DO_HALL.find((item) => item.id === preferencia.foco)
  const destaque = foco.ordem.map((id) => modulosPermitidos.find((modulo) => modulo.id === id && modulo.disponivel)).find(Boolean) ?? todos.find((modulo) => modulo.disponivel)
  const idsDoPulso = ordenarPaineisDoHall(preferencia, modulosPermitidos.map((modulo) => modulo.id))
  const podeOrganizar = organizar && !buscando
  const sensores = useSensoresDoHall()
  const clique = useCliqueAposArrasto()
  // #1839 D: uma leitura só alimenta os números de cima e a linha viva de cada cartão.
  const permitido = (id) => modulosPermitidos.some((modulo) => modulo.id === id)
  const { fontes } = usePulsoDoHall({
    ativo: fonteApi, atendimento: permitido('atendimento'), pedidos: permitido('cozinha') || permitido('entregas'),
    financeiro: permitido('financeiro'), cardapio: permitido('cardapio'),
  })
  const gravar = (proxima, texto, guardarAnterior = true) => {
    if (guardarAnterior && JSON.stringify(proxima) !== JSON.stringify(preferencia)) setAnterior(preferencia)
    setPreferencia(proxima)
    const persistiu = gravarPreferenciasHall(chavePreferencia, proxima)
    setMensagem(`${texto}${persistiu ? '' : chavePreferencia ? ' A personalização vale só nesta visita.' : ' A personalização vale só nesta tela.'}`)
  }
  const desfazer = (foco) => {
    gravar(anterior, 'Personalização anterior restaurada.', false)
    setAnterior(null)
    if (foco.current && !foco.current.disabled) foco.current.focus()
    else buscaRef.current?.focus()
  }
  const favoritar = (id) => {
    const modulo = modulosPermitidos.find((item) => item.id === id && item.disponivel)
    if (!modulo) return
    const proxima = alternarFavoritoDoHall(preferencia, id)
    gravar(proxima, `${modulo.nome} ${proxima.favoritos.includes(id) ? 'adicionado aos' : 'removido dos'} favoritos.`)
  }
  const mover = (id, destino) => {
    if (buscando || !modulos.some((modulo) => modulo.id === id) || !modulos.some((modulo) => modulo.id === destino) || id === destino) return
    const proxima = moverModuloDoHall(preferencia, id, destino)
    const ordenados = ordenarModulosDoHall(modulosPermitidos, proxima)
    gravar(proxima, `${ordenados.find((modulo) => modulo.id === id).nome} movido para a posição ${ordenados.findIndex((modulo) => modulo.id === id) + 1} de ${ordenados.length}.`)
  }
  const moverPorBotao = (id, direcao) => {
    const destino = modulos[modulos.findIndex((modulo) => modulo.id === id) + direcao]?.id
    if (destino) mover(id, destino)
  }
  const moverPainel = (id, destino) => {
    const proxima = moverPainelDoHall(preferencia, id, destino)
    const ordem = ordenarPaineisDoHall(proxima, modulosPermitidos.map((modulo) => modulo.id))
    gravar(proxima, `Número movido para a posição ${ordem.indexOf(id) + 1} de ${ordem.length}.`)
  }
  const anuncio = (id) => modulos.find((modulo) => modulo.id === id)?.nome ?? 'Módulo'
  return (
    <div className={[css.hall, !preferencia.movimentos && css.semMovimentos].filter(Boolean).join(' ')}>
      <header className={css.topo}>
        <Marca />
        <div className={css.acoes}>
          {ajuda}
          <InterruptorTema />
          {notificacoes && <div className={css.avisos}>{notificacoes}</div>}
          {/* #1870: o Sair fica atrás do avatar, como no pé do trilho do Balcão e dos módulos. */}
          {aoSair && <MenuDaConta lado="abaixo" nome={conta?.nome ?? sessao?.usuario?.nome} detalhe={conta?.casa} aoSair={aoSair}
            rotuloSair={sessao?.persistente ? 'Sair deste aparelho' : 'Sair'} />}
        </div>
      </header>
      <main className={css.conteudo}>
        <div className={css.abertura}>
          <div className={css.cabeca}>
            <h1 className={css.titulo} tabIndex={-1} data-inicio-tela id="conteudo-principal">{saudacaoDoHall(agora, sessao?.usuario?.nome)}</h1>
            {sessao?.persistente && <p className={css.apoio}>A Cozinha permanece conectada. Ao trocar de pessoa, use Sair no menu da conta.</p>}
            {modulos.length === 0 && !busca && <output>Seu perfil não tem módulos liberados. Peça à administração da loja para revisar seu acesso.</output>}
          </div>
          <div className={css.busca}>
            <CampoTexto ref={buscaRef} rotulo="Buscar módulo ou tela" tipo="search"
              placeholder="Ex.: insumos, caixa ou entregas" value={busca}
              onChange={(evento) => setBusca(evento.target.value)} />
            {buscando && <Botao variante="texto" onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Limpar busca</Botao>}
          </div>
        </div>
        {!buscando && fonteApi && <PulsoDoHall ids={idsDoPulso} foco={foco.nome} fontes={fontes} movimentos={preferencia.movimentos} aoMover={moverPainel} />}
        {/* #1810: no modo API o destaque repetia um cartão de área; ficam só os números e as áreas. */}
        {!buscando && destaque && !fonteApi && <section className={css.destaque} aria-labelledby="destaque-hall">
          <div className={css.ilustracao} aria-hidden="true"><IconeDoModulo modulo={destaque} tamanho={40} /></div>
          <div className={css.textoDestaque}><h2 id="destaque-hall">{destaque.nome}</h2></div>
          <a className={css.acaoDestaque} href={destaque.href}>Abrir {destaque.nome}<MarcaIcone nome="proximo" tamanho={22} /></a>
        </section>}
        {/* #1608: o que fazer agora vem antes da lista de módulos. Quem compõe é o App. */}
        {!buscando && painel}
        {!buscando && favoritos.length > 0 && <nav className={css.rotina} aria-label="Favoritos da sua rotina">
          <div className={css.cabecaRotina}><span className={css.rotinaTitulo}>Favoritos</span></div>
          <ul className={css.linksRotina}>{favoritos.map((modulo) => <li key={modulo.id}>
            <a className={css.linkRotina} href={modulo.href}><IconeDoModulo modulo={modulo} tamanho={24} /> {modulo.nome}<MarcaIcone nome="proximo" tamanho={16} /></a>
          </li>)}</ul>
        </nav>}
        {!buscando && organizar && <div className={css.organizacao}>
          <div className={css.instrucoes}>
            <h2 className={css.subtitulo}>Organizar áreas</h2>
            <p>Arraste pelo botão Mover ou use as setas de cada cartão. Favorite as áreas da sua rotina.</p>
          </div>
          <div className={css.ferramentas}>
            {anterior && <Botao variante="texto" onClick={() => desfazer(organizarRef)}>Desfazer</Botao>}
            <Botao ref={organizarRef} variante="primario" onClick={() => { setOrganizar(false); setMensagem(''); focoAposOrganizar.current = 'ajustar' }}>
              <MarcaIcone nome="confirmar" tamanho={20} />Concluir organização
            </Botao>
          </div>
        </div>}
        {podeOrganizar && <div className={css.opcoesOrdem}>
          <Botao variante="texto" disabled={favoritos.length === 0} onClick={() => gravar(favoritosPrimeiroNoHall(preferencia), 'Favoritos primeiro. As outras áreas seguem a ordem original.')}>Favoritos primeiro</Botao>
          <Botao variante="texto" onClick={() => gravar(restaurarOrdemDoHall(preferencia), 'Ordem original restaurada. Seus favoritos foram mantidos.')}>Restaurar ordem original</Botao>
          <span>Teclado: Espaço para pegar, setas para mover, Espaço para soltar ou Esc para cancelar.</span>
        </div>}
        <output className={css.resultado} aria-live="polite">
          {buscando ? (destinos.length === 1 ? '1 tela encontrada.' : `${destinos.length} telas encontradas.`) : mensagem}
        </output>
        {buscando ? <section aria-label="Resultados da busca">
          {destinos.length > 0 ? <ul className={css.destinos}>
            {destinos.map(({ modulo, tela, href }) => <li key={href}>
              <a className={css.destino} href={href}>
                <IconeDoModulo modulo={modulo} telaId={tela.id} tamanho={24} />
                <span className={css.textoDestino}><strong>{tela.rotulo}</strong><span>{modulo.nome}{fonteApi && tela.indisponivelNaApi && ' · Em preparação'}</span></span>
                <MarcaIcone nome="proximo" tamanho={20} />
              </a>
            </li>)}
          </ul> : <Vazio titulo="Nenhum módulo ou tela encontrado" acao={<Botao onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Ver todos os módulos</Botao>}>
            Tente o nome de uma área ou tela, como insumos, caixa ou entregas.
          </Vazio>}
        </section> : <DndContext sensors={sensores} collisionDetection={colisoesDoHall}
          onDragStart={({ active }) => { clique.aoComecar(); setArrastando(active.id) }}
          onDragCancel={() => { clique.aoTerminar(); setArrastando(null) }}
          accessibility={{
            screenReaderInstructions: { draggable: 'Enter abre o módulo. Para mudar a ordem, pressione Espaço, use as setas para escolher a posição, Espaço de novo para soltar ou Escape para cancelar.' },
            announcements: {
              onDragStart: ({ active }) => `${anuncio(active.id)} selecionado para mover.`,
              onDragOver: ({ active, over }) => over ? `${anuncio(active.id)} sobre a posição ${modulos.findIndex((modulo) => modulo.id === over.id) + 1} de ${modulos.length}.` : `${anuncio(active.id)} fora da grade.`,
              onDragEnd: ({ active, over }) => over ? `${anuncio(active.id)} solto na posição ${modulos.findIndex((modulo) => modulo.id === over.id) + 1} de ${modulos.length}.` : 'Movimento cancelado. A ordem foi mantida.',
              onDragCancel: () => 'Movimento cancelado. A ordem foi mantida.',
            },
          }} onDragEnd={({ active, over }) => { clique.aoTerminar(); setArrastando(null); if (over) mover(active.id, over.id) }}>
          <SortableContext items={modulos.map((modulo) => modulo.id)} strategy={rectSortingStrategy}>
            <ul className={css.grade} aria-label="Módulos" onClickCapture={clique.onClickCapture}>
              {modulos.map((modulo, indice) => <CartaoModulo key={modulo.id} modulo={modulo}
                favorito={preferencia.favoritos.includes(modulo.id)} organizar={podeOrganizar} arrastavel={!buscando} movimentos={preferencia.movimentos}
                indice={indice} total={modulos.length} pendencia={fonteApi ? pendenciaDoModulo(modulo.id, fontes) : null}
                aoFavoritar={favoritar} aoMover={moverPorBotao} />)}
            </ul>
          </SortableContext>
          <DragOverlay dropAnimation={null}>
            {moduloArrastando && <div className={css.previaArrasto} aria-hidden="true">
              <IconeDoModulo modulo={moduloArrastando} tamanho={32} />
              <strong>{anuncio(arrastando)}</strong>
              <span>Solte na posição desejada</span>
            </div>}
          </DragOverlay>
        </DndContext>}
        {!buscando && !organizar && todos.length > 0 && <div className={css.rodapeHall}>
          {anterior && <Botao variante="texto" onClick={() => desfazer(ajustarRef)}>Desfazer</Botao>}
          <Botao ref={ajustarRef} variante="texto" onClick={() => setPersonalizando(true)}><MarcaIcone nome="configuracoes" tamanho={20} />Ajustar tela inicial</Botao>
          {/* #1839 (F1): as automáticas só abriam pelo trilho do Balcão; fora da grade arrastável. */}
          {permite('atendimento') && <Botao variante="texto" onClick={() => { window.location.hash = hashDoModulo('atendimento', 'respostas') }}>
            <Icone nome="raio" tamanho={20} />Automáticas
          </Botao>}
          <span className={css.dicaArrasto}>Arraste {fonteApi ? 'os números e os cartões' : 'os cartões'} para mudar a ordem.</span>
        </div>}
      </main>
      {personalizando && <PersonalizarHall preferencia={preferencia} modulos={modulosPermitidos} aoFechar={() => setPersonalizando(false)}
        aoOrganizar={todos.length > 0 ? () => { setPersonalizando(false); setOrganizar(true); setMensagem(''); focoAposOrganizar.current = 'organizar' } : null} aoSalvar={(proxima) => { gravar(proxima, 'Painel personalizado.'); setPersonalizando(false) }} />}
    </div>
  )
}
