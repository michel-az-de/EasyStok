import { useEffect, useId, useRef, useState } from 'react'
import { Icone } from './Icone'
import css from './Combobox.module.css'

const normalizar = (s) => String(s ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR')

// Uma seleção, com busca, mantém o foco no campo e anuncia a opção ativa para o leitor de tela.
export function Combobox({ rotulo, valor, opcoes, aoMudar, erro, disabled = false, placeholder = 'Selecione', id: informado }) {
  const gerado = useId(), id = informado ?? gerado
  const campo = useRef(null)
  const [aberto, abrir] = useState(false)
  const [busca, buscar] = useState('')
  const [ativo, ativar] = useState(0)
  const selecionado = opcoes.find(o => String(o.valor) === String(valor))
  const lista = opcoes.filter(o => normalizar(`${o.rotulo} ${o.descricao ?? ''}`).includes(normalizar(busca)))
  const indice = Math.min(ativo, Math.max(0, lista.length - 1))
  useEffect(() => { if (aberto) document.getElementById(`${id}-opcao-${indice}`)?.scrollIntoView({ block: 'nearest' }) }, [aberto, id, indice])
  const escolher = (opcao) => { if (!opcao || opcao.desabilitada) return; aoMudar(opcao.valor); abrir(false); buscar(''); campo.current?.focus() }
  const iniciar = () => { abrir(true); buscar(''); ativar(Math.max(0, opcoes.findIndex(o => String(o.valor) === String(valor)))) }
  const navegar = (evento) => {
    if (evento.key === 'Escape' && aberto) { evento.preventDefault(); evento.stopPropagation(); abrir(false); buscar(''); return }
    if (evento.key === 'Tab') { abrir(false); buscar(''); return }
    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(evento.key)) {
      evento.preventDefault()
      if (!aberto) { iniciar(); return }
      ativar(evento.key === 'Home' ? 0 : evento.key === 'End' ? lista.length - 1 : Math.max(0, Math.min(lista.length - 1, indice + (evento.key === 'ArrowDown' ? 1 : -1))))
    } else if (evento.key === 'Enter' && aberto) { evento.preventDefault(); escolher(lista[indice]) }
  }
  return <div className={css.campo} onBlur={e => { if (!e.currentTarget.contains(e.relatedTarget)) { abrir(false); buscar('') } }}>
    <label className={css.rotulo} htmlFor={id}>{rotulo}</label>
    <div className={css.controle}>
      <input ref={campo} id={id} role="combobox" aria-autocomplete="list" autoComplete="off" aria-expanded={aberto}
        aria-controls={`${id}-opcoes`} aria-activedescendant={aberto && lista.length ? `${id}-opcao-${indice}` : undefined}
        aria-invalid={erro ? true : undefined} aria-describedby={erro ? `${id}-erro` : undefined} disabled={disabled}
        value={aberto ? busca : selecionado?.rotulo ?? ''} placeholder={aberto ? 'Buscar opção' : placeholder}
        onFocus={iniciar} onClick={() => { if (!aberto) iniciar() }} onKeyDown={navegar}
        onChange={e => { buscar(e.target.value); ativar(0); abrir(true) }} />
      <Icone nome="chevron-down" tamanho={20} />
    </div>
    {aberto && <ul id={`${id}-opcoes`} role="listbox" aria-label={rotulo} className={css.lista}>
      {lista.map((o, i) => <li id={`${id}-opcao-${i}`} role="option" key={o.valor} aria-selected={String(o.valor) === String(valor)}
        aria-disabled={o.desabilitada || undefined} className={i === indice ? css.ativa : undefined}
        onMouseDown={e => e.preventDefault()} onMouseEnter={() => ativar(i)} onClick={() => escolher(o)}>
        <span><strong>{o.rotulo}</strong>{o.descricao && <small>{o.descricao}</small>}</span>{String(o.valor) === String(valor) && <Icone nome="check" tamanho={20} />}
      </li>)}
      {!lista.length && <li className={css.vazio} role="presentation">Nenhuma opção encontrada.</li>}
    </ul>}
    {erro && <p className={css.erro} id={`${id}-erro`} role="alert">{erro}</p>}
  </div>
}
