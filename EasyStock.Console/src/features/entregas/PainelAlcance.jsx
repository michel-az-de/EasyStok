import { useState } from 'react'
import { Chip } from '../../componentes/Chip'
import { PERIODOS_DE_ALCANCE, alcancePorBairro } from '../../dominio/alcance'
import { moeda, plural } from '../../dominio/formato'
import css from './dashboard.module.css'

// Issue #17 (feedback da Thatiane): onde a casa vende mais e onde vende
// menos, por bairro, com período. A conta é de `dominio/alcance.js` (histórico
// de pedidos de cada cliente mais o pedido vivo de hoje); aqui só desenha.
// Barra com o número escrito ao lado: cor e comprimento nunca informam
// sozinhos.
function Variacao({ atual, anterior }) {
  if (anterior == null) return null
  const diferenca = atual - anterior
  const texto = diferenca === 0
    ? 'igual ao período anterior'
    : `${diferenca > 0 ? '+' : ''}${diferenca} vs. período anterior`
  return <span className={diferenca < 0 ? css.alcanceCaiu : css.alcanceVariacao}>{texto}</span>
}

export function PainelAlcance({ conversas, agora, cardapio }) {
  const [periodo, setPeriodo] = useState('30')
  const escolhido = PERIODOS_DE_ALCANCE.find((p) => p.valor === periodo)
  const { bairros, totalPedidos, totalValor } = alcancePorBairro(conversas, { agora, dias: escolhido.dias, cardapio })
  const maior = Math.max(1, ...bairros.map((b) => b.pedidos))

  return (
    <section className={css.alcance} aria-labelledby="titulo-alcance">
      <div className={css.alcanceTopo}>
        <h2 id="titulo-alcance">Alcance por bairro</h2>
        <p className={css.proximaConferencia}>
          {plural(totalPedidos, 'pedido', 'pedidos')} · {moeda(totalValor)} no período, sem cancelados
        </p>
      </div>
      <div className={css.linhaChips} role="radiogroup" aria-label="Período do alcance">
        <strong>Período:</strong>
        {PERIODOS_DE_ALCANCE.map((p) => (
          <Chip key={p.valor} papel="escolha" ativo={periodo === p.valor} onClick={() => setPeriodo(p.valor)}>
            {p.rotulo}
          </Chip>
        ))}
      </div>
      <ol className={css.alcanceLista}>
        {bairros.map((b, indice) => (
          <li key={b.bairro} className={css.alcanceLinha}>
            <span className={css.alcanceBairro}>
              {b.bairro}
              {indice === 0 && b.pedidos > 0 && <span className={css.alcanceMarca}>vende mais</span>}
              {indice === bairros.length - 1 && bairros.length > 1 && (
                <span className={css.alcanceMarca}>{b.pedidos === 0 ? 'sem venda' : 'vende menos'}</span>
              )}
            </span>
            <span className={css.alcanceBarra} aria-hidden="true">
              <span style={{ width: `${(b.pedidos / maior) * 100}%` }} />
            </span>
            <span className={css.alcanceNumeros}>
              {plural(b.pedidos, 'pedido', 'pedidos')} · {moeda(b.total)} · {plural(b.clientes, 'cliente', 'clientes')}
              {' '}<Variacao atual={b.pedidos} anterior={b.anterior} />
            </span>
          </li>
        ))}
      </ol>
    </section>
  )
}
