import { useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { baixarPdf } from '../../componentes/pdfNoNavegador'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { useResumoDoCliente } from '../../aplicacao/useResumoDoCliente'
import { useFotoCliente } from '../../aplicacao/useFotoCliente'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import { enviarFotoCliente, removerFotoCliente } from '../../aplicacao/fotoCliente'
import { gerarFichaClientePdf } from '../../aplicacao/fichaClientePdf'
import { apresentacaoDoNome } from '../../dominio/nomeDoContato'
import { hashDoCadastroDoCliente } from '../../dominio/fichaCliente'
import { BlocoCliente } from './BlocoCliente'
import { ModalHistorico } from './ModalHistorico'
import css from './ModalCliente.module.css'

export function ModalCliente({ conversa, aoFechar, children, forcarCadastro = false }) {
  const { fonteApi, historico = [], agora } = useAtendimento()
  const { relerClienteDaConversa } = useAcoes()
  const { permite } = useAcessoModulos()
  const [salvandoNota, setSalvandoNota] = useState(false)
  const [baixandoPdf, setBaixandoPdf] = useState(false)
  const [alterandoFoto, setAlterandoFoto] = useState(false)
  const arquivoFoto = useRef(null)
  const [erro, setErro] = useState(null)
  // #2258 (auditoria T17, T18): o cadastro avisa quando há texto digitado e entrega o lugar do botão principal.
  const [cadastroSujo, setCadastroSujo] = useState(false)
  const [notaSuja, setNotaSuja] = useState(false)
  const [, definirNota] = useRascunhoDoAjuste(`nota-cadastro:${conversa.clienteId ?? conversa.cadastroId ?? conversa.id}`)
  const chaveCadastro = `cadastro-conversa:${conversa.id}`
  const [, definirRapido] = useRascunhoDoAjuste(`${chaveCadastro}:rapido`)
  const [, definirNome] = useRascunhoDoAjuste(`${chaveCadastro}:nome`)
  const [, definirTelefone] = useRascunhoDoAjuste(`${chaveCadastro}:telefone`)
  const [, definirEndereco] = useRascunhoDoAjuste(`${chaveCadastro}:endereco`)
  const [salvandoNome] = useGravacaoDoAjuste(`${chaveCadastro}:nome`)
  const [salvandoTelefone] = useGravacaoDoAjuste(`${chaveCadastro}:telefone`)
  const [salvandoEndereco] = useGravacaoDoAjuste(`${chaveCadastro}:endereco`)
  const [rodapeDoCadastro, setRodapeDoCadastro] = useState(null)
  const [consultadaEm] = useState(() => new Date().toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }))
  const { cliente } = conversa
  const foto = useFotoCliente(cliente.fotoUrl)
  const notas = (cliente.notas ?? []).map((n) => `${n.id}:${n.criadoEm}`).join(',')
  const resumo = useResumoDoCliente(conversa.id, `${cliente.pedidos ?? 0}|${notas}`, fonteApi && Boolean(conversa.clienteId) && cliente.totalGasto !== undefined)
  const ocupado = salvandoNota || salvandoNome || salvandoTelefone || salvandoEndereco || baixandoPdf || alterandoFoto
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
      // #2838: a leitura em voo pode ter saído antes da gravação e traria a foto anterior.
      if (await relerClienteDaConversa(conversa.id) === false) {
        setErro('A alteração da foto foi salva. Reabra a ficha para atualizar a imagem.')
      }
    } catch (falha) {
      setErro(falha.message || 'Não foi possível alterar a foto. Tente de novo.')
    } finally { setAlterandoFoto(false) }
  }

  // O Fechar do rodapé passa pela mesma pergunta do Esc e do X (Modal.confirmarDescarte).
  const pedirFechamento = (evento) => evento.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))
  const fechar = () => { definirNota(null); definirNome(null); definirTelefone(null); definirEndereco(null); definirRapido(null); aoFechar() }
  // #2734: a mesma pessoa tem um cadastro completo em Cadastros (documentos, endereços, telefones, pedidos). A Ficha
  // leva até ele; com texto por salvar o atalho espera, para a troca de tela não descartar nada em silêncio.
  const sujo = cadastroSujo || notaSuja
  const podeAbrirCadastro = fonteApi && Boolean(conversa.clienteId) && permite('cadastros')
  const abrirCadastroCompleto = () => { const destino = hashDoCadastroDoCliente(conversa.clienteId); fechar(); window.location.hash = destino }

  // Um só caminho para "texto não salvo": confirmarDescarte (a pergunta do Modal), o mesmo do resto do sistema.
  // O segurarFechamento da PR (que travava Esc e fundo sem perguntar) saiu: a pergunta já protege o digitado.
  return <Modal titulo="Ficha do cliente" largura="lg" className={css.modal} aoFechar={ocupado ? undefined : fechar}
    confirmarDescarte={sujo}
    rodape={<div className={css.acoes}>
      {!podeBaixarPdf && <small>{!fonteApi ? 'Conecte-se à loja para baixar a ficha.' : 'Cadastre o cliente para baixar a ficha.'}</small>}
      <Botao variante="texto" disabled={ocupado} onClick={pedirFechamento}>Fechar</Botao>
      {podeAbrirCadastro && <Botao variante="secundario" icone="usuarios" disabled={ocupado || sujo}
        motivo={sujo ? 'Salve ou descarte o que foi digitado para abrir a ficha completa.' : undefined} onClick={abrirCadastroCompleto}>Abrir ficha completa</Botao>}
      <Botao variante="secundario" icone="download" disabled={ocupado || !podeBaixarPdf} onClick={baixarFicha}>{baixandoPdf ? 'Preparando PDF…' : 'Baixar PDF'}</Botao>
      <span ref={setRodapeDoCadastro} className={css.lugarDoSalvar} />
    </div>}>
    <article className={css.papel} aria-label={`Ficha de ${apresentacaoDoNome(conversa.nome, { comanda: conversa.numeroComanda }).rotulo}`}>
      <div className={css.carimbo}><span>{cadastro ? `Cadastro ${String(cadastro).slice(0, 8)}` : 'Contato sem cadastro'}</span><time>Consulta em {consultadaEm}</time></div>
      {(erro || foto.erro) && <p role="alert" className={css.erro}>{erro || foto.erro}</p>}
      {alterandoFoto && <output role="status">Salvando foto…</output>}
      <input ref={arquivoFoto} type="file" hidden accept="image/jpeg,image/png,image/webp" aria-label="Foto do cliente"
        onChange={(e) => { const arquivo = e.target.files?.[0]; e.target.value = ''; if (arquivo) alterarFoto(arquivo) }} />
      {children}
      <div className={css.cadastro}><BlocoCliente conversa={conversa} integrado aoMudarSujo={setCadastroSujo} rodapeDoCadastro={rodapeDoCadastro} forcarCadastro={forcarCadastro} aoCadastrado={forcarCadastro ? fechar : undefined} resumoIa={resumo.texto} fotoCadastro={foto.url}
        aoEscolherFoto={fonteApi && conversa.clienteId ? () => arquivoFoto.current?.click() : undefined}
        aoRemoverFoto={fonteApi && conversa.clienteId && cliente.fotoUrl ? () => alterarFoto() : undefined}
        alterandoFoto={alterandoFoto} /></div>
      <div className={css.historico}>
        <h3>Histórico e notas</h3>
        <ModalHistorico conversa={conversa} historico={historico} agora={agora} embutido aoOcupar={setSalvandoNota} aoMudarSujo={setNotaSuja} />
      </div>
    </article>
  </Modal>
}
