import { useEffect, useId, useRef, useState } from 'react'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { Icone } from '../../componentes/Icone'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import {
  chaveDaTag, ehRestricao, ordenarTags, ROTULO_RESTRICAO, rotuloDaTag, sugestoesDeTag,
} from '../../dominio/cliente'
import { SEGUNDOS_PARA_DESFAZER } from '../../dominio/esteira'
import css from './cliente.module.css'

const TAG_REPETIDA = 'Esta tag já existe.'

// Tags como chips (seção 2 da direção visual, US-016). Restrição sempre
// primeiro (RN-39/RN-52): ícone de alerta e borda `--alerta`. Remover usa o
// mesmo padrão de desfazer da esteira (`SEGUNDOS_PARA_DESFAZER`), sem criar
// ação nova: `Desfazer` só chama `adicionarTag` de volta.
export function TagsDoCliente({ conversa }) {
  const { tags } = conversa.cliente
  const { conversas } = useAtendimento()
  const { restricoes } = useCatalogo()
  const { adicionarTag, removerTag, editarTag } = useAcoes()

  const [editando, setEditando] = useState(null)
  const [rascunho, setRascunho] = useState('')
  const [adicionando, setAdicionando] = useState(false)
  const [novaTag, setNovaTag] = useState('')
  const [desfazer, setDesfazer] = useState(null)
  // #2734: o erro guarda em qual campo nasceu (`edicao` ou `nova`), para ficar ligado a ele por aria-describedby.
  const [erroTag, setErroTag] = useState(null)
  const idErro = useId()
  const recusaEm = (campo) => (erroTag?.em === campo ? { 'aria-invalid': true, 'aria-describedby': idErro } : {})
  const cancelou = useRef(false)

  useEffect(() => {
    setEditando(null)
    setErroTag(null)
    setAdicionando(false)
    setDesfazer(null)
  }, [conversa.id])

  useEffect(() => {
    if (!desfazer) return undefined
    if (desfazer.restam <= 1) { const t = setTimeout(() => setDesfazer(null), 1000); return () => clearTimeout(t) }
    const t = setTimeout(() => setDesfazer((d) => (d ? { ...d, restam: d.restam - 1 } : d)), 1000)
    return () => clearTimeout(t)
  }, [desfazer])

  function remover(tag) {
    removerTag(conversa.id, tag)
    setDesfazer({ tag, restam: SEGUNDOS_PARA_DESFAZER })
  }

  function desfazerRemocao() {
    if (!desfazer) return
    adicionarTag(conversa.id, desfazer.tag)
    setDesfazer(null)
  }

  function abrirEdicao(tag) {
    cancelou.current = false
    setErroTag(null)
    setEditando(tag)
    setRascunho(rotuloDaTag(tag))
  }

  // #2734 (A29): tag repetida (mesma chave, sem caixa nem acento) é recusada com mensagem.
  const jaExiste = (texto, exceto) => tags.some((t) => t !== exceto && chaveDaTag(t) === chaveDaTag(texto))

  function confirmarEdicao() {
    const novo = rascunho.trim()
    if (novo && chaveDaTag(novo) !== chaveDaTag(editando)) {
      if (jaExiste(novo, editando)) { setErroTag({ texto: TAG_REPETIDA, em: 'edicao' }); return }
      editarTag(conversa.id, editando, novo)
    }
    setErroTag(null)
    setEditando(null)
  }

  // O Esc cancela: o blur que vem depois (a janela perde o foco, o input some) não pode salvar.
  function cancelarEdicao() {
    cancelou.current = true
    setErroTag(null)
    setEditando(null)
  }

  function escolherSugestao(tag) {
    adicionarTag(conversa.id, tag)
    setNovaTag('')
  }

  function confirmarNovaTag() {
    const tag = novaTag.trim()
    if (tag && jaExiste(tag)) { setErroTag({ texto: TAG_REPETIDA, em: 'nova' }); return }
    if (tag) adicionarTag(conversa.id, tag)
    setErroTag(null)
    setNovaTag('')
  }

  const sugestoes = adicionando
    ? sugestoesDeTag(novaTag, { conversas, cadastroIdAtual: conversa.cadastroId, tagsAtuais: tags, restricoes })
    : null

  return (
    <div className={css.blocoTags} data-tags-cliente>
      <ul className={css.tagsLinha} aria-label="Tags do cliente">
        {ordenarTags(tags, restricoes).map((tag) => (
          <li key={tag}>
            {editando === tag ? (
              <input
                autoFocus
                className={css.chipEdicao}
                aria-label={`Editar tag ${rotuloDaTag(tag)}`}
                {...recusaEm('edicao')}
                value={rascunho}
                onChange={(e) => { setRascunho(e.target.value); setErroTag(null) }}
                onBlur={() => { if (cancelou.current) { cancelou.current = false; return } confirmarEdicao() }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') confirmarEdicao()
                  if (e.key === 'Escape') cancelarEdicao()
                }}
              />
            ) : (
              <span className={[css.chip, ehRestricao(tag, restricoes) ? css.chipRestricao : ''].filter(Boolean).join(' ')}>
                {ehRestricao(tag, restricoes) && <Icone nome="alerta" tamanho={20} rotulo={ROTULO_RESTRICAO} />}
                <button type="button" className={css.chipTexto} onClick={() => abrirEdicao(tag)}>
                  {rotuloDaTag(tag)}
                </button>
                <BotaoIcone rotulo={`Tirar tag ${rotuloDaTag(tag)}`} icone="x" className={css.chipRemover} onClick={() => remover(tag)} />
              </span>
            )}
          </li>
        ))}
        <li>
          {adicionando ? (
            <input
              autoFocus
              className={css.chipEdicao}
              placeholder="Nova tag"
              aria-label="Nova tag"
              {...recusaEm('nova')}
              value={novaTag}
              onChange={(e) => { setNovaTag(e.target.value); setErroTag(null) }}
              onBlur={() => { if (!novaTag.trim()) setAdicionando(false) }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') confirmarNovaTag()
                if (e.key === 'Escape') { setAdicionando(false); setErroTag(null) }
              }}
            />
          ) : (
            <button type="button" className={css.chipTracejado} onClick={() => setAdicionando(true)}>
              <Icone nome="plus" tamanho={20} /> Tag
            </button>
          )}
        </li>
      </ul>
      {erroTag && <p id={idErro} className={css.erroEmLinha} role="alert">{erroTag.texto}</p>}

      {adicionando && (sugestoes.restricao.length > 0 || sugestoes.gosto.length > 0) && (
        <div className={css.sugestoesTag}>
          {sugestoes.restricao.length > 0 && (
            <div>
              <p className={css.grupoSugestao}>Restrição</p>
              <div className={css.tagsLinha}>
                {sugestoes.restricao.map((s) => (
                  <button key={s} type="button" className={css.chipSugestao} onMouseDown={(e) => e.preventDefault()} onClick={() => escolherSugestao(s)}>
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}
          {sugestoes.gosto.length > 0 && (
            <div>
              <p className={css.grupoSugestao}>Gosto</p>
              <div className={css.tagsLinha}>
                {sugestoes.gosto.map((s) => (
                  <button key={s} type="button" className={css.chipSugestao} onMouseDown={(e) => e.preventDefault()} onClick={() => escolherSugestao(s)}>
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {desfazer && (
        <output className={css.avisoDesfazer}>
          Tag tirada ·
          <button type="button" onClick={desfazerRemocao}>Desfazer · {desfazer.restam}</button>
        </output>
      )}
    </div>
  )
}
