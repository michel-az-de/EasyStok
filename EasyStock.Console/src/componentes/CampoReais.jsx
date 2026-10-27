import { useId, useState } from 'react'
import { centavosCortados, textoDigitadoEmReais, textoDosCentavos } from './moedaNoCampo'
import base from './CampoMascarado.module.css'
import css from './CampoReais.module.css'

// J2 (#1990): campo de dinheiro do caixa, digitado em reais. No laboratório (23/10) a máscara de
// centavos do `CampoMascarado` ("100" vira R$ 1,00) abriu o caixa com R$ 1,00 e fechou "58" como
// R$ 0,58. Aqui o campo guarda o texto como ela digitou ("100", "58,50") e devolve os centavos
// lidos por `ler` (a tela passa `lerMoeda`, do domínio: a fronteira de camada não deixa
// `componentes` importar `dominio`). Ao sair do campo o valor volta escrito por inteiro.
//
// `centavos` nulo é campo vazio; `aoMudar` recebe nulo quando ela apaga tudo.
export function CampoReais({
  rotulo, rotuloOculto, dica, erro, centavos, aoMudar, ler, id: idInformado, className, onBlur, placeholder = '0,00', ...resto
}) {
  const idGerado = useId()
  const id = idInformado ?? idGerado
  // Zero aparece vazio (com "0,00" de exemplo): o campo que nascia "0,00" fazia a tecla cair depois
  // dos centavos e não entrar.
  const mostrar = (valor) => (valor ? textoDosCentavos(valor) : '')
  const [texto, setTexto] = useState(() => mostrar(centavos))
  // #2080: "1,500" não cabe (centavos têm 2 casas): o campo diz o que ficou em vez de calar.
  const [cortou, setCortou] = useState(false)
  const lido = (valor) => (valor.trim() ? ler(valor) : null)

  // Valor trocado por fora (zerou depois de lançar, a sugestão chegou): o texto acompanha. O que
  // ela mesma digitou não volta formatado no meio da digitação.
  const [anterior, setAnterior] = useState(centavos)
  if (centavos !== anterior) {
    setAnterior(centavos)
    if ((lido(texto) ?? 0) !== (centavos ?? 0)) setTexto(mostrar(centavos))
  }

  function aoDigitar(evento) {
    const novo = textoDigitadoEmReais(evento.target.value)
    setCortou(centavosCortados(evento.target.value))
    setTexto(novo)
    aoMudar(lido(novo))
  }

  function aoSair(evento) {
    if (texto.trim()) setTexto(textoDosCentavos(ler(texto)))
    onBlur?.(evento)
  }

  return (
    <div className={base.campo}>
      <label className={rotuloOculto ? 'sr' : base.rotulo} htmlFor={id}>{rotulo}</label>
      <div className={css.envelope}>
        <span className={css.prefixo} aria-hidden="true">R$</span>
        <input
          id={id}
          inputMode="decimal"
          data-tipo="moeda"
          className={[base.controle, css.controle, erro ? base.erro : '', className].filter(Boolean).join(' ')}
          value={texto}
          placeholder={placeholder}
          onChange={aoDigitar}
          onBlur={aoSair}
          aria-invalid={Boolean(erro) || undefined}
          {...resto}
          aria-describedby={[resto['aria-describedby'], erro ? `${id}-erro` : dica ? `${id}-dica` : null].filter(Boolean).join(' ') || undefined}
        />
      </div>
      {erro && <p id={`${id}-erro`} className={base.erroTexto} role="alert">{erro}</p>}
      {!erro && cortou && <output className={base.dica}>Centavos têm 2 casas: ficou R$ {texto}.</output>}
      {!erro && dica && <p id={`${id}-dica`} className={base.dica}>{dica}</p>}
    </div>
  )
}
