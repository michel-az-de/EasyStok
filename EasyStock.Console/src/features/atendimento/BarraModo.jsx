import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { MODOS } from '../../dominio/automatico'
import { resumoDoModo } from '../../dominio/resumoDoModo'
import { primeiroNome } from '../../dominio/mensagem'
import { automaticoDesligadoNaConversa } from '../../dominio/motivoDeEscalada'
import css from './atendimento.module.css'

// Quem responde esta conversa. Um lugar só, no topo da thread, com os três
// estados e os dois toques: Assumir e Devolver ao automático (RN-04, D4).
// O log do automático saiu daqui (corte #39): cada mensagem automática já vem
// marcada como tal na própria conversa, contar de novo era ruído.
// Cadastro bloqueado não oferece toque nenhum: a conversa só volta a andar
// pelo desbloqueio na ficha.
//
// Rodada 12 (issue #16, dúvidas da Thatiane): "Devolver ao automático" agora
// explica o efeito e pede confirmação, e depois que ela responde a barra
// sugere o próximo passo (encerrar x aguardar o cliente).
//
// #1442 (homologação de 07/10, "muito prolixo"): uma linha só. Rótulo curto,
// uma frase objetiva e as ações; a frase inteira fica no `title` da linha.
export function BarraModo({
  conversa, pausado, bloqueada = false, aoAssumir, aoDevolver, aoEncerrar,
}) {
  const [confirmandoDevolver, setConfirmandoDevolver] = useState(null)
  const menu = useRef(null)
  const resumo = resumoDoModo(conversa, pausado, bloqueada)
  const encerrada = resumo.chave === MODOS.ENCERRADO
  // Assumir só faz sentido em conversa que ainda está no automático ou que
  // acabou de ser devolvida. Devolver só onde o automático está parado.
  const podeAssumir = resumo.chave === MODOS.LIGADO || resumo.chave === MODOS.COM_VOCE
  // #1761 (L6): com o automático desligado na loja não há para quem devolver.
  const semAutomatico = automaticoDesligadoNaConversa(conversa)
  const podeDevolver = (resumo.chave === MODOS.PAUSADO || resumo.chave === MODOS.COM_VOCE) && !semAutomatico
  // Automático desligado não está "pausado" nem "volta": a faixa diz só o que é verdade.
  const linha = semAutomatico && !encerrada && resumo.chave !== MODOS.LIGADO ? 'O Assistente está desligado.' : resumo.linha
  const sugereEncerrar = resumo.sugereEncerrar && Boolean(aoEncerrar)
  // #1870: "Passou para você" já é o estado e o Assumir, a ação; a frase repetia os dois (e o
  // cartão do Balcão). Fica para o leitor de tela e no title da pílula; a faixa é uma linha só.
  const fraseRepetida = (resumo.chave === MODOS.COM_VOCE || (resumo.chave === MODOS.LIGADO && !semAutomatico)) && linha === resumo.linha
  const nome = primeiroNome(conversa.nome)
  const mostrarLinha = semAutomatico || bloqueada

  const fecharAcoes = () => {
    if (!menu.current) return
    menu.current.open = false
    menu.current.querySelector('summary')?.focus({ preventScroll: true })
  }

  const devolver = () => {
    setConfirmandoDevolver(null)
    aoDevolver()
  }

  return (
    <section className={`${css.modo} ${css['modo_' + resumo.chave]}`} aria-label="Quem responde esta conversa">
      <div className={css.modoLinha}>
        <span title={resumo.detalhe}><Pilula tom={resumo.tom} fina>{resumo.rotulo}</Pilula></span>
        <p className={fraseRepetida || !mostrarLinha ? 'sr' : css.modoTexto} title={linha === resumo.linha ? resumo.detalhe : linha}>{linha}</p>
        {!encerrada && !bloqueada && (
          <span className={css.modoAcoes}>
            {podeAssumir && (
              <Botao variante="primario" className={css.acaoModo} onClick={aoAssumir}>
                Assumir
              </Botao>
            )}
            {(podeDevolver || sugereEncerrar) && (
              <details key={conversa.id} ref={menu} className={css.maisAcoesModo} onKeyDown={(evento) => {
                if (evento.key === 'Escape' && evento.currentTarget.open) {
                  evento.preventDefault(); evento.stopPropagation(); fecharAcoes()
                }
              }}>
                <summary>Mais ações</summary>
                <div className={css.listaAcoesModo}>
                  {podeDevolver && <Botao variante="texto" className={css.acaoModo}
                    title="O Assistente volta a responder esta conversa" aria-haspopup="dialog"
                    onClick={() => { fecharAcoes(); setConfirmandoDevolver({ conversaId: conversa.id }) }}>Devolver ao Assistente</Botao>}
                  {sugereEncerrar && <Botao variante="texto" icone="circle-check" className={css.acaoModo}
                    onClick={() => { fecharAcoes(); aoEncerrar() }}>Concluir conversa</Botao>}
                </div>
              </details>
            )}
          </span>
        )}
      </div>

      {confirmandoDevolver && confirmandoDevolver.conversaId === conversa.id && podeDevolver && !encerrada && !bloqueada && (
        <Modal
          titulo="Devolver ao Assistente?"
          descricao={`O Assistente volta a responder as próximas mensagens de ${nome}.`}
          aoFechar={() => setConfirmandoDevolver(null)}
          rodape={(
            <>
              <Botao onClick={() => setConfirmandoDevolver(null)}>Continuar comigo</Botao>
              <Botao variante="primario" onClick={devolver}>Devolver ao Assistente</Botao>
            </>
          )}
        >
          <ul className={css.listaEfeito}>
            <li>Ele responde sozinho dúvida de cardápio, horário, entrega e pagamento.</li>
            <li>Restrição alimentar, reclamação e prato sem saldo continuam vindo para você.</li>
            <li>Você pode assumir de novo a qualquer momento, pelo botão Assumir.</li>
          </ul>
        </Modal>
      )}
    </section>
  )
}
