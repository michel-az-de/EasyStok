import { useState } from 'react'
import { useMiniaturaAcervo } from '../../../aplicacao/useAcervo'
import { Botao } from '../../../componentes/Botao'
import { Modal } from '../../../componentes/Modal'
import css from './acervo.module.css'

// #1808 item 5: a IA propõe qual imagem aprovada combina com qual prato. Só propõe: cada par passa
// pela conferência e a publicação é o mesmo POST imagens/{id}/publicar da ficha da imagem.
function Miniatura({ id, titulo }) {
  const { url, erro } = useMiniaturaAcervo(id)
  return url ? <img className={css.foto} src={url} alt={titulo} /> : <div className={css.foto}>{erro ? 'Prévia indisponível.' : 'Carregando imagem…'}</div>
}

export function SugestoesDoAcervo({ acervo: a }) {
  const [resultado, setResultado] = useState(null)
  const [pedindo, setPedindo] = useState(false)
  const [erro, setErro] = useState('')
  const [conferindo, setConferindo] = useState(null)
  const [aviso, setAviso] = useState('')

  const sugerir = async () => {
    setPedindo(true); setErro(''); setAviso('')
    try { setResultado(await a.api.sugerirAssociacoes()) }
    catch (e) { setErro(e.message) }
    finally { setPedindo(false) }
  }
  const tirar = (s) => setResultado((r) => ({ ...r, sugestoes: r.sugestoes.filter((x) => x !== s) }))
  const conferir = async (s) => {
    const item = await a.fazer(() => a.obterItem(s.itemId))
    if (item !== false) setConferindo({ sugestao: s, capaEsperada: item.fotoUrl ?? null, galeria: false, operacaoId: crypto.randomUUID() })
  }
  const publicar = async () => {
    const { sugestao: s, capaEsperada, galeria, operacaoId } = conferindo
    const ok = await a.fazer(() => a.api.publicarImagem(s.imagemId, { operacaoId, itemId: s.itemId, galeria, capaEsperada }))
    if (ok !== false) { tirar(s); setConferindo(null); setAviso(`Publicado em ${s.prato}.`) }
  }

  return <section className={css.fotosCardapio} aria-label="Sugestões de imagem para os pratos">
    <div className={css.cabecalho}>
      <div><h3>Sugestões de imagem para os pratos</h3><p>Confira as imagens sugeridas antes de publicar.</p></div>
      <Botao disabled={pedindo || a.ocupado} title="Sugere imagens aprovadas do acervo. Você confere antes de publicar." onClick={sugerir}>{pedindo ? 'Buscando sugestões…' : 'Sugerir imagens'}</Botao>
    </div>
    {erro && <p role="alert" className={css.erro}>{erro}</p>}
    {aviso && <output>{aviso}</output>}
    {resultado && <>
      <output>{resultado.sugestoes.length
        ? `${resultado.porIa ? 'Sugestões da IA.' : 'Sugestões sem IA.'} ${resultado.sugestoes.length} para conferir.`
        : 'Nenhuma sugestão encontrada.'}</output>
      <div className={css.grade}>{resultado.sugestoes.map((s) => <article key={`${s.imagemId}:${s.itemId}`} className={css.grupoFoto}>
        <Miniatura id={s.imagemId} titulo={s.titulo} />
        <strong>{s.prato}</strong>
        <span>Imagem: {s.titulo}</span>
        <small>{s.motivo}</small>
        <div className={css.acoes}>
          <Botao variante="primario" disabled={a.ocupado} aria-label={`Conferir ${s.titulo} em ${s.prato}`} onClick={() => conferir(s)}>Conferir</Botao>
          <Botao variante="texto" disabled={a.ocupado} aria-label={`Descartar sugestão ${s.titulo} em ${s.prato}`} onClick={() => tirar(s)}>Descartar</Botao>
        </div>
      </article>)}</div>
    </>}
    {conferindo && <Modal titulo={`Imagem para ${conferindo.sugestao.prato}`} aoFechar={a.ocupado ? undefined : () => setConferindo(null)}>
      <div className={css.formulario}>
        {a.erro && <p role="alert" className={css.erro}>{a.erro}</p>}
        <Miniatura id={conferindo.sugestao.imagemId} titulo={conferindo.sugestao.titulo} />
        <p>{conferindo.sugestao.motivo}</p>
        {conferindo.capaEsperada ? <details><summary>Ver capa atual</summary><img className={css.foto} src={a.fotoUrl(conferindo.capaEsperada)} alt="Capa atual do prato" /></details> : <p>O prato ainda não tem foto.</p>}
        <fieldset><label><input type="checkbox" checked={conferindo.galeria} disabled={a.ocupado} onChange={(e) => setConferindo({ ...conferindo, galeria: e.target.checked })} /> Adicionar à galeria em vez de trocar a capa</label></fieldset>
        <Botao variante="primario" disabled={a.ocupado} onClick={publicar}>Confirmar publicação</Botao>
      </div>
    </Modal>}
  </section>
}
