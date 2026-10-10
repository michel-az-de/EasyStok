import { useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { MarcaIcone } from '../../../componentes/MarcaIcone'
import { useGestaoCardapioApi } from '../../../aplicacao/useGestaoCardapioApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { moeda } from '../../../dominio/formato'
import css from './gestaoCardapio.module.css'

const LINHA = { servir: 'Para servir', casa: 'Preparar em casa' }
const FILTROS = [
  { id: 'todos', rotulo: 'Todos', aceita: () => true },
  { id: 'hoje', rotulo: 'Disponíveis hoje', aceita: (item) => item.hoje && !item.arquivado },
  { id: 'fora', rotulo: 'Fora de hoje', aceita: (item) => !item.hoje || item.arquivado },
  { id: 'sem-foto', rotulo: 'Sem foto', aceita: (item) => !item.foto },
]
const dataCurta = (iso) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`
const normalizar = (texto) => (texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

function FotoDoPrato({ item }) {
  const [falhou, setFalhou] = useState(null)
  return item.foto && falhou !== item.foto
    ? <img className={css.foto} src={item.foto} alt="" loading="lazy" onError={() => setFalhou(item.foto)} />
    : <span className={css.semFoto}><Icone nome="imagem" tamanho={28} /><span>{item.foto ? 'Foto indisponível' : 'Sem foto'}</span></span>
}

// O editor vem do App: a lista e o Balcão usam a mesma ficha sem importar outra feature.
export function AbaCardapioApi({ renderizarEditor }) {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarCardapio === true
  const gestao = useGestaoCardapioApi()
  const [busca, setBusca] = useState('')
  const [filtro, setFiltro] = useState('todos')
  const buscaRef = useRef(null)
  const [ocupado, setOcupado] = useState(null)
  const [editor, setEditor] = useState(null)
  const temEditor = podeEditar && typeof renderizarEditor === 'function'

  const contagens = useMemo(() => Object.fromEntries(FILTROS.map((f) => [f.id, gestao.itens.filter(f.aceita).length])), [gestao.itens])
  const itens = useMemo(() => {
    const termo = normalizar(busca.trim())
    const aceita = FILTROS.find((f) => f.id === filtro).aceita
    return gestao.itens.filter((item) => aceita(item)
      && (!termo || normalizar(`${item.nome} ${item.categoria ?? ''} ${item.porcao ?? ''}`).includes(termo)))
  }, [gestao.itens, busca, filtro])

  const fazer = (sku, acao) => {
    setOcupado(sku)
    Promise.resolve(acao()).finally(() => setOcupado(null))
  }
  const abrirEditor = (item = null, focoInicial = null) => setEditor({ item, focoInicial })
  const limpar = () => { setBusca(''); setFiltro('todos'); buscaRef.current?.focus() }
  // A posição é a lista completa do servidor; ordenar um recorte mudaria vizinhos invisíveis.
  const ordenando = !busca.trim() && filtro === 'todos'

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando o cardápio…</output>
  if (gestao.estado === 'erro' && gestao.itens.length === 0 && !editor) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não consegui ler o cardápio"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  return (
    <div className={css.aba}>
      <header className={css.cabecalho}>
        <div className={css.apresentacao}>
          <span className={css.marca}><MarcaIcone nome="produto" tamanho={28} /></span>
          <div>
            <h3>Pratos da casa</h3>
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
          <legend className="sr">Filtrar pratos</legend>
          {FILTROS.map((f) => (
            <button type="button" key={f.id} aria-pressed={filtro === f.id} className={css.filtro} onClick={() => setFiltro(f.id)}>
              {f.rotulo}{' '}<span className={css.contagem}>{contagens[f.id]}</span>
            </button>
          ))}
        </fieldset>
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
      {gestao.estado === 'erro' && <div className={css.aviso} role="alert"><span>Não consegui atualizar o cardápio: {gestao.erro}</span><Botao variante="texto" onClick={gestao.recarregar}>Tentar de novo</Botao></div>}
      {!podeEditar && <p className={css.descricao}>Você pode consultar os pratos e ajustar a disponibilidade de hoje. As outras alterações ficam com a dona ou um gerente.</p>}

      {itens.length === 0 ? (
        <Vazio icone={gestao.itens.length ? 'search' : 'cardapio'}
          titulo={gestao.itens.length ? 'Nenhum prato neste recorte' : 'Seu primeiro prato começa aqui'}
          acao={gestao.itens.length ? <Botao onClick={limpar}>Ver todos os pratos</Botao> : temEditor ? <Botao variante="primario" onClick={() => abrirEditor()}>Novo prato</Botao> : null}>
          {gestao.itens.length ? 'Experimente outro nome ou veja o cardápio completo.' : podeEditar ? 'Cadastre o nome, a foto e as porções para preparar o cardápio.' : 'A dona ou um gerente pode cadastrar os pratos.'}
        </Vazio>
      ) : (
        <ol className={css.lista}>
          {itens.map((item, indice) => (
            <li key={item.sku} className={css.prato} aria-busy={ocupado === item.sku}>
              <div className={css.cabecalhoPrato}>
                {temEditor ? (
                  <button type="button" className={css.abrirFoto} disabled={ocupado === item.sku}
                    aria-label={`${item.foto ? 'Alterar' : 'Adicionar'} foto de ${item.nome}`} onClick={() => abrirEditor(item, 'foto')}>
                    <FotoDoPrato item={item} />
                  </button>
                ) : <div className={css.abrirFoto}><FotoDoPrato item={item} /></div>}
                <div className={css.resumo}>
                  <h4 className={css.nome}>{item.nome}</h4>
                  <p className={css.detalhe}>{[LINHA[item.linha], item.categoria, item.porcoes > 0 ? `${item.porcoes} ${item.porcoes === 1 ? 'porção' : 'porções'}` : item.porcao].filter(Boolean).join(' · ')}</p>
                  {(item.arquivado || item.emValidacao || !item.noSite || item.novidadeAte) && (
                    <p className={css.situacao}>{[
                      item.arquivado && 'Fora do cardápio',
                      item.emValidacao && 'Em validação',
                      !item.noSite && 'Oculto no site',
                      item.novidadeAte && `Novidade até ${dataCurta(item.novidadeAte)}`,
                    ].filter(Boolean).join(' · ')}</p>
                  )}
                </div>
                <div className={css.valor}>
                  <span className={css.preco}>{item.porcoes > 0 && <small>A partir de </small>}{moeda(item.preco)}</span>
                  <label className={css.hoje}>
                    <input type="checkbox" checked={item.hoje} disabled={ocupado === item.sku}
                      aria-label={`Disponível hoje: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarHoje(item.sku))} />
                    {item.hoje ? 'Hoje' : 'Fora de hoje'}
                  </label>
                </div>
              </div>
              <div className={css.rodapePrato}>
                {temEditor && <div className={css.acoesCadastro}>
                  <Botao variante="texto" disabled={ocupado === item.sku} aria-label={`Editar prato ${item.nome}`} onClick={() => abrirEditor(item)}><MarcaIcone nome="editar" tamanho={20} />Editar prato</Botao>
                  <Botao variante="texto" disabled={ocupado === item.sku} aria-label={`Foto de ${item.nome}`} onClick={() => abrirEditor(item, 'foto')}><Icone nome="imagem" tamanho={18} />Foto</Botao>
                </div>}
                <details className={css.outrasAcoes}>
                  <summary aria-label={`Mais ações para ${item.nome}`}>Mais ações<MarcaIcone nome="abaixo" tamanho={16} /></summary>
                  <div className={css.painelAcoes}>
                    <label className={css.chave}>
                      <input type="checkbox" checked={item.noSite} disabled={!podeEditar || ocupado === item.sku}
                        aria-label={`No site: ${item.nome}`} onChange={() => fazer(item.sku, () => gestao.alternarNoSite(item.sku))} />
                      No site
                    </label>
                    {item.emValidacao && <Botao variante="texto" disabled={!podeEditar || ocupado === item.sku}
                      aria-label={`Validar ${item.nome}`} onClick={() => fazer(item.sku, () => gestao.validar(item.sku))}><MarcaIcone nome="confirmar" tamanho={18} />Validar</Botao>}
                    <Botao variante="texto" disabled={!podeEditar || ocupado === item.sku}
                      aria-label={`${item.arquivado ? 'Repor' : 'Arquivar'} ${item.nome}`} onClick={() => fazer(item.sku, () => gestao.alternarArquivado(item.sku))}>
                      {item.arquivado ? 'Repor no cardápio' : 'Arquivar'}
                    </Botao>
                    {podeEditar && (ordenando ? (
                      <span className={css.ordem}>
                        <span>Ordem</span>
                        <Botao variante="texto" aria-label={`Subir ${item.nome}`} disabled={indice === 0 || ocupado === item.sku} onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'subir'))}><MarcaIcone nome="acima" tamanho={18} /></Botao>
                        <Botao variante="texto" aria-label={`Descer ${item.nome}`} disabled={indice === itens.length - 1 || ocupado === item.sku} onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'descer'))}><MarcaIcone nome="abaixo" tamanho={18} /></Botao>
                      </span>
                    ) : <span className={css.detalhe}>Veja Todos e limpe a busca para mudar a ordem.</span>)}
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
