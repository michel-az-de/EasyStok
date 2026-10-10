import { useEffect, useState } from 'react'
import { Bloco } from '../../componentes/Bloco'
import { Icone } from '../../componentes/Icone'
import { useAcoes } from '../../aplicacao/contextos'
import { useReagendamentoPedido } from '../../aplicacao/useReagendamentoPedido'
import { EscolhaJanelaPedido } from '../../componentes/EscolhaJanelaPedido'
import css from './ficha.module.css'

// Rascunho escolhe a janela localmente; pedido criado troca a vaga no servidor (M3.4).
// Ambos usam vagas da vitrine que respeitam o prazo de preparo (S16).
export function SeletorJanelaApi({ pedido, editavel, aoEscolher }) {
  if (pedido.pedidoId) return <ReagendamentoPedido key={pedido.pedidoId} pedidoId={pedido.pedidoId} aoEscolher={aoEscolher} />
  return <EscolhaRascunho pedido={pedido} editavel={editavel} aoEscolher={aoEscolher} />
}

function ReagendamentoPedido({ pedidoId, aoEscolher }) {
  const campos = useReagendamentoPedido(pedidoId, aoEscolher)
  return <Bloco titulo="Janela de entrega"><EscolhaJanelaPedido {...campos} /></Bloco>
}

function EscolhaRascunho({ pedido, editavel, aoEscolher }) {
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
