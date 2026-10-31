import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { baixarPdf } from '../../componentes/pdfNoNavegador'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useResumoDoCliente } from '../../aplicacao/useResumoDoCliente'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { enviarFotoCliente, removerFotoCliente } from '../../aplicacao/fotoCliente'
import { gerarFichaClientePdf } from '../../aplicacao/fichaClientePdf'
import { BlocoCliente } from './BlocoCliente'
import { ModalHistorico } from './ModalHistorico'
import css from './ModalCliente.module.css'

export function ModalCliente({ conversa, aoFechar, children }) {
  const { fonteApi, historico = [], agora } = useAtendimento()
  const { carregarClienteDaConversa } = useAcoes()
  const [salvandoNota, setSalvandoNota] = useState(false)
  const [baixandoPdf, setBaixandoPdf] = useState(false)
  const [alterandoFoto, setAlterandoFoto] = useState(false)
  const arquivoFoto = useRef(null)
  const [erro, setErro] = useState(null)
  const [consultadaEm] = useState(() => new Date().toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }))
  const { cliente } = conversa
  const foto = useFotoCliente(cliente.fotoUrl)
  const notas = (cliente.notas ?? []).map((n) => `${n.id}:${n.criadoEm}`).join(',')
  const resumo = useResumoDoCliente(conversa.id, `${cliente.pedidos ?? 0}|${notas}`, fonteApi && Boolean(conversa.clienteId) && cliente.totalGasto !== undefined)
  const ocupado = salvandoNota || baixandoPdf || alterandoFoto
  const cadastro = conversa.clienteId ?? conversa.cadastroId
  const podeBaixarPdf = fonteApi && Boolean(conversa.clienteId)

  async function baixarFicha() {
    if (ocupado || !podeBaixarPdf) return
    setBaixandoPdf(true); setErro(null)
    try {
      baixarPdf(await gerarFichaClientePdf(conversa.clienteId))
    } catch (falha) {
      setErro(falha.message || 'Não foi possível baixar a ficha. Tente de novo.')
    } finally { setBaixandoPdf(false) }
  }

  async function alterarFoto(arquivo = null) {
    if (ocupado || !fonteApi || !conversa.clienteId) return
    setAlterandoFoto(true); setErro(null)
    try {
      if (arquivo) await enviarFotoCliente(conversa.clienteId, arquivo)
      else await removerFotoCliente(conversa.clienteId)
      if (await carregarClienteDaConversa(conversa.id) === false) {
        setErro('A alteração da foto foi salva. Reabra a ficha para atualizar a imagem.')
      }
    } catch (falha) {
      setErro(falha.message || 'Não foi possível alterar a foto. Tente de novo.')
    } finally { setAlterandoFoto(false) }
  }

  return <Modal titulo="Ficha" largura="820px" className={css.modal} aoFechar={ocupado ? undefined : aoFechar}
    rodape={<div className={css.acoes}>
      {!podeBaixarPdf && <small>{!fonteApi ? 'Conecte-se à loja para baixar a ficha.' : 'Cadastre o cliente para baixar a ficha.'}</small>}
      <Botao variante="texto" disabled={ocupado} onClick={aoFechar}>Fechar</Botao>
      <Botao icone="download" disabled={ocupado || !podeBaixarPdf} onClick={baixarFicha}>{baixandoPdf ? 'Preparando PDF…' : 'Baixar PDF'}</Botao>
    </div>}>
    <article className={css.papel} aria-label={`Ficha de ${conversa.nome}`}>
      <div className={css.carimbo}><span>{cadastro ? `Cadastro ${String(cadastro).slice(0, 8)}` : 'Contato sem cadastro'}</span><time>Consulta em {consultadaEm}</time></div>
      {(erro || foto.erro) && <p role="alert" className={css.erro}>{erro || foto.erro}</p>}
      {alterandoFoto && <output role="status">Salvando foto…</output>}
      <input ref={arquivoFoto} type="file" hidden accept="image/jpeg,image/png,image/webp" aria-label="Foto do cliente"
        onChange={(e) => { const arquivo = e.target.files?.[0]; e.target.value = ''; if (arquivo) alterarFoto(arquivo) }} />
      {children}
      <div className={css.cadastro}><BlocoCliente conversa={conversa} integrado resumoIa={resumo.texto} fotoCadastro={foto.url}
        aoEscolherFoto={fonteApi && conversa.clienteId ? () => arquivoFoto.current?.click() : undefined}
        aoRemoverFoto={fonteApi && conversa.clienteId && cliente.fotoUrl ? () => alterarFoto() : undefined}
        alterandoFoto={alterandoFoto} /></div>
      <div className={css.historico}>
        <h3>Histórico e notas</h3>
        <ModalHistorico conversa={conversa} historico={historico} agora={agora} embutido aoOcupar={setSalvandoNota} />
      </div>
    </article>
  </Modal>
}
