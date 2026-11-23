import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react'
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
  // Formulário com algo digitado trava o `fechar` da barra (devolve false e pede o "Descartar?"): vale para Esc, clique
  // fora, o botão Respostas e a Nota interna, que fecham por ele. O Popover só fecha se `aoFechar` não devolver false.
  const { fechar, travar, pedidoDescarte } = seletor
  // A ação do modo API devolve `false` quando não gravou (o motivo vai para `sincronizacao.aviso`); a da
  // demonstração não devolve nada. Sem `false`, volta à lista com o item atualizado.
  const gravar = (dados) => Promise.resolve(
    editando === 'nova' ? incluirRespostaPronta(dados) : editarRespostaPronta(editando, dados),
  ).then((ok) => { if (ok === false) setRecusou(true); else setEditando(null) })
  const opcaoAtiva = useRef(null)
  // Aberto por "/" não há campo de busca: F2 vale no campo de texto do compositor, onde o foco está.
  const { pelaBarra } = seletor
  const itemAtivo = itens[destaque]
  useEffect(() => {
    if (!pelaBarra || !editavel || editando !== null || !itemAtivo) return undefined
    const aoTeclar = (e) => {
      if (e.key !== 'F2' || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.isComposing) return
      // stopPropagation: na demonstração a Moldura escuta F2 na janela (Simular) e abriria junto.
      e.preventDefault(); e.stopPropagation(); setRecusou(false); setEditando(itemAtivo.id)
    }
    document.addEventListener('keydown', aoTeclar)
    return () => document.removeEventListener('keydown', aoTeclar)
  }, [pelaBarra, editavel, editando, itemAtivo])
  useLayoutEffect(() => {
    const opcao = opcaoAtiva.current
    const caixa = opcao?.closest('[data-rolagem]')
    if (!caixa) return
    const item = opcao.getBoundingClientRect()
    const janela = caixa.getBoundingClientRect()
    // Só a lista acompanha o destaque; scrollIntoView também deslocaria as molduras.
    if (item.top < janela.top) caixa.scrollTop += item.top - janela.top
    else if (item.bottom > janela.bottom) caixa.scrollTop += item.bottom - janela.bottom
  }, [destaque, itens])

  // O Editar é irmão da opção, não filho (botão dentro de role="option" é interativo aninhado), e a lista visual não
  // tem papel: o listbox e os grupos ficam em elementos vazios que assumem as opções por aria-owns, e assim o
  // listbox só tem group/option (ARIA 1.2) enquanto o Editar segue na linha (#3208).
  const opcao = (r, i) => (
    <li key={r.id} className={`${css.itemResposta} ${i === destaque ? css.ativa : ''}`}>
      <div
        id={idDe(i)}
        role="option"
        ref={i === destaque ? opcaoAtiva : undefined}
        aria-selected={i === destaque}
        className={css.opcaoResposta}
        onMouseEnter={() => seletor.destacar(i)}
        // Não tira o foco do campo antes do clique valer.
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => seletor.inserir(r, aoInserido)}
      >
        <span className={css.titulo}>{r.titulo}</span>
        <span className={css.atalho}>{r.atalho}</span>
        <span className={css.previa}>{r.texto}</span>
      </div>
      {editavel && (
        <Botao
          variante="texto" icone="lapis" className={css.editar} aria-label={`Editar resposta ${r.titulo}`}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => abrirForm(r.id)}
        >Editar</Botao>
      )}
    </li>
  )

  return (
    <Popover rotulo="Respostas rápidas" papel="group" aoFechar={fechar} semAutoFoco portal>
      <div className={css.seletor}>
        {editando !== null && emEdicao ? (
          <FormResposta
            key={editando}
            className={css.formNoPainel}
            inicial={emEdicao}
            regras={REGRAS_DO_FORM}
            categorias={categorias}
            erro={recusou ? (sincronizacao?.aviso ?? 'Não foi possível salvar a resposta.') : null}
            aoSalvar={gravar}
            aoCancelar={() => setEditando(null)}
            aoMudarAlterado={travar}
            pedidoDescarte={pedidoDescarte}
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
                aria-keyshortcuts={editavel ? 'F2' : undefined}
                onKeyDown={(e) => {
                  // F2 edita a resposta ativa (o botão Editar fica fora da lista de opções).
                  if (e.key === 'F2' && editavel && itens[destaque]) { e.preventDefault(); e.stopPropagation(); abrirForm(itens[destaque].id); return }
                  seletor.aoTeclar(e, aoInserido)
                }}
              />
            )}

            {itens.length === 0 ? (
              <p className={css.vazio}>
                {seletor.temRespostas ? 'Nenhuma resposta com esse termo.' : 'Nenhuma resposta pronta cadastrada ainda.'}
              </p>
            ) : (
              <>
                <ul data-rolagem className={css.lista}>
                  {agrupado ? grupos.map((g, n) => (
                    <li key={g.chave ?? 'sem-categoria'}>
                      <p id={`${idLista}-grupo-${n}`} aria-hidden="true" className={css.grupo}>{g.rotulo}</p>
                      <ul className={css.grupoLista}>
                        {g.itens.map(({ resposta, indice }) => opcao(resposta, indice))}
                      </ul>
                    </li>
                  )) : itens.map((r, i) => opcao(r, i))}
                </ul>
                <div className={css.posse}>
                  <div
                    id={idLista} role="listbox" aria-label="Respostas prontas" aria-keyshortcuts={editavel ? 'F2' : undefined}
                    aria-owns={agrupado ? grupos.map((g, n) => `${idLista}-g${n}`).join(' ') : itens.map((r, i) => idDe(i)).join(' ')}
                  />
                  {agrupado && grupos.map((g, n) => (
                    <div
                      key={g.chave ?? 'sem-categoria'} id={`${idLista}-g${n}`} role="group" aria-labelledby={`${idLista}-grupo-${n}`}
                      aria-owns={g.itens.map(({ indice }) => idDe(indice)).join(' ')}
                    />
                  ))}
                </div>
              </>
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
