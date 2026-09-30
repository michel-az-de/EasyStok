// Frente 7 · Menu de simulações (rodada 5, seção 7). Gaveta pela esquerda,
// NÃO modal (o Balcão continua clicável ao lado, para a dona ver a
// simulação chegar). Botão "Simular" e o atalho F2 já vêm prontos do passo
// zero (`app/Moldura.jsx`); este arquivo é só o corpo, que nasceu vazio.
import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { useAtendimento } from '../../aplicacao/contextos'
import { horaCurta } from '../../dominio/formato'
import { CENARIOS, GRUPOS } from '../../dominio/simulacoes'
import css from './PainelSimulacoes.module.css'

// Mesma chave que `app/Moldura.jsx` lê (só leitura, lá): o painel ainda não
// existia quando o passo zero escreveu o botão "Simular", então a escrita
// fica para aqui (comentário de `Moldura.jsx`: "o painel ainda não tem esse
// controle, então só a leitura existe").
const CHAVE_SIMULAR_ESCONDIDO = 'casa-da-baba:simular-escondido'

function lerEscondido() {
  try {
    return window.localStorage.getItem(CHAVE_SIMULAR_ESCONDIDO) === '1'
  } catch {
    return false
  }
}

function gravarEscondido(valor) {
  try {
    window.localStorage.setItem(CHAVE_SIMULAR_ESCONDIDO, valor ? '1' : '0')
  } catch {
    // Sem localStorage (aba privada, bloqueio de site): o botão some só
    // nesta sessão, via estado do componente; não é motivo para travar a
    // tela.
  }
}

const GRUPOS_EM_ORDEM = [
  GRUPOS.CHEGANDO, GRUPOS.PEDIDO, GRUPOS.ENTREGA, GRUPOS.PROBLEMAS, GRUPOS.VOLUME,
]

// Rodada 11 (issue #8, registro 92): `roteiro` chega pronto de `app/App.jsx`
// (o hook `useRoteiro` mora lá para sobreviver à gaveta fechada), e iniciar um
// cenário fecha a gaveta: a conversa simulada precisa ficar à vista.
export function PainelSimulacoes({ roteiro, aoFechar }) {
  const { agora } = useAtendimento()
  const {
    cenarioEmCurso, pausado, iniciar, alternarPausa, desfazer, deslocarRelogioSimulado, zerarRelogioSimulado,
  } = roteiro
  const [escondido, setEscondido] = useState(lerEscondido)
  const [confirmandoLimpar, setConfirmandoLimpar] = useState(false)

  const alternarEsconder = (valor) => {
    setEscondido(valor)
    gravarEscondido(valor)
  }

  return (
    <aside className={css.gaveta} aria-label="Simulações">
      <header className={css.cabecalho}>
        <span>Simulações</span>
        <Botao variante="discreto" onClick={aoFechar}>
          <Icone nome="fechar" /> Fechar
        </Botao>
      </header>

      <div className={css.rolavel}>
        <div className={css.relogio}>
          <span className={css.horaAtual}>{horaCurta(new Date(agora).toISOString())}</span>
          <div className={css.botoesRelogio}>
            <Botao onClick={() => deslocarRelogioSimulado(10)}>+10 min</Botao>
            <Botao onClick={() => deslocarRelogioSimulado(30)}>+30 min</Botao>
            <Botao onClick={zerarRelogioSimulado}>Agora</Botao>
          </div>
        </div>

        {cenarioEmCurso && (
          <p className={css.emCurso}>
            <span>
              Simulação: {cenarioEmCurso.titulo}
              {cenarioEmCurso.concluido ? '' : '…'}
            </span>
          </p>
        )}

        {GRUPOS_EM_ORDEM.map((grupo) => {
          const doGrupo = CENARIOS.filter((c) => c.grupo === grupo)
          if (doGrupo.length === 0) return null
          return (
            <section key={grupo} className={css.grupo}>
              <h3>{grupo}</h3>
              <div className={css.listaCenarios}>
                {doGrupo.map((cenario) => (
                  <button
                    key={cenario.id}
                    type="button"
                    className={css.cenario}
                    onClick={() => { iniciar(cenario); aoFechar() }}
                  >
                    <strong>{cenario.titulo}</strong>
                    <span>{cenario.resumo}</span>
                  </button>
                ))}
              </div>
            </section>
          )
        })}

        <label className={css.esconder}>
          <input
            type="checkbox"
            checked={escondido}
            onChange={(e) => alternarEsconder(e.target.checked)}
          />
          Esconder botão (volta com F2)
        </label>
      </div>

      <footer className={css.rodape}>
        <Botao onClick={alternarPausa}>
          <Icone nome={pausado ? 'check' : 'x'} /> {pausado ? 'Retomar' : 'Pausar'}
        </Botao>
        {confirmandoLimpar ? (
          <span className={css.confirmar}>
            Limpar tudo?
            <Botao variante="primario" onClick={() => { desfazer(); setConfirmandoLimpar(false) }}>Sim</Botao>
            <Botao onClick={() => setConfirmandoLimpar(false)}>Não</Botao>
          </span>
        ) : (
          <Botao onClick={() => setConfirmandoLimpar(true)}>Limpar simulações</Botao>
        )}
      </footer>
    </aside>
  )
}
