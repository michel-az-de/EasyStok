import { useEffect, useRef } from 'react'
import css from './cozinha.module.css'

// #3014: no celular o quadro é um carrossel de uma coluna por tela e as contagens das outras ficavam fora da
// vista. A faixa mostra nome e contagem de todas, sempre; tocar leva o carrossel até a coluna. Mesma faixa para a
// demonstração e para a tela da API. Oculta fora do celular (a regra está no CSS).
const reduzMovimento = () => typeof window !== 'undefined'
  && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

const frasePedidos = (n) => `${n} ${n === 1 ? 'pedido' : 'pedidos'}`

// itens: [{ id, rotulo, contagem }]. Cada coluna do quadro leva data-coluna={id}.
export function FaixaColunas({ itens }) {
  const faixa = useRef(null)
  const chave = itens.map((i) => i.id).join('|')

  // Marca o botão da coluna à vista direto no DOM (aria-current), sem estado duplicado.
  useEffect(() => {
    const quadro = faixa.current?.nextElementSibling
    if (!quadro || typeof IntersectionObserver === 'undefined') return undefined
    const marcar = (id, aVista) => {
      const botao = faixa.current?.querySelector(`[data-alvo="${id}"]`)
      if (!botao) return
      if (aVista) botao.setAttribute('aria-current', 'true')
      else botao.removeAttribute('aria-current')
    }
    const olho = new IntersectionObserver((entradas) => {
      entradas.forEach((e) => marcar(e.target.dataset.coluna, e.isIntersecting))
    }, { root: quadro, threshold: 0.6 })
    quadro.querySelectorAll('[data-coluna]').forEach((no) => olho.observe(no))
    return () => olho.disconnect()
  }, [chave])

  const ir = (id) => {
    const alvo = faixa.current?.nextElementSibling?.querySelector(`[data-coluna="${id}"]`)
    alvo?.scrollIntoView({ behavior: reduzMovimento() ? 'auto' : 'smooth', inline: 'start', block: 'nearest' })
  }

  return (
    <nav ref={faixa} className={css.faixaColunas} aria-label="Colunas do quadro">
      {itens.map(({ id, rotulo, contagem }) => (
        <button
          key={id} type="button" className={css.faixaItem} data-alvo={id}
          aria-label={`${rotulo}, ${frasePedidos(contagem)}`} onClick={() => ir(id)}
        >
          <span className={css.faixaContagem} aria-hidden="true">{contagem}</span>
          <span className={css.faixaRotulo} aria-hidden="true">{rotulo}</span>
        </button>
      ))}
    </nav>
  )
}
