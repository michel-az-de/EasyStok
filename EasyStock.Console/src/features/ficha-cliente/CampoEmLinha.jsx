import { useEffect, useRef, useState } from 'react'
import { Icone } from '../../componentes/Icone'
import { mascaraTelefone } from '../../dominio/formato'
import css from './cliente.module.css'

const SEGUNDOS_SALVOU = 2

// Edição em linha de nome, telefone e endereço (seção 2 da direção visual).
// Em repouso o valor é um botão com cara de texto; o toque vira campo com
// dois botões de ícone. `tipo="telefone"` aplica a máscara enquanto digita
// (mascaraTelefone é puro, dominio/formato.js: feature pode importar dominio
// direto, diferente de componentes/CampoMascarado.jsx). `validar` decide se
// salva; devolve a mensagem de erro ou `null`.
export function CampoEmLinha({
  rotulo, valor, vazio = 'Sem valor', tipo = 'texto', validar, aoSalvar,
}) {
  const [editando, setEditando] = useState(false)
  const [rascunho, setRascunho] = useState('')
  const [erro, setErro] = useState(null)
  const [salvou, setSalvou] = useState(false)
  const inputRef = useRef(null)

  useEffect(() => {
    if (!editando) return
    inputRef.current?.focus()
    inputRef.current?.select()
  }, [editando])

  useEffect(() => {
    if (!salvou) return undefined
    const t = setTimeout(() => setSalvou(false), SEGUNDOS_SALVOU * 1000)
    return () => clearTimeout(t)
  }, [salvou])

  function abrir() {
    setRascunho(valor ?? '')
    setErro(null)
    setEditando(true)
  }

  function cancelar() {
    setEditando(false)
    setErro(null)
  }

  function confirmar() {
    const mensagem = validar?.(rascunho) ?? null
    if (mensagem) { setErro(mensagem); return }
    setEditando(false)
    setErro(null)
    if (rascunho !== (valor ?? '')) {
      aoSalvar(rascunho)
      setSalvou(true)
    }
  }

  function aoDigitar(evento) {
    const bruto = evento.target.value
    setRascunho(tipo === 'telefone' ? mascaraTelefone(bruto) : bruto)
  }

  function aoTeclar(evento) {
    if (evento.key === 'Enter') confirmar()
    if (evento.key === 'Escape') cancelar()
  }

  if (!editando) {
    return (
      <div className={css.campoEmLinha}>
        <button type="button" className={css.valorBotao} onClick={abrir} aria-label={`Editar ${rotulo}`}>
          <span className={valor ? undefined : css.valorVazio}>{valor || vazio}</span>
          <Icone nome="lapis" tamanho={20} />
        </button>
        {salvou && (
          <output className={css.salvo}>
            <Icone nome="check" tamanho={20} /> Salvo
          </output>
        )}
      </div>
    )
  }

  return (
    <div className={css.campoEmLinha}>
      <div className={css.edicaoEmLinha}>
        <label className="sr" htmlFor={`campo-${rotulo}`}>{rotulo}</label>
        <input
          id={`campo-${rotulo}`}
          ref={inputRef}
          className={[css.controleEmLinha, erro ? css.controleComErro : ''].filter(Boolean).join(' ')}
          value={rascunho}
          onChange={aoDigitar}
          onKeyDown={aoTeclar}
          onBlur={confirmar}
          aria-invalid={Boolean(erro) || undefined}
        />
        <button
          type="button"
          className={css.botaoIconeEdicao}
          aria-label="Salvar"
          onClick={confirmar}
        >
          <Icone nome="check" tamanho={20} />
        </button>
        <button
          type="button"
          className={css.botaoIconeEdicao}
          aria-label="Desfazer"
          onMouseDown={(e) => e.preventDefault()}
          onClick={cancelar}
        >
          <Icone nome="x" tamanho={20} />
        </button>
      </div>
      {erro && <p className={css.erroEmLinha} role="alert">{erro}</p>}
    </div>
  )
}
