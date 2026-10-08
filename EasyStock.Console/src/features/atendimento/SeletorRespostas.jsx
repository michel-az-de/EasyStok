import { useId } from 'react'
import { Popover } from '../../componentes/Popover'
import css from './seletorRespostas.module.css'

// Lista compacta de respostas prontas acima do compositor (#1441). Uma linha por
// resposta: título, atalho e o começo do texto. Sem botão por linha: clicar ou Enter
// insere no campo. Quem cria, edita e arquiva é a Gestão ("Gerenciar respostas").
export function SeletorRespostas({ seletor, aoGerenciar }) {
  const idLista = useId()
  const idDe = (indice) => `${idLista}-${indice}`
  const { itens, destaque } = seletor

  return (
    <Popover rotulo="Respostas rápidas" aoFechar={seletor.fechar} semAutoFoco portal>
      <div className={css.seletor}>
        {!seletor.pelaBarra && (
          <input
            autoFocus
            className={css.busca}
            type="search"
            aria-label="Buscar resposta pronta"
            aria-controls={idLista}
            aria-activedescendant={itens[destaque] ? idDe(destaque) : undefined}
            placeholder="Buscar por título, atalho ou texto"
            value={seletor.busca}
            onChange={(e) => seletor.mudarBusca(e.target.value)}
            onKeyDown={(e) => { seletor.aoTeclar(e) }}
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
                aria-selected={i === destaque}
                className={`${css.linha} ${i === destaque ? css.ativa : ''}`}
                onMouseEnter={() => seletor.destacar(i)}
                // Não tira o foco do campo antes do clique valer.
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => seletor.inserir(r)}
              >
                <span className={css.titulo}>{r.titulo}</span>
                <span className={css.atalho}>{r.atalho}</span>
                <span className={css.previa}>{r.texto}</span>
              </li>
            ))}
          </ul>
        )}

        <div className={css.rodape}>
          <span className={css.dica}>↑↓ escolhe · Enter insere · Esc fecha</span>
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
