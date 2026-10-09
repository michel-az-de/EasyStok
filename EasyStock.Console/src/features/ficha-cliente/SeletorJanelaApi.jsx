import { useEffect, useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Icone } from '../../componentes/Icone'
import { useAcoes } from '../../aplicacao/contextos'
import css from './ficha.module.css'

// Janelas no modo API (F03): as da vitrine com vaga que respeitam o prazo dos itens da
// comanda (S16), já filtradas pelo EasyStok. Diferente do seletor da demonstração, a janela
// tem data e a lotação é a do servidor; cheia não aparece porque não dá para reservar.
export function SeletorJanelaApi({ pedido, editavel, aoEscolher }) {
  const { carregarJanelasComanda } = useAcoes()
  const [janelas, setJanelas] = useState(null)
  const [erro, setErro] = useState(null)
  // Recarrega quando muda o que entra no prazo (os itens), não a cada quantidade.
  const skus = [...new Set(pedido.itens.map((l) => l.sku))].sort().join(',')

  useEffect(() => {
    let vivo = true
    carregarJanelasComanda(skus ? skus.split(',') : [])
      .then((lidas) => {
        if (!vivo) return
        setJanelas(lidas.janelas)
        setErro(lidas.lojaDisponivel ? null : 'A empresa não tem vitrine ativa para entrega.')
      })
      .catch((e) => { if (vivo) setErro(`Janelas não carregaram: ${e.message}`) })
    return () => { vivo = false }
  }, [skus, carregarJanelasComanda])

  return (
    <Bloco titulo="Janela de entrega">
      {janelas === null && !erro && <p className={css.corpoBloco}>Carregando janelas…</p>}
      {janelas?.length === 0 && !erro && (
        <p className={css.corpoBloco}>Nenhuma janela com vaga no prazo dos itens nos próximos dias.</p>
      )}
      {janelas?.length > 0 && (
        <ul className={css.janelas}>
          {janelas.map((janela) => {
            const escolhida = pedido.janela === janela.id
            return (
              <li key={janela.id} className={css.janela}>
                <button
                  type="button"
                  className={css.escolhaJanela}
                  aria-pressed={escolhida}
                  disabled={!editavel}
                  onClick={() => aoEscolher(janela.id)}
                >
                  <span className={css.faixaJanela}>
                    {janela.rotulo}
                    {escolhida && <Icone nome="check" rotulo="Janela escolhida" />}
                  </span>
                  <span className={css.vagas}>
                    {janela.vagas} {janela.vagas === 1 ? 'vaga livre' : 'vagas livres'} de {janela.capacidade}
                  </span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {erro && <output className={css.avisoErro} role="alert">{erro}</output>}
    </Bloco>
  )
}
