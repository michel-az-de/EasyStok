import { useEffect, useState } from 'react'
import { Modal } from '../../componentes/Modal'
import { AvatarAssistente } from '../../componentes/AvatarAssistente'
import { useAtendimento } from '../../aplicacao/contextos'
import { contextoCadastro, obterCadastro } from '../../aplicacao/cadastros'
import { useFichaUnicaDoCliente } from '../../aplicacao/fichaUnicaDoCliente'
import { avisarMudancaNoCliente } from '../../aplicacao/mudancasDoCliente'
import { useResumoDoCliente } from '../../aplicacao/useResumoDoCliente'
import { AvisosDoCliente } from './AvisosDoCliente'
import { ModalCliente } from './ModalCliente'
import css from './ModalCliente.module.css'

// #2849: no Atendimento, quem já tem cadastro abre a MESMA ficha de Cadastros e do Buscar na Casa, lida pelo
// cliente e não pela conversa. O que só a conversa sabe (apontamentos do assistente, avisos, endereço que
// chegou pela conversa) entra como complemento, depois do resumo. Lead, modo demonstração e leitura que
// falhou seguem com a ficha da conversa: o clique nunca morre.
export function FichaDoClienteNoAtendimento({ conversa, aoFechar, editarAoAbrir = false, cadastrarLead = false, children }) {
  const { fonteApi } = useAtendimento()
  const renderizar = useFichaUnicaDoCliente()
  const clienteId = conversa.clienteId
  const unica = Boolean(fonteApi && clienteId && renderizar)
  const [contexto] = useState(contextoCadastro)
  const [editor, editar] = useState(null)
  const [falhou, falhar] = useState(false)
  useEffect(() => {
    if (!unica) return undefined
    const controle = new AbortController()
    obterCadastro('clientes', clienteId, contexto, controle.signal)
      .then(dados => { if (!controle.signal.aborted) editar({ tela: 'clientes', dados, inicial: dados }) })
      .catch(() => { if (!controle.signal.aborted) falhar(true) })
    return () => controle.abort()
  }, [unica, clienteId, contexto])

  if (!unica || falhou) return <ModalCliente conversa={conversa} aoFechar={aoFechar} forcarCadastro={cadastrarLead}>{children}</ModalCliente>
  if (!editor) return <Modal titulo="Ficha do cliente" largura="md" aoFechar={aoFechar}><p role="status">Abrindo a ficha…</p></Modal>
  return renderizar({
    tela: 'clientes', editor, aoMudar: editar, contexto, conversa, podeEditar: true, editarAoAbrir,
    complemento: <ComplementoDoAtendimento conversa={conversa}>{children}</ComplementoDoAtendimento>,
    aoGravarRelacionamento: () => avisarMudancaNoCliente(clienteId),
    aoFechar, aoSalvo: () => { avisarMudancaNoCliente(clienteId); aoFechar() },
  })
}

function ComplementoDoAtendimento({ conversa, children }) {
  const { cliente } = conversa
  const notas = (cliente.notas ?? []).map((n) => `${n.id}:${n.criadoEm}`).join(',')
  const resumo = useResumoDoCliente(conversa.id, `${cliente.pedidos ?? 0}|${notas}`, cliente.totalGasto !== undefined)
  return <div className={css.complemento} data-complemento-do-atendimento>
    {children}
    {resumo.texto && <section data-resumo-cliente aria-label="Resumo do assistente">
      <AvatarAssistente tamanho={36} />
      <div><h3>Apontamentos</h3><p>{resumo.texto}</p></div>
    </section>}
    <section className={css.avisosDaFicha} aria-label="Avisos do cliente"><h3>Avisos</h3><AvisosDoCliente conversa={conversa} /></section>
  </div>
}
