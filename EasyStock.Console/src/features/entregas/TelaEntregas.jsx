import { useRelogio } from '../../hooks/useRelogio'
import { horaCurta } from '../../dominio/formato'
import { estaAberta } from '../../dominio/funcionamento'
import { useEspelhoDeEntregas } from '../../aplicacao/useEspelhoDeEntregas'
import { useAvisoSonoro } from '../../aplicacao/useAvisoSonoro'
import { ConteudoEntregas } from './ConteudoEntregas'
import css from './dashboard.module.css'

// A janela de Entregas (rodada 5, seção 6): `window.open('#/entregas', ...)`
// abre isto SEM `AtendimentoProvider` por perto — ela é um espelho do estado
// do Balcão (`useEspelhoDeEntregas`, que fala com
// `infra/canalEntreJanelas.js`), nunca uma segunda árvore de estado. Sem
// resposta do Balcão em 1 s, mostra só a faixa de aviso (seção 6, "Tela sem
// estado não finge estado"). `TelaEntregasPronta` só nasce depois que o
// primeiro estado chega, para o relógio local (`useRelogio`) partir do MESMO
// instante do Balcão (`estado.catalogo.instanteInicial`), sem depender de um
// segundo mount para corrigir o valor inicial.
export function TelaEntregas() {
  const { estado, semBalcao, acoes } = useEspelhoDeEntregas()

  if (!estado) {
    return semBalcao
      ? (
        <div className={css.pagina}>
          <p className={css.aviso}>Abra o balcão para as entregas andarem.</p>
        </div>
      )
      : null
  }

  return <TelaEntregasPronta estado={estado} acoes={acoes} />
}

function TelaEntregasPronta({ estado, acoes }) {
  const agoraLocal = useRelogio(estado.catalogo.instanteInicial)
  const agora = agoraLocal + (estado.relogio?.deslocamentoMs ?? 0)
  const aberta = estaAberta(agora, { funcionamento: estado.funcionamento, lojaAberta: estado.lojaAberta })

  // Achado 1 (rodada 10, P0): mesmo som e destaque da Cozinha, nesta janela.
  const destacados = useAvisoSonoro({
    conversas: estado.conversas, som: estado.som, agora, aberta, janelas: estado.catalogo.janelas,
  })

  const acoesComAgora = {
    ...acoes,
    avancarEsteira: (id, passo, entregador) => acoes.avancarEsteira(id, passo, agora, entregador),
    cancelarPedido: (id, texto) => acoes.cancelarPedido(id, texto, agora),
    marcarEstorno: (id) => acoes.marcarEstorno(id, agora),
  }

  return (
    <div className={css.pagina}>
      <header className={css.topoPagina}>
        <h1>Entregas de hoje</h1>
        <span className={css.horaTopo}>{horaCurta(new Date(agora).toISOString())}</span>
      </header>
      <ConteudoEntregas
        conversas={estado.conversas}
        janelas={estado.catalogo.janelas}
        agora={agora}
        cardapio={estado.catalogo.cardapio}
        enderecoDaCasa={estado.catalogo.enderecoDaCasa}
        entregadores={estado.catalogo.entregadores}
        integracoesLogistica={estado.catalogo.integracoesLogistica}
        constantes={{
          minutosTrecho: estado.catalogo.minutosTrecho,
          minutosChegadaEntregador: estado.catalogo.minutosChegadaEntregador,
          minutosConferir: estado.catalogo.minutosConferir,
        }}
        acoes={acoesComAgora}
        destacados={destacados}
      />
    </div>
  )
}
