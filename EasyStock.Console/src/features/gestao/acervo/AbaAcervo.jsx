import { useEffect, useRef, useState } from 'react'
import { useAcervo, useMiniaturaAcervo } from '../../../aplicacao/useAcervo'
import { Botao } from '../../../componentes/Botao'
import { Modal } from '../../../componentes/Modal'
import css from './acervo.module.css'
import { ProducaoAcervo, canaisAcervo } from './ProducaoAcervo'
import { FotosCardapio } from './FotosCardapio'
import { pratosParaUsar } from '../../../dominio/pratosDoAcervo'

function Foto({ imagem }) {
  const previa = useMiniaturaAcervo(imagem.id)
  return <VisualFoto imagem={imagem} previa={previa} />
}
function VisualFoto({ imagem, previa }) {
  const { url, erro } = previa
  return url ? <img className={css.foto} src={url} alt={imagem.titulo} /> : <div className={css.foto}>{erro ? 'Prévia indisponível. Atualize o acervo.' : 'Carregando imagem…'}</div>
}
const ids = (imagem) => JSON.parse(imagem.itensJson || '[]')
const formatos = ['1:1', '4:5', '16:9', '9:16', '3:2', 'livre']

export function AbaAcervo() {
  const a = useAcervo()
  const [busca, setBusca] = useState('')
  const [filtros, setFiltros] = useState({})
  useEffect(() => {
    const tempo = setTimeout(() => a.setFiltros({ ...filtros, busca }), 300)
    return () => clearTimeout(tempo)
  }, [busca, filtros])
  const [selecionada, setSelecionada] = useState(null)
  const selecionadaAtual = useRef(null)
  const [dialogo, definirDialogo] = useState(null)
  const fluxoAtual = useRef(0)
  const setDialogo = (novo) => { fluxoAtual.current++; definirDialogo(novo) }
  const [historico, setHistorico] = useState([])
  const [publicacao, setPublicacao] = useState(null)
  const [aviso, setAviso] = useState('')
  const [solicitacaoId, setSolicitacaoId] = useState(null)
  // #1808: o prato escolhido na ficha ("Usar em um prato") e o prato clicado em "Fotos do cardápio".
  const [pratoEscolhido, setPratoEscolhido] = useState('')
  const [pratoAlvo, setPratoAlvo] = useState(null)
  const [paraGaleria, setParaGaleria] = useState(false)
  const detalhe = useRef(null)
  const imagem = a.imagens.find((i) => i.id === selecionada)
  const previa = useMiniaturaAcervo(imagem?.id)
  useEffect(() => {
    if (selecionada && detalhe.current) {
      detalhe.current.focus({ preventScroll: true })
      const reduzirMovimento = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      detalhe.current.scrollIntoView({ block: 'start', behavior: reduzirMovimento ? 'auto' : 'smooth' })
    }
  }, [selecionada])
  const executar = async (acao, mensagem) => {
    const fluxo = fluxoAtual.current
    const resultado = await a.fazer(acao)
    if (resultado !== false && fluxoAtual.current === fluxo) { setDialogo(null); setAviso(mensagem) }
    return resultado
  }
  async function prepararPublicacao(itemId, galeria, imagemId = imagem.id) {
    const fluxo = fluxoAtual.current
    const item = await a.fazer(() => a.obterItem(itemId))
    if (item !== false && selecionadaAtual.current === imagemId && fluxoAtual.current === fluxo) {
      setPublicacao({ imagemId, itemId, galeria, capaEsperada: item.fotoUrl, nome: item.nome, operacaoId: crypto.randomUUID() })
      setDialogo('publicar')
    }
  }
  const selecionar = (id) => { selecionadaAtual.current = id; setSelecionada(id); setHistorico([]); setPublicacao(null); setPratoEscolhido('') }
  // #1808: na grade "Fotos do cardápio", o clique no prato abre a escolha de uma imagem aprovada.
  const escolherImagemParaPrato = (item) => { setPratoAlvo({ id: item.cardapioItemId, nome: item.nome }); setParaGaleria(false); setDialogo('escolherImagem') }
  async function usarImagemNoPrato(imagemId) {
    selecionar(imagemId)
    setPratoEscolhido(pratoAlvo.id)
    await prepararPublicacao(pratoAlvo.id, paraGaleria, imagemId)
  }
  async function enviarArquivo(e) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const pai = dialogo === 'retorno' ? imagem : null
    const origem = pai ? 'Canva' : f.get('origem')
    const dados = { titulo: f.get('titulo'), origem, formato: f.get('formato'),
      itens: pai ? ids(pai) : f.getAll('itemId').filter(Boolean), natureza: f.get('natureza'), prompt: f.get('prompt') || '', referencias: f.get('referencias') || '',
      originalId: pai?.id ?? null, canvaUrl: pai?.canvaUrl ?? null, solicitacaoId: f.get('solicitacaoId') || null }
    await executar(() => a.api.adicionarImagem(f.get('file'), dados), 'Imagem guardada. Confira esta versão antes de aprovar.')
  }
  return <section className={css.raiz}>
    <div className={css.cabecalho}><p>Fotos, originais e peças do Canva.</p>
      <div className={css.acoes}><Botao disabled={a.ocupado || a.carregando} onClick={() => a.carregar().catch((e) => a.setErro(e.message))}>Atualizar acervo</Botao><Botao disabled={a.ocupado} onClick={() => { setSolicitacaoId(crypto.randomUUID()); setDialogo('solicitar') }}>Solicitar imagem</Botao><Botao variante="primario" disabled={a.ocupado} onClick={() => setDialogo('upload')}>Adicionar imagem</Botao></div></div>
    {a.erro && <p role="alert" className={css.erro}>{a.erro}</p>}
    {aviso && <p role="status">{aviso}</p>}
    {a.aviso && <p role="status">{a.aviso}</p>}
    <FotosCardapio acervo={a} aoEscolherPrato={escolherImagemParaPrato} />
    <h3>Originais e versões do acervo</h3>
    <label className={css.campo}>Buscar em todo o acervo<input value={busca} disabled={a.ocupado} onChange={(e) => setBusca(e.target.value)} placeholder="Título da imagem" /></label>
    <div className={css.filtros}>{[
      ['origem', 'Origem da imagem', [['Fotografia', 'Foto real'], ['IA', 'Imagem gerada'], ['Canva', 'Exportação Canva']]],
      ['natureza', 'Natureza da imagem', [['Produto', 'Imagem de produto'], ['Comunicacao', 'Peça de comunicação'], ['NaoClassificada', 'Classificação pendente']]],
      ['formato', 'Formato da imagem', formatos.map((f) => [f, f])],
      ['itemId', 'Produto vinculado', a.itens.map((i) => [i.cardapioItemId, i.nome])],
      ['canal', 'Uso confirmado', Object.entries(canaisAcervo)],
    ].map(([campo, titulo, opcoes]) => <label className={css.campo} key={campo}>{titulo}<select value={filtros[campo] || ''} disabled={a.ocupado} onChange={(e) => setFiltros({ ...filtros, [campo]: e.target.value })}><option value="">Todos</option>{opcoes.map(([id, nome]) => <option key={id} value={id}>{nome}</option>)}</select></label>)}</div>
    {a.carregando ? <p role="status">Carregando acervo…</p> : <div className={css.grade}>
      {a.imagens.map((i) =>
        <button type="button" disabled={a.ocupado} key={i.id} className={`${css.cartao} ${selecionada === i.id ? css.ativo : ''}`} onClick={() => { selecionar(i.id); setDialogo(null) }}>
          <Foto imagem={i} /><strong>{i.titulo}</strong><span>{i.origem} · {i.formato} · {i.aprovadoEm ? 'Aprovada' : 'Em revisão'}</span>
        </button>)}
      {!a.imagens.length && <p>Nenhuma imagem nos filtros selecionados.</p>}
    </div>}
    <div className={css.acoes}><Botao disabled={!a.pagina || a.carregando || a.ocupado} onClick={() => a.setPagina(a.pagina - 1)}>Anterior</Botao><span>Página {a.pagina + 1}</span><Botao disabled={a.imagens.length < 60 || a.carregando || a.ocupado} onClick={() => a.setPagina(a.pagina + 1)}>Próxima</Botao></div>
    {imagem && <section ref={detalhe} tabIndex={-1} className={css.detalhe} aria-label="Ficha da imagem">
      <div><h3>{imagem.titulo}</h3><p>{imagem.origem === 'IA' ? 'Imagem ilustrativa gerada por IA.' : imagem.origem} Original preservado. Uma edição deve ser adicionada como nova versão.</p>
      <div className={css.acoes}><Botao disabled={a.ocupado} onClick={() => a.baixar(imagem.id, imagem.titulo)}>Baixar original para Canva</Botao>
        <Botao onClick={() => setDialogo('canva')}>Vincular design Canva</Botao>
        {imagem.canvaUrl && <><a href={imagem.canvaUrl} target="_blank" rel="noopener noreferrer">Abrir design no Canva</a><Botao onClick={() => setDialogo('retorno')}>Adicionar versão do Canva</Botao></>}
        {!imagem.aprovadoEm && <Botao variante="primario" disabled={!previa.url || a.ocupado} onClick={() => setDialogo('aprovar')}>Revisar e aprovar</Botao>}
      </div>
      <p>Canva: baixe o original, edite o design e adicione a exportação como nova versão. A conexão por conta ainda não está configurada.</p>
      <details><summary>Prompt e referências</summary><p className={css.texto}>{imagem.prompt || 'Sem prompt.'}</p><p className={css.texto}>{imagem.referencias || 'Sem referências registradas.'}</p><small>SHA-256: {imagem.sha256}</small></details>
      <h4>Adequação por canal</h4><p>{imagem.largura && imagem.altura ? `${imagem.largura} × ${imagem.altura} px` : 'Dimensões serão conferidas ao confirmar o uso.'} · {imagem.natureza === 'Comunicacao' ? 'Peça de comunicação' : imagem.natureza === 'Produto' ? 'Imagem de produto' : 'Classificação pendente'}</p>
      <Botao disabled={a.ocupado} onClick={() => setDialogo('classificar')}>Classificar imagem ou peça</Botao>
      <p>{a.usos.filter((u) => u.imagemId === imagem.id).map((u) => `${canaisAcervo[u.canal]}${u.larguraMm ? ` (${u.larguraMm} × ${u.alturaMm} mm)` : ''}`).join(' · ') || 'Nenhum uso confirmado.'}</p>
      <Botao disabled={!imagem.aprovadoEm || !previa.url || a.ocupado} onClick={() => setDialogo('uso')}>Confirmar adequação a um canal</Botao>
      <h4>Usar no cardápio</h4>
      {ids(imagem).map((id) => <div className={css.linha} key={id}><span>{a.itens.find((i) => i.cardapioItemId === id)?.nome || 'Item vinculado'}</span>
        <div className={css.acoes}><Botao disabled={!imagem.aprovadoEm || !previa.url || a.ocupado} onClick={() => prepararPublicacao(id, false)}>Prévia como capa</Botao><Botao disabled={!imagem.aprovadoEm || !previa.url || a.ocupado} onClick={() => prepararPublicacao(id, true)}>Adicionar à galeria</Botao></div></div>)}
      {!ids(imagem).length && <p>Imagem de uso geral, sem item de cardápio vinculado.</p>}
      <div className={css.linha}>
        <label className={css.campo}>Usar em um prato
          <select value={pratoEscolhido} disabled={!imagem.aprovadoEm || a.ocupado} onChange={(e) => setPratoEscolhido(e.target.value)}>
            <option value="">Escolha o prato</option>
            {pratosParaUsar(a.itens, ids(imagem)).map((p) => <option key={p.id} value={p.id}>{p.vinculado ? `${p.nome} (já vinculada)` : p.nome}</option>)}
          </select>
        </label>
        <div className={css.acoes}><Botao disabled={!pratoEscolhido || !imagem.aprovadoEm || !previa.url || a.ocupado} onClick={() => prepararPublicacao(pratoEscolhido, false)}>Prévia como capa</Botao><Botao disabled={!pratoEscolhido || !imagem.aprovadoEm || !previa.url || a.ocupado} onClick={() => prepararPublicacao(pratoEscolhido, true)}>Adicionar à galeria</Botao></div>
      </div>
      {!imagem.aprovadoEm && <p>Aprove esta versão para usar a imagem em um prato.</p>}
      <Botao disabled={a.ocupado} onClick={async () => { const fluxo = fluxoAtual.current; const h = await a.fazer(() => a.api.historicoImagem(imagem.id)); if (h !== false && selecionadaAtual.current === imagem.id && fluxoAtual.current === fluxo) setHistorico(h) }}>Consultar publicações</Botao>
      {historico.map((h) => <p key={h.id}>{new Date(h.criadoEm).toLocaleString('pt-BR')} · {h.galeria ? 'Galeria' : 'Capa'} · <a href={a.fotoUrl(h.url)} target="_blank" rel="noopener noreferrer">Ver arquivo publicado</a></p>)}
      </div><div><VisualFoto imagem={imagem} previa={previa} />{previa.erro && <Botao onClick={previa.tentar}>Tentar prévia novamente</Botao>}</div>
    </section>}
    <ProducaoAcervo acervo={a} />
    <section><h3>Fila de produção</h3><p>Solicitações ficam registradas para execução assistida com as referências do acervo.</p>
      {a.solicitacoes.map((s) => <details key={s.id}><summary>{a.itens.find((i) => i.cardapioItemId === s.itemId)?.nome || 'Item'} · {s.formato} · {s.concluidoEm ? 'Concluída; revisar resultado' : 'Pendente'}</summary><p className={css.texto}>{s.brief}</p><p>Referências: {JSON.parse(s.referenciasJson).map((id) => a.imagens.find((i) => i.id === id)?.titulo || id).join(', ') || 'Nenhuma'}</p><small>Solicitação: {s.id}{s.briefingId && ` · Briefing: ${s.briefingId}`}</small></details>)}
      {!a.solicitacoes.length && <p>Nenhuma solicitação registrada.</p>}
    </section>
    {dialogo && <Modal titulo={({ upload: 'Adicionar original', retorno: 'Adicionar exportação do Canva', canva: 'Vincular design', aprovar: 'Aprovar esta versão', publicar: 'Conferir publicação', escolherImagem: pratoAlvo ? `Imagem para ${pratoAlvo.nome}` : 'Imagem para o prato', solicitar: 'Solicitar nova imagem', uso: 'Confirmar adequação ao canal', classificar: 'Classificar imagem ou peça' })[dialogo]} aoFechar={a.ocupado ? undefined : () => setDialogo(null)}>
      {a.erro && <p role="alert" className={css.erro}>{a.erro}</p>}
      {(dialogo === 'upload' || dialogo === 'retorno') && <form className={css.formulario} onSubmit={enviarArquivo}>
        <label>Título<input name="titulo" required maxLength={160} defaultValue={dialogo === 'retorno' ? `${imagem.titulo} · Canva` : ''} /></label>
        <label>Arquivo PNG, JPEG ou WebP, até 6 MB<input name="file" type="file" accept="image/png,image/jpeg,image/webp" required /></label>
        {dialogo === 'upload' && <><label>Origem<select name="origem"><option value="Fotografia">Fotografia</option><option value="IA">Geração por IA</option></select></label><label>Item do cardápio<select name="itemId"><option value="">Uso geral</option>{a.itens.map((i) => <option key={i.cardapioItemId} value={i.cardapioItemId}>{i.nome}</option>)}</select></label></>}
        <label>Formato<select name="formato" defaultValue={imagem?.formato || '4:5'}>{formatos.map((f) => <option key={f}>{f}</option>)}</select></label>
        <label>Natureza<select name="natureza" defaultValue={dialogo === 'retorno' ? 'Comunicacao' : 'Produto'}><option value="Produto">Imagem de produto</option><option value="Comunicacao">Peça de comunicação</option></select></label>
        <label>Prompt da geração<textarea name="prompt" maxLength={12000} /></label><label>Referências<textarea name="referencias" maxLength={6000} /></label>
        {dialogo === 'upload' && <label>Atende uma solicitação<select name="solicitacaoId" onChange={(e) => { const pedido = a.solicitacoes.find((s) => s.id === e.target.value); if (pedido) { const f = e.target.form; f.elements.itemId.value = pedido.itemId; f.elements.formato.value = pedido.formato; f.elements.prompt.value = pedido.brief; f.elements.referencias.value = pedido.referenciasJson; if (pedido.briefingId) f.elements.origem.value = 'IA' } }}><option value="">Nenhuma</option>{a.solicitacoes.filter((s) => !s.concluidoEm).map((s) => <option value={s.id} key={s.id}>{a.itens.find((i) => i.cardapioItemId === s.itemId)?.nome} · {s.formato}</option>)}</select></label>}
        <Botao tipo="submit" variante="primario" disabled={a.ocupado}>Guardar versão</Botao>
      </form>}
      {dialogo === 'canva' && <form className={css.formulario} onSubmit={(e) => { e.preventDefault(); const url = new FormData(e.currentTarget).get('url'); executar(() => a.api.vincularCanva(imagem.id, url), 'Design vinculado.') }}><label>Link do design Canva<input name="url" type="url" required defaultValue={imagem.canvaUrl || ''} /></label><Botao tipo="submit" disabled={a.ocupado}>Salvar vínculo</Botao></form>}
      {dialogo === 'classificar' && <form className={css.formulario} onSubmit={(e) => { e.preventDefault(); const natureza = new FormData(e.currentTarget).get('natureza'); executar(() => a.api.classificarImagem(imagem.id, natureza, imagem.natureza), 'Classificação registrada. Origem e original preservados.') }}><label>Natureza da imagem<select name="natureza" disabled={a.ocupado} defaultValue={imagem.natureza === 'Comunicacao' ? 'Comunicacao' : 'Produto'}><option value="Produto">Imagem de produto</option><option value="Comunicacao">Peça de comunicação</option></select></label><p>Uma fotografia e uma imagem IA conservam sua origem mesmo quando usadas em uma peça.</p><Botao tipo="submit" disabled={a.ocupado}>Confirmar classificação</Botao></form>}
      {dialogo === 'uso' && <form className={css.formulario} onSubmit={(e) => { e.preventDefault(); const f = new FormData(e.currentTarget); executar(() => a.api.confirmarUso(imagem.id, { canal: f.get('canal'), larguraMm: f.get('larguraMm') ? Number(f.get('larguraMm')) : null, alturaMm: f.get('alturaMm') ? Number(f.get('alturaMm')) : null }), 'Adequação registrada para este canal. A imagem não foi publicada.') }}>
        <VisualFoto imagem={imagem} previa={previa} /><label>Canal<select name="canal">{Object.entries(canaisAcervo).map(([id, nome]) => <option key={id} value={id}>{nome}</option>)}</select></label>
        <p>Confira alimento, composição e legibilidade no canal. A proporção e resolução serão conferidas no original.</p>
        <label>Largura em mm, somente impressão<input name="larguraMm" type="number" min="0.1" max="2000" step="0.1" /></label><label>Altura em mm, somente impressão<input name="alturaMm" type="number" min="0.1" max="2000" step="0.1" /></label>
        <p>Impressão exige pelo menos 300 dpi no tamanho informado. Arte final e prova física devem ser conferidas separadamente.</p><Botao tipo="submit" disabled={!previa.url || a.ocupado}>Confirmar este uso</Botao>
      </form>}
      {dialogo === 'aprovar' && <div className={css.formulario}><VisualFoto imagem={imagem} previa={previa} /><p>Confira a apresentação do prato, os ingredientes visíveis e a adequação da imagem aos itens vinculados. A aprovação vale para esta versão.</p><Botao variante="primario" disabled={!previa.url || a.ocupado} onClick={() => executar(() => a.api.aprovarImagem(imagem.id), 'Versão aprovada para uso.')}>Aprovar esta versão</Botao></div>}
      {dialogo === 'publicar' && <div className={css.formulario}><h3>{publicacao.nome}</h3><VisualFoto imagem={imagem} previa={previa} /><p>{publicacao.galeria ? 'Acrescentar esta imagem à galeria.' : 'Usar esta versão como capa do prato no site.'}</p>{!ids(imagem).includes(publicacao.itemId) && <p>A imagem passa a ficar vinculada a este prato no acervo.</p>}{publicacao.capaEsperada && <details><summary>Ver capa atual</summary><img className={css.foto} src={a.fotoUrl(publicacao.capaEsperada)} alt="Capa atual do prato" /></details>}<Botao variante="primario" disabled={!previa.url || a.ocupado} onClick={() => executar(() => a.api.publicarImagem(publicacao.imagemId, publicacao), 'Publicação registrada. A nova imagem já está no cardápio.')}>Confirmar publicação</Botao></div>}
      {dialogo === 'escolherImagem' && pratoAlvo && <div className={css.formulario}>
        <p>Escolha uma imagem aprovada do acervo. Você confere a prévia antes de publicar.</p>
        <fieldset><label><input type="checkbox" checked={paraGaleria} disabled={a.ocupado} onChange={(e) => setParaGaleria(e.target.checked)} /> Adicionar à galeria em vez de trocar a capa</label></fieldset>
        <div className={css.grade}>{a.imagens.filter((i) => i.aprovadoEm).map((i) =>
          <button type="button" key={i.id} className={css.cartao} disabled={a.ocupado} onClick={() => usarImagemNoPrato(i.id)}>
            <Foto imagem={i} /><strong>{i.titulo}</strong><span>{ids(i).includes(pratoAlvo.id) ? 'Já vinculada a este prato' : `${i.origem} · ${i.formato}`}</span>
          </button>)}</div>
        {!a.imagens.some((i) => i.aprovadoEm) && <p>Nenhuma imagem aprovada nesta página do acervo. Aprove uma imagem ou mude os filtros abaixo.</p>}
      </div>}
      {dialogo === 'solicitar' && <form className={css.formulario} onSubmit={(e) => { e.preventDefault(); const f = new FormData(e.currentTarget); executar(() => a.api.solicitarImagem({ operacaoId: solicitacaoId, itemId: f.get('itemId'), formato: f.get('formato'), brief: f.get('brief'), referencias: imagem ? [imagem.id] : [] }), 'Solicitação registrada na fila.') }}>
        <label>Item<select name="itemId" required>{a.itens.map((i) => <option key={i.cardapioItemId} value={i.cardapioItemId}>{i.nome}</option>)}</select></label>
        <label>Formato<select name="formato">{formatos.map((f) => <option key={f}>{f}</option>)}</select></label><label>Descreva o que precisa<textarea name="brief" required maxLength={12000} /></label>
        <p>{imagem ? `Referência: ${imagem.titulo}` : 'Selecione uma imagem no acervo antes de solicitar se quiser usá-la como referência.'}</p><Botao tipo="submit" variante="primario" disabled={a.ocupado}>Registrar solicitação</Botao>
      </form>}
    </Modal>}
  </section>
}
