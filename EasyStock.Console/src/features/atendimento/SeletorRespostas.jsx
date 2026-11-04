import { useId, useLayoutEffect, useRef } from 'react'
import { Popover } from '../../componentes/Popover'
import css from './seletorRespostas.module.css'

// Lista compacta de respostas prontas acima do compositor (#1441). Uma linha por
// resposta: título, atalho e o começo do texto. Sem botão por linha: clicar ou Enter
// insere no campo. Quem cria, edita e arquiva é a Gestão ("Gerenciar respostas").
// `aoInserido` (#1587): chamado depois que a resposta entra no campo, pelo clique ou pelo Enter
// da busca; o Composer devolve o foco ao campo de texto.
export function SeletorRespostas({ seletor, aoGerenciar, aoInserido, idLista: idInformado }) {
  const idGerado = useId()
  const idLista = idInformado ?? idGerado
  const idDe = (indice) => `${idLista}-${indice}`
  const { itens, destaque } = seletor
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

  return (
    <Popover rotulo="Respostas rápidas" papel="group" aoFechar={seletor.fechar} semAutoFoco portal>
      <div className={css.seletor}>
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
            placeholder="Buscar por título, atalho ou texto"
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
            {itens.map((r, i) => (
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
                <span className={css.atalho}>{r.atalho}</span>
                <span className={css.previa}>{r.texto}</span>
              </li>
            ))}
          </ul>
        )}

        <div className={css.rodape}>
          <span className={css.dica}>Enter insere no rascunho</span>
          <button
            type="button"
            className={css.gerenciar}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => { seletor.fechar(); aoGerenciar?.() }}
          >
            Gerenciar respostas
          </button>
        </div>
      </div>
    </Popover>
  )
}
