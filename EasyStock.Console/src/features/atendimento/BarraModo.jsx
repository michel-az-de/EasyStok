import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { MODOS } from '../../dominio/automatico'
import { resumoDoModo } from '../../dominio/resumoDoModo'
import { automaticoDesligadoNaConversa } from '../../dominio/motivoDeEscalada'
import css from './atendimento.module.css'

// Quem responde esta conversa: o estado e, quando cabe, o Assumir. Uma linha, um lugar só, no topo
// da conversa (RN-04, D4). O que cada estado oferece vem de `acoesDaConversa`; Devolver e Concluir
// moram no menu ⋮ do cabeçalho (#2185: "Mais ações" era um segundo menu para a mesma conversa).
//
// O motivo da passagem fica à vista: "Passou para você" sem dizer "alergia a camarão" obrigava a
// procurar o porquê. A frase dos outros estados repetia a pílula e fica para o leitor de tela e o
// `title` (#1442, #1870). Cadastro bloqueado é explicado junto do campo de mensagem.
export function BarraModo({ conversa, pausado, bloqueada = false, acoes, aoAssumir }) {
  const [confirmando, setConfirmando] = useState(null)
  const [assumindoId, setAssumindoId] = useState(null)
  const emVoo = useRef(false)
  const resumo = resumoDoModo(conversa, pausado, bloqueada)
  // Com o Assistente desligado na loja, "pausado" e "volta" prometem o que não acontece.
  const semAssistente = automaticoDesligadoNaConversa(conversa) && resumo.chave !== MODOS.ENCERRADO && resumo.chave !== MODOS.LIGADO
  const linha = semAssistente ? 'O Assistente está desligado.' : resumo.linha
  const mostrarLinha = semAssistente || resumo.chave === MODOS.COM_VOCE

  async function assumir() {
    setConfirmando(null)
    if (emVoo.current) return
    emVoo.current = true
    setAssumindoId(conversa.id)
    try { await aoAssumir() } finally {
      emVoo.current = false
      setAssumindoId(null)
    }
  }

  return (
    <section className={css.modo} data-controle-conversa aria-label="Quem responde esta conversa">
      <div className={css.modoLinha}>
        <span className={css.modoEstado} title={resumo.detalhe}><Pilula tom={resumo.tom} fina>{resumo.rotulo}</Pilula></span>
        <p className={mostrarLinha ? css.modoTexto : 'sr'} title={linha === resumo.linha ? resumo.detalhe : linha}>{linha}</p>
        {acoes.assumir && (
          <Botao
            variante="primario" className={css.modoAssumir} disabled={assumindoId === conversa.id}
            aria-haspopup={acoes.confirmarAoAssumir ? 'dialog' : undefined}
            onClick={acoes.confirmarAoAssumir ? () => setConfirmando({ conversaId: conversa.id }) : assumir}
          >
            Assumir
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
