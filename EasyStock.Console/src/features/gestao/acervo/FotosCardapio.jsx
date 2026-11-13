import { useState } from 'react'
import { Icone } from '../../../componentes/Icone'
import { filtrarCardapio } from '../../../dominio/vitrineCardapio'
import { agruparApresentacoes } from '../../../dominio/apresentacoesDoCardapio'
import css from './acervo.module.css'

// #1808: o prato troca a foto por uma imagem do acervo. #2424: o cartão mostra a capa, o estado
// (oculto ou arquivado) num selo, o ícone para abrir a foto e a troca como ação discreta em âmbar.
function FotoDoProduto({ item, url, nome }) {
  const [falhou, setFalhou] = useState(false)
  return <div className={css.moldura}>
    {falhou ? <span className={css.foto}>Foto indisponível.</span> : <img className={css.foto} src={url} alt={item.nome} loading="lazy" onError={() => setFalhou(true)} />}
    {(item.arquivado || !item.visivel) && <span className={css.selo}><Icone nome={item.arquivado ? 'archive' : 'ocultoNoSite'} tamanho={16} />{item.arquivado ? 'Produto arquivado' : 'Oculto no cardápio'}</span>}
    <a className={css.ampliar} href={url} target="_blank" rel="noopener noreferrer" aria-label={`Abrir foto de ${nome}`} title="Abrir foto"><Icone nome="ampliar" tamanho={16} /></a>
  </div>
}

function FotosDoGrupo({ grupo, fotoUrl, aoEscolherPrato, ocupado }) {
  const [selecionado, setSelecionado] = useState(null)
  const item = grupo.itens.find(i => i.cardapioItemId === selecionado) ?? grupo.itens[0]
  return <article className={css.grupoFoto}>
    <FotoDoProduto key={`${item.cardapioItemId}:${item.fotoUrl}`} item={item} url={fotoUrl(item.fotoUrl)} nome={item.nome} />
    <strong className={css.nomeFoto}>{grupo.nome}</strong>
    {grupo.itens.length > 1
      ? <div className={css.porcao}><span aria-hidden="true">Porção</span><div className={css.tamanhos} role="group" aria-label={`Porção de ${grupo.nome}`}>{grupo.itens.map(i => <button key={i.cardapioItemId} type="button" aria-pressed={item.cardapioItemId === i.cardapioItemId} onClick={() => setSelecionado(i.cardapioItemId)}>{i.porcao}</button>)}</div></div>
      : item.porcao && <div className={css.porcao}><span>Porção</span><span className={css.porcaoUnica}>{item.porcao}</span></div>}
    <button type="button" className={css.trocar} disabled={ocupado || item.arquivado} onClick={() => aoEscolherPrato?.(item)}
      aria-label={`Trocar a foto de ${item.nome} por uma imagem do acervo`}>
      <Icone nome="trocarLado" tamanho={16} />Trocar pelo acervo
    </button>
  </article>
}

export function FotosCardapio({ acervo: a, aoEscolherPrato }) {
  const [busca, setBusca] = useState('')
  const fotos = a.itens.filter(i => a.fotoUrl(i.fotoUrl))
  const encontradas = filtrarCardapio(fotos, busca)
  const grupos = agruparApresentacoes(encontradas.map(i => ({ ...i, sku: i.cardapioItemId, porcoes: i.porcoes > 0 ? [i.cardapioItemId] : [] })))
  return <section className={css.fotosCardapio} aria-label="Fotos do cardápio">
    <label className={css.buscaFotos}>
      <span className="sr">Buscar foto por nome, categoria ou porção</span>
      <Icone nome="busca" tamanho={20} />
      <input type="search" value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar por nome, categoria ou porção" />
    </label>
    {a.carregando ? <p role="status">Carregando fotos…</p> : a.erro ? <p>Atualize o acervo para conferir as fotos.</p> : <>
      <div className={css.gradeFotos}>{grupos.map(grupo => <FotosDoGrupo key={grupo.nome + grupo.itens[0].cardapioItemId} grupo={grupo} fotoUrl={a.fotoUrl} aoEscolherPrato={aoEscolherPrato} ocupado={a.ocupado} />)}</div>
      {!encontradas.length && <p>{fotos.length ? 'Nenhuma foto encontrada para esta busca.' : 'Os produtos ainda não têm foto de capa.'}</p>}
    </>}
  </section>
}
