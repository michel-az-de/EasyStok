import { useId, useRef, useState } from 'react'
import { BarraDeAbas } from '../../componentes/BarraDeAbas'
import { Botao } from '../../componentes/Botao'
import { CampoArea } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { ehLead } from '../../dominio/conversa'
import { enviosAutomaticos } from '../../dominio/automacao'
import { diasEntre, quandoRelativo, resumoFinanceiro } from '../../dominio/cliente'
import { moeda } from '../../dominio/formato'
import { rotuloDoEstado } from '../../dominio/pedido'
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
            <Pilula tom={pedido.estado === 'entregue' ? 'ok' : 'neutro'} fina>{rotuloDoEstado(pedido.estado)}</Pilula>
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

function AbaNotas({ conversa, ocupado, aoOcupar }) {
  const { salvarNota } = useAcoes()
  const [texto, setTexto] = useState('')
  const [aviso, setAviso] = useState(null)
  const salvando = useRef(false)
  const campo = useRef(null)
  const notas = conversa.cliente.notas

  async function salvar() {
    if (!texto.trim() || salvando.current) return
    salvando.current = true
    campo.current?.focus()
    aoOcupar(true)
    setAviso(null)
    try {
      if (await salvarNota(conversa.id, texto.trim()) === false) {
        setAviso({ erro: true, texto: 'Sem confirmação da nota. Seu texto continua aqui. Confira a ficha antes de tentar de novo.' })
      } else {
        setTexto('')
        setAviso({ erro: false, texto: 'Nota salva.' })
      }
    } catch (erro) {
      setAviso({ erro: true, texto: erro.message || 'Não foi possível salvar a nota. Seu texto continua aqui.' })
    } finally {
      salvando.current = false
      aoOcupar(false)
    }
  }

  return (
    <div className={css.abaNotas}>
      <div className={css.novaNota}>
        <CampoArea
          rotulo="Nova nota"
          rows={2}
          value={texto}
          ref={campo}
          readOnly={ocupado}
          aria-busy={ocupado}
          onChange={(e) => setTexto(e.target.value)}
          placeholder="Nunca chega ao cliente."
        />
        <Botao variante="primario" disabled={!texto.trim() || ocupado} onClick={salvar}>{ocupado ? 'Salvando…' : 'Salvar nota'}</Botao>
      </div>
      {aviso && <p className={aviso.erro ? css.avisoErro : css.corpoBloco} role={aviso.erro ? 'alert' : 'status'}>{aviso.texto}</p>}
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

// #1441: o que a casa mandou sozinha nesta conversa (automáticas do EasyStok, avisos do
// pedido e o agente). A API grava cada envio como mensagem; o gatilho de cada uma e os
// disparos que não saíram (fora da janela, desligada) ainda não ficam registrados.
const ROTULO_ORIGEM = { agente: 'Agente', sistema: 'Automática' }
const horaCompleta = (iso) => new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

function AbaAutomaticas({ envios }) {
  if (envios.length === 0) return <p className={css.corpoBloco}>Nada automático nesta conversa.</p>
  return (
    <ul className={css.listaNotas}>
      {envios.map((m) => (
        <li key={m.id} className={css.linhaNota}>
          <span className={css.quemNota}>
            {ROTULO_ORIGEM[m.origemAutomatica] ?? 'Automática'} · {horaCompleta(m.em)}{m.status === 'falhou' ? ' · não entregue' : ''}
          </span>
          {m.texto}
        </li>
      ))}
    </ul>
  )
}

export function ModalHistorico({ conversa, historico, agora, aoFechar }) {
  const id = useId()
  const [aba, setAba] = useState('pedidos')
  const [salvandoNota, setSalvandoNota] = useState(false)
  // #1586: o resumo de cada encerramento só existe na demonstração; no modo API o EasyStok não
  // guarda, e a aba ficaria sempre vazia.
  const { fonteApi } = useAtendimento()
  const { cliente } = conversa
  const lead = ehLead(conversa)
  const financeiro = resumoFinanceiro(historico)
  const automaticas = enviosAutomaticos(conversa.mensagens)

  return (
    <Modal
      titulo={`Histórico · ${conversa.nome}`}
      aoFechar={salvandoNota ? undefined : aoFechar}
      largura="min(880px, calc(100vw - 32px))"
      rodape={<Botao disabled={salvandoNota} onClick={aoFechar}>Fechar</Botao>}
    >
      {lead ? (
        <p className={css.faixaLead}>
          Lead desde {quandoRelativo(diasEntre(conversa.mensagens[0]?.em ?? agora, agora))} · nenhum pedido
        </p>
      ) : (
        <div className={css.faixaNumeros}>
          {cliente.desde && <div><b>{cliente.desde}</b><span>Cliente desde</span></div>}
          <div><b>{cliente.pedidos}</b><span>{cliente.pedidos === 1 ? 'pedido' : 'pedidos'}</span></div>
          <div><b>{moeda(financeiro.total)}</b><span>total comprado</span></div>
          <div><b>{moeda(financeiro.ticketMedio)}</b><span>Ticket médio</span></div>
        </div>
      )}

      <BarraDeAbas
        desabilitada={salvandoNota}
        idPainel={`${id}-painel`}
        prefixoId={`${id}-aba`}
        abas={[
          { id: 'pedidos', rotulo: 'Pedidos', contador: cliente.pedidos },
          ...(fonteApi ? [] : [{ id: 'atendimentos', rotulo: 'Atendimentos', contador: (conversa.atendimentos ?? []).length }]),
          { id: 'notas', rotulo: 'Notas', contador: cliente.notas.length },
          { id: 'automaticas', rotulo: 'Automáticas', contador: automaticas.length },
        ]}
        ativa={aba}
        aoTrocar={(novaAba) => { if (!salvandoNota) setAba(novaAba) }}
      />

      <div role="tabpanel" id={`${id}-painel`} aria-labelledby={`${id}-aba-${aba}`} tabIndex={0} className={css.painelAba}>
        {aba === 'pedidos' && <AbaPedidos historico={historico} pedidosAnteriores={cliente.pedidos} />}
        {aba === 'atendimentos' && (
          <AbaAtendimentos conversaId={conversa.id} atendimentos={conversa.atendimentos ?? []} />
        )}
        <div hidden={aba !== 'notas'}>
          <AbaNotas conversa={conversa} ocupado={salvandoNota} aoOcupar={setSalvandoNota} />
        </div>
        {aba === 'automaticas' && <AbaAutomaticas envios={automaticas} />}
      </div>
    </Modal>
  )
}
