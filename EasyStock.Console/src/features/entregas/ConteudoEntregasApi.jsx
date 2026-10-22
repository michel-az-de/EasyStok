import { useId, useMemo, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { useEntregasApi } from '../../aplicacao/useEntregasApi'
import { chaveDaEntrega, chaveDaSaida } from '../../aplicacao/envioComDesfazer'
import { DesfazerComPrazo } from '../../componentes/DesfazerComPrazo'
import {
  EMPRESAS_ENTREGADOR, TIPOS_ENTREGADOR, confirmacaoDaRecusa, motivoDaAprovacao, motivoDeSaida, paineisDeEntregas,
  rotuloSituacaoViagem, situacaoDaLista, viagensConcluidasNoDia,
} from '../../dominio/entregasApi'
import { moeda } from '../../dominio/formato'
import { nomeDoPedido } from '../../dominio/pedido'
import { aReceberDoCartao } from '../../dominio/pagamento'
import { roteiroDoDia } from '../../dominio/roteiroDoDia'
import { CadastroEntregaApi } from './CadastroEntregaApi'
import { CorridasDaViagem } from './CorridasDaViagem'
import { Endereco, EntregasDoDia } from './EntregasDoDia'
import css from './entregasApi.module.css'

// Entregas no modo API (F04, issue #1221). Mesmo conteúdo na gaveta e na
// janela própria: aprovação da exceção (S12/S14), prontos para despachar,
// viagens (S44, RN-32), entregadores, chamados e o cadastro da loja (S45). Desde a #1440
// "Entregas de hoje" vem organizada por janela do dia, com o roteiro para imprimir.
// A máquina de estados e as regras são da API; a tela só mostra e chama.
const ABAS = [['hoje', 'Do dia'], ['entregadores', 'Entregadores'], ['cadastro', 'Janelas e frete']]

export function ConteudoEntregasApi() {
  const [aba, setAba] = useState('hoje')
  const id = useId()
  const abasRef = useRef({})
  const {
    pedidos, viagens, entregadores, chamados, erro, aoVivo, ocupado, acoes, limparErro, dia, mudarDia, doDia,
  } = useEntregasApi()
  const situacao = situacaoDaLista(pedidos, erro)
  const trocarAba = (proxima) => {
    // #1577: o roteiro não relê mais janelas e bloqueios a cada evento de pedido; o que ela
    // mexeu em "Janelas e frete" volta para ele na saída da aba.
    if (aba === 'cadastro' && proxima !== 'cadastro') acoes.recarregar()
    setAba(proxima)
    abasRef.current[proxima]?.focus()
  }
  const navegarAbas = (evento, indice) => {
    const destino = { ArrowRight: (indice + 1) % ABAS.length, ArrowLeft: (indice + ABAS.length - 1) % ABAS.length, Home: 0, End: ABAS.length - 1 }[evento.key]
    if (destino === undefined) return
    evento.preventDefault()
    trocarAba(ABAS[destino][0])
  }

  return (
    <div className={css.conteudo}>
      <div className={css.navegacao}>
        <div className={css.abas} role="tablist" aria-label="Seções de entregas">
          {ABAS.map(([chave, rotulo], indice) => (
            <Botao key={chave} ref={(elemento) => { abasRef.current[chave] = elemento }}
              id={`${id}-${chave}`} role="tab" aria-selected={aba === chave} aria-controls={`${id}-painel`}
              tabIndex={aba === chave ? 0 : -1} variante="texto" className={css.abaInterna}
              onKeyDown={(evento) => navegarAbas(evento, indice)} onClick={() => trocarAba(chave)}>
              {rotulo}
            </Botao>
          ))}
        </div>
        <output className={css.apoio}>{aoVivo ? 'Ao vivo' : 'Atualizando a cada 15 s'}</output>
      </div>
      {erro && (
        <p className={css.faixa} role="alert">
          <Icone nome="circle-x" tamanho={16} /> <span className={css.cresce}>{erro}</span>
          <Botao variante="secundario" disabled={ocupado} onClick={acoes.recarregar}>Atualizar entregas</Botao>
          {pedidos !== null && <Botao variante="texto" onClick={limparErro}>Fechar aviso</Botao>}
        </p>
      )}
      <div id={`${id}-painel`} role="tabpanel" aria-labelledby={`${id}-${aba}`} tabIndex={0} className={css.painel}>
        {aba === 'hoje' && situacao === 'carregando' && <p className={css.vazio}>Carregando as entregas…</p>}
        {aba === 'hoje' && situacao === 'falhou' && <p className={css.vazio}>Não foi possível carregar as entregas.</p>}
        {aba === 'hoje' && situacao === 'pronta' && <Hoje {...{ pedidos, viagens, entregadores, chamados, ocupado, acoes, dia, mudarDia, doDia }} aoAbrirCadastro={() => trocarAba('cadastro')} />}
        {aba === 'entregadores' && <Entregadores entregadores={entregadores} ocupado={ocupado} acoes={acoes} />}
        {aba === 'cadastro' && <CadastroEntregaApi />}
      </div>
    </div>
  )
}

function Hoje({ pedidos, viagens, entregadores, chamados, ocupado, acoes, dia, mudarDia, doDia, aoAbrirCadastro }) {
  const viagensRef = useRef(null)
  const p = paineisDeEntregas(pedidos, viagens)
  const roteiro = useMemo(() => (doDia ? roteiroDoDia({ ...doDia, viagens, entregadores }) : null), [doDia, viagens, entregadores])
  // Prontos que o dia escolhido não mostra (de outro dia), para nenhum ficar sem viagem.
  const prontosFora = roteiro ? p.prontos.filter((x) => !roteiro.ids.has(x.id)) : []
  const pedidoPorId = new Map(pedidos.map((x) => [x.id, x]))
  const opcoesEntregador = [{ valor: '', rotulo: 'Sem entregador' }, ...entregadores.map((e) => ({ valor: e.id, rotulo: e.nome }))]
  // #1474 (R7): recusar cancela, estorna e avisa o cliente; confirma em linha antes.
  const [recusando, setRecusando] = useState(null)
  const concluidas = viagensConcluidasNoDia(viagens, dia)

  return (
    <div className={css.painelDia}>
      {p.aprovacao.length > 0 && (
        <section className={css.secao} aria-label="Aguardando sua aprovação">
          <h3>Aguardando sua aprovação</h3>
          <ul className={css.lista}>
            {p.aprovacao.map((x) => (
              <li key={x.id} className={css.cartao}>
                <div className={css.linha}>
                  <span className={css.numero} title={nomeDoPedido(x).titulo || undefined}>{nomeDoPedido(x).principal}{nomeDoPedido(x).referencia && <small className={css.referencia}>ref {nomeDoPedido(x).referencia}</small>}</span>
                  <span className={css.cresce}>{x.clienteNome}</span>
                  <span className={css.apoio}>{motivoDaAprovacao(x.motivoRequerAprovacao)}</span>
                </div>
                <Endereco texto={x.endereco} />
                {recusando === x.id ? (
                  <div role="alertdialog" aria-label={`Recusar ${nomeDoPedido(x).frase}`}>
                    <p>{confirmacaoDaRecusa(nomeDoPedido(x).frase, x.clienteNome)}</p>
                    <div className={css.linha}>
                      <Botao variante="texto" disabled={ocupado} onClick={() => setRecusando(null)}>Manter</Botao>
                      <Botao variante="primario" disabled={ocupado} onClick={() => { setRecusando(null); acoes.recusar(x.id) }}>
                        Recusar e devolver
                      </Botao>
                    </div>
                  </div>
                ) : (
                  <div className={css.linha}>
                    <Botao variante="primario" disabled={ocupado} onClick={() => acoes.aprovar(x.id)}>Aprovar</Botao>
                    <Botao variante="texto" disabled={ocupado} onClick={() => setRecusando(x.id)}>Recusar</Botao>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      <EntregasDoDia dia={dia} mudarDia={mudarDia} roteiro={roteiro} montando={p.montando} ocupado={ocupado} acoes={acoes}
        aoAbrirCadastro={aoAbrirCadastro} aoVerViagens={() => viagensRef.current?.focus()} />

      {prontosFora.length > 0 && (
        <section className={css.secao} aria-label="Prontos de outro dia">
          <h3>Prontos de outro dia</h3>
          <ul className={css.lista}>
            {prontosFora.map((x) => (
              <li key={x.id} className={css.cartao}>
                <div className={css.linha}>
                  <span className={css.numero} title={nomeDoPedido(x).titulo || undefined}>{nomeDoPedido(x).principal}{nomeDoPedido(x).referencia && <small className={css.referencia}>ref {nomeDoPedido(x).referencia}</small>}</span>
                  <span className={css.cresce}>{[x.clienteNome, x.clienteApt].filter(Boolean).join(' · ')}</span>
                  {x.janela && <span className={css.apoio}>{x.janela.label}</span>}
                </div>
                <Endereco texto={x.endereco} />
                <div className={css.linha}>
                  {p.montando.map((v, i) => (
                    <Botao key={v.id} variante="texto" disabled={ocupado} onClick={() => acoes.incluirParada(v.id, x.id)}>
                      Pôr na viagem {i + 1}
                    </Botao>
                  ))}
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section ref={viagensRef} tabIndex={-1} className={css.secao} aria-label="Viagens">
        <div className={css.linha}>
          <h3 className={css.cresce}>Viagens</h3>
          <Botao variante="secundario" icone="plus" disabled={ocupado} onClick={() => acoes.criarViagem(null)}>Nova viagem</Botao>
        </div>
        {p.montando.length + p.emRota.length === 0 && <p className={css.vazio}>Nenhuma viagem montando ou na rua.</p>}
      </section>

      {[...p.montando, ...p.emRota].map((v, i) => (
        <Viagem
          key={v.id} viagem={v} indice={i + 1} pedidoPorId={pedidoPorId}
          opcoesEntregador={opcoesEntregador} entregadores={entregadores} ocupado={ocupado} acoes={acoes}
        />
      ))}

      {concluidas.length > 0 && (
        <section className={css.secao} aria-label="Viagens concluídas">
          <h3>Viagens concluídas</h3>
          <p className={css.apoio}>O preço da corrida mudou no fim? Anote ou corrija aqui.</p>
          {concluidas.map((v) => (
            <div key={v.id} className={css.cartao}>
              <p className={css.cresce}>
                {v.paradas.length === 1 ? '1 entrega' : `${v.paradas.length} entregas`}
                {v.paradas[0]?.entregadorNome ? ` · ${v.paradas[0].entregadorNome}` : ''}
              </p>
              <CorridasDaViagem viagem={v} entregadores={entregadores} ocupado={ocupado} />
            </div>
          ))}
        </section>
      )}

      <Chamados chamados={chamados} ocupado={ocupado} acoes={acoes} />
    </div>
  )
}

function Viagem({ viagem, indice, pedidoPorId, opcoesEntregador, entregadores, ocupado, acoes }) {
  const montando = viagem.situacao === 'Montando'
  const motivo = montando ? motivoDeSaida(viagem) : null
  const n = viagem.paradas.length
  // #1776 (item 19): entregar com saldo a receber pede confirmação. A tela pergunta pelo saldo que já
  // conhece; se ele mudou desde a leitura, a API recusa com o valor certo e a pergunta aparece igual.
  const [confirmando, setConfirmando] = useState(null) // { pedidoId, valor }
  const marcarEntregue = (pedidoId, confirmado = false) => acoes.entregue(viagem.id, pedidoId, confirmado)
    .then((r) => setConfirmando(r?.confirmar ? { pedidoId, valor: r.confirmar } : null))

  return (
    <section className={css.secao} aria-label={`Viagem ${indice}`}>
      <div className={css.linha}>
        <h3 className={css.cresce}>Viagem {indice} · {rotuloSituacaoViagem(viagem.situacao)}</h3>
        {viagem.rotaUrl && <a href={viagem.rotaUrl} target="_blank" rel="noreferrer">Rota no Maps</a>}
      </div>
      {montando && (
        <CampoSelecao
          rotulo="Entregador" opcoes={opcoesEntregador} value={viagem.entregadorId ?? ''} disabled={ocupado}
          onChange={(e) => acoes.definirEntregador(viagem.id, e.target.value)}
        />
      )}
      {n === 0 && <p className={css.vazio}>Viagem vazia. Ponha um pedido pronto nela.</p>}
      <ul className={css.lista}>
        {viagem.paradas.map((parada) => {
          const aReceber = aReceberDoCartao(pedidoPorId?.get(parada.pedidoId)) ?? 0
          return (
          <li key={parada.pedidoId} className={css.cartao}>
            <div className={css.linha}>
              <span className={css.numero}>{parada.ordem}. {parada.numeroPedido}</span>
              <span className={css.cresce}>{parada.clienteNome}</span>
              {parada.entregueEm && <span className={css.apoio}><Icone nome="check" tamanho={16} /> Entregue</span>}
            </div>
            <Endereco texto={parada.endereco} />
            {parada.entregadorNome && (
              <p className={css.apoio}>{[parada.entregadorNome, parada.veiculo, parada.placa].filter(Boolean).join(' · ')}</p>
            )}
            {!parada.entregueEm && aReceber > 0 && <p className={css.linha}><strong>Receber {moeda(aReceber)} na entrega</strong></p>}
            {acoes.adiadoAte(chaveDaEntrega(viagem.id, parada.pedidoId)) ? (
              <DesfazerComPrazo className={css.linha} ate={acoes.adiadoAte(chaveDaEntrega(viagem.id, parada.pedidoId))}
                texto={`Marca entregue e avisa ${parada.clienteNome}`}
                aoDesfazer={() => acoes.desfazerEnvio(chaveDaEntrega(viagem.id, parada.pedidoId))} />
            ) : confirmando?.pedidoId === parada.pedidoId ? (
              <div className={css.linha} role="alertdialog" aria-label={`Confirmar a entrega do pedido ${parada.numeroPedido}`}>
                <span className={`${css.apoio} ${css.cresce}`}>
                  Falta receber {moeda(confirmando.valor)}. Marque entregue se recebeu na porta ou combinou a cobrança; o valor segue em aberto até o pagamento ser lançado.
                </span>
                <Botao variante="primario" disabled={ocupado} onClick={() => marcarEntregue(parada.pedidoId, true)}>Marcar entregue</Botao>
                <Botao variante="texto" disabled={ocupado} onClick={() => setConfirmando(null)}>Voltar</Botao>
              </div>
            ) : (
            <div className={css.linha}>
              {montando && parada.ordem > 1 && (
                <Botao variante="texto" disabled={ocupado} onClick={() => acoes.reordenar(viagem.id, parada.pedidoId, parada.ordem - 1)}>Subir</Botao>
              )}
              {montando && parada.ordem < n && (
                <Botao variante="texto" disabled={ocupado} onClick={() => acoes.reordenar(viagem.id, parada.pedidoId, parada.ordem + 1)}>Descer</Botao>
              )}
              {montando && (
                <Botao variante="texto" disabled={ocupado} onClick={() => acoes.retirarParada(viagem.id, parada.pedidoId)}>Tirar</Botao>
              )}
              {!montando && !parada.entregueEm && (
                <Botao variante="primario" disabled={ocupado} onClick={() => (aReceber > 0
                  ? setConfirmando({ pedidoId: parada.pedidoId, valor: aReceber })
                  : marcarEntregue(parada.pedidoId))}>Entregue</Botao>
              )}
            </div>
            )}
          </li>
          )
        })}
      </ul>
      <CorridasDaViagem viagem={viagem} entregadores={entregadores} ocupado={ocupado} />
      {montando && acoes.adiadoAte(chaveDaSaida(viagem.id)) && (
        <DesfazerComPrazo className={css.linha} ate={acoes.adiadoAte(chaveDaSaida(viagem.id))}
          texto="A viagem sai e os clientes são avisados" aoDesfazer={() => acoes.desfazerEnvio(chaveDaSaida(viagem.id))} />
      )}
      {montando && !acoes.adiadoAte(chaveDaSaida(viagem.id)) && (
        <div className={css.linha}>
          <Botao variante="primario" icone="moto" disabled={ocupado || !!motivo} onClick={() => acoes.sair(viagem.id)}>
            Saiu para entrega
          </Botao>
          <Botao variante="texto" disabled={ocupado} onClick={() => acoes.desfazer(viagem.id)}>Desfazer viagem</Botao>
          {motivo && <span className={css.apoio}>{motivo}</span>}
        </div>
      )}
    </section>
  )
}

function Chamados({ chamados, ocupado, acoes }) {
  const [texto, setTexto] = useState('')
  const [confirmacao, setConfirmacao] = useState(null)
  const abrir = (e) => {
    e.preventDefault()
    if (!texto.trim()) return
    setConfirmacao(null)
    acoes.abrirChamado(texto.trim(), null).then((ok) => { if (ok) { setTexto(''); setConfirmacao('Chamado aberto. Acompanhe o atendimento na lista abaixo.') } })
  }
  return (
    <section className={css.secao} aria-label="Chamados de entregador">
      <h3>Chamar entregador</h3>
      <form className={css.linha} onSubmit={abrir}>
        <div className={css.cresce}>
          <CampoTexto rotulo="O que precisa" value={texto} onChange={(e) => setTexto(e.target.value)} maxLength={500} disabled={ocupado} />
        </div>
        <Botao tipo="submit" variante="secundario" disabled={ocupado || !texto.trim()}>Chamar</Botao>
      </form>
      {confirmacao && <output className={`${css.apoio} ${css.bloco}`}>{confirmacao}</output>}
      <ul className={css.lista}>
        {chamados.map((c) => (
          <li key={c.id} className={`${css.cartao} ${css.linha}`}>
            <span className={css.cresce}>{c.texto}</span>
            <Botao variante="texto" disabled={ocupado} onClick={() => acoes.atenderChamado(c.id)}>Atendido</Botao>
            <Botao variante="texto" disabled={ocupado} onClick={() => acoes.cancelarChamado(c.id)}>Cancelar</Botao>
          </li>
        ))}
      </ul>
    </section>
  )
}

const ENTREGADOR_VAZIO = { nome: '', tipo: 'Motoboy', empresa: 'Propria', telefone: '', veiculo: '', placa: '' }

function Entregadores({ entregadores, ocupado, acoes }) {
  const [form, setForm] = useState(ENTREGADOR_VAZIO)
  const [confirmacao, setConfirmacao] = useState(null)
  const campo = (k) => ({ value: form[k], disabled: ocupado, onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  const salvar = (e) => {
    e.preventDefault()
    if (!form.nome.trim()) return
    setConfirmacao(null)
    acoes.criarEntregador({ ...form, nome: form.nome.trim() }).then((ok) => {
      if (ok) { setForm(ENTREGADOR_VAZIO); setConfirmacao('Entregador cadastrado. Você já pode selecioná-lo nas viagens.') }
    })
  }
  return (
    <section className={css.secao} aria-label="Entregadores">
      <h3>Novo entregador</h3>
      <form className={css.formulario} onSubmit={salvar}>
        <CampoTexto rotulo="Nome" {...campo('nome')} required />
        <CampoSelecao rotulo="Tipo" opcoes={TIPOS_ENTREGADOR} {...campo('tipo')} />
        <CampoSelecao rotulo="Empresa" opcoes={EMPRESAS_ENTREGADOR} {...campo('empresa')} />
        <CampoTexto rotulo="Telefone" tipo="tel" {...campo('telefone')} />
        <CampoTexto rotulo="Veículo" {...campo('veiculo')} />
        <CampoTexto rotulo="Placa" {...campo('placa')} />
        <Botao tipo="submit" variante="primario" disabled={ocupado || !form.nome.trim()}>Cadastrar</Botao>
      </form>
      {confirmacao && <output className={`${css.apoio} ${css.bloco}`}>{confirmacao}</output>}
      <h3>Ativos</h3>
      {entregadores.length === 0 && <p className={css.vazio}>Nenhum entregador cadastrado.</p>}
      <ul className={css.lista}>
        {entregadores.map((e) => (
          <li key={e.id} className={`${css.cartao} ${css.linha}`}>
            <span className={css.cresce}>{e.nome}</span>
            <span className={css.apoio}>{[e.veiculo, e.placa, e.telefone].filter(Boolean).join(' · ')}</span>
            <Botao variante="texto" disabled={ocupado} onClick={() => acoes.desativarEntregador(e.id)}>Desativar</Botao>
          </li>
        ))}
      </ul>
    </section>
  )
}
