import { useState } from 'react'
import { useAcoes } from '../../aplicacao/contextos'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import css from './ficha.module.css'

export function ConferenciaPessoaDaComanda({ conversa, aoAbrirCadastro }) {
  const { reconferirPessoaDaComanda } = useAcoes()
  const [conferencia, conferir] = useState(null)
  const [erro, falhar] = useState(null)
  const identidade = conversa.pedido?.identidadeProposta
  const pendente = !identidade?.clienteId || identidade.clienteId !== conversa.clienteId
    || identidade.vinculoRevisao !== conversa.vinculoRevisao
  if (conversa.pedido?.pedidoId || !conversa.pedido?.itens?.length || !pendente) return null
  const mudou = conferencia && (conferencia.clienteId !== conversa.clienteId || conferencia.vinculoRevisao !== conversa.vinculoRevisao)
  const confirmar = () => {
    const resultado = reconferirPessoaDaComanda(conversa.id, conferencia)
    if (resultado?.erro) falhar(resultado.erro)
    else conferir(null)
  }
  return <section className={css.avisoBloqueio} aria-label="Pessoa da comanda">
    <p className={css.corpoBloco}>Confira a pessoa antes de continuar esta comanda. Seus itens estão preservados.</p>
    {conversa.clienteId ? <Botao onClick={() => { falhar(null); conferir({ clienteId: conversa.clienteId, vinculoRevisao: conversa.vinculoRevisao, nome: conversa.nome, telefone: conversa.cliente?.telefoneCanal }) }}>Conferir pessoa da comanda</Botao>
      : <><p className={css.rodapeBloco}>Vincule um cadastro à conversa para conferir a pessoa desta comanda.</p><Botao onClick={aoAbrirCadastro}>Conferir cadastro</Botao></>}
    {conferencia && <Modal titulo="Conferir pessoa da comanda" aoFechar={() => conferir(null)} rodape={<>
      <Botao onClick={() => conferir(null)}>Cancelar</Botao>
      <Botao variante="primario" disabled={Boolean(mudou)} onClick={confirmar}>Confirmar pessoa e manter itens</Botao>
    </>}>
      <p>Esta comanda será criada para <strong>{conferencia.nome}</strong>{conferencia.telefone ? ` (${conferencia.telefone})` : ''}.</p>
      <p>Confira o cadastro com a pessoa. A confirmação mantém os itens, a entrega e a forma de pagamento escolhidos.</p>
      {(mudou || erro) && <p className={css.avisoErro} role="alert">{erro || 'O cadastro mudou durante a conferência. Feche e confira novamente.'}</p>}
    </Modal>}
  </section>
}
