import { useEffect, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao } from '../../componentes/Campo'
import { Modal } from '../../componentes/Modal'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import css from './ficha.module.css'

// Passar a conversa para outro atendente (F09, S41). Só existe no modo API: a lista vem de
// `api/atendimento/atendentes`, sem quem já está logado.
export function ModalTransferir({ conversa, aoFechar }) {
  const { sessao } = useAtendimento()
  const { listarAtendentes, transferirConversa } = useAcoes()
  const [atendentes, setAtendentes] = useState(null)
  const [erro, setErro] = useState(null)
  const [escolhido, setEscolhido] = useState('')

  useEffect(() => {
    let vivo = true
    listarAtendentes()
      .then((lista) => { if (vivo) setAtendentes(lista.filter((a) => a.usuarioId !== sessao?.usuario?.id)) })
      .catch((e) => { if (vivo) setErro(`Atendentes não carregaram: ${e.message}`) })
    return () => { vivo = false }
  }, [listarAtendentes, sessao])

  const destino = atendentes?.find((a) => a.usuarioId === escolhido) ?? null
  const opcoes = [
    { valor: '', rotulo: atendentes ? 'Escolha quem atende' : 'Carregando…' },
    ...(atendentes ?? []).map((a) => ({ valor: a.usuarioId, rotulo: a.nome })),
  ]

  return (
    <Modal
      titulo={`Transferir a conversa com ${conversa.nome}`}
      descricao="A conversa passa para quem você escolher e sai da sua fila."
      aoFechar={aoFechar}
      rodape={(
        <>
          <Botao onClick={aoFechar}>Cancelar</Botao>
          <Botao
            variante="primario"
            disabled={!destino}
            onClick={() => { transferirConversa(conversa.id, destino); aoFechar() }}
          >
            Transferir
          </Botao>
        </>
      )}
    >
      <div className={css.formBloqueio}>
        {atendentes?.length === 0
          ? <p className={css.corpoBloco}>Nenhum outro atendente ativo nesta empresa.</p>
          : (
            <CampoSelecao
              rotulo="Atendente"
              opcoes={opcoes}
              value={escolhido}
              onChange={(e) => setEscolhido(e.target.value)}
            />
          )}
        {erro && <p className={css.corpoBloco} role="alert">{erro}</p>}
      </div>
    </Modal>
  )
}
