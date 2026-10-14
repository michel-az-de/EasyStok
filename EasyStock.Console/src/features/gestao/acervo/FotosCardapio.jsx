import { useState } from 'react'
import { filtrarCardapio } from '../../../dominio/vitrineCardapio'
import { agruparApresentacoes } from '../../../dominio/apresentacoesDoCardapio'
import css from './acervo.module.css'

// #1808: clicar na foto abre a escolha de uma imagem do acervo para o prato; "Abrir foto" segue no link.
function FotoDoProduto({ item, url, aoEscolherPrato, ocupado }) {
  const [falhou, setFalhou] = useState(false)
  return <>
    <button type="button" className={css.cartao} disabled={ocupado || item.arquivado} onClick={() => aoEscolherPrato?.(item)} aria-label={`Escolher imagem do acervo para ${item.nome}`}>
      {falhou ? <span className={css.foto}>Foto indisponível.</span> : <img className={css.foto} src={url} alt={item.nome} loading="lazy" onError={() => setFalhou(true)} />}
      <span>{item.arquivado ? 'Produto arquivado' : item.visivel ? 'Trocar pela imagem do acervo' : 'Produto oculto no cardápio · Trocar pela imagem do acervo'}</span>
    </button>
    <a href={url} target="_blank" rel="noopener noreferrer" aria-label={`Abrir foto de ${item.nome}`}>Abrir foto</a>
  </>
}

function FotosDoGrupo({ grupo, fotoUrl, aoEscolherPrato, ocupado }) {
  const [selecionado, setSelecionado] = useState(null)
  const item = grupo.itens.find(i => i.cardapioItemId === selecionado) ?? grupo.itens[0]
  return <article className={css.grupoFoto}>
    <FotoDoProduto key={`${item.cardapioItemId}:${item.fotoUrl}`} item={item} url={fotoUrl(item.fotoUrl)} aoEscolherPrato={aoEscolherPrato} ocupado={ocupado} />
    <strong>{grupo.nome}</strong>
    {grupo.itens.length > 1 && <div className={css.tamanhos} role="group" aria-label={`Fotos de ${grupo.nome}`}>{grupo.itens.map(i => <button key={i.cardapioItemId} type="button" aria-pressed={item.cardapioItemId === i.cardapioItemId} onClick={() => setSelecionado(i.cardapioItemId)}>{i.porcao}</button>)}</div>}
  </article>
}

export function FotosCardapio({ acervo: a, aoEscolherPrato }) {
  const [busca, setBusca] = useState('')
  const fotos = a.itens.filter(i => a.fotoUrl(i.fotoUrl))
  const encontradas = filtrarCardapio(fotos, busca)
  const grupos = agruparApresentacoes(encontradas.map(i => ({ ...i, sku: i.cardapioItemId, porcoes: i.porcoes > 0 ? [i.cardapioItemId] : [] })))
  return <section className={css.fotosCardapio} aria-label="Fotos do cardápio">
    <div><h3>Fotos do cardápio</h3><p>Capas dos produtos. Clique na foto para trocá-la por uma imagem aprovada do acervo.</p></div>
    <label className={css.campo}>Buscar foto por produto<input type="search" value={busca} onChange={e => setBusca(e.target.value)} placeholder="Nome, categoria ou porção" /></label>
    {a.carregando ? <p role="status">Carregando fotos…</p> : a.erro ? <p>Atualize o acervo para conferir as fotos.</p> : <>
      <div className={css.grade}>{grupos.map(grupo => <FotosDoGrupo key={grupo.nome + grupo.itens[0].cardapioItemId} grupo={grupo} fotoUrl={a.fotoUrl} aoEscolherPrato={aoEscolherPrato} ocupado={a.ocupado} />)}</div>
      {!encontradas.length && <p>{fotos.length ? 'Nenhuma foto encontrada para esta busca.' : 'Os produtos ainda não têm foto de capa.'}</p>}
    </>}
  </section>
}
