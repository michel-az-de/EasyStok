import { useState } from 'react'
import { BarraDeAbas } from '../../componentes/BarraDeAbas'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes } from '../../aplicacao/contextos'
import { ehLead } from '../../dominio/conversa'
import { diasEntre, quandoRelativo, resumoFinanceiro } from '../../dominio/cliente'
import { moeda } from '../../dominio/formato'
import { numeroParaEntregador, textoDoEntregador } from '../../dominio/despacho'
import css from './cliente.module.css'

const dataCurta = (iso) =>
  new Date(iso + (iso.length === 10 ? 'T12:00:00' : '')).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })

// Nasce de `BlocoHistorico.jsx` (rodada 4), mas em modal e com três abas
// (seção 2 da direção visual). `BlocoHistorico.jsx` e `BlocoNotas.jsx`
// continuam de pé na coluna: tirar os dois é do passe de integração
// (auditoria/decisoes/19, seção 8, "PainelFicha.jsx não é de ninguém nas
// frentes"), então até lá o conteúdo aparece duas vezes na tela. Aceito, é o
// mesmo caso do Endereço capturado.
function AvaliacaoCliente({ avaliacao }) {
  if (avaliacao === 'positiva') return <span className={css.avaliacao}><Icone nome="thumbs-up" tamanho={20} /> Positiva</span>
  if (avaliacao === 'negativa') return <span className={css.avaliacao}><Icone nome="thumbs-down" tamanho={20} /> Negativa</span>
  return <span className={css.avaliacaoVazia}>Sem avaliação</span>
}

function AbaPedidos({ historico, pedidosAnteriores }) {
  if (historico.length === 0) {
    return (
      <Vazio titulo="Nenhum pedido ainda">
        {pedidosAnteriores > 0
          ? `${pedidosAnteriores} ${pedidosAnteriores === 1 ? 'pedido anterior' : 'pedidos anteriores'}, sem detalhe salvo neste protótipo.`
          : 'Primeira vez na casa.'}
      </Vazio>
    )
  }
  return (
    <ul className={css.listaPedidos}>
      {historico.map((pedido) => (
        <li key={pedido.numero} className={css.linhaPedido}>
          <header>
            <time dateTime={pedido.em}>{dataCurta(pedido.em)}</time>
            <b>{pedido.numero}</b>
            <span className={css.itensPedido}>{pedido.itens.join(', ')}</span>
            <b className={css.totalPedido}>{moeda(pedido.total)}</b>
            <Pilula tom={pedido.estado === 'entregue' ? 'ok' : 'neutro'} fina>{pedido.estado}</Pilula>
            <AvaliacaoCliente avaliacao={pedido.avaliacaoCliente} />
          </header>
          {/* Issue #17: quem levou, com veículo, placa e empresa, e o número
              que o entregador conferiu. */}
          {pedido.entregador && (
            <p className={css.notaPedido}>
              {pedido.estado === 'entregue' ? 'Entregue por ' : 'Com '}{textoDoEntregador(pedido.entregador)}
              {' · pedido '}{numeroParaEntregador(pedido)}
            </p>
          )}
          {pedido.nota && <p className={css.notaPedido}>{pedido.nota}</p>}
        </li>
      ))}
    </ul>
  )
}

// Desfecho e avisos (decisão 46, "Encerrar" v2): quem encerrou, quando, com
// que desfecho, e se mandou mensagem, e-mail e SMS — o log de auditoria do
// atendimento que a direção pede visível aqui. `desfecho` só existe a partir
// desta decisão: atendimento antigo (nenhum nesta massa) cairia no rótulo
// padrão "manual" em vez de quebrar.
const ROTULO_DESFECHO = { manual: 'encerrado por', tempo: 'encerrado por tempo, sem mensagem —' }

function LogDeEncerramento({ atendimento }) {
  const desfecho = atendimento.desfecho ?? 'manual'
  const avisos = [
    atendimento.mensagemEnviada && 'mensagem enviada',
    atendimento.avisoEmailEnviado && 'e-mail enviado',
    atendimento.avisoSmsEnviado && 'SMS enviado',
  ].filter(Boolean)
  return (
    <p className={css.logEncerramento}>
      {ROTULO_DESFECHO[desfecho]} {atendimento.encerradoPor}
      {avisos.length > 0 && ` · ${avisos.join(', ')}`}
    </p>
  )
}

// Ponta (d) da integração: cada linha é um resumo que a F5 congelou em
// `conversa.atendimentos` (número AT, trecho, duração, receita líquida,
// avaliação). "Ver resumo" abre a MESMA folha do Encerrar, só leitura
// (seção 5: "o resumo volta pela modal Histórico, só pra leitura").
function AbaAtendimentos({ conversaId, atendimentos }) {
  const { abrirEncerramento } = useAcoes()
  if (atendimentos.length === 0) {
    return <Vazio titulo="Nenhum atendimento registrado">Aparece aqui a cada encerramento.</Vazio>
  }
  return (
    <ul className={css.listaPedidos}>
      {[...atendimentos].reverse().map((a) => (
        <li key={a.numero ?? a.encerradoEm} className={css.linhaPedido}>
          <header>
            {/* `encerradoEm` é o relógio da tela em ms (casos/encerramento.js). */}
            <time dateTime={new Date(a.encerradoEm).toISOString()}>
              {new Date(a.encerradoEm).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' })}
            </time>
            <span>{a.numero} · {a.faixaHorario} · {a.duracaoTexto}</span>
            <b className={css.totalPedido}>{a.receita ? moeda(a.receita.liquido) : '—'}</b>
            <AvaliacaoCliente avaliacao={a.avaliacaoCliente} />
          </header>
          <LogDeEncerramento atendimento={a} />
          {a.anotacao && <p className={css.notaPedido}>{a.anotacao}</p>}
          <Botao variante="texto" onClick={() => abrirEncerramento(conversaId, a.numero)}>
            Ver resumo {a.numero}
          </Botao>
        </li>
      ))}
    </ul>
  )
}

function AbaNotas({ conversa }) {
  const { salvarNota } = useAcoes()
  const [texto, setTexto] = useState('')
  const notas = conversa.cliente.notas

  function salvar() {
    if (!texto.trim()) return
    salvarNota(conversa.id, texto.trim())
    setTexto('')
  }

  return (
    <div className={css.abaNotas}>
      <div className={css.novaNota}>
        <CampoArea
          rotulo="Nova nota"
          rows={2}
          value={texto}
          onChange={(e) => setTexto(e.target.value)}
          placeholder="Nunca chega ao cliente."
        />
        <Botao variante="primario" disabled={!texto.trim()} onClick={salvar}>Salvar nota</Botao>
      </div>
      {notas.length === 0
        ? <p className={css.corpoBloco}>Nenhuma nota.</p>
        : (
          <ul className={css.listaNotas}>
            {notas.map((n) => (
              <li key={n.id ?? n.em + n.texto.slice(0, 12)} className={css.linhaNota}>
                <span className={css.quemNota}>{n.autor} · {n.em}</span>
                {n.texto}
              </li>
            ))}
          </ul>
        )}
    </div>
  )
}

export function ModalHistorico({ conversa, historico, agora, aoFechar }) {
  const [aba, setAba] = useState('pedidos')
  const { cliente } = conversa
  const lead = ehLead(conversa)
  const financeiro = resumoFinanceiro(historico)

  return (
    <Modal
      titulo={`Histórico · ${conversa.nome}`}
      aoFechar={aoFechar}
      largura="min(880px, calc(100vw - 32px))"
      rodape={<Botao onClick={aoFechar}>Fechar</Botao>}
    >
      {lead ? (
        <p className={css.faixaLead}>
          Lead desde {quandoRelativo(diasEntre(conversa.mensagens[0]?.em ?? agora, agora))} · nenhum pedido
        </p>
      ) : (
        <div className={css.faixaNumeros}>
          <div><b>{cliente.desde}</b><span>Cliente desde</span></div>
          <div><b>{cliente.pedidos}</b><span>{cliente.pedidos === 1 ? 'pedido' : 'pedidos'}</span></div>
          <div><b>{moeda(financeiro.total)}</b><span>gastos</span></div>
          <div><b>{moeda(financeiro.ticketMedio)}</b><span>Ticket médio</span></div>
        </div>
      )}

      <BarraDeAbas
        abas={[
          { id: 'pedidos', rotulo: 'Pedidos', contador: cliente.pedidos },
          { id: 'atendimentos', rotulo: 'Atendimentos', contador: (conversa.atendimentos ?? []).length },
          { id: 'notas', rotulo: 'Notas', contador: cliente.notas.length },
        ]}
        ativa={aba}
        aoTrocar={setAba}
      />

      <div role="tabpanel" id="painel-da-aba" className={css.painelAba}>
        {aba === 'pedidos' && <AbaPedidos historico={historico} pedidosAnteriores={cliente.pedidos} />}
        {aba === 'atendimentos' && (
          <AbaAtendimentos conversaId={conversa.id} atendimentos={conversa.atendimentos ?? []} />
        )}
        {aba === 'notas' && <AbaNotas conversa={conversa} />}
      </div>
    </Modal>
  )
}
