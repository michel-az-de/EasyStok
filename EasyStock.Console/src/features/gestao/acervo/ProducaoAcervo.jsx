import { useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Modal } from '../../../componentes/Modal'
import css from './acervo.module.css'

export const canaisAcervo = { site: 'Site/cardápio', instagram: 'Instagram', whatsapp: 'WhatsApp', impressao: 'Materiais impressos' }
const formatos = ['1:1', '4:5', '16:9', '9:16', '3:2', 'livre']
const restricoesBase = 'Preservar o produto das referências. Não inventar ingredientes, porções, embalagem, peso ou características. Manter texto e marca em camada separada. Resultado ilustrativo; aprovação humana antes de publicar.'

export function ProducaoAcervo({ acervo: a }) {
  const [canal, setCanal] = useState('')
  const [item, setItem] = useState('')
  const [somenteLacunas, setSomenteLacunas] = useState(true)
  const [formulario, setFormulario] = useState(null)
  const [aviso, setAviso] = useState('')
  const operacoesFila = useRef({})
  const atuais = a.briefings.filter((b) => !a.briefings.some((v) => v.anteriorId === b.id))
  function abrir(dados = {}) { setFormulario({ operacaoId: crypto.randomUUID(), ...dados, referenciasSelecionadas: JSON.parse(dados.referenciasJson || '[]') }) }
  function selecionarReferencia(id, selecionada) {
    setFormulario((f) => ({ ...f, referenciasSelecionadas: selecionada ? [...new Set([...f.referenciasSelecionadas, id])] : f.referenciasSelecionadas.filter((r) => r !== id) }))
  }
  async function salvar(e) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const atual = formulario
    const resultado = await a.fazer(() => a.api.salvarBriefing({ operacaoId: atual.operacaoId, anteriorId: atual.anteriorId || null,
      itemId: f.get('itemId'), canal: f.get('canal'), formato: f.get('formato'), finalidade: f.get('finalidade'),
      restricoes: f.get('restricoes'), prompt: f.get('prompt'), referencias: atual.referenciasSelecionadas }))
    if (resultado !== false) { setFormulario(null); setAviso(`Briefing v${resultado.versao} salvo. Encaminhe para a fila quando estiver preparado.`) }
  }
  async function enfileirar(b) {
    const id = operacoesFila.current[b.id] ||= crypto.randomUUID()
    const resultado = await a.fazer(() => a.api.enfileirarBriefing(b.id, id))
    if (resultado !== false) setAviso(resultado.concluidoEm ? 'Este briefing já tem um resultado. Crie nova versão para outra produção.' : 'Briefing na fila. Repetir esta ação preserva a mesma solicitação.')
  }
  const linhas = a.cobertura.filter((c) => (!canal || c.canal === canal) && (!item || c.itemId === item) && (!somenteLacunas || c.formatosPendentes.length))
  const referencias = a.imagens.filter((i) => i.aprovadoEm)
  const referenciasSelecionadas = formulario?.referenciasSelecionadas || []
  return <section className={css.producao} aria-label="Cobertura e produção assistida">
    <h3>Cobertura por produto e canal</h3>
    <p>Adequação confirmada para cada uso. Fotos já presentes no cardápio e candidatos do acervo aparecem separados. Formatos pendentes orientam a curadoria; confira as referências antes de gerar.</p>
    <div className={css.filtros}>
      <label className={css.campo}>Produto da cobertura<select value={item} disabled={a.ocupado} onChange={(e) => setItem(e.target.value)}><option value="">Todos</option>{a.itens.map((i) => <option key={i.cardapioItemId} value={i.cardapioItemId}>{i.nome}</option>)}</select></label>
      <label className={css.campo}>Canal da cobertura<select value={canal} disabled={a.ocupado} onChange={(e) => setCanal(e.target.value)}><option value="">Todos</option>{Object.entries(canaisAcervo).map(([id, nome]) => <option key={id} value={id}>{nome}</option>)}</select></label>
      <label><input type="checkbox" checked={somenteLacunas} onChange={(e) => setSomenteLacunas(e.target.checked)} /> Somente lacunas</label>
    </div>
    <div className={css.grade}>{linhas.map((c) => <article className={css.cobertura} key={`${c.itemId}-${c.canal}`}>
      <h4>{c.nome}</h4><strong>{canaisAcervo[c.canal]}</strong>
      <p>{c.adequadas} com uso confirmado · {c.candidatas} candidatos com formato e resolução</p>
      <p>{c.fotografias} fotos reais · {c.geradas} imagens IA · {c.comunicacoes} peças de comunicação. {c.total} versões vinculadas.</p>
      {c.fotosNoCardapio > 0 && <p>{c.fotosNoCardapio} fotos no cardápio atual; origem e adequação devem ser conferidas no acervo.</p>}
      <p>{c.formatosPendentes.length ? `Falta confirmar: ${c.formatosPendentes.join(', ')}` : 'Usos previstos confirmados.'} {c.pendentes > 0 && `${c.pendentes} em produção.`}</p>
      {c.canal === 'impressao' && <small>Adequação à medida cadastrada e 300 dpi. Prova física e arte final continuam necessárias.</small>}
      <Botao disabled={a.ocupado} onClick={() => abrir({ itemId: c.itemId, canal: c.canal, formato: c.canal === 'impressao' ? 'livre' : c.formatosPendentes[0] || '1:1' })}>Preparar briefing</Botao>
    </article>)}</div>
    {!linhas.length && !a.carregando && <p>Nenhuma lacuna nos filtros selecionados.</p>}
    <div className={css.linha}><h3>Briefings reutilizáveis</h3><Botao disabled={a.ocupado} onClick={() => abrir()}>Novo briefing</Botao></div>
    <p>Uma versão guarda produto, canal, referências e prompt. Um briefing tem uma produção; outra tentativa começa em nova versão. Últimos 200 briefings, todas as solicitações pendentes e 200 concluídas disponíveis para consulta.</p>
    {aviso && <p role="status">{aviso}</p>}
    {atuais.map((b) => <details key={b.id}><summary>{a.itens.find((i) => i.cardapioItemId === b.itemId)?.nome || 'Produto'} · {canaisAcervo[b.canal]} · {b.formato} · v{b.versao}</summary>
      <p>{b.finalidade}</p><p className={css.texto}>{b.prompt}</p><p className={css.texto}>Restrições: {b.restricoes}</p>
      <p>Referências: {JSON.parse(b.referenciasJson).map((id) => a.imagens.find((i) => i.id === id)?.titulo || id).join(', ')}</p>
      <div className={css.acoes}><Botao disabled={a.ocupado} onClick={() => enfileirar(b)}>Encaminhar para fila</Botao><Botao disabled={a.ocupado} onClick={() => abrir({ ...b, anteriorId: b.id, operacaoId: crypto.randomUUID() })}>Criar nova versão</Botao></div>
      {a.briefings.filter((v) => v.raizId === b.raizId && v.id !== b.id).map((v) => <details key={v.id}><summary>Versão anterior v{v.versao}</summary><p>{canaisAcervo[v.canal]} · {v.formato} · {v.finalidade}</p><p className={css.texto}>{v.prompt}</p><p className={css.texto}>{v.restricoes}</p><p className={css.texto}>Referências: {JSON.parse(v.referenciasJson).map((id) => a.imagens.find((i) => i.id === id)?.titulo || id).join(', ')}</p></details>)}
    </details>)}
    {!atuais.length && <p>Nenhum briefing preparado.</p>}
    {formulario && <Modal titulo={formulario.anteriorId ? 'Nova versão do briefing' : 'Preparar briefing'} aoFechar={a.ocupado ? undefined : () => setFormulario(null)}>
      <form className={css.formulario} onSubmit={salvar}>
        <fieldset className={css.campos} disabled={a.ocupado}>
        {a.erro && <p role="alert" className={css.erro}>{a.erro}</p>}
        <label>Produto<select name="itemId" required defaultValue={formulario.itemId}>{a.itens.filter((i) => !formulario.anteriorId || i.cardapioItemId === formulario.itemId).map((i) => <option key={i.cardapioItemId} value={i.cardapioItemId}>{i.nome}</option>)}</select></label>
        <label>Canal<select name="canal" required defaultValue={formulario.canal || 'site'}>{Object.entries(canaisAcervo).map(([id, nome]) => <option key={id} value={id}>{nome}</option>)}</select></label>
        <label>Formato<select name="formato" defaultValue={formulario.formato || '1:1'}>{formatos.map((f) => <option key={f}>{f}</option>)}</select></label>
        <p>Site: 1:1, 3:2 ou banner 16:9. Instagram: 1:1, 4:5 ou 9:16. WhatsApp: 1:1 ou 4:5. Impressão: medida física da peça.</p>
        <label>Finalidade<textarea name="finalidade" required maxLength={500} defaultValue={formulario.finalidade || ''} /></label>
        <fieldset><legend>Referências aprovadas nesta página do acervo</legend>{referencias.map((i) => <label key={i.id}><input type="checkbox" name="referencia" value={i.id} checked={referenciasSelecionadas.includes(i.id)} onChange={(e) => selecionarReferencia(i.id, e.target.checked)} /> {i.titulo} · {i.origem}</label>)}
          {referenciasSelecionadas.filter((id) => !referencias.some((i) => i.id === id)).map((id) => <label key={id}><input type="checkbox" name="referencia" value={id} checked onChange={(e) => selecionarReferencia(id, e.target.checked)} /> Referência preservada: {id}</label>)}
          {!referencias.length && !referenciasSelecionadas.length && <p>Busque e aprove referências no acervo antes de preparar a produção.</p>}
        </fieldset>
        <label>Restrições<textarea name="restricoes" required maxLength={4000} defaultValue={formulario.restricoes || restricoesBase} /></label>
        <label>Prompt desta versão<textarea name="prompt" required maxLength={7000} defaultValue={formulario.prompt || ''} /></label>
        <p>Geração assistida. O resultado exigirá revisão e aprovação explícita antes de publicar.</p>
        <Botao tipo="submit" variante="primario" disabled={a.ocupado || !referenciasSelecionadas.length}>Salvar briefing</Botao>
        </fieldset>
      </form>
    </Modal>}
  </section>
}
