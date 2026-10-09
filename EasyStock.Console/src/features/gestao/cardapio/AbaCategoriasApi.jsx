import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useSecoesCardapioApi } from '../../../aplicacao/useSecoesCardapioApi'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import css from './abaCardapio.module.css'

// M1 › Categorias (M1.3, #1483). As categorias que o cliente vê no site e que agrupam a comanda,
// na ordem que ela define. Renomear, esconder (sem apagar os pratos), subir/descer e excluir só a
// vazia. "Converter as categorias antigas" cria uma categoria por texto livre que os pratos já
// tinham e liga cada prato a ela; pode rodar de novo sem duplicar.
function LinhaCategoria({ secao, primeira, ultima, gestao }) {
  const [editando, setEditando] = useState(false)
  const [nome, setNome] = useState(secao.nome)
  const [ocupado, setOcupado] = useState(false)

  const fazer = (acao) => {
    setOcupado(true)
    return Promise.resolve(acao()).finally(() => setOcupado(false))
  }

  const salvar = (evento) => {
    evento.preventDefault()
    fazer(() => gestao.renomear(secao.id, nome)).then((ok) => { if (ok !== false) setEditando(false) })
  }

  return (
    <li className={css.linha} aria-busy={ocupado}>
      {editando ? (
        <form className={css.resumo} onSubmit={salvar}>
          <input
            className={css.busca}
            aria-label={`Novo nome de ${secao.nome}`}
            value={nome}
            maxLength={100}
            onChange={(e) => setNome(e.target.value)}
          />
        </form>
      ) : (
        <div className={css.resumo}>
          <span className={css.nome}>{secao.nome}</span>
          <span className={css.detalhe}>{secao.itens === 1 ? '1 prato' : `${secao.itens} pratos`}</span>
        </div>
      )}
      <div className={css.estados}>
        {!secao.visivel && <Pilula tom="neutro" fina>Escondida</Pilula>}
      </div>
      <div className={css.acoes}>
        {editando ? (
          <>
            <Botao variante="texto" onClick={() => { setNome(secao.nome); setEditando(false) }}>Cancelar</Botao>
            <Botao variante="primario" disabled={ocupado || !nome.trim()} onClick={salvar}>Salvar</Botao>
          </>
        ) : (
          <>
            <Botao variante="texto" disabled={ocupado} onClick={() => setEditando(true)}>Renomear</Botao>
            <Botao variante="texto" disabled={ocupado} onClick={() => fazer(() => gestao.alternarVisivel(secao.id))}>
              {secao.visivel ? 'Esconder' : 'Mostrar'}
            </Botao>
            <Botao
              variante="texto"
              disabled={ocupado || secao.itens > 0}
              title={secao.itens > 0 ? 'Mude os pratos de categoria antes de excluir' : undefined}
              onClick={() => fazer(() => gestao.excluir(secao.id))}
            >
              Excluir
            </Botao>
            <span className={css.ordem}>
              <Botao variante="texto" icone="chevron-up" aria-label={`Subir ${secao.nome}`}
                disabled={primeira || ocupado} onClick={() => fazer(() => gestao.mover(secao.id, 'subir'))} />
              <Botao variante="texto" icone="chevron-down" aria-label={`Descer ${secao.nome}`}
                disabled={ultima || ocupado} onClick={() => fazer(() => gestao.mover(secao.id, 'descer'))} />
            </span>
          </>
        )}
      </div>
    </li>
  )
}

export function AbaCategoriasApi() {
  const { acoes } = useAcessoModulos()
  const gestao = useSecoesCardapioApi()
  const [nova, setNova] = useState('')
  const [criando, setCriando] = useState(false)

  const criar = (evento) => {
    evento.preventDefault()
    if (!nova.trim()) return
    setCriando(true)
    Promise.resolve(gestao.criar(nova)).then((ok) => { if (ok !== false) setNova('') }).finally(() => setCriando(false))
  }

  if (gestao.estado === 'carregando') return <p className={css.descricao}>Carregando as categorias…</p>
  if (gestao.estado === 'erro' && gestao.secoes.length === 0) {
    return (
      <div className={css.aba} role="alert">
        <p>Não consegui ler as categorias: {gestao.erro}</p>
        <Botao onClick={gestao.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }

  if (acoes.editarCardapio !== true) return (
    <div className={css.aba}>
      <p className={css.descricao}>Você pode consultar as categorias. As alterações ficam com a dona.</p>
      {gestao.secoes.length === 0 ? <p>Nenhuma categoria cadastrada.</p> : (
        <ol className={css.lista}>{gestao.secoes.map((secao) => (
          <li className={css.linha} key={secao.id}><span className={css.nome}>{secao.nome}</span><span>{secao.itens} prato(s)</span></li>
        ))}</ol>
      )}
    </div>
  )
  return (
    <div className={css.aba}>
      <div className={css.topo}>
        <p className={css.descricao}>
          As categorias que o cliente vê no site e que agrupam a comanda, nesta ordem. Esconder tira a categoria
          inteira do site sem apagar os pratos. Só dá para excluir categoria vazia.
        </p>
        <Botao onClick={gestao.migrar}>Converter as categorias antigas</Botao>
      </div>

      {gestao.aviso && (
        <output className={css.aviso}>
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </output>
      )}

      <form className={css.topo} onSubmit={criar}>
        <input
          className={css.busca}
          placeholder="Nova categoria (ex.: Massas)"
          aria-label="Nome da nova categoria"
          maxLength={100}
          value={nova}
          onChange={(e) => setNova(e.target.value)}
        />
        <Botao tipo="submit" variante="primario" disabled={criando || !nova.trim()}>Criar categoria</Botao>
      </form>

      {gestao.secoes.length === 0 ? (
        <p className={css.descricao}>Nenhuma categoria ainda. Crie uma ou converta as categorias antigas dos pratos.</p>
      ) : (
        <ol className={css.lista}>
          {gestao.secoes.map((secao, indice) => (
            <LinhaCategoria
              key={secao.id}
              secao={secao}
              primeira={indice === 0}
              ultima={indice === gestao.secoes.length - 1}
              gestao={gestao}
            />
          ))}
        </ol>
      )}
    </div>
  )
}
