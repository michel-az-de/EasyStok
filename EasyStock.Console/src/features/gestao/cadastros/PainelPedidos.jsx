import { useEffect, useRef, useState } from 'react'
import { anunciarAberturaDeConversa } from '../../../aplicacao/aberturaDeConversa'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { contextoCadastro, useContextoVisualCadastro } from '../../../aplicacao/cadastros'
import { useAcoes, useAtendimento } from '../../../aplicacao/contextos'
import { obterPedido, useListaPedidos } from '../../../aplicacao/pedidosCadastro'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Combobox } from '../../../componentes/Combobox'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { telefoneNaTela } from '../../../dominio/cadastros'
import { horaCurta, moeda } from '../../../dominio/formato'
import { hashDoModulo } from '../../../dominio/modulos'
import {
  numeroDoPedido, ORDENS_PEDIDO, PAGINA_PEDIDOS, PERIODOS_PEDIDO, rotuloDaSituacao, rotuloDoCanal, SITUACOES_PEDIDO,
  situacaoDoPagamento, situacaoPedeAcao, tituloDoPedido,
} from '../../../dominio/pedidosCadastro'
import { DetalhePedido } from './DetalhePedido'
import base from './cadastros.module.css'
import css from './pedidos.module.css'

const SITUACOES = [{ valor: '', rotulo: 'Todas' }, ...SITUACOES_PEDIDO]
const INICIAL = { busca: '', pagina: 1, situacao: '', periodo: 'tudo', ordem: 'mais-recentes', posicao: 0 }
const FILTROS = ['busca', 'situacao', 'periodo', 'ordem']

// #2478: todos os pedidos da loja, de qualquer canal e dia, em Cadastros. Daqui não se altera pedido; o detalhe abre num modal
// e a lista guarda filtros, página e rolagem enquanto a pessoa navega pelo console.
export function PainelPedidos({ aoAbrirConversa }) {
  const { acoes } = useAcessoModulos()
  const contexto = contextoCadastro()
  if (acoes.verPedidos !== true) return <Vazio titulo="Esta área precisa de outro acesso">Pedidos abre para quem atende ou gerencia a loja. Peça à pessoa responsável para conferir o seu perfil.</Vazio>
  return <ListaPedidos key={contexto.identidade} contexto={contexto} aoAbrirConversa={aoAbrirConversa} />
}

function ListaPedidos({ contexto, aoAbrirConversa }) {
  const [memoria, guardar] = useContextoVisualCadastro('pedidos', contexto.identidade, INICIAL)
  const { busca, pagina, situacao, periodo, ordem } = memoria
  const definir = (valores) => guardar({ ...memoria, ...valores })
  const filtrar = (valores) => definir({ ...valores, pagina: 1, posicao: 0 })
  const consulta = useListaPedidos({ busca, pagina, situacao, periodo, ordem }, contexto)
  const [detalhe, mostrar] = useState(null)
  const [abrindo, abrir] = useState(null)
  const [erro, falhar] = useState(null)
  const leitura = useRef(null)
  const tabela = useRef(null)
  const { conversas } = useAtendimento()
  const { selecionar } = useAcoes()
  const comFiltro = FILTROS.some((campo) => memoria[campo] !== INICIAL[campo])
  const paginas = Math.max(1, Math.ceil(consulta.total / PAGINA_PEDIDOS))
  useEffect(() => () => leitura.current?.abort(), [])
  useEffect(() => { if (tabela.current && consulta.carregado && !consulta.carregando) tabela.current.scrollTop = memoria.posicao ?? 0 }, [consulta.carregando])
  // Filtro que encolhe a lista (outra pessoa cancelou, o período virou o dia) não deixa a página vazia além do fim.
  useEffect(() => { if (consulta.carregado && !consulta.carregando && !consulta.erro && pagina > paginas) definir({ pagina: paginas, posicao: 0 }) }, [consulta.carregando])

  async function verPedido(item) {
    if (abrindo) return
    falhar(null); abrir(item.id)
    const controle = new AbortController(); leitura.current?.abort(); leitura.current = controle
    try {
      const dados = await obterPedido(item.id, contexto, controle.signal)
      if (!controle.signal.aborted) mostrar(dados)
    } catch (e) { if (!controle.signal.aborted) falhar(e.message) }
    finally { if (!controle.signal.aborted) abrir(null) }
  }

  // A conversa da cliente já carregada no Atendimento; sem ela o detalhe não oferece o atalho.
  const abridorDaConversa = (pedido) => {
    const lista = conversas ?? []
    const conversa = lista.find((c) => c.pedido?.pedidoId === pedido.id) ?? (pedido.clienteId ? lista.find((c) => c.clienteId === pedido.clienteId) : null)
    if (!conversa) return null
    return () => { selecionar(conversa.id); window.location.hash = hashDoModulo('atendimento', 'balcao'); anunciarAberturaDeConversa(conversa.id); aoAbrirConversa?.(conversa.id) }
  }
  const lista = consulta.itens
  const primeiraCarga = consulta.carregando && !consulta.carregado
  return <section className={base.painel} aria-label="Pedidos">
    <header className={base.cabecalho}>
      <div><h2>Pedidos</h2><p>Todos os pedidos da loja, de qualquer canal e de qualquer dia.</p></div>
    </header>
    {(erro || consulta.erro) && <div className={base.erro} role="alert"><p>{erro || consulta.erro}</p>
      <Botao onClick={() => { falhar(null); if (consulta.erro) consulta.recarregar() }}>{consulta.erro ? 'Tentar de novo' : 'Entendi'}</Botao></div>}
    <div className={base.superficie}>
      <div className={css.filtros}>
        <CampoTexto rotulo="Buscar pedido" tipo="search" placeholder="Nº, nome ou telefone" value={busca} onChange={(e) => filtrar({ busca: e.target.value })} />
        <Combobox rotulo="Feito em" valor={periodo} opcoes={PERIODOS_PEDIDO} aoMudar={(valor) => filtrar({ periodo: valor })} />
        <Combobox rotulo="Situação" valor={situacao} opcoes={SITUACOES} aoMudar={(valor) => filtrar({ situacao: valor })} />
        <Combobox rotulo="Ordem" valor={ordem} opcoes={ORDENS_PEDIDO} aoMudar={(valor) => filtrar({ ordem: valor })} />
        <Botao icone="refresh-cw" disabled={consulta.carregando} onClick={consulta.recarregar}>Atualizar</Botao>
      </div>
      {primeiraCarga ? <Vazio titulo="Carregando pedidos">Aguarde um instante.</Vazio>
        : lista.length === 0 && !consulta.erro ? <Vazio titulo={comFiltro ? 'Nenhum pedido com estes filtros' : 'Nenhum pedido ainda'}
          acao={comFiltro ? <Botao onClick={() => filtrar({ busca: '', situacao: '', periodo: 'tudo', ordem: 'mais-recentes' })}>Limpar filtros</Botao> : undefined}>
          {comFiltro ? 'Tente outro número, nome ou período.' : 'Os pedidos do site, do WhatsApp e do balcão aparecem aqui.'}</Vazio>
        : lista.length > 0 && <div ref={tabela} className={`${base.lista} ${css.tabela}`} aria-busy={consulta.carregando} onScroll={(e) => definir({ posicao: e.currentTarget.scrollTop })}>
          <table>
            <caption className="sr">Pedidos, página {pagina} de {paginas}</caption>
            <thead><tr><th scope="col">Pedido</th><th scope="col">Cliente</th><th scope="col">Situação</th><th scope="col" className={css.valor}>Total</th><th scope="col"><span className="sr">Ações</span></th></tr></thead>
            <tbody>{lista.map((item) => {
              const pagamento = situacaoDoPagamento(item)
              const telefone = telefoneNaTela(item.clienteTelefone)
              const canal = rotuloDoCanal(item.origem)
              const nome = item.clienteNome || 'Cliente sem nome'
              const rotulo = rotuloDaSituacao(item.status)
              const situacaoNaLinha = situacaoPedeAcao(item.status) ? <Pilula tom="aviso">{rotulo}</Pilula> : rotulo
              return <tr key={item.id} className={base.clicavel} onClick={(e) => { if (!e.target.closest('button, a')) verPedido(item) }}>
                <td><strong>{numeroDoPedido(item)}</strong><small>{[item.criadoEm && horaCurta(item.criadoEm), canal].filter(Boolean).join(' · ')}</small>
                  <span className={css.soCelular}><small>{nome}</small><small>{situacaoNaLinha}</small></span></td>
                <td><span>{nome}</span>{(telefone || item.clienteApt) && <small>{[telefone, item.clienteApt && `Apto ${item.clienteApt}`].filter(Boolean).join(' · ')}</small>}</td>
                <td>{situacaoNaLinha}</td>
                <td className={css.valor}><span>{moeda(item.total)}</span>{(pagamento?.falta > 0 || pagamento?.sobra > 0) && <small className={css.falta}>{pagamento.texto}</small>}</td>
                <td><Botao disabled={Boolean(abrindo)} onClick={() => verPedido(item)} aria-label={`Abrir ${tituloDoPedido(item)}, ${nome}`}>{abrindo === item.id ? 'Abrindo…' : 'Abrir'}</Botao></td>
              </tr>
            })}</tbody>
          </table>
        </div>}
      <footer className={base.rodape}>
        <span role="status">{consulta.carregando ? 'Atualizando…' : consulta.carregado ? `${consulta.total} ${consulta.total === 1 ? 'pedido' : 'pedidos'}` : ''}</span>
        {paginas > 1 && <div className={base.acoes}>
          <Botao disabled={pagina <= 1 || consulta.carregando} onClick={() => definir({ pagina: pagina - 1, posicao: 0 })}>Anterior</Botao>
          <span>Página {pagina} de {paginas}</span>
          <Botao disabled={pagina >= paginas || consulta.carregando} onClick={() => definir({ pagina: pagina + 1, posicao: 0 })}>Próxima</Botao>
        </div>}
      </footer>
    </div>
    {detalhe && <DetalhePedido dados={detalhe} aoFechar={() => mostrar(null)} abrirConversa={abridorDaConversa(detalhe.pedido)} />}
  </section>
}
