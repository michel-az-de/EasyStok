import { Botao } from '../../componentes/Botao'
import { Pilula } from '../../componentes/Pilula'
import { MODOS } from '../../dominio/automatico'
import { acoesDoModo } from '../../dominio/acoesDoModo'
import { faixaDaTransferencia } from '../../dominio/faixaDeTransferencia'
import { resumoDoModo } from '../../dominio/resumoDoModo'
import css from './atendimento.module.css'

// Quem responde esta conversa. Um lugar só, no topo da thread, com o estado e UMA ação: Assumir
// conversa (RN-04, D4). O resto (Devolver ao Assistente, Concluir conversa) mora no ⋮ do
// cabeçalho; antes a faixa tinha um menu próprio e a conversa ficava com dois menus (#2232, A3 e A4).
// Cadastro bloqueado não oferece toque nenhum: a conversa só volta a andar pelo desbloqueio na ficha.
//
// #1442 (homologação de 07/10, "muito prolixo"): uma linha só; a frase inteira fica no `title`.
// #2232 (A2): quando o automático passou a conversa, a faixa diz "O automático passou para você · motivo"
// em vez da pílula "Passou para você".
export function BarraModo({ conversa, pausado, bloqueada = false, aoAssumir }) {
  const resumo = resumoDoModo(conversa, pausado, bloqueada)
  const { podeAssumir, semAutomatico } = acoesDoModo(conversa, pausado, bloqueada)
  const encerrada = resumo.chave === MODOS.ENCERRADO
  const faixa = resumo.chave === MODOS.COM_VOCE ? faixaDaTransferencia(conversa.passagem) : null
  // Automático desligado não está "pausado" nem "volta": a faixa diz só o que é verdade.
  const linha = semAutomatico && !encerrada && resumo.chave !== MODOS.LIGADO ? 'O Assistente está desligado.' : resumo.linha
  // #1870: "Assistente respondendo" já é o estado; a frase repetia. Fica para o leitor de tela.
  const fraseRepetida = (resumo.chave === MODOS.COM_VOCE || (resumo.chave === MODOS.LIGADO && !semAutomatico)) && linha === resumo.linha
  const mostrarLinha = semAutomatico || bloqueada

  return (
    <section className={`${css.modo} ${css['modo_' + resumo.chave]}`} data-controle-conversa aria-label="Quem responde esta conversa">
      <div className={css.modoLinha}>
        {faixa ? (
          <p className={css.faixaTransferencia} title={faixa.longo}>{faixa.texto}</p>
        ) : (
          <>
            <span title={resumo.detalhe}><Pilula tom={resumo.tom} fina>{resumo.rotulo}</Pilula></span>
            <p className={fraseRepetida || !mostrarLinha ? 'sr' : css.modoTexto} title={linha === resumo.linha ? resumo.detalhe : linha}>{linha}</p>
          </>
        )}
        {podeAssumir && (
          <span className={css.modoAcoes}>
            <Botao variante="primario" className={css.acaoModo} onClick={aoAssumir}>
              Assumir conversa
            </Botao>
          </span>
        )}
      </div>
    </section>
  )
}
