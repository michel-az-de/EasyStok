import { useMemo, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { useEntregasApi } from '../../aplicacao/useEntregasApi'
import {
  EMPRESAS_ENTREGADOR, TIPOS_ENTREGADOR, motivoDaAprovacao, motivoDeSaida, paineisDeEntregas, rotuloSituacaoViagem,
  situacaoDaLista,
} from '../../dominio/entregasApi'
import { roteiroDoDia } from '../../dominio/roteiroDoDia'
import { CadastroEntregaApi } from './CadastroEntregaApi'
import { Endereco, EntregasDoDia } from './EntregasDoDia'
import css from './entregasApi.module.css'

// Entregas no modo API (F04, issue #1221). Mesmo conteúdo na gaveta e na
// janela própria: aprovação da exceção (S12/S14), prontos para despachar,
// viagens (S44, RN-32), entregadores, chamados e o cadastro da loja (S45). Desde a #1440
// "Entregas de hoje" vem organizada por janela do dia, com o roteiro para imprimir.
// A máquina de estados e as regras são da API; a tela só mostra e chama.
export function ConteudoEntregasApi() {
  const [aba, setAba] = useState('hoje')
  const {
    pedidos, viagens, entregadores, chamados, erro, aoVivo, ocupado, acoes, limparErro, dia, mudarDia, doDia,
  } = useEntregasApi()
  const situacao = situacaoDaLista(pedidos, erro)

  return (
    <div className={css.conteudo}>
      <div className={css.abas} role="tablist">
        {[['hoje', 'Entregas de hoje'], ['entregadores', 'Entregadores'], ['cadastro', 'Janelas e frete']].map(([id, rotulo]) => (
          <Botao key={id} role="tab" aria-selected={aba === id} variante={aba === id ? 'primario' : 'secundario'} onClick={() => setAba(id)}>
            {rotulo}
          </Botao>
        ))}
        <span className={`${css.apoio} ${css.cresce}`}>{aoVivo ? 'Ao vivo' : 'Atualizando a cada 15 s'}</span>
      </div>
      {erro && (
        <p className={css.faixa} role="alert">
          <Icone nome="circle-x" tamanho={16} /> <span className={css.cresce}>{erro}</span>
          <Botao variante="texto" onClick={limparErro}>Fechar</Botao>
        </p>
      )}
      {aba === 'hoje' && situacao === 'carregando' && <p className={css.vazio}>Carregando as entregas…</p>}
      {aba === 'hoje' && situacao === 'falhou' && <p className={css.vazio}>Sem entregas para mostrar enquanto a carga falhar.</p>}
      {aba === 'hoje' && situacao === 'pronta' && <Hoje {...{ pedidos, viagens, entregadores, chamados, ocupado, acoes, dia, mudarDia, doDia }} />}
      {aba === 'entregadores' && <Entregadores entregadores={entregadores} ocupado={ocupado} acoes={acoes} />}
      {aba === 'cadastro' && <CadastroEntregaApi />}
    </div>
  )
}

function Hoje({ pedidos, viagens, entregadores, chamados, ocupado, acoes, dia, mudarDia, doDia }) {
  const p = paineisDeEntregas(pedidos, viagens)
  const roteiro = useMemo(() => (doDia ? roteiroDoDia({ ...doDia, viagens, entregadores }) : null), [doDia, viagens, entregadores])
  // Prontos que o dia escolhido não mostra (de outro dia), para nenhum ficar sem viagem.
  const prontosFora = roteiro ? p.prontos.filter((x) => !roteiro.ids.has(x.id)) : []
  const pedidoPorId = new Map(pedidos.map((x) => [x.id, x]))
  const opcoesEntregador = [{ valor: '', rotulo: 'Sem entregador' }, ...entregadores.map((e) => ({ valor: e.id, rotulo: e.nome }))]

  return (
    <>
      {p.aprovacao.length > 0 && (
        <section className={css.secao} aria-label="Aguardando sua aprovação">
          <h3>Aguardando sua aprovação</h3>
          <ul className={css.lista}>
            {p.aprovacao.map((x) => (
              <li key={x.id} className={css.cartao}>
                <div className={css.linha}>
                  <span className={css.numero}>{x.numeroCurto}</span>
                  <span className={css.cresce}>{x.clienteNome}</span>
                  <span className={css.apoio}>{motivoDaAprovacao(x.motivoRequerAprovacao)}</span>
                </div>
                <Endereco texto={x.endereco} />
                <div className={css.linha}>
                  <Botao variante="primario" disabled={ocupado} onClick={() => acoes.aprovar(x.id)}>Aprovar</Botao>
                  <Botao variante="texto" disabled={ocupado} onClick={() => acoes.recusar(x.id)}>Recusar</Botao>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      <EntregasDoDia dia={dia} mudarDia={mudarDia} roteiro={roteiro} montando={p.montando} ocupado={ocupado} acoes={acoes} />

      {prontosFora.length > 0 && (
        <section className={css.secao} aria-label="Prontos de outro dia">
          <h3>Prontos de outro dia</h3>
          <ul className={css.lista}>
            {prontosFora.map((x) => (
              <li key={x.id} className={css.cartao}>
                <div className={css.linha}>
                  <span className={css.numero}>{x.numeroCurto}</span>
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

      <section className={css.secao} aria-label="Viagens">
        <div className={css.linha}>
          <h3 className={css.cresce}>Viagens</h3>
          <Botao variante="secundario" icone="plus" disabled={ocupado} onClick={() => acoes.criarViagem(null)}>Nova viagem</Botao>
        </div>
        {p.montando.length + p.emRota.length === 0 && <p className={css.vazio}>Nenhuma viagem montando ou na rua.</p>}
      </section>

      {[...p.montando, ...p.emRota].map((v, i) => (
        <Viagem
          key={v.id} viagem={v} indice={i + 1} pedidoPorId={pedidoPorId}
          opcoesEntregador={opcoesEntregador} ocupado={ocupado} acoes={acoes}
        />
      ))}

      <Chamados chamados={chamados} ocupado={ocupado} acoes={acoes} />
    </>
  )
}

function Viagem({ viagem, indice, opcoesEntregador, ocupado, acoes }) {
  const montando = viagem.situacao === 'Montando'
  const motivo = montando ? motivoDeSaida(viagem) : null
  const n = viagem.paradas.length

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
        {viagem.paradas.map((parada) => (
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
                <Botao variante="primario" disabled={ocupado} onClick={() => acoes.entregue(viagem.id, parada.pedidoId)}>Entregue</Botao>
              )}
            </div>
          </li>
        ))}
      </ul>
      {montando && (
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
  const abrir = (e) => {
    e.preventDefault()
    if (!texto.trim()) return
    acoes.abrirChamado(texto.trim(), null).then(() => setTexto(''))
  }
  return (
    <section className={css.secao} aria-label="Chamados de entregador">
      <h3>Chamar entregador</h3>
      <form className={css.linha} onSubmit={abrir}>
        <div className={css.cresce}>
          <CampoTexto rotulo="O que precisa" value={texto} onChange={(e) => setTexto(e.target.value)} maxLength={500} />
        </div>
        <Botao tipo="submit" variante="secundario" disabled={ocupado || !texto.trim()}>Chamar</Botao>
      </form>
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
  const campo = (k) => ({ value: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  const salvar = (e) => {
    e.preventDefault()
    if (!form.nome.trim()) return
    acoes.criarEntregador({ ...form, nome: form.nome.trim() }).then(() => setForm(ENTREGADOR_VAZIO))
  }
  return (
    <section className={css.secao} aria-label="Entregadores">
      <h3>Novo entregador</h3>
      <form className={css.formulario} onSubmit={salvar}>
        <CampoTexto rotulo="Nome" {...campo('nome')} required />
        <CampoSelecao rotulo="Tipo" opcoes={TIPOS_ENTREGADOR} {...campo('tipo')} />
        <CampoSelecao rotulo="Empresa" opcoes={EMPRESAS_ENTREGADOR} {...campo('empresa')} />
        <CampoTexto rotulo="Telefone" {...campo('telefone')} />
        <CampoTexto rotulo="Veículo" {...campo('veiculo')} />
        <CampoTexto rotulo="Placa" {...campo('placa')} />
        <Botao tipo="submit" variante="primario" disabled={ocupado || !form.nome.trim()}>Cadastrar</Botao>
      </form>
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
