import { useRef, useState } from 'react'
import { DndContext, DragOverlay, KeyboardSensor, MouseSensor, TouchSensor, useSensor, useSensors } from '@dnd-kit/core'
import { rectSortingStrategy, SortableContext, sortableKeyboardCoordinates, useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Marca } from '../../componentes/Marca'
import { CampoTexto } from '../../componentes/Campo'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { chaveDasPreferenciasHall, gravarPreferenciasHall, lerPreferenciasHall } from '../../aplicacao/preferenciasHall'
import { Botao } from '../../componentes/Botao'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { Vazio } from '../../componentes/Vazio'
import { filtrarModulos, hashDoModulo, modulosDoHall, saudacaoDoHall } from '../../dominio/modulos'
import {
  alternarFavoritoDoHall, favoritosDoHall, favoritosPrimeiroNoHall, moverModuloDoHall,
  ordenarModulosDoHall, restaurarOrdemDoHall,
} from '../../dominio/preferenciasHall'
import { IconeDoModulo } from './IconeDoModulo'
import { colisoesDoHall } from './colisoesDoHall'
import css from './hall.module.css'

const doisDigitos = (n) => String(n).padStart(2, '0')

function CartaoModulo({ modulo, favorito, organizar, indice, total, aoFavoritar, aoMover }) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: modulo.id, disabled: !organizar,
    transition: { duration: 180, easing: 'cubic-bezier(0.2, 0, 0, 1)' },
  })
  const mover = (direcao, evento) => {
    aoMover(modulo.id, direcao)
    // O botão pode ficar desabilitado depois de chegar à ponta da grade.
    evento.currentTarget.closest('li')?.querySelector('[data-alca-modulo]')?.focus()
  }
  return (
    <li ref={setNodeRef} data-modulo={modulo.id}
      className={[css.cartao, !modulo.disponivel && css.emBreve, isDragging && css.arrastando, organizar && css.organizando].filter(Boolean).join(' ')}
      style={{ transform: CSS.Transform.toString(transform), transition: organizar ? transition : undefined }}>
      <div className={css.cabecaCartao}>
        <span className={css.icone}><IconeDoModulo modulo={modulo} tamanho={32} /></span>
        <div className={css.controlesCartao}>
          {modulo.disponivel && <button type="button" className={css.favoritar} aria-pressed={favorito}
            aria-label={`${favorito ? 'Remover' : 'Adicionar'} ${modulo.nome} ${favorito ? 'dos' : 'aos'} favoritos`}
            onClick={() => aoFavoritar(modulo.id)}>
            <MarcaIcone nome={favorito ? 'confirmar' : 'adicionar'} tamanho={16} />
            <span>{favorito ? 'Favorito' : 'Favoritar'}</span>
          </button>}
          {!organizar && <span className={css.numero}>{doisDigitos(modulo.numero)}</span>}
        </div>
      </div>
      {modulo.disponivel ? <>
        <a className={css.abrir} href={modulo.href}>
          <span className={css.nome}>{modulo.nome}</span>
          <MarcaIcone nome="proximo" tamanho={20} />
        </a>
        <span className={css.resumo}>{modulo.resumo}</span>
        <ul className={css.atalhos} aria-label={`Telas de ${modulo.nome}`}>
          {modulo.telas.map((tela) => <li key={tela.id}>
            <a className={css.atalho} href={hashDoModulo(modulo.id, tela.id)}>{tela.rotulo}</a>
          </li>)}
        </ul>
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
  const { permite, aoSair, notificacoes } = useAcessoModulos()
  const [busca, setBusca] = useState('')
  const [organizar, setOrganizar] = useState(false)
  const [preferencia, setPreferencia] = useState(() => lerPreferenciasHall(chavePreferencia))
  const [anterior, setAnterior] = useState(null)
  const [mensagem, setMensagem] = useState('')
  const [arrastando, setArrastando] = useState(null)
  const buscaRef = useRef(null)
  const organizarRef = useRef(null)
  const modulosPermitidos = modulosDoHall({ fonteApi }).filter((modulo) => permite(modulo.id))
  const todos = ordenarModulosDoHall(modulosPermitidos, preferencia)
  const modulos = filtrarModulos(todos, busca)
  const moduloArrastando = modulos.find((modulo) => modulo.id === arrastando)
  const favoritos = favoritosDoHall(modulosPermitidos, preferencia)
  const buscando = Boolean(busca.trim())
  const podeOrganizar = organizar && !buscando
  const sensores = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )
  const gravar = (proxima, texto, guardarAnterior = true) => {
    if (guardarAnterior && JSON.stringify(proxima) !== JSON.stringify(preferencia)) setAnterior(preferencia)
    setPreferencia(proxima)
    const persistiu = gravarPreferenciasHall(chavePreferencia, proxima)
    setMensagem(`${texto}${persistiu ? '' : chavePreferencia ? ' A personalização vale só nesta visita.' : ' A personalização vale só nesta tela.'}`)
  }
  const favoritar = (id) => {
    const modulo = modulosPermitidos.find((item) => item.id === id && item.disponivel)
    if (!modulo) return
    const proxima = alternarFavoritoDoHall(preferencia, id)
    gravar(proxima, `${modulo.nome} ${proxima.favoritos.includes(id) ? 'adicionado aos' : 'removido dos'} favoritos.`)
  }
  const mover = (id, destino) => {
    if (!podeOrganizar || !modulos.some((modulo) => modulo.id === id) || !modulos.some((modulo) => modulo.id === destino) || id === destino) return
    const proxima = moverModuloDoHall(preferencia, id, destino)
    const ordenados = ordenarModulosDoHall(modulosPermitidos, proxima)
    gravar(proxima, `${ordenados.find((modulo) => modulo.id === id).nome} movido para a posição ${ordenados.findIndex((modulo) => modulo.id === id) + 1} de ${ordenados.length}.`)
  }
  const moverPorBotao = (id, direcao) => {
    const destino = modulos[modulos.findIndex((modulo) => modulo.id === id) + direcao]?.id
    if (destino) mover(id, destino)
  }
  const anuncio = (id) => modulos.find((modulo) => modulo.id === id)?.nome ?? 'Módulo'
  return (
    <div className={css.hall}>
      <header className={css.topo}>
        <Marca />
        <div className={css.acoes}>
          {ajuda}
          <InterruptorTema />
          {notificacoes && <div className={css.avisos}>{notificacoes}</div>}
          {aoSair && <Botao className={css.sair} variante="texto" onClick={aoSair}>{sessao?.persistente ? 'Sair deste aparelho' : 'Sair'}</Botao>}
        </div>
      </header>
      <main className={css.conteudo}>
        <div className={css.abertura}>
          <div className={css.cabeca}>
            <p className={css.legenda}>Módulos da casa</p>
            <h1 className={css.titulo} tabIndex={-1} data-inicio-tela id="conteudo-principal">{saudacaoDoHall(agora, sessao?.usuario?.nome)}</h1>
            <p className={css.apoio}>Seu espaço de trabalho. Deixe as áreas da sua rotina sempre à mão.</p>
            {sessao?.persistente && <p className={css.apoio}>A Cozinha permanece conectada neste aparelho. Use Sair ao trocar de pessoa.</p>}
            {modulos.length === 0 && !busca && <output>Seu perfil ainda não tem módulos liberados. Peça à dona para revisar seu acesso.</output>}
          </div>
          <div className={css.busca}>
            <CampoTexto ref={buscaRef} rotulo="Buscar módulo ou tela" tipo="search"
              placeholder="Ex.: insumos, caixa ou entregas" value={busca}
              onChange={(evento) => setBusca(evento.target.value)} />
            {buscando && <Botao variante="texto" onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Limpar busca</Botao>}
          </div>
        </div>
        {/* #1608: o que fazer agora vem antes da lista de módulos. Quem compõe é o App. */}
        {!buscando && painel}
        {!buscando && favoritos.length > 0 && <nav className={css.rotina} aria-label="Favoritos da sua rotina">
          <div className={css.cabecaRotina}><span className={css.rotinaTitulo}>Sua rotina</span><span className={css.rotinaApoio}>Um toque para começar</span></div>
          <ul className={css.linksRotina}>{favoritos.map((modulo) => <li key={modulo.id}>
            <a className={css.linkRotina} href={modulo.href}><IconeDoModulo modulo={modulo} tamanho={24} /> {modulo.nome}<MarcaIcone nome="proximo" tamanho={16} /></a>
          </li>)}</ul>
        </nav>}
        {todos.length > 0 && <div className={css.organizacao}>
          <div className={css.instrucoes}>
            <h2 className={css.subtitulo}>Todas as áreas</h2>
            <p>{organizar ? (buscando ? 'Limpe a busca para reorganizar os módulos.' : 'Arraste pelo botão Mover ou use as setas de cada cartão.') : 'Favorite as áreas que você usa mais. A ordem só muda quando você escolher.'}</p>
          </div>
          <div className={css.ferramentas}>
            {anterior && <Botao variante="texto" onClick={() => {
              gravar(anterior, 'Personalização anterior restaurada.', false)
              setAnterior(null)
              if (organizarRef.current?.disabled) buscaRef.current?.focus()
              else organizarRef.current?.focus()
            }}>Desfazer</Botao>}
            <Botao ref={organizarRef} aria-pressed={organizar} variante={organizar ? 'primario' : 'secundario'}
              disabled={!organizar && (buscando || todos.length < 2)} onClick={() => { setOrganizar(!organizar); setMensagem('') }}>
              <MarcaIcone nome={organizar ? 'confirmar' : 'ordenar'} tamanho={20} />{organizar ? 'Concluir organização' : 'Organizar módulos'}
            </Botao>
          </div>
        </div>}
        {podeOrganizar && <div className={css.opcoesOrdem}>
          <Botao variante="texto" disabled={favoritos.length === 0} onClick={() => gravar(favoritosPrimeiroNoHall(preferencia), 'Favoritos primeiro. As outras áreas seguem a ordem original.')}>Favoritos primeiro</Botao>
          <Botao variante="texto" onClick={() => gravar(restaurarOrdemDoHall(preferencia), 'Ordem original restaurada. Seus favoritos foram mantidos.')}>Restaurar ordem original</Botao>
          <span>Teclado: Espaço para pegar, setas para mover, Espaço para soltar ou Esc para cancelar.</span>
        </div>}
        <output className={css.resultado} aria-live="polite">
          {buscando ? (modulos.length === 1 ? '1 módulo encontrado.' : `${modulos.length} módulos encontrados.`) : mensagem}
        </output>
        <DndContext sensors={sensores} collisionDetection={colisoesDoHall}
          onDragStart={({ active }) => setArrastando(active.id)}
          onDragCancel={() => setArrastando(null)}
          accessibility={{
            screenReaderInstructions: { draggable: 'No botão Mover, pressione Espaço para pegar o módulo, use as setas para escolher a posição, Espaço para soltar ou Escape para cancelar.' },
            announcements: {
              onDragStart: ({ active }) => `${anuncio(active.id)} selecionado para mover.`,
              onDragOver: ({ active, over }) => over ? `${anuncio(active.id)} sobre a posição ${modulos.findIndex((modulo) => modulo.id === over.id) + 1} de ${modulos.length}.` : `${anuncio(active.id)} fora da grade.`,
              onDragEnd: ({ active, over }) => over ? `${anuncio(active.id)} solto na posição ${modulos.findIndex((modulo) => modulo.id === over.id) + 1} de ${modulos.length}.` : 'Movimento cancelado. A ordem foi mantida.',
              onDragCancel: () => 'Movimento cancelado. A ordem foi mantida.',
            },
          }} onDragEnd={({ active, over }) => { setArrastando(null); if (over) mover(active.id, over.id) }}>
          <SortableContext items={modulos.map((modulo) => modulo.id)} strategy={rectSortingStrategy}>
            <ul className={css.grade} aria-label="Módulos">
              {modulos.map((modulo, indice) => <CartaoModulo key={modulo.id} modulo={modulo}
                favorito={preferencia.favoritos.includes(modulo.id)} organizar={podeOrganizar}
                indice={indice} total={modulos.length} aoFavoritar={favoritar} aoMover={moverPorBotao} />)}
            </ul>
          </SortableContext>
          <DragOverlay dropAnimation={null}>
            {moduloArrastando && <div className={css.previaArrasto} aria-hidden="true">
              <IconeDoModulo modulo={moduloArrastando} tamanho={32} />
              <strong>{anuncio(arrastando)}</strong>
              <span>Solte na posição desejada</span>
            </div>}
          </DragOverlay>
        </DndContext>
        {buscando && modulos.length === 0 && <Vazio titulo="Nenhum módulo ou tela encontrado">
          Confira o nome ou use Limpar busca para ver seus módulos novamente.
        </Vazio>}
      </main>
    </div>
  )
}
