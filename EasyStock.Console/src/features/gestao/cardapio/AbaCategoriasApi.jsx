import { useState } from 'react'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { useSecoesCardapioApi } from '../../../aplicacao/useSecoesCardapioApi'
import { resumoCategoriasAntigas } from '../../../aplicacao/gestaoSecoes'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import css from './abaCardapio.module.css'

// M1 › Categorias (M1.3, #1483). As categorias que o cliente vê no site e que agrupam a comanda,
// na ordem que ela define. Renomear, esconder (sem apagar os pratos), subir/descer e excluir só a
// vazia. "Converter as categorias antigas" cria uma categoria por texto livre que os pratos já
// tinham e liga cada prato a ela; pode rodar de novo sem duplicar. #1803: enquanto houver prato só
// com o texto antigo, a tela diz quais categorias são e o que a conversão faz (prévia do servidor).
function LinhaCategoria({ secao, primeira, ultima, gestao }) {
  const [editando, setEditando] = useState(false)
  const [nome, setNome] = useState(secao.nome)
  const [ocupado, setOcupado] = useState(false)

  const fazer = (acao) => {
    if (ocupado) return Promise.resolve(false)
    setOcupado(true)
    return Promise.resolve(acao()).finally(() => setOcupado(false))
  }

  const salvar = (evento) => {
    evento.preventDefault()
    fazer(() => gestao.renomear(secao.id, nome)).then((ok) => { if (ok !== false) setEditando(false) })
  }

  return (
    <li className={css.linha} aria-busy={ocupado}>
      {editando && <Modal titulo={`Renomear ${secao.nome}`} aoFechar={ocupado ? undefined : () => setEditando(false)}>
        <form className={css.resumo} onSubmit={salvar}>
          <CampoTexto
            rotulo="Nome da categoria"
            aria-label={`Novo nome de ${secao.nome}`}
            value={nome}
            disabled={ocupado}
            maxLength={100}
            onChange={(e) => setNome(e.target.value)}
          />
          {gestao.aviso && <p role="alert" className={css.aviso}>{gestao.aviso}</p>}
          <div className={css.acoes}><Botao disabled={ocupado} onClick={() => setEditando(false)}>Cancelar</Botao><Botao tipo="submit" variante="primario" disabled={ocupado || !nome.trim()}>Salvar nome</Botao></div>
        </form>
      </Modal>}
        <div className={css.resumo}>
          <span className={css.nome}>{secao.nome}</span>
          <span className={css.detalhe}>{secao.itens === 1 ? '1 prato' : `${secao.itens} pratos`}</span>
        </div>
      <div className={css.estados}>
        {!secao.visivel && <Pilula tom="neutro" fina>Oculta no site</Pilula>}
      </div>
      <div className={css.acoes}>
          <>
            <Botao variante="texto" icone="lapis" disabled={ocupado} aria-label={`Renomear ${secao.nome}`} aria-haspopup="dialog" onClick={() => { setNome(secao.nome); gestao.fecharAviso(); setEditando(true) }}>Renomear</Botao>
            <Botao variante="texto" disabled={ocupado} onClick={() => fazer(() => gestao.alternarVisivel(secao.id))}>
              {secao.visivel ? 'Ocultar no site' : 'Mostrar no site'}
            </Botao>
            <Botao
              className={css.acaoDiscreta}
              variante="texto"
              icone="lixeira"
              disabled={ocupado || secao.itens > 0}
              aria-label={`Excluir ${secao.nome}`}
              title={secao.itens > 0 ? 'Mude os pratos de categoria antes de excluir' : undefined}
              onClick={() => fazer(() => gestao.excluir(secao.id))}
            >
              Excluir
            </Botao>
            <span className={css.ordem}>
              <Botao className={css.acaoDiscreta} variante="texto" icone="chevron-up" aria-label={`Subir ${secao.nome}`}
                disabled={primeira || ocupado} onClick={() => fazer(() => gestao.mover(secao.id, 'subir'))} />
              <Botao className={css.acaoDiscreta} variante="texto" icone="chevron-down" aria-label={`Descer ${secao.nome}`}
                disabled={ultima || ocupado} onClick={() => fazer(() => gestao.mover(secao.id, 'descer'))} />
            </span>
          </>
      </div>
    </li>
  )
}

export function AbaCategoriasApi() {
  const { acoes } = useAcessoModulos()
  const gestao = useSecoesCardapioApi({ comPrevia: acoes.editarCardapio === true })
  const antigas = resumoCategoriasAntigas(gestao.previa)
  const [nova, setNova] = useState('')
  const [criando, setCriando] = useState(false)
  const [cadastroAberto, setCadastroAberto] = useState(false)
  const [convertendo, setConvertendo] = useState(false)

  const converter = () => {
    setConvertendo(true)
    Promise.resolve(gestao.migrar()).finally(() => setConvertendo(false))
  }

  const criar = (evento) => {
    evento.preventDefault()
    if (criando || !nova.trim()) return
    setCriando(true)
    Promise.resolve(gestao.criar(nova)).then((ok) => { if (ok !== false) { setNova(''); setCadastroAberto(false) } }).finally(() => setCriando(false))
  }

  if (gestao.estado === 'carregando') return <output className={css.descricao}>Carregando as categorias…</output>
  if (gestao.estado === 'erro' && gestao.secoes.length === 0) {
    return (
      <Vazio icone="alerta" role="alert" titulo="Não foi possível carregar as categorias"
        acao={<Botao icone="refresh-cw" onClick={gestao.recarregar}>Tentar de novo</Botao>}>
        {gestao.erro}
      </Vazio>
    )
  }

  if (acoes.editarCardapio !== true) return (
    <div className={css.aba}>
      {gestao.estado === 'erro' && <p className={css.aviso} role="alert">
        As categorias não foram atualizadas. A lista abaixo pode estar desatualizada. {gestao.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={gestao.recarregar}>Recarregar categorias</Botao>
      </p>}
      <p className={css.descricao}>Seu perfil permite apenas consultar as categorias.</p>
      {gestao.secoes.length === 0 ? <p>Nenhuma categoria cadastrada.</p> : (
        <ol className={css.lista}>{gestao.secoes.map((secao) => (
          <li className={css.linha} key={secao.id}><span className={css.nome}>{secao.nome}</span><span>{secao.itens} {secao.itens === 1 ? 'prato' : 'pratos'}</span></li>
        ))}</ol>
      )}
    </div>
  )
  return (
    <div className={css.aba}>
      {gestao.estado === 'erro' && <p className={css.aviso} role="alert">
        As categorias não foram atualizadas. A lista abaixo pode estar desatualizada. {gestao.erro}
        {' '}<Botao variante="texto" icone="refresh-cw" onClick={gestao.recarregar}>Recarregar categorias</Botao>
      </p>}
      <div className={css.topo}>
        <p className={css.descricao}>
          As categorias que o cliente vê no site e que agrupam a comanda, nesta ordem. Ocultar tira a categoria
          inteira do site sem apagar os pratos. Só dá para excluir categoria vazia.
        </p>
        {!antigas && !gestao.previa && (
          <Botao disabled={convertendo} onClick={converter}>{convertendo ? 'Convertendo…' : 'Converter as categorias antigas'}</Botao>
        )}
      </div>

      {antigas && (
        <section className={css.secao} aria-labelledby="categorias-antigas">
          <p id="categorias-antigas" className={css.nome}>{antigas.texto}</p>
          <p id="efeito-conversao" className={css.descricao}>{antigas.efeito}</p>
          <div>
            <Botao variante={gestao.secoes.length === 0 ? 'primario' : undefined} disabled={convertendo}
              aria-describedby="efeito-conversao" onClick={converter}>
              {convertendo ? 'Convertendo…' : 'Converter as categorias antigas'}
            </Botao>
          </div>
        </section>
      )}

      {gestao.aviso && (
        <output className={css.aviso}>
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </output>
      )}

      <Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => { gestao.fecharAviso(); setCadastroAberto(true) }}>Nova categoria</Botao>
      {cadastroAberto && <Modal titulo="Nova categoria do cardápio" aoFechar={criando ? undefined : () => setCadastroAberto(false)}><form className={`${css.campos} ${css.formulario} ${css.cadastroCurto}`} onSubmit={criar}>
        <CampoTexto
          rotulo="Nome da nova categoria"
          placeholder="Nova categoria (ex.: Massas)"
          maxLength={100}
          value={nova}
          disabled={criando}
          onChange={(e) => setNova(e.target.value)}
        />
        {gestao.aviso && <p className={css.aviso} role="alert">{gestao.aviso}</p>}
        <Botao tipo="submit" variante="primario" icone="plus" disabled={criando || !nova.trim()}>{criando ? 'Criando…' : 'Criar categoria'}</Botao>
      </form></Modal>}

      {gestao.secoes.length === 0 ? (!antigas && (
        <p className={css.descricao}>
          {gestao.previa
            ? 'Nenhuma categoria ainda, e nenhum prato com categoria antiga. Crie a primeira em Nova categoria.'
            : 'Nenhuma categoria ainda. Crie uma ou converta as categorias antigas dos pratos.'}
        </p>
      )) : (
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
