import { useEffect, useId, useState } from 'react'
import { CampoTexto } from './Campo'
import css from './Combobox.module.css'

// Os resultados chegam da consulta da tela. Enter escolhe uma entidade; texto livre continua sendo busca.
export function BuscaComSugestoes({ valor, aoMudar, opcoes, aoEscolher, carregando, placeholder, rotulo = 'Buscar', rotuloLista = 'Cadastros encontrados', textoCarregando = 'Consultando cadastros…', minimo = 3 }) {
  const id = useId()
  const [foco, focar] = useState(false)
  const [dispensado, dispensar] = useState(false)
  const [indice, ativar] = useState(0)
  const aberta = foco && !dispensado && valor.trim().length >= minimo && (carregando || opcoes.length > 0)
  const ativo = Math.min(indice, Math.max(0, opcoes.length - 1))
  useEffect(() => { if (aberta && opcoes.length) document.getElementById(`${id}-${ativo}`)?.scrollIntoView({ block: 'nearest' }) }, [aberta, ativo, id, opcoes.length])
  const escolher = (opcao) => { if (!opcao || carregando) return; dispensar(true); aoEscolher(opcao.item) }
  function teclado(e) {
    if (!aberta) return
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); dispensar(true) }
    if (carregando) { if (e.key === 'Enter') e.preventDefault(); return }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); ativar(Math.max(0, Math.min(opcoes.length - 1, ativo + (e.key === 'ArrowDown' ? 1 : -1)))) }
    if (e.key === 'Enter' && opcoes.length) { e.preventDefault(); escolher(opcoes[ativo]) }
  }
  return <div className={css.campo}>
    <CampoTexto rotulo={rotulo} value={valor} placeholder={placeholder} role="combobox" aria-autocomplete="list" aria-expanded={aberta}
      aria-controls={`${id}-lista`} aria-activedescendant={aberta && !carregando && opcoes.length ? `${id}-${ativo}` : undefined}
      onFocus={() => { focar(true); dispensar(false) }} onBlur={() => focar(false)} onKeyDown={teclado} onChange={e => { dispensar(false); ativar(0); aoMudar(e.target.value) }} />
    {aberta && <ul id={`${id}-lista`} role="listbox" aria-label={rotuloLista} className={css.lista}>
      {carregando ? <li role="presentation" className={css.vazio}>{textoCarregando}</li> : opcoes.map((o, i) => <li key={o.id} id={`${id}-${i}`} role="option" aria-selected={ativo === i} className={ativo === i ? css.ativa : undefined}
        onMouseDown={e => e.preventDefault()} onMouseEnter={() => ativar(i)} onClick={() => escolher(o)}><span><strong>{o.rotulo}</strong>{o.descricao && <small>{o.descricao}</small>}</span></li>)}
    </ul>}
  </div>
}
