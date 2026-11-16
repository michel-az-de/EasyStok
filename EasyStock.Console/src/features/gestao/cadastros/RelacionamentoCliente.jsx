import { useEffect, useId, useRef, useState } from 'react'
import { adicionarNotaCadastro, adicionarTagCadastro, removerTagCadastro } from '../../../aplicacao/cadastros'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { Icone } from '../../../componentes/Icone'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import { ehRestricao, ordenarTags, rotuloDaTag } from '../../../dominio/cliente'
import { previaDasNotas } from '../../../dominio/ficha'
import { avisoDasNotas, erroDaNota, erroDaTag, LIMITE_DA_NOTA, LIMITE_DA_TAG, mensagemDaFicha } from '../../../dominio/fichaCliente'
import { plural } from '../../../dominio/formato'
import css from './cadastros.module.css'

// #2734: o cadastro mostra o que a Ficha do Atendimento mostra da mesma pessoa: bloqueio, notas internas e tags,
// com as mesmas palavras. Nota e tag gravam sozinhas, fora do formulário: não marcam o cadastro como alterado.
const TITULO = { nota: 'Nota interna', tag: 'Nova tag', tirarTag: 'Tirar tag?' }
const CONFIRMAR = { nota: 'Fixar nota', tag: 'Adicionar tag', tirarTag: 'Tirar tag' }
const SUCESSO = { nota: 'Nota adicionada.', tag: 'Tag adicionada.', tirarTag: 'Tag tirada.' }
const TAG_REPETIDA = 'Esta tag já existe.'
const pedirFechamento = (evento) => evento.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))

// `aoRascunhar` avisa quem abre o cadastro de que há nota ou tag sendo escrita: o atalho por endereço espera
// (PainelCadastros), em vez de trocar de pessoa com o texto na tela. O editor monta este bloco com `key` da pessoa.
// `restricoes` chega por propriedade (o bloco também abre fora do Atendimento): a restrição vem primeiro e com alerta.
export function RelacionamentoCliente({ clienteId, relacionamento, contexto, podeEditar, aoGravar, aoRascunhar, restricoes = [] }) {
  const ids = { notas: useId(), tags: useId(), form: useId() }
  const { dados, erro: erroDeLeitura, recarregar, aplicar } = relacionamento
  const [abertas, abrirNotas] = useState(false)
  const [intencao, escolher] = useState(null)
  const [valor, definirValor] = useState('')
  const [erroDoCampo, recusar] = useState(null)
  const [erro, falhar] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const [sucesso, avisar] = useState(null)
  const emVoo = useRef(false)
  const formRef = useRef(null)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false } }, [])
  const comRascunho = Boolean(intencao && intencao.acao !== 'tirarTag' && valor.trim())
  useEffect(() => { aoRascunhar?.(comRascunho); return () => aoRascunhar?.(false) }, [comRascunho, aoRascunhar])

  if (!dados) {
    return <div className={css.relacionamento}>
      {erroDeLeitura
        ? <div className={css.erro} role="alert"><p>{erroDeLeitura.message || 'As notas e as tags não carregaram.'}</p><Botao onClick={recarregar}>Tentar de novo</Botao></div>
        : <p className={css.legenda}>Carregando notas e tags…</p>}
    </div>
  }

  const { bloqueio, notas, tags } = dados
  const previa = previaDasNotas(notas, abertas)
  const limite = intencao?.acao === 'nota' ? LIMITE_DA_NOTA : LIMITE_DA_TAG
  const tamanho = valor.trim().length
  const acimaDoLimite = tamanho > limite ? (intencao?.acao === 'nota' ? erroDaNota(valor) : erroDaTag(valor)) : null
  const focarCampo = () => requestAnimationFrame(() => formRef.current?.querySelector('textarea, input')?.focus())

  const abrir = (acao, alvo = null) => { falhar(null); avisar(null); recusar(null); definirValor(''); escolher({ acao, alvo }) }
  const fechar = () => { if (!emVoo.current) escolher(null) }
  const mudar = (texto) => { definirValor(texto); recusar(null) }

  async function confirmar(evento) {
    evento?.preventDefault()
    if (emVoo.current || !intencao) return
    const { acao, alvo } = intencao
    const problema = acao === 'nota' ? erroDaNota(valor) : acao === 'tag' ? erroDaTag(valor, tags) : null
    if (problema) { recusar(problema); focarCampo(); return }
    emVoo.current = true; ocupar(true); falhar(null)
    try {
      const gravado = acao === 'nota' ? await adicionarNotaCadastro(clienteId, valor.trim(), contexto)
        : acao === 'tag' ? await adicionarTagCadastro(clienteId, valor.trim(), contexto)
        : await removerTagCadastro(clienteId, alvo, contexto)
      // A Ficha do Atendimento relê a pessoa mesmo que este editor já tenha fechado.
      aoGravar?.()
      if (!montado.current) return
      // O que o servidor devolveu entra na lista na hora; a releitura do dossiê só confirma.
      if (acao === 'nota' && gravado) aplicar(atual => ({ ...atual, notas: [gravado, ...atual.notas] }))
      if (acao === 'tag' && gravado) aplicar(atual => ({ ...atual, tags: atual.tags.includes(gravado) ? atual.tags : [...atual.tags, gravado] }))
      if (acao === 'tirarTag') aplicar(atual => ({ ...atual, tags: atual.tags.filter(t => t !== alvo) }))
      escolher(null); avisar(SUCESSO[acao]); recarregar()
    } catch (e) {
      if (!montado.current) return
      // A tag que outra pessoa acabou de pôr volta como 409: o motivo fica no campo e a lista é relida.
      if (acao === 'tag' && e.status === 409) { recusar(TAG_REPETIDA); recarregar(); focarCampo() } else falhar(mensagemDaFicha(e))
    } finally { emVoo.current = false; if (montado.current) ocupar(false) }
  }

  const digitando = intencao && intencao.acao !== 'tirarTag'
  return <div className={css.relacionamento}>
    {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
    {bloqueio && <div className={css.avisoBloqueio}>
      <p className={css.tituloBloqueio}><Icone nome="bloqueio" tamanho={20} /><strong>Cadastro bloqueado</strong><span>vale em todos os canais</span></p>
      <p>{bloqueio.motivo}</p>
      <p className={css.legenda}>O desbloqueio é feito na Ficha do Atendimento.</p>
    </div>}
    <div className={css.notasETags}>
      <section aria-labelledby={ids.notas}>
        <div className={css.barra}><h3 id={ids.notas}>Notas internas</h3>{podeEditar && <Botao icone="mais" onClick={() => abrir('nota')}>Adicionar nota</Botao>}</div>
        {notas.length === 0 ? <Vazio variante="linha" titulo="Nenhuma nota interna." />
          : <ul className={css.notas}>{previa.visiveis.map(n => <li key={n.id}>
            <p>{n.texto}</p>
            <small>{[n.autor, n.em].filter(Boolean).join(' · ')}</small>
          </li>)}</ul>}
        {previa.escondidas > 0 && <Botao variante="texto" onClick={() => abrirNotas(true)}>Ver mais {plural(previa.escondidas, 'nota', 'notas')}</Botao>}
        {abertas && notas.length > previaDasNotas(notas).visiveis.length && <Botao variante="texto" onClick={() => abrirNotas(false)}>Mostrar só as mais recentes</Botao>}
        {abertas && avisoDasNotas(notas.length) && <p className={css.legenda}>{avisoDasNotas(notas.length)}</p>}
      </section>
      <section aria-labelledby={ids.tags}>
        <div className={css.barra}><h3 id={ids.tags}>Tags</h3>{podeEditar && <Botao icone="mais" onClick={() => abrir('tag')}>Adicionar tag</Botao>}</div>
        {tags.length === 0 ? <Vazio variante="linha" titulo="Nenhuma tag." />
          : <ul className={css.tags}>{ordenarTags(tags, restricoes).map(t => { const restricao = ehRestricao(t, restricoes); return <li key={t}>
            <Chip papel="tag" icone={restricao ? 'alerta' : undefined} className={restricao ? css.tagRestricao : undefined}
              onRemover={podeEditar ? () => abrir('tirarTag', t) : undefined} rotuloRemover={`Tirar tag ${rotuloDaTag(t)}`}>{rotuloDaTag(t)}</Chip>
          </li> })}</ul>}
      </section>
    </div>
    {intencao && <Modal titulo={TITULO[intencao.acao]} descricao={intencao.acao === 'nota' ? 'Só a equipe vê. O cliente não recebe.' : undefined} largura="sm" focarCampo={Boolean(digitando)}
      aoFechar={ocupado ? undefined : fechar} segurarFechamento={ocupado} confirmarDescarte={Boolean(digitando) && Boolean(valor.trim()) && !ocupado}
      rodape={<><Botao disabled={ocupado} onClick={digitando ? pedirFechamento : fechar}>Cancelar</Botao>
        <Botao variante={digitando ? 'primario' : 'perigo'} disabled={ocupado} {...(digitando ? { tipo: 'submit', form: ids.form } : { onClick: confirmar })}>{ocupado ? 'Salvando…' : CONFIRMAR[intencao.acao]}</Botao></>}>
      {digitando && <form id={ids.form} ref={formRef} onSubmit={confirmar} noValidate>
        {intencao.acao === 'nota'
          ? <CampoArea rotulo="Texto da nota" rows={4} placeholder="Ex.: prefere a massa mais al dente." value={valor} disabled={ocupado} data-foco-inicial
            erro={erroDoCampo} aviso={acimaDoLimite} dica={`${tamanho} de ${LIMITE_DA_NOTA} caracteres`} onChange={e => mudar(e.target.value)} />
          : <CampoTexto rotulo="Tag" placeholder="Ex.: sem lactose" value={valor} disabled={ocupado} data-foco-inicial autoComplete="off"
            erro={erroDoCampo} aviso={acimaDoLimite} dica="Uma palavra ou expressão curta." onChange={e => mudar(e.target.value)} />}
      </form>}
      {intencao.acao === 'tirarTag' && <p>A tag <strong>{rotuloDaTag(intencao.alvo)}</strong> sai do cadastro e da Ficha do Atendimento.</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>}
  </div>
}
