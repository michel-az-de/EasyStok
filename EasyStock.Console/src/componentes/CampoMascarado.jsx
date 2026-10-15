import { useId } from 'react'
import { cursorDeMoeda, digitosDeMoeda } from './moedaNoCampo'
import css from './CampoMascarado.module.css'

// Campo para dado que só existe em dígito por dentro: moeda, telefone e cep
// (seção 2 e 4 da direção visual, passo zero). A MÁSCARA (mostrar) e o
// PARSE (colar) são do domínio, em `dominio/formato.js`
// (mascaraMoeda/mascaraTelefone/mascaraCep e lerMoeda): a fronteira de
// camada não deixa `componentes` importar `dominio` (verificar-camadas.mjs),
// então quem monta a tela — lá em cima, na feature — calcula `valor` já
// mascarado e decide o que fazer com os dígitos crus e o texto colado.
//
// `tipo` só liga o comportamento de colar: em "moeda" o colado pode trazer
// "R$ 68,50" ou "68,5" e precisa de `lerMoeda`, então o componente intercepta
// e devolve o texto cru para quem chama decidir; em telefone e cep colar é
// igual a digitar, o navegador já mistura o texto no campo sozinho.
//
// Sem `maxLength` nunca (seção 2): ele corta o que foi colado sem avisar.
//
// #1595: em "moeda" a tecla conta sempre como último dígito (`digitosDeMoeda`) e o cursor volta
// para o fim (`cursorDeMoeda`), que é onde ela entra. Telefone e cep não: neles corrigir um
// dígito no meio faz sentido.
function manterCursorNoFim(evento) {
  const campo = evento.target
  const destino = cursorDeMoeda({ inicio: campo.selectionStart, fim: campo.selectionEnd, tamanho: campo.value.length })
  if (destino !== null) campo.setSelectionRange(destino, destino)
}

export function CampoMascarado({
  tipo, rotulo, rotuloOculto, dica, erro, valor, aoMudarDigitos, aoColarTexto, id: idInformado, className, ...resto
}) {
  const idGerado = useId()
  const id = idInformado ?? idGerado

  function aoDigitar(evento) {
    const texto = evento.target.value
    aoMudarDigitos(tipo === 'moeda' ? digitosDeMoeda(String(valor ?? ''), texto) : texto.replace(/\D/g, ''))
  }

  function aoColar(evento) {
    if (tipo !== 'moeda' || !aoColarTexto) return
    evento.preventDefault()
    aoColarTexto(evento.clipboardData.getData('text'))
  }

  return (
    <div className={css.campo}>
      <label className={rotuloOculto ? 'sr' : css.rotulo} htmlFor={id}>{rotulo}</label>
      <input
        id={id}
        inputMode="numeric"
        data-tipo={tipo}
        className={[css.controle, erro ? css.erro : '', className].filter(Boolean).join(' ')}
        value={valor}
        onChange={aoDigitar}
        onPaste={aoColar}
        onSelect={tipo === 'moeda' ? manterCursorNoFim : undefined}
        aria-invalid={Boolean(erro) || undefined}
        {...resto}
        aria-describedby={[resto['aria-describedby'], erro ? `${id}-erro` : dica ? `${id}-dica` : null].filter(Boolean).join(' ') || undefined}
      />
      {erro && <p id={`${id}-erro`} className={css.erroTexto} role="alert">{erro}</p>}
      {!erro && dica && <p id={`${id}-dica`} className={css.dica}>{dica}</p>}
    </div>
  )
}
