import { useId, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { FormResposta } from '../../componentes/FormResposta'
import { Popover } from '../../componentes/Popover'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { respostasEditaveis } from '../../dominio/acessoModulos'
import { REGRAS_DO_FORM, agruparRespostasDoSeletor, categoriasUsadas } from '../../dominio/respostas'
import css from './seletorRespostas.module.css'

// Lista compacta de respostas prontas acima do compositor (#1441). Uma linha por
// resposta: título, atalho e o começo do texto; clicar ou Enter insere no campo.
// #3208: com alguma categoria a lista sai em grupos, e quem pode editar cria ("Nova resposta") e edita
// ("Editar") ali mesmo, num formulário dentro do painel. Arquivar segue na Gestão ("Gerenciar respostas").
// `aoInserido` (#1587): chamado depois que a resposta entra no campo, pelo clique ou pelo Enter
// da busca; o Composer devolve o foco ao campo de texto.
export function SeletorRespostas({ seletor, aoGerenciar, aoInserido, idLista: idInformado }) {
  const idGerado = useId()
  const idLista = idInformado ?? idGerado
  const idDe = (indice) => `${idLista}-${indice}`
  const { itens, destaque } = seletor
  const { fonteApi, sincronizacao } = useAtendimento()
  const { incluirRespostaPronta, editarRespostaPronta } = useAcoes()
  const { respostasProntas } = useCatalogo()
  const { acoes } = useAcessoModulos()
  // #2185: quem não edita abre a biblioteca só para consultar.
  const editavel = respostasEditaveis({ fonteApi, acoes })
  // 'nova', o id da resposta em edição, ou null (a lista). `recusou`: o servidor não gravou, o formulário segue aberto.
  const [editando, setEditando] = useState(null)
  const [recusou, setRecusou] = useState(false)
  const categorias = useMemo(() => categoriasUsadas(respostasProntas), [respostasProntas])
  const { agrupado, grupos } = useMemo(() => agruparRespostasDoSeletor(itens), [itens])
  const emEdicao = editando === 'nova' ? {} : (respostasProntas ?? []).find((r) => r.id === editando)
  const abrirForm = (alvo) => { setRecusou(false); setEditando(alvo) }
  // A ação do modo API devolve `false` quando não gravou (o motivo vai para `sincronizacao.aviso`); a da
  // demonstração não devolve nada. Sem `false`, volta à lista com o item atualizado.
  const gravar = (dados) => Promise.resolve(
    editando === 'nova' ? incluirRespostaPronta(dados) : editarRespostaPronta(editando, dados),
  ).then((ok) => { if (ok === false) setRecusou(true); else setEditando(null) })
  const opcaoAtiva = useRef(null)
  useLayoutEffect(() => {
    const opcao = opcaoAtiva.current
    const caixa = opcao?.closest('[role="listbox"]')
    if (!caixa) return
    const item = opcao.getBoundingClientRect()
    const janela = caixa.getBoundingClientRect()
    // Só a lista acompanha o destaque; scrollIntoView também deslocaria as molduras.
    if (item.top < janela.top) caixa.scrollTop += item.top - janela.top
    else if (item.bottom > janela.bottom) caixa.scrollTop += item.bottom - janela.bottom
  }, [destaque, itens])

  const opcao = (r, i) => (
    <li
      key={r.id}
      id={idDe(i)}
      role="option"
      ref={i === destaque ? opcaoAtiva : undefined}
      aria-selected={i === destaque}
      className={`${css.linha} ${i === destaque ? css.ativa : ''}`}
      onMouseEnter={() => seletor.destacar(i)}
      // Não tira o foco do campo antes do clique valer.
      onMouseDown={(e) => e.preventDefault()}
      onClick={() => seletor.inserir(r, aoInserido)}
    >
      <span className={css.titulo}>{r.titulo}</span>
      <span className={css.lateral}>
        <span className={css.atalho}>{r.atalho}</span>
        {editavel && (
          <Botao
            variante="texto" icone="lapis" aria-label={`Editar resposta ${r.titulo}`}
            onMouseDown={(e) => e.preventDefault()}
            onClick={(e) => { e.stopPropagation(); abrirForm(r.id) }}
          >Editar</Botao>
        )}
      </span>
      <span className={css.previa}>{r.texto}</span>
    </li>
  )

  return (
    <Popover rotulo="Respostas rápidas" papel="group" aoFechar={seletor.fechar} semAutoFoco portal>
      <div className={css.seletor}>
        {editando !== null && emEdicao ? (
          <FormResposta
            key={editando}
            inicial={emEdicao}
            regras={REGRAS_DO_FORM}
            categorias={categorias}
            erro={recusou ? (sincronizacao?.aviso ?? 'Não foi possível salvar a resposta.') : null}
            aoSalvar={gravar}
            aoCancelar={() => setEditando(null)}
          />
        ) : (
          <>
            {!seletor.pelaBarra && (
              <input
                autoFocus
                className={css.busca}
                type="search"
                role="combobox"
                aria-label="Buscar resposta pronta"
                aria-autocomplete="list"
                aria-expanded={itens.length > 0}
                aria-controls={itens.length ? idLista : undefined}
                aria-activedescendant={itens[destaque] ? idDe(destaque) : undefined}
                placeholder="Buscar por título, atalho, texto ou categoria"
                value={seletor.busca}
                onChange={(e) => seletor.mudarBusca(e.target.value)}
                onKeyDown={(e) => { seletor.aoTeclar(e, aoInserido) }}
              />
            )}

            {itens.length === 0 ? (
              <p className={css.vazio}>
                {seletor.temRespostas ? 'Nenhuma resposta com esse termo.' : 'Nenhuma resposta pronta cadastrada ainda.'}
              </p>
            ) : (
              <ul id={idLista} role="listbox" aria-label="Respostas prontas" className={css.lista}>
                {agrupado ? grupos.map((g, n) => (
                  <li key={g.chave ?? 'sem-categoria'} role="presentation">
                    <p id={`${idLista}-grupo-${n}`} className={css.grupo}>{g.rotulo}</p>
                    <ul role="group" aria-labelledby={`${idLista}-grupo-${n}`} className={css.grupoLista}>
                      {g.itens.map(({ resposta, indice }) => opcao(resposta, indice))}
                    </ul>
                  </li>
                )) : itens.map((r, i) => opcao(r, i))}
              </ul>
            )}

            <div className={css.rodape}>
              <span className={css.dica}>Enter insere no rascunho</span>
              {editavel && (
                <Botao variante="texto" icone="plus" onMouseDown={(e) => e.preventDefault()} onClick={() => abrirForm('nova')}>Nova resposta</Botao>
              )}
              <button
                type="button"
                className={css.gerenciar}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => { seletor.fechar(); aoGerenciar?.() }}
              >
                {editavel ? 'Gerenciar respostas' : 'Ver respostas'}
              </button>
            </div>
          </>
        )}
      </div>
    </Popover>
  )
}
