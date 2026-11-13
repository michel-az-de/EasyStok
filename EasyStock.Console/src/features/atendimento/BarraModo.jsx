import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { MODOS } from '../../dominio/automatico'
import { faixaDaTransferencia } from '../../dominio/faixaDeTransferencia'
import { resumoDoModo } from '../../dominio/resumoDoModo'
import { automaticoDesligadoNaConversa } from '../../dominio/motivoDeEscalada'
import css from './atendimento.module.css'

// Quem responde esta conversa: o estado e, quando cabe, o Assumir. Uma linha, um lugar só, no topo
// da conversa (RN-04, D4). O que cada estado oferece vem de `acoesDaConversa`; Devolver e Concluir
// moram no menu ⋮ do cabeçalho (#2185: a faixa tinha um segundo menu para a mesma conversa).
//
// #2232 (A2): quando o Assistente passou a conversa, a faixa diz quem passou, por quê e quando, em
// vez da pílula "Passou para você". #2232 (A4): o Assumir é "Assumir conversa".
// O motivo da passagem fica à vista: "Passou para você" sem dizer "alergia a camarão" obrigava a
// procurar o porquê. A frase dos outros estados repetia a pílula e fica para o leitor de tela e o
// `title` (#1442, #1870). Cadastro bloqueado é explicado junto do campo de mensagem.
export function BarraModo({ conversa, pausado, bloqueada = false, podeReabrir = true, acoes, aoAssumir }) {
  const [confirmando, setConfirmando] = useState(null)
  // A barra não remonta ao trocar de conversa: o Assumir em andamento é de uma conversa só (#2185 F22).
  const [assumindo, setAssumindo] = useState(() => new Set())
  const emVoo = useRef(new Set())
  const resumo = resumoDoModo(conversa, pausado, bloqueada, { podeReabrir })
  const faixa = resumo.chave === MODOS.COM_VOCE ? faixaDaTransferencia(conversa.passagem) : null
  // Com o Assistente desligado na loja, "pausado" e "volta" prometem o que não acontece.
  const semAssistente = automaticoDesligadoNaConversa(conversa) && resumo.chave !== MODOS.ENCERRADO && resumo.chave !== MODOS.LIGADO
  const linha = semAssistente ? 'O Assistente está desligado.' : resumo.linha
  const mostrarLinha = semAssistente || resumo.chave === MODOS.COM_VOCE

  async function assumir() {
    setConfirmando(null)
    const id = conversa.id
    if (emVoo.current.has(id)) return
    emVoo.current.add(id)
    setAssumindo(new Set(emVoo.current))
    try { await aoAssumir() } finally {
      emVoo.current.delete(id)
      setAssumindo(new Set(emVoo.current))
    }
  }

  return (
    <section className={`${css.modo} ${css['modo_' + resumo.chave]}`} data-controle-conversa aria-label="Quem responde esta conversa">
      <div className={css.modoLinha}>
        {/* #2420: título em negrito e motivo embaixo; o " · " fica para o leitor de tela ler a frase inteira. */}
        {faixa ? (
          <p className={css.faixaTransferencia} title={faixa.longo}>
            <Icone nome="hand" className={css.faixaIcone} />
            <span className={css.faixaTexto}>
              <strong>{faixa.titulo}</strong><span className="sr"> · </span><span className={css.faixaMotivo}>{faixa.motivo}</span>
            </span>
          </p>
        ) : (
          <span className={css.modoEstado} title={resumo.detalhe}><Pilula tom={resumo.tom} fina>{resumo.rotulo}</Pilula></span>
        )}
        {!faixa && <p className={mostrarLinha ? css.modoTexto : 'sr'} title={linha === resumo.linha ? resumo.detalhe : linha}>{linha}</p>}
        {acoes.assumir && (
          <Botao
            variante="primario" className={css.modoAssumir} disabled={assumindo.has(conversa.id)}
            aria-haspopup={acoes.confirmarAoAssumir ? 'dialog' : undefined}
            onClick={acoes.confirmarAoAssumir ? () => setConfirmando({ conversaId: conversa.id }) : assumir}
          >
            Assumir conversa
          </Botao>
        )}
      </div>

      {confirmando?.conversaId === conversa.id && acoes.confirmarAoAssumir && (
        <Modal
          titulo="Assumir esta conversa?"
          descricao={`Ela está com ${conversa.responsavel ?? 'outro atendente'}. As próximas respostas passam a ser suas.`}
          aoFechar={() => setConfirmando(null)}
          rodape={(
            <>
              <Botao onClick={() => setConfirmando(null)}>Cancelar</Botao>
              <Botao variante="primario" onClick={assumir}>Assumir conversa</Botao>
            </>
          )}
        />
      )}
    </section>
  )
}
