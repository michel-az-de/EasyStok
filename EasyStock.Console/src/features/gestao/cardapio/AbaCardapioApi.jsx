import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { Vazio } from '../../../componentes/Vazio'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { MarcaIcone } from '../../../componentes/MarcaIcone'
import { MenuDeAcoes } from '../../../componentes/MenuDeAcoes'
import { useGestaoCardapioApi } from '../../../aplicacao/useGestaoCardapioApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { FILTROS_DO_CARDAPIO, contagensDoCardapio, filtrarCardapio, filtroDaHash, hashDoFiltro, podeReordenar, rotuloDaCategoria } from '../../../dominio/filtroDoCardapio'
import { ehNovidade } from '../../../dominio/cardapio'
import { moeda } from '../../../dominio/formato'
import { EXPLICACAO_DOS_CONTROLES as EXPLICA, dicaEstatica, legendaDasPendencias, rotuloDasPendencias } from '../../../dominio/dicasDoCardapio'
import css from './gestaoCardapio.module.css'

const LINHA = { servir: 'Para servir', casa: 'Preparar em casa' }
const dataCurta = (iso) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`
// #2424: a porção só entra no detalhe quando o nome ainda não a diz ("Talharim 500 g" já diz).
const porcaoDoDetalhe = (item) => {
  if (item.porcoes > 0) return `${item.porcoes} ${item.porcoes === 1 ? 'porção' : 'porções'}`
  return item.porcao && !item.nome?.toLocaleLowerCase('pt-BR').includes(item.porcao.toLocaleLowerCase('pt-BR')) ? item.porcao : null
}

function FotoDoPrato({ item }) {
  const [falhou, setFalhou] = useState(null)
  return item.foto && falhou !== item.foto
    ? <img className={css.foto} src={item.foto} alt="" loading="lazy" onError={() => setFalhou(item.foto)} />
    : <span className={css.semFoto} title={item.foto ? 'Foto indisponível' : 'Sem foto'}><Icone nome="imagem" tamanho={20} /><span className="sr">{item.foto ? 'Foto indisponível' : 'Sem foto'}</span></span>
}

// #1808: o que falta no prato (foto, categoria, descrição). #2424: vira a coluna Pendência, com o rótulo
// curto em âmbar e a dica completa no title; quem edita pede a dica da IA num ícone discreto. A resposta
// aparece numa linha própria do prato; sem IA o servidor devolve a mesma dica estática e a tela avisa.
function DicaDoPrato({ item, pedirDica }) {
  const [resposta, setResposta] = useState(null)
  const [pedindo, setPedindo] = useState(false)
  const estatica = dicaEstatica(item.pendencias)
  if (!estatica && !resposta) return null
  const pedir = () => {
    setPedindo(true)
    Promise.resolve(pedirDica(item.sku))
      .then((r) => setResposta({ texto: r.texto, porIa: r.porIa === true }))
      .catch((e) => setResposta({ texto: estatica, erro: e.message }))
      .finally(() => setPedindo(false))
  }
  return (
    <>
      <div className={css.pendencia}>
        {estatica && <span className={css.rotuloPendencia} title={estatica}>{rotuloDasPendencias(item.pendencias)}<span className="sr">: {estatica}</span></span>}
        {pedirDica && (!resposta || resposta.erro) && <BotaoIcone icone="dica" tamanhoIcone={16} className={css.pedirDica} disabled={pedindo}
          rotulo={`Pedir dica para ${item.nome}`} dica={pedindo ? 'Pedindo dica…' : 'Pedir ao Assistente uma dica para este prato. Nada será alterado.'} onClick={pedir} />}
      </div>
      <p className={css.dica} aria-live="polite">
        {resposta && <>
          <span><strong>{resposta.porIa ? 'Sugestão do Assistente: ' : 'Dica: '}</strong>{resposta.texto || estatica}</span>
          {!resposta.porIa && resposta.erro && <small>O Assistente não respondeu. Exibindo a dica do sistema.</small>}
        </>}
      </p>
    </>
  )
}

// O editor vem do App: a lista e o Balcão usam a mesma ficha sem importar outra feature.
export function AbaCardapioApi({ renderizarEditor }) {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarCardapio === true
  const gestao = useGestaoCardapioApi()
  const [busca, setBusca] = useState('')
  // #2258 (auditoria C1): a home leva a `#/m/cardapio/itens/pendencia`; o filtro nasce da rota e a acompanha.
  const [filtro, setFiltro] = useState(() => filtroDaHash(globalThis.location?.hash) ?? 'todos')
  // #1808: a categoria combina com a disponibilidade e com a busca; nula é "Todas".
  const [categoria, setCategoria] = useState(null)
  const buscaRef = useRef(null)
  const [ocupado, setOcupado] = useState(null)
  const [editor, setEditor] = useState(null)
  const temEditor = podeEditar && typeof renderizarEditor === 'function'
  useEffect(() => {
    const aoMudarHash = () => {
      const doLink = filtroDaHash(window.location.hash)
      if (doLink) { setFiltro(doLink); setCategoria(null); setBusca('') }
    }
    window.addEventListener('hashchange', aoMudarHash)
    return () => window.removeEventListener('hashchange', aoMudarHash)
  }, [])

  const recorte = useMemo(() => ({ filtro, categoria, busca }), [filtro, categoria, busca])
  const contagens = useMemo(() => contagensDoCardapio(gestao.itens, recorte), [gestao.itens, recorte])
  const itens = useMemo(() => filtrarCardapio(gestao.itens, recorte), [gestao.itens, recorte])
  const legenda = useMemo(() => legendaDasPendencias(itens), [itens])

  const fazer = (sku, acao) => {
    setOcupado(sku)
    Promise.resolve(acao()).finally(() => setOcupado(null))
  }
  const abrirEditor = (item = null, focoInicial = null) => setEditor({ item, focoInicial })
  // O filtro escolhido volta para a rota só quando ela já é a desta lista; assim o link da home, aberto de novo, reaplica.
  const escolherFiltro = (id) => {
    setFiltro(id)
    try { if (window.location.hash.startsWith('#/m/cardapio/itens')) history.replaceState(null, '', hashDoFiltro(id)) } catch { /* a rota segue a mesma */ }
  }
  const limpar = () => { setBusca(''); escolherFiltro('todos'); setCategoria(null); buscaRef.current?.focus() }
  const ordenando = podeReordenar(recorte)

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando o cardápio…</output>
  if (gestao.estado === 'erro' && gestao.itens.length === 0 && !editor) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não foi possível carregar o cardápio"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  return (
    <div className={css.aba}>
      <header className={css.cabecalho}>
        <p className={css.descricao}>Fotos, preços e disponibilidade dos pratos.</p>
        {temEditor && <Botao variante="primario" onClick={() => abrirEditor()}><MarcaIcone nome="adicionar" tamanho={20} />Novo prato</Botao>}
      </header>

      <div className={css.ferramentas}>
        <div className={css.busca}>
          <div className={css.campoBusca}>
            <CampoTexto ref={buscaRef} tipo="search" rotulo="Buscar prato, categoria ou porção" rotuloOculto className={css.entradaBusca}
              placeholder="Buscar prato, categoria ou porção" value={busca} onChange={(e) => setBusca(e.target.value)} />
            <Icone nome="busca" tamanho={20} className={css.lupa} />
          </div>
          {contagens.categorias.length > 0 && <div className={css.campoCategoria}><CampoSelecao rotulo="Categoria" value={categoria ?? ''}
            onChange={(e) => setCategoria(e.target.value || null)}
            opcoes={[
              { valor: '', rotulo: `Todas (${contagens.todas})` },
              ...contagens.categorias.map((c) => ({ valor: c.nome, rotulo: `${rotuloDaCategoria(c.nome)} (${c.total})` })),
            ]} /></div>}
        </div>
        <fieldset className={css.filtros}>
          <legend className="sr">Filtrar pratos</legend>
          {FILTROS_DO_CARDAPIO.map((f) => (
            <button type="button" key={f.id} aria-pressed={filtro === f.id} className={css.filtro} title={EXPLICA.filtros} onClick={() => escolherFiltro(f.id)}>
              {f.rotulo}{' '}<span className={css.contagem}>{contagens.filtros[f.id]}</span>
            </button>
          ))}
        </fieldset>
        <div className={css.leitura}>
          <div className={css.contador}>
            <output>{itens.length} de {gestao.itens.length} {gestao.itens.length === 1 ? 'prato' : 'pratos'}</output>
            {!ordenando && <Botao variante="texto" className={css.link} onClick={limpar}>Limpar filtros</Botao>}
          </div>
          <details className={css.ajuda}>
            <summary>Entenda os controles</summary>
            <p><strong>Hoje</strong> permite oferecer o prato; desligado, ele só volta quando você religar. <strong>No site</strong> controla a exibição para o cliente. <strong>Arquivar</strong> tira do cardápio e permite repor depois. Um prato em validação só é oferecido pelo assistente depois de validado.</p>
          </details>
        </div>
      </div>

      {gestao.aviso && <div className={css.aviso} role="alert"><span>{gestao.aviso}</span><Botao variante="texto" onClick={gestao.fecharAviso}>Fechar aviso</Botao></div>}
      {gestao.estado === 'erro' && <div className={css.aviso} role="alert"><span>Não foi possível atualizar o cardápio. {gestao.erro}</span><Botao variante="texto" onClick={gestao.recarregar}>Tentar de novo</Botao></div>}
      {!podeEditar && <p className={css.descricao}>Seu perfil permite consultar pratos e ajustar a disponibilidade de hoje.</p>}

      {itens.length === 0 ? (
        <Vazio icone={gestao.itens.length ? 'search' : 'cardapio'}
          titulo={gestao.itens.length ? 'Nenhum prato encontrado' : 'Nenhum prato cadastrado'}
          acao={gestao.itens.length ? <Botao onClick={limpar}>Ver todos os pratos</Botao> : temEditor ? <Botao variante="primario" onClick={() => abrirEditor()}>Novo prato</Botao> : null}>
          {gestao.itens.length ? 'Busque outro nome ou limpe os filtros.' : podeEditar ? 'Adicione o primeiro prato ao cardápio.' : 'Peça à gerência para cadastrar os pratos.'}
        </Vazio>
      ) : (
        <div className={css.tabela}>
          {/* #2424: cabeçalho visual das colunas; cada controle da linha já tem o nome completo. */}
          <div className={css.colunas} aria-hidden="true">
            <span className={css.colunaPrato}>Prato</span><span className={css.colunaPreco}>Preço</span><span>Hoje</span><span>No site</span><span>Pendência</span><span className={css.colunaAcoes}>Ações</span>
          </div>
          <ol className={css.lista}>
            {itens.map((item, indice) => (
              // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- Atalho opcional; o botão Editar mantém o acesso por teclado.
              <li key={item.sku} className={css.prato} aria-busy={ocupado === item.sku}
                onDoubleClick={(e) => {
                  if (temEditor && ocupado !== item.sku && !e.target.closest('button, input, label, a, summary, select, textarea, [role="menu"]')) abrirEditor(item)
                }}>
                <div className={css.cabecalhoPrato}>
                  {temEditor ? (
                    <button type="button" className={css.abrirFoto} disabled={ocupado === item.sku}
                      title={EXPLICA.foto}
                      aria-label={`${item.foto ? 'Alterar' : 'Adicionar'} foto de ${item.nome}`} onClick={() => abrirEditor(item, 'foto')}>
                      <FotoDoPrato item={item} />
                    </button>
                  ) : <div className={css.abrirFoto}><FotoDoPrato item={item} /></div>}
                  <div className={css.resumo}>
                    <h3 className={css.nome}>{item.nome}</h3>
                    <p className={css.detalhe}>
                      {item.categoria && <><span className={css.categoria}>{rotuloDaCategoria(item.categoria)}</span>{' · '}</>}
                      {[LINHA[item.linha], porcaoDoDetalhe(item)].filter(Boolean).join(' · ')}
                    </p>
                    {(item.arquivado || item.emValidacao || ehNovidade(item, Date.now())) && (
                      <p className={css.situacao}>{[
                        item.arquivado && 'Fora do cardápio',
                        item.emValidacao && 'Em validação',
                        // #2258 (auditoria C3): o selo some quando a data passa.
                        ehNovidade(item, Date.now()) && `Novidade até ${dataCurta(item.novidadeAte)}`,
                      ].filter(Boolean).join(' · ')}</p>
                    )}
                  </div>
                  <div className={css.valor}>
                    <span className={css.preco}>{item.porcoes > 0 && <small>A partir de </small>}{moeda(item.preco)}</span>
                    {/* #2258 (auditoria C2): os dois interruptores lado a lado; "No site" não fica escondido no menu. */}
                    <div className={css.interruptores}>
                      <label className={`${css.hoje} ${css.chaveHoje}`} title={EXPLICA.hoje}>
                        <input type="checkbox" checked={item.hoje} disabled={ocupado === item.sku}
                          aria-label={`Disponível hoje: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarHoje(item.sku))} />
                        <span className={css.rotuloChave}>Hoje</span>
                      </label>
                      <label className={`${css.hoje} ${css.chaveSite}`} title={EXPLICA.noSite}>
                        <input type="checkbox" checked={item.noSite} disabled={!podeEditar || ocupado === item.sku}
                          aria-label={`No site: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarNoSite(item.sku))} />
                        <span className={css.rotuloChave}>No site</span>
                      </label>
                    </div>
                  </div>
                </div>
                <DicaDoPrato item={item} pedirDica={podeEditar ? gestao.dicaDoPrato : null} />
                <div className={css.rodapePrato}>
                  {temEditor && <div className={css.acoesCadastro}>
                    <BotaoIcone rotulo={`Editar prato ${item.nome}`} dica={EXPLICA.editar} icone="lapis" disabled={ocupado === item.sku} onClick={() => abrirEditor(item)} />
                  </div>}
                  <MenuDeAcoes
                    rotulo={`Mais opções de ${item.nome}`} dica={EXPLICA.maisAcoes}
                    itens={[
                      item.emValidacao && {
                        chave: 'validar', rotulo: 'Validar', icone: 'check', detalhe: EXPLICA.validar,
                        desabilitado: !podeEditar || ocupado === item.sku, aoEscolher: () => fazer(item.sku, () => gestao.validar(item.sku)),
                      },
                      {
                        chave: 'arquivar', rotulo: item.arquivado ? 'Repor no cardápio' : 'Arquivar', icone: 'archive', detalhe: EXPLICA.arquivar,
                        desabilitado: !podeEditar || ocupado === item.sku, aoEscolher: () => fazer(item.sku, () => gestao.alternarArquivado(item.sku)),
                      },
                      ...(podeEditar && ordenando ? [
                        {
                          chave: 'subir', rotulo: 'Subir na ordem', icone: 'arrow-up',
                          desabilitado: indice === 0 || ocupado === item.sku, aoEscolher: () => fazer(item.sku, () => gestao.mover(item.sku, 'subir')),
                        },
                        {
                          chave: 'descer', rotulo: 'Descer na ordem', icone: 'arrow-down',
                          desabilitado: indice === itens.length - 1 || ocupado === item.sku, aoEscolher: () => fazer(item.sku, () => gestao.mover(item.sku, 'descer')),
                        },
                      ] : []),
                      podeEditar && !ordenando && {
                        chave: 'ordem', rotulo: 'Mudar a ordem', icone: 'arrow-up-down', desabilitado: true,
                        motivo: 'Para mudar a ordem, escolha Todos e Todas as categorias e limpe a busca.',
                      },
                    ]}
                  />
                </div>
              </li>
            ))}
          </ol>
          {legenda.length > 0 && <p className={css.nota}>{legenda.map((l) => <span key={l.codigo}><strong>{l.rotulo}:</strong> {l.texto}.</span>)}</p>}
        </div>
      )}
      {editor && renderizarEditor?.({ ...editor, aoFechar: () => setEditor(null), aoSalvar: gestao.recarregar })}
    </div>
  )
}
