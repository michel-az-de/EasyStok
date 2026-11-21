import { useRef, useState } from 'react'
import { atualizarCategoriaCadastro, criarCategoriaCadastro, excluirCategoriaCadastro, useCategoriasCadastro } from '../../../aplicacao/cadastros'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Combobox } from '../../../componentes/Combobox'
import { MenuDeAcoes } from '../../../componentes/MenuDeAcoes'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import { arvoreDeCategorias, bloqueioDeExclusao, corpoDaCategoria, corpoDaNovaCategoria, destinosParaMover, erroDoNomeDaCategoria, mensagemDaCategoria, sucessoDaCategoria } from '../../../dominio/categoriasProduto'
import css from './cadastros.module.css'

// #2495 (S4, I3): Cadastros › Categorias de produto. Dois níveis, como o GET devolve: raiz e subcategoria.
const TITULO = {
  criar: ({ pai }) => pai ? `Nova subcategoria em ${pai.nome}` : 'Nova categoria',
  renomear: ({ categoria }) => `Renomear ${categoria.nome}`,
  mover: ({ categoria }) => `Mover ${categoria.nome}`,
  excluir: ({ categoria }) => `Excluir ${categoria.nome}?`,
}
const CONFIRMAR = { criar: ({ pai }) => pai ? 'Criar subcategoria' : 'Criar categoria', renomear: 'Salvar nome', mover: 'Mover', excluir: 'Excluir' }

export function CategoriasCadastro({ contexto }) {
  const { acoes } = useAcessoModulos()
  const podeEditar = acoes.editarCadastrosProdutos === true
  const consulta = useCategoriasCadastro(contexto)
  const arvore = arvoreDeCategorias(consulta.itens)
  const [intencao, escolher] = useState(null)
  const [valor, definirValor] = useState('')
  const [erro, falhar] = useState(null)
  const [erroNome, definirErroNome] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const [sucesso, avisar] = useState(null)
  const emVoo = useRef(false)
  const abrir = (acao, categoria = null, pai = null) => { falhar(null); definirErroNome(null); definirValor(acao === 'renomear' ? categoria.nome : ''); escolher({ acao, categoria, pai }) }
  const fechar = () => { if (!emVoo.current) escolher(null) }
  // #2734 (A22): Esc, Cancelar e o fundo perguntam antes de perder o que foi digitado ou escolhido.
  const alterado = intencao?.acao === 'criar' ? valor.trim() !== '' : intencao?.acao === 'renomear' ? valor.trim() !== intencao.categoria.nome : intencao?.acao === 'mover' && valor !== ''
  // O erro aparece no próprio campo; só excluir, que não tem campo, mostra no corpo.
  const erroDoCampo = intencao?.acao === 'excluir' ? null : erroNome ?? erro

  async function confirmar(evento) {
    evento?.preventDefault()
    if (emVoo.current || !intencao) return
    const { acao, categoria, pai } = intencao
    const problema = acao === 'criar' || acao === 'renomear' ? erroDoNomeDaCategoria(valor) : null
    if (problema) { definirErroNome(problema); return }
    if (acao === 'mover' && !valor) { definirErroNome('Escolha para onde mover.'); return }
    emVoo.current = true; ocupar(true); falhar(null)
    try {
      if (acao === 'criar') await criarCategoriaCadastro(corpoDaNovaCategoria(valor, pai?.id, contexto.empresaId), contexto)
      else if (acao === 'excluir') await excluirCategoriaCadastro(categoria.id, contexto)
      else await atualizarCategoriaCadastro(corpoDaCategoria(categoria, acao === 'mover' ? { categoriaPaiId: valor } : { nome: valor }, contexto.empresaId), contexto)
      avisar(sucessoDaCategoria(intencao, valor, arvore))
      escolher(null); consulta.recarregar()
    } catch (e) { falhar(mensagemDaCategoria(acao, categoria, e)) }
    finally { emVoo.current = false; ocupar(false) }
  }

  // Renomear, mover e excluir ficam no menu ⋮ de cada linha; o botão visível é só criar subcategoria.
  const acoesDa = (categoria, pai) => {
    const bloqueio = bloqueioDeExclusao(categoria)
    return <div className={css.acoes}>
      {!pai && <Botao icone="mais" aria-label={`Nova subcategoria em ${categoria.nome}`} onClick={() => abrir('criar', null, categoria)}>Subcategoria</Botao>}
      <MenuDeAcoes rotulo={`Mais opções de ${categoria.nome}`} itens={[
        { chave: 'renomear', rotulo: 'Renomear', aoEscolher: () => abrir('renomear', categoria) },
        destinosParaMover(arvore, categoria).length > 0 && { chave: 'mover', rotulo: 'Mover para outra categoria', aoEscolher: () => abrir('mover', categoria) },
        { chave: 'excluir', rotulo: 'Excluir', tom: 'perigo', desabilitado: Boolean(bloqueio), motivo: bloqueio ?? undefined, aoEscolher: () => abrir('excluir', categoria) },
      ]} />
    </div>
  }

  return <section className={css.painel} aria-label="Categorias de produto">
    <header className={css.cabecalho}>
      <div><h2>Categorias de produto</h2><p>Organize os produtos em categorias e subcategorias.</p></div>
      <div className={css.acoes}>
        {podeEditar && <Botao variante="primario" icone="mais" onClick={() => abrir('criar')}>Nova categoria</Botao>}
        <Botao icone="refresh-cw" onClick={consulta.recarregar} disabled={consulta.carregando}>Atualizar</Botao>
      </div>
    </header>
    {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
    {consulta.erro && <div className={css.erro} role="alert"><p>{consulta.erro}</p><Botao onClick={consulta.recarregar}>Tentar de novo</Botao></div>}
    {consulta.carregando && arvore.length === 0 ? <Vazio titulo="Carregando categorias">Aguarde um instante.</Vazio>
      : !consulta.erro && arvore.length === 0 ? <Vazio titulo="Nenhuma categoria cadastrada" acao={podeEditar ? <Botao variante="primario" icone="mais" onClick={() => abrir('criar')}>Nova categoria</Botao> : undefined}>{podeEditar ? 'Crie a primeira para organizar os produtos.' : 'Peça à gerência para cadastrar categorias.'}</Vazio>
      : <ul className={css.cartoes}>{arvore.map(raiz => <li key={raiz.id}>
        <div>
          <h3>{raiz.nome}</h3>
          <span className={css.situacao}>{raiz.subCategorias.length === 0 ? 'Sem subcategorias' : raiz.subCategorias.length === 1 ? '1 subcategoria' : `${raiz.subCategorias.length} subcategorias`}</span>
        </div>
        {raiz.subCategorias.length > 0 && <ul className={css.subcategorias} aria-label={`Subcategorias de ${raiz.nome}`}>{raiz.subCategorias.map(sub => <li key={sub.id}>
          <span>{sub.nome}</span>
          {podeEditar && acoesDa(sub, raiz)}
        </li>)}</ul>}
        {podeEditar && acoesDa(raiz, null)}
      </li>)}</ul>}
    {intencao && <Modal titulo={TITULO[intencao.acao](intencao)} aoFechar={ocupado ? undefined : fechar} segurarFechamento={ocupado} confirmarDescarte={alterado && !ocupado}
      rodape={<><Botao disabled={ocupado} onClick={e => e.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))}>Cancelar</Botao><Botao variante={intencao.acao === 'excluir' ? 'perigo' : 'primario'} disabled={ocupado} onClick={confirmar}>{ocupado ? 'Salvando…' : typeof CONFIRMAR[intencao.acao] === 'function' ? CONFIRMAR[intencao.acao](intencao) : CONFIRMAR[intencao.acao]}</Botao></>}>
      {(intencao.acao === 'criar' || intencao.acao === 'renomear') && <form onSubmit={confirmar}>
        <CampoTexto rotulo="Nome" obrigatorio value={valor} erro={erroDoCampo} disabled={ocupado} onChange={e => { definirValor(e.target.value); definirErroNome(null); falhar(null) }} />
      </form>}
      {intencao.acao === 'mover' && <>
        <p>{intencao.categoria.categoriaPaiId ? 'Os produtos desta subcategoria continuam com ela.' : 'Esta categoria passa a ser subcategoria da escolhida.'}</p>
        <Combobox rotulo="Mover para" valor={valor} aoMudar={v => { definirValor(v); definirErroNome(null); falhar(null) }} erro={erroDoCampo} opcoes={destinosParaMover(arvore, intencao.categoria)} disabled={ocupado} />
      </>}
      {intencao.acao === 'excluir' && <p>{intencao.categoria.categoriaPaiId ? 'A subcategoria sai da lista.' : 'A categoria sai da lista.'} Se algum produto ainda usa {intencao.categoria.nome}, não é possível excluir. Mude esses produtos de categoria antes.</p>}
      {intencao.acao === 'excluir' && erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>}
  </section>
}
