import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { EscolhaJanelaPedido } from '../../componentes/EscolhaJanelaPedido'
import { useReagendamentoPedido } from '../../aplicacao/useReagendamentoPedido'
import {
  faixaParaCliente, motivoDoBloqueio, ocupacaoDeHoje, proximaLivre, rotuloDaOcupacao,
} from '../../dominio/entrega'
import css from './dashboard.module.css'
import { textoAvisoDeAgendamento } from '../../aplicacao/acoes/entregas'

export function ModalAlterarAgendamentoApi({ pedidoId, nome, aoFechar, aoAlterado }) {
  const campos = useReagendamentoPedido(pedidoId, null, aoAlterado)
  return (
    <Modal titulo={`Alterar agendamento · ${nome}`} aoFechar={() => { if (!campos.enviando) aoFechar() }}>
      <EscolhaJanelaPedido {...campos} />
    </Modal>
  )
}

// Modal "Alterar agendamento" (seção 6): a lista de janelas é a mesma régua
// de `SeletorJanela.jsx` (ficha do cliente), mas reescrita aqui dentro — a
// F2 é dona daquele arquivo, e feature nenhuma importa outra feature
// (`ferramentas/verificar-camadas.mjs`). As funções puras que fazem a conta
// (`dominio/entrega.js`) são as mesmas nos dois lugares.
export function ModalAlterarAgendamento({
  conversa, conversas, janelas, agora, cardapio, aoFechar, aoAlterar,
}) {
  const [escolhida, setEscolhida] = useState(conversa.pedido.janela)
  const [avisar, setAvisar] = useState(true)
  const ocupacoes = ocupacaoDeHoje(janelas, conversas, agora, conversa.pedido.janela, cardapio)
  const faixaBruta = ocupacoes.find((o) => o.id === escolhida)?.faixa ?? ''
  // RN-06 e RN-22 (US-028): o aviso de reagendamento é prazo prometido ao
  // cliente, então passa pelo respiro, ponto único em dominio/entrega.js.
  const faixaEscolhida = faixaBruta ? faixaParaCliente(faixaBruta, agora) : ''

  return (
    <Modal
      titulo={'Alterar agendamento · ' + conversa.nome}
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao variante="primario" disabled={escolhida === conversa.pedido.janela} onClick={() => aoAlterar(escolhida, avisar, textoAvisoDeAgendamento(faixaEscolhida))}>
            Alterar
          </Botao>
          <Botao variante="texto" onClick={aoFechar}>Voltar</Botao>
        </>
      )}
    >
      <ul className={css.listaRota}>
        {ocupacoes.map((ocupacao) => {
          const motivo = motivoDoBloqueio(ocupacao)
          const livre = motivo ? proximaLivre(ocupacoes, ocupacao.id) : null
          return (
            <li key={ocupacao.id}>
              <button
                type="button" className={css.linhaJanela} aria-pressed={escolhida === ocupacao.id}
                disabled={Boolean(motivo) && ocupacao.id !== conversa.pedido.janela}
                onClick={() => setEscolhida(ocupacao.id)}
              >
                {escolhida === ocupacao.id && <Icone nome="check" />}
                <span>{ocupacao.faixa}</span>
                <span>{rotuloDaOcupacao(ocupacao)}</span>
              </button>
              {motivo && ocupacao.id !== conversa.pedido.janela && (
                <p className={css.motivoJanela}>
                  {motivo} {livre && `Próxima livre: ${livre.faixa}.`}
                </p>
              )}
            </li>
          )
        })}
      </ul>

      {escolhida !== conversa.pedido.janela && (
        <label className={css.previaAviso}>
          <input type="checkbox" checked={avisar} onChange={(e) => setAvisar(e.target.checked)} />
          <span>Avisar {conversa.nome}: "{textoAvisoDeAgendamento(faixaEscolhida)}"</span>
        </label>
      )}
    </Modal>
  )
}
