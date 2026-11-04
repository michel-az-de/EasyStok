import { useEffect, useState } from 'react'
import { Modal } from '../../componentes/Modal'
import { Botao } from '../../componentes/Botao'
import { lerFotosDoAcervo } from '../../aplicacao/edicaoCardapio'
import { useMiniaturaAcervo } from '../../aplicacao/useAcervo'
import css from './editorItem.module.css'

function FotoDoAcervo({ imagem, aoEscolher }) {
  const { url, erro, tentar } = useMiniaturaAcervo(imagem.id)
  return <div className={css.imagemAcervo}>
    <button type="button" disabled={!url} onClick={() => aoEscolher(imagem)} aria-label={`Usar foto ${imagem.titulo}`}>
      {url ? <img src={url} alt="" /> : <span>{erro ? 'Prévia indisponível' : 'Carregando foto…'}</span>}
      <strong>{imagem.titulo}</strong>
    </button>
    {erro && <Botao variante="texto" onClick={tentar}>Tentar carregar foto</Botao>}
  </div>
}

export function EscolherFotoDoAcervo({ aoEscolher, aoFechar }) {
  const [consulta, setConsulta] = useState({ pagina: 0, busca: '' })
  const [resultado, setResultado] = useState(null)
  const [tentativa, setTentativa] = useState(0)
  useEffect(() => {
    let ativo = true
    const tempo = setTimeout(() => {
      lerFotosDoAcervo(consulta.pagina, consulta.busca)
        .then((imagens) => { if (ativo) setResultado({ consulta, imagens }) })
        .catch((e) => { if (ativo) setResultado({ consulta, erro: e.message }) })
    }, 250)
    return () => { ativo = false; clearTimeout(tempo) }
  }, [consulta, tentativa])
  const carregando = resultado?.consulta !== consulta
  const imagens = carregando ? [] : resultado.imagens ?? []
  const aprovadas = imagens.filter((i) => i.aprovadoEm)
  return <Modal titulo="Escolher foto do banco de imagens" aoFechar={aoFechar} largura="min(760px, calc(100vw - 24px))"
    descricao="Escolha uma foto aprovada. Ela será aplicada quando você salvar o prato ou a foto."
    rodape={<Botao onClick={aoFechar}>Voltar ao prato</Botao>}>
    <label className={css.campo}>Buscar foto<input type="search" value={consulta.busca}
      onChange={(e) => setConsulta({ pagina: 0, busca: e.target.value })} placeholder="Nome da foto" /></label>
    {carregando ? <output>Carregando banco de imagens…</output> : resultado.erro ? <div role="alert" className={css.erro}>
      <p>{resultado.erro}</p><Botao onClick={() => { setResultado(null); setTentativa((n) => n + 1) }}>Tentar de novo</Botao>
    </div> : <>
      <div className={css.gradeAcervo}>{aprovadas.map((imagem) => <FotoDoAcervo key={imagem.id} imagem={imagem} aoEscolher={aoEscolher} />)}</div>
      {!aprovadas.length && <p>Nenhuma foto aprovada nesta página. Busque por outro nome ou aprove uma foto no Banco de imagens.</p>}
    </>}
    <nav className={css.acoesFoto} aria-label="Páginas do banco de imagens">
      <Botao disabled={carregando || consulta.pagina === 0} onClick={() => setConsulta({ ...consulta, pagina: consulta.pagina - 1 })}>Anterior</Botao>
      <span>Página {consulta.pagina + 1}</span>
      <Botao disabled={carregando || imagens.length < 60} onClick={() => setConsulta({ ...consulta, pagina: consulta.pagina + 1 })}>Próxima</Botao>
    </nav>
  </Modal>
}
