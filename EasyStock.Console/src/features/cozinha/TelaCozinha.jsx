import { useRelogio } from '../../hooks/useRelogio'
import { horaCurta } from '../../dominio/formato'
import { estaAberta } from '../../dominio/funcionamento'
import { useEspelhoDeCozinha } from '../../aplicacao/useEspelhoDeCozinha'
import { useAvisoSonoro } from '../../aplicacao/useAvisoSonoro'
import { ConteudoCozinha } from './ConteudoCozinha'
import css from './cozinha.module.css'

// A janela de Cozinha (US-037, US-038, o tablet fixado na parede, D6):
// `window.open('#/cozinha', ...)` abre isto SEM `AtendimentoProvider` por
// perto, mesmo desenho de `features/entregas/TelaEntregas.jsx` (rodada 5,
// seção 6, usado como modelo) — espelho do Balcão pelo mesmo
// `infra/canalEntreJanelas.js`, nunca uma segunda árvore de estado. Sem
// resposta do Balcão em 1 s, mostra só o aviso: D6/D8, a tela nunca finge um
// estado que não chegou, e sem rede ela produz pelo canhoto de papel mesmo.
export function TelaCozinha() {
  const { estado, semBalcao, acoes } = useEspelhoDeCozinha()

  if (!estado) {
    return semBalcao
      ? (
        <div className={css.pagina}>
          <p className={css.aviso}>Abra o balcão para a cozinha andar.</p>
        </div>
      )
      : null
  }

  return <TelaCozinhaPronta estado={estado} acoes={acoes} />
}

function TelaCozinhaPronta({ estado, acoes }) {
  const agoraLocal = useRelogio(estado.catalogo.instanteInicial)
  const agora = agoraLocal + (estado.relogio?.deslocamentoMs ?? 0)
  // Mesma conta do Balcão (`AtendimentoProvider.jsx`), com os mesmos dois
  // campos que já viajam no estado espelhado: "Precisa de você" e o aviso
  // sonoro precisam da MESMA resposta em toda janela.
  const aberta = estaAberta(agora, { funcionamento: estado.funcionamento, lojaAberta: estado.lojaAberta })

  // Achado 1 (rodada 10, P0): som e destaque também nesta janela, ouvindo o
  // MESMO estado que `conectarEspelho` já entrega, sem abrir conversa nenhuma.
  const destacados = useAvisoSonoro({
    conversas: estado.conversas, som: estado.som, agora, aberta, janelas: estado.catalogo.janelas,
  })

  const acoesComAgora = {
    avancarEsteira: (id, passo, entregador) => acoes.avancarEsteira(id, passo, agora, entregador),
  }

  return (
    <div className={css.pagina}>
      <header className={css.topoPagina}>
        <h1>Cozinha</h1>
        <span className={css.horaTopo}>{horaCurta(new Date(agora).toISOString())}</span>
      </header>
      <ConteudoCozinha
        conversas={estado.conversas}
        janelas={estado.catalogo.janelas}
        cardapio={estado.catalogo.cardapio}
        linhas={estado.catalogo.linhas}
        entregadores={estado.catalogo.entregadores}
        agora={agora}
        acoes={acoesComAgora}
        destacados={destacados}
      />
    </div>
  )
}
