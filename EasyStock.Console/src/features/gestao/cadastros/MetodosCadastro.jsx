import { useRef, useState } from 'react'
import { ativarMetodoCadastro, useListaCadastros } from '../../../aplicacao/cadastros'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { Botao } from '../../../componentes/Botao'
import { Modal } from '../../../componentes/Modal'
import { Vazio } from '../../../componentes/Vazio'
import css from './cadastros.module.css'

export function MetodosCadastro({ contexto }) {
  const { acoes } = useAcessoModulos()
  const consulta = useListaCadastros('pagamentos', {}, contexto)
  const [intencao, escolher] = useState(null)
  const [erro, falhar] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const [sucesso, avisar] = useState(null)
  const emVoo = useRef(false)
  async function salvar() {
    if (emVoo.current || !intencao) return
    emVoo.current = true; ocupar(true); falhar(null)
    try {
      await ativarMetodoCadastro(intencao.metodo, intencao.ativo, intencao.requestId, contexto)
      avisar(`${intencao.metodo.nome}: ${intencao.ativo ? 'ativado' : 'inativado'}.`)
      escolher(null); consulta.recarregar()
    } catch (e) { falhar(e.status === 409 ? 'Esta forma de pagamento mudou. Feche e atualize a lista antes de tentar novamente.' : e.message) }
    finally { emVoo.current = false; ocupar(false) }
  }
  return <section className={css.painel} aria-label="Formas de pagamento">
    <header className={css.cabecalho}><div><h2>Formas de pagamento</h2><p>Escolha os meios aceitos pela loja.</p></div><Botao icone="refresh-cw" onClick={consulta.recarregar} disabled={consulta.carregando}>Atualizar</Botao></header>
    {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
    {consulta.erro && <div className={css.erro} role="alert">{consulta.erro}<Botao onClick={consulta.recarregar}>Tentar de novo</Botao></div>}
    {consulta.carregando ? <Vazio titulo="Carregando formas de pagamento" /> : <ul className={css.cartoes}>{consulta.itens.map(m => <li key={m.codigo}>
      <div><h3>{m.nome}</h3><span className={css.situacao}>{m.ativo ? 'Ativa' : 'Inativa'}</span></div>
      {acoes.gerenciarCadastrosFinanceiros && <Botao onClick={() => { falhar(null); escolher({ metodo: m, ativo: !m.ativo, requestId: crypto.randomUUID() }) }}>{m.ativo ? 'Inativar' : 'Ativar'}</Botao>}
    </li>)}</ul>}
    {intencao && <Modal titulo={`${intencao.ativo ? 'Ativar' : 'Inativar'} ${intencao.metodo.nome}?`} aoFechar={ocupado ? undefined : () => escolher(null)} segurarFechamento={ocupado}
      rodape={<><Botao disabled={ocupado} onClick={() => escolher(null)}>Cancelar</Botao><Botao variante="primario" disabled={ocupado} onClick={salvar}>{ocupado ? 'Salvando…' : 'Confirmar alteração'}</Botao></>}>
      <p>{intencao.ativo ? 'Esta forma ficará disponível para novos recebimentos.' : 'Esta forma deixará de ser oferecida para novos recebimentos.'} O histórico já registrado permanece.</p>
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>}
  </section>
}
