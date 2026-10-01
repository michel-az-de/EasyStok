import { useEffect, useState } from 'react'
import { Icone } from '../../componentes/Icone'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { SEM_CADASTRO } from '../../aplicacao/api/ficha'
import { ehRestricao, ordenarTags, sugestoesDeTag } from '../../dominio/cliente'
import { SEGUNDOS_PARA_DESFAZER } from '../../dominio/esteira'
import css from './cliente.module.css'

// Tags como chips (seção 2 da direção visual, US-016). Restrição sempre
// primeiro (RN-39/RN-52): ícone de alerta e borda `--atencao`. Remover usa o
// mesmo padrão de desfazer da esteira (`SEGUNDOS_PARA_DESFAZER`), sem criar
// ação nova: `Desfazer` só chama `adicionarTag` de volta.
export function TagsDoCliente({ conversa }) {
  const { tags } = conversa.cliente
  const { conversas, fonteApi } = useAtendimento()
  // Modo API (F09): tag mora no cadastro do EasyStok; lead sem cadastro não tem onde gravar.
  const semCadastro = fonteApi && !conversa.clienteId
  const { restricoes } = useCatalogo()
  const { adicionarTag, removerTag, editarTag } = useAcoes()

  const [editando, setEditando] = useState(null)
  const [rascunho, setRascunho] = useState('')
  const [adicionando, setAdicionando] = useState(false)
  const [novaTag, setNovaTag] = useState('')
  const [desfazer, setDesfazer] = useState(null)

  useEffect(() => {
    setEditando(null)
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
    setEditando(tag)
    setRascunho(tag)
  }

  function confirmarEdicao() {
    const novo = rascunho.trim()
    if (novo && novo !== editando) editarTag(conversa.id, editando, novo)
    setEditando(null)
  }

  function escolherSugestao(tag) {
    adicionarTag(conversa.id, tag)
    setNovaTag('')
  }

  function confirmarNovaTag() {
    const tag = novaTag.trim()
    if (tag) adicionarTag(conversa.id, tag)
    setNovaTag('')
  }

  const sugestoes = adicionando
    ? sugestoesDeTag(novaTag, { conversas, cadastroIdAtual: conversa.cadastroId, tagsAtuais: tags, restricoes })
    : null

  return (
    <div className={css.blocoTags}>
      <ul className={css.tagsLinha} aria-label="Tags do cliente">
        {ordenarTags(tags, restricoes).map((tag) => (
          <li key={tag}>
            {editando === tag ? (
              <input
                autoFocus
                className={css.chipEdicao}
                value={rascunho}
                onChange={(e) => setRascunho(e.target.value)}
                onBlur={confirmarEdicao}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') confirmarEdicao()
                  if (e.key === 'Escape') setEditando(null)
                }}
              />
            ) : (
              <span className={[css.chip, ehRestricao(tag, restricoes) ? css.chipRestricao : ''].filter(Boolean).join(' ')}>
                {ehRestricao(tag, restricoes) && <Icone nome="alerta" tamanho={20} />}
                <button type="button" className={css.chipTexto} onClick={() => abrirEdicao(tag)}>
                  {tag}
                </button>
                <button
                  type="button"
                  className={css.chipRemover}
                  aria-label={`Tirar tag ${tag}`}
                  onClick={() => remover(tag)}
                >
                  <Icone nome="x" tamanho={20} />
                </button>
              </span>
            )}
          </li>
        ))}
        <li>
          {semCadastro ? (
            <span className={css.avisoCanal}>{SEM_CADASTRO}</span>
          ) : adicionando ? (
            <input
              autoFocus
              className={css.chipEdicao}
              placeholder="Nova tag"
              value={novaTag}
              onChange={(e) => setNovaTag(e.target.value)}
              onBlur={() => { if (!novaTag.trim()) setAdicionando(false) }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') confirmarNovaTag()
                if (e.key === 'Escape') setAdicionando(false)
              }}
            />
          ) : (
            <button type="button" className={css.chipTracejado} onClick={() => setAdicionando(true)}>
              <Icone nome="plus" tamanho={20} /> Tag
            </button>
          )}
        </li>
      </ul>

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
