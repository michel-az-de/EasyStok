import { useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { MarcaIcone } from '../../../componentes/MarcaIcone'
import { useGestaoCardapioApi } from '../../../aplicacao/useGestaoCardapioApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { FILTROS_DO_CARDAPIO, contagensDoCardapio, filtrarCardapio, podeReordenar } from '../../../dominio/filtroDoCardapio'
import { moeda } from '../../../dominio/formato'
import { EXPLICACAO_DOS_CONTROLES as EXPLICA, dicaEstatica } from '../../../dominio/dicasDoCardapio'
import css from './gestaoCardapio.module.css'

const LINHA = { servir: 'Para servir', casa: 'Preparar em casa' }
const dataCurta = (iso) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`

function FotoDoPrato({ item }) {
  const [falhou, setFalhou] = useState(null)
  return item.foto && falhou !== item.foto
    ? <img className={css.foto} src={item.foto} alt="" loading="lazy" onError={() => setFalhou(item.foto)} />
    : <span className={css.semFoto}><Icone nome="imagem" tamanho={28} /><span>{item.foto ? 'Foto indisponível' : 'Sem foto'}</span></span>
}

// #1808: o que falta no prato (foto, categoria, descrição). Quem edita pede a dica da IA; sem IA o
// servidor devolve a mesma dica estática e a tela avisa.
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
    <p className={css.dica} aria-live="polite">
      <span><strong>{resposta?.porIa ? 'Sugestão do Assistente: ' : 'Dica: '}</strong>{resposta?.texto || estatica}</span>
      {resposta && !resposta.porIa && resposta.erro && <small>O Assistente não respondeu. Exibindo a dica do sistema.</small>}
      {pedirDica && (!resposta || resposta.erro) && <Botao variante="texto" disabled={pedindo} title="Pedir ao Assistente uma dica para este prato. Nada será alterado."
        aria-label={`Pedir dica para ${item.nome}`} onClick={pedir}>{pedindo ? 'Pedindo dica…' : 'Pedir dica'}</Botao>}
    </p>
  )
}

// O editor vem do App: a lista e o Balcão usam a mesma ficha sem importar outra feature.
export function AbaCardapioApi({ renderizarEditor }) {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarCardapio === true
  const gestao = useGestaoCardapioApi()
  const [busca, setBusca] = useState('')
  const [filtro, setFiltro] = useState('todos')
  // #1808: a categoria combina com a disponibilidade e com a busca; nula é "Todas".
  const [categoria, setCategoria] = useState(null)
  const buscaRef = useRef(null)
  const [ocupado, setOcupado] = useState(null)
  const [editor, setEditor] = useState(null)
  const temEditor = podeEditar && typeof renderizarEditor === 'function'

  const recorte = useMemo(() => ({ filtro, categoria, busca }), [filtro, categoria, busca])
  const contagens = useMemo(() => contagensDoCardapio(gestao.itens, recorte), [gestao.itens, recorte])
  const itens = useMemo(() => filtrarCardapio(gestao.itens, recorte), [gestao.itens, recorte])

  const fazer = (sku, acao) => {
    setOcupado(sku)
    Promise.resolve(acao()).finally(() => setOcupado(null))
  }
  const abrirEditor = (item = null, focoInicial = null) => setEditor({ item, focoInicial })
  const limpar = () => { setBusca(''); setFiltro('todos'); setCategoria(null); buscaRef.current?.focus() }
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
        <div className={css.apresentacao}>
          <div>
            <p className={css.descricao}>Fotos, informações e o que está disponível hoje.</p>
          </div>
        </div>
        {temEditor && <Botao variante="primario" onClick={() => abrirEditor()}><MarcaIcone nome="adicionar" tamanho={20} />Novo prato</Botao>}
      </header>

      <div className={css.ferramentas}>
        <div className={css.busca}>
          <CampoTexto ref={buscaRef} tipo="search" rotulo="Buscar prato, categoria ou porção"
            placeholder="Ex.: lasanha, massas ou 800 g" value={busca} onChange={(e) => setBusca(e.target.value)} />
          {busca && <Botao variante="texto" onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Limpar busca</Botao>}
        </div>
        <div className={css.faixaFiltros}><fieldset className={css.filtros}>
          <legend>Filtrar pratos</legend>
          {FILTROS_DO_CARDAPIO.map((f) => (
            <button type="button" key={f.id} aria-pressed={filtro === f.id} className={css.filtro} title={EXPLICA.filtros} onClick={() => setFiltro(f.id)}>
              {f.rotulo}{' '}<span className={css.contagem}>{contagens.filtros[f.id]}</span>
            </button>
          ))}
        </fieldset>
        {contagens.categorias.length > 0 && <fieldset className={css.filtros}>
          <legend>Categoria</legend>
          <button type="button" aria-pressed={!categoria} className={css.filtro} title={EXPLICA.categoria} onClick={() => setCategoria(null)}>
            Todas{' '}<span className={css.contagem}>{contagens.todas}</span>
          </button>
          {contagens.categorias.map((c) => (
            <button type="button" key={c.nome} aria-pressed={categoria === c.nome} className={css.filtro} title={EXPLICA.categoria} onClick={() => setCategoria(c.nome)}>
              {c.nome}{' '}<span className={css.contagem}>{c.total}</span>
            </button>
          ))}
        </fieldset>}
      <div className={css.leitura}>
        <output className={css.descricao}>{itens.length} de {gestao.itens.length} {gestao.itens.length === 1 ? 'prato' : 'pratos'}</output>
        <details className={css.ajuda}>
          <summary>Entenda os controles</summary>
          <p><strong>Hoje</strong> permite oferecer o prato; desligado, ele só volta quando você religar. <strong>No site</strong> controla a exibição para o cliente. <strong>Arquivar</strong> tira do cardápio e permite repor depois. Um prato em validação só é oferecido pelo assistente depois de validado.</p>
        </details>
      </div>
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
        <ol className={css.lista}>
          {itens.map((item, indice) => (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- Atalho opcional; o botão Editar mantém o acesso por teclado.
            <li key={item.sku} className={css.prato} aria-busy={ocupado === item.sku}
              onDoubleClick={(e) => {
                if (temEditor && ocupado !== item.sku && !e.target.closest('button, input, label, a, summary, select, textarea')) abrirEditor(item)
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
                  {item.categoria && <span className={css.categoria}>{item.categoria}</span>}
                  <h3 className={css.nome}>{item.nome}</h3>
                  <p className={css.detalhe}>{[LINHA[item.linha], item.porcoes > 0 ? `${item.porcoes} ${item.porcoes === 1 ? 'porção' : 'porções'}` : item.porcao].filter(Boolean).join(' · ')}</p>
                  {(item.arquivado || item.emValidacao || !item.noSite || item.novidadeAte) && (
                    <p className={css.situacao}>{[
                      item.arquivado && 'Fora do cardápio',
                      item.emValidacao && 'Em validação',
                      !item.noSite && 'Oculto no site',
                      item.novidadeAte && `Novidade até ${dataCurta(item.novidadeAte)}`,
                    ].filter(Boolean).join(' · ')}</p>
                  )}
                  <DicaDoPrato item={item} pedirDica={podeEditar ? gestao.dicaDoPrato : null} />
                </div>
                <div className={css.valor}>
                  <span className={css.preco}>{item.porcoes > 0 && <small>A partir de </small>}{moeda(item.preco)}</span>
                  <label className={css.hoje} title={EXPLICA.hoje}>
                    <input type="checkbox" checked={item.hoje} disabled={ocupado === item.sku}
                      aria-label={`Disponível hoje: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarHoje(item.sku))} />
                    {item.hoje ? 'Hoje' : 'Fora de hoje'}
                  </label>
                </div>
              </div>
              <div className={css.rodapePrato}>
                {temEditor && <div className={css.acoesCadastro}>
                  <Botao variante="texto" disabled={ocupado === item.sku} title={EXPLICA.editar} aria-label={`Editar prato ${item.nome}`} onClick={() => abrirEditor(item)}><MarcaIcone nome="editar" tamanho={20} /></Botao>
                </div>}
                <details className={css.outrasAcoes}>
                  <summary title={EXPLICA.maisAcoes} aria-label={`Mais ações para ${item.nome}`}><span aria-hidden="true">•••</span></summary>
                  <div className={css.painelAcoes}>
                    <label className={css.chave} title={EXPLICA.noSite}>
                      <input type="checkbox" checked={item.noSite} disabled={!podeEditar || ocupado === item.sku}
                        aria-label={`No site: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarNoSite(item.sku))} />
                      No site
                    </label>
                    {item.emValidacao && <Botao variante="texto" disabled={!podeEditar || ocupado === item.sku} title={EXPLICA.validar}
                      aria-label={`Validar ${item.nome}`} onClick={() => fazer(item.sku, () => gestao.validar(item.sku))}><MarcaIcone nome="confirmar" tamanho={18} />Validar</Botao>}
                    <Botao variante="texto" disabled={!podeEditar || ocupado === item.sku} title={EXPLICA.arquivar}
                      aria-label={`${item.arquivado ? 'Repor' : 'Arquivar'} ${item.nome}`} onClick={() => fazer(item.sku, () => gestao.alternarArquivado(item.sku))}>
                      {item.arquivado ? 'Repor no cardápio' : 'Arquivar'}
                    </Botao>
                    {podeEditar && (ordenando ? (
                      <span className={css.ordem} title={EXPLICA.ordem}>
                        <span>Ordem</span>
                        <Botao variante="texto" aria-label={`Subir ${item.nome}`} disabled={indice === 0 || ocupado === item.sku} onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'subir'))}><MarcaIcone nome="acima" tamanho={18} /></Botao>
                        <Botao variante="texto" aria-label={`Descer ${item.nome}`} disabled={indice === itens.length - 1 || ocupado === item.sku} onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'descer'))}><MarcaIcone nome="abaixo" tamanho={18} /></Botao>
                      </span>
                    ) : <span className={css.detalhe}>Para mudar a ordem, escolha Todos e Todas as categorias e limpe a busca.</span>)}
                  </div>
                </details>
              </div>
            </li>
          ))}
        </ol>
      )}
      {editor && renderizarEditor?.({ ...editor, aoFechar: () => setEditor(null), aoSalvar: gestao.recarregar })}
    </div>
  )
}
