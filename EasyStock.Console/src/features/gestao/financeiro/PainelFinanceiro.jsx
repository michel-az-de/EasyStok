import { useCallback, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto, CampoSelecao } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { MarcaIcone } from '../../../componentes/MarcaIcone'
import { Vazio } from '../../../componentes/Vazio'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useConsultaFinanceira, listarContasFinanceiras, obterContaFinanceira, obterResumoFinanceiro, obterExtratoFinanceiro, obterRelatorioFinanceiro, listarCategoriasFinanceiras, listarCentrosFinanceiros, emitirContaFinanceira, cancelarContaFinanceira, estornarParcelaFinanceira, removerParcelaFinanceira, inativarCadastroFinanceiro } from '../../../aplicacao/useFinanceiroApi'
import { STATUS_CONTAS, nomeStatusConta, diaFinanceiro, periodoFinanceiro } from '../../../dominio/financeiro'
import { moeda } from '../../../dominio/formato'
import { nomeDoMetodoCaixa } from '../../../dominio/caixa'
import { hashDoModulo } from '../../../dominio/modulos'
import { ModalContaFinanceira, ModalPagamentoFinanceiro, ModalConfirmacaoFinanceira, ModalCadastroFinanceiro, ModalParcelaFinanceira, ErroFinanceiro } from './ModaisFinanceiras'
import css from './financeiro.module.css'

const dataCurta = (valor) => diaFinanceiro(valor).split('-').reverse().join('/')
const carregarCadastros = async () => { const [categorias, centros] = await Promise.all([listarCategoriasFinanceiras(), listarCentrosFinanceiros()]); return { categorias, centros } }
const pagoNaConta = (conta) => conta.totalPago ?? conta.totalRecebido
const vencimentoDaConta = (conta) => conta.parcelas.filter((item) => item.saldo > 0).sort((a, b) => Date.parse(a.dataVencimento) - Date.parse(b.dataVencimento))[0]?.dataVencimento

function EstadoFinanceiro({ consulta, children }) {
  if (consulta.erro) return <div className={css.painel}><ErroFinanceiro erro={consulta.erro} /><Botao onClick={consulta.recarregar}>Tentar novamente</Botao></div>
  if (consulta.carregando && !consulta.dados) return <Vazio titulo="Carregando financeiro">Aguarde um instante.</Vazio>
  return <div aria-busy={consulta.carregando}>{children}</div>
}
function Indicador({ nome, valor, detalhe }) { return <div className={css.indicador}><span>{nome}</span><strong>{moeda(valor)}</strong>{detalhe && <small>{detalhe}</small>}</div> }
function SituacaoConta({ status }) { return <Pilula tom={status === 'Vencida' ? 'erro' : status === 'Paga' ? 'ok' : 'neutro'}>{nomeStatusConta(status)}</Pilula> }

function ResumoFinanceiro() {
  const consulta = useConsultaFinanceira(obterResumoFinanceiro)
  const resumo = consulta.dados
  return <EstadoFinanceiro consulta={consulta}>{resumo && <div className={css.painel}>
    <div className={css.barra}><p className={css.aviso}>Posição atual das contas e valores realizados no mês.</p><Botao icone="refresh-cw" onClick={consulta.recarregar}>Atualizar</Botao></div>
    <div className={css.indicadores}>
      <Indicador nome="A pagar nos próximos 30 dias" valor={resumo.totalAVencer30dPagar} detalhe={`${resumo.qtdContasPagarAVencer30d} ${resumo.qtdContasPagarAVencer30d === 1 ? 'conta' : 'contas'}`} />
      <Indicador nome="A receber nos próximos 30 dias" valor={resumo.totalAVencer30dReceber} detalhe={`${resumo.qtdContasReceberAVencer30d} ${resumo.qtdContasReceberAVencer30d === 1 ? 'conta' : 'contas'}`} />
      <Indicador nome="Pagamentos vencidos" valor={resumo.totalVencidoPagar} />
      <Indicador nome="Recebimentos vencidos" valor={resumo.totalVencidoReceber} />
      <Indicador nome="Pago no mês" valor={resumo.totalPagoMes} />
      <Indicador nome="Recebido no mês" valor={resumo.totalRecebidoMes} />
    </div>
    <div className={css.atalhos}>{[['pagar', 'Contas a pagar', 'caixa'], ['receber', 'Contas a receber', 'financeiro'], ['caixa', 'Caixa do dia', 'caixa'], ['extrato', 'Extrato', 'receita'], ['relatorios', 'Relatórios', 'grade']].map(([id, nome, icone]) => <a key={id} className={css.atalho} href={hashDoModulo('financeiro', id)}><MarcaIcone nome={icone} tamanho={28} />{nome}</a>)}</div>
  </div>}</EstadoFinanceiro>
}

function ContasFinanceiras({ lado }) {
  const { acoes } = useAcessoModulos()
  const podeGerenciar = acoes[lado === 'pagar' ? 'gerenciarContasPagar' : 'gerenciarContasReceber'] === true
  const [rascunhoFiltro, setRascunhoFiltro] = useState({ busca: '', status: '', vencimentoDe: '', vencimentoAte: '' })
  const [filtros, setFiltros] = useState({ ...rascunhoFiltro, page: 1 })
  const carregar = useCallback(() => listarContasFinanceiras(lado, filtros), [lado, filtros])
  const consulta = useConsultaFinanceira(carregar)
  const cadastros = useConsultaFinanceira(carregarCadastros)
  const [nova, setNova] = useState(false)
  const [selecionadaId, setSelecionadaId] = useState(null)
  const [detalheId, setDetalheId] = useState(null)
  const selecionada = consulta.dados?.itens.find((item) => item.id === selecionadaId)
  const campo = (nome, valor) => setRascunhoFiltro((atual) => ({ ...atual, [nome]: valor }))
  return <div className={css.painel}>
    <div className={css.barra}><p className={css.aviso}>{consulta.dados ? `${consulta.dados.total} ${consulta.dados.total === 1 ? 'conta encontrada' : 'contas encontradas'}` : 'Consulte vencimentos, parcelas e pagamentos.'}</p><div className={css.acoes}><Botao onClick={consulta.recarregar} icone="refresh-cw">Atualizar</Botao>{podeGerenciar && <Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => setNova(true)}>Nova conta a {lado}</Botao>}</div></div>
    <form className={css.filtros} onSubmit={(evento) => { evento.preventDefault(); setFiltros({ ...rascunhoFiltro, page: 1 }) }}>
      <CampoTexto rotulo="Buscar conta" value={rascunhoFiltro.busca} onChange={(e) => campo('busca', e.target.value)} />
      <CampoSelecao rotulo="Situação" value={rascunhoFiltro.status} onChange={(e) => campo('status', e.target.value)} opcoes={[{ valor: '', rotulo: 'Todas' }, ...STATUS_CONTAS.map((valor) => ({ valor, rotulo: nomeStatusConta(valor) }))]} />
      <CampoTexto rotulo="Vencimento de" tipo="date" value={rascunhoFiltro.vencimentoDe} onChange={(e) => campo('vencimentoDe', e.target.value)} />
      <CampoTexto rotulo="Vencimento até" tipo="date" min={rascunhoFiltro.vencimentoDe || undefined} value={rascunhoFiltro.vencimentoAte} onChange={(e) => campo('vencimentoAte', e.target.value)} />
      <Botao tipo="submit" icone="busca">Filtrar</Botao>
    </form>
    <EstadoFinanceiro consulta={consulta}>
      <div className={css.comContexto}>
        <div className={css.painel}>
          {consulta.dados?.itens.length === 0 && <Vazio titulo="Nenhuma conta encontrada">Revise os filtros ou cadastre uma conta.</Vazio>}
          <ul className={css.lista}>{consulta.dados?.itens.map((conta) => <li key={conta.id} className={`${css.linha} ${conta.id === selecionadaId ? css.linhaSelecionada : ''}`}>
            <button type="button" className={css.selecionar} aria-pressed={conta.id === selecionadaId} onClick={() => setSelecionadaId(conta.id)}><strong>{conta.descricao}</strong><small>{conta.parcelas.length} {conta.parcelas.length === 1 ? 'parcela' : 'parcelas'} · Total {moeda(conta.valorTotal)}{vencimentoDaConta(conta) ? ` · Vence ${dataCurta(vencimentoDaConta(conta))}` : ''}</small></button>
            <span className={css.valor}>{moeda(conta.pendente)}</span><SituacaoConta status={conta.status} />
            <Botao aria-label={`Abrir conta ${conta.descricao}`} aria-haspopup="dialog" onClick={() => { setSelecionadaId(conta.id); setDetalheId(conta.id) }}>Abrir</Botao>
          </li>)}</ul>
          {consulta.dados?.total > 20 && <div className={css.paginacao}><Botao disabled={filtros.page === 1 || consulta.carregando} onClick={() => setFiltros((atual) => ({ ...atual, page: atual.page - 1 }))}>Anterior</Botao><span>Página {filtros.page} de {Math.ceil(consulta.dados.total / 20)}</span><Botao disabled={filtros.page * 20 >= consulta.dados.total || consulta.carregando} onClick={() => setFiltros((atual) => ({ ...atual, page: atual.page + 1 }))}>Próxima</Botao></div>}
        </div>
        <aside className={css.contexto} aria-label="Resumo da conta selecionada">{selecionada ? <>
          <h2>{selecionada.descricao}</h2><SituacaoConta status={selecionada.status} />
          <dl className={css.detalhes}><div><dt>Total</dt><dd>{moeda(selecionada.valorTotal)}</dd></div><div><dt>{lado === 'pagar' ? 'Pago' : 'Recebido'}</dt><dd>{moeda(pagoNaConta(selecionada))}</dd></div><div><dt>Pendente</dt><dd>{moeda(selecionada.pendente)}</dd></div><div><dt>Emissão</dt><dd>{dataCurta(selecionada.dataEmissao)}</dd></div></dl>
          <Botao variante="primario" aria-haspopup="dialog" onClick={() => setDetalheId(selecionada.id)}>Parcelas e histórico</Botao>
        </> : <><h2>Conta selecionada</h2><p>Selecione uma conta para acompanhar seu saldo. Abra as parcelas para registrar {lado === 'pagar' ? 'pagamentos' : 'recebimentos'} ou consultar o histórico.</p></>}</aside>
      </div>
    </EstadoFinanceiro>
    {nova && <EstadoCadastroEmModal consulta={cadastros} aoFechar={() => setNova(false)}><ModalContaFinanceira lado={lado} cadastros={cadastros.dados} aoFechar={() => setNova(false)} aoGravar={consulta.recarregar} /></EstadoCadastroEmModal>}
    {detalheId && <ModalDetalheConta lado={lado} id={detalheId} cadastros={cadastros} podeGerenciar={podeGerenciar} aoFechar={() => setDetalheId(null)} aoGravar={consulta.recarregar} />}
  </div>
}

function EstadoCadastroEmModal({ consulta, aoFechar, children }) {
  return consulta.dados && !consulta.erro ? children : <Modal titulo="Cadastros financeiros" aoFechar={aoFechar}><EstadoFinanceiro consulta={consulta} /></Modal>
}

function ModalDetalheConta({ lado, id, cadastros, podeGerenciar, aoFechar, aoGravar }) {
  const carregar = useCallback(() => obterContaFinanceira(lado, id), [lado, id])
  const consulta = useConsultaFinanceira(carregar)
  const conta = consulta.dados
  const [modal, setModal] = useState(null)
  const atualizado = async () => { await Promise.all([consulta.recarregar(), aoGravar()]) }
  const confirmar = (titulo, descricao, acao, exigirMotivo = false) => setModal({ tipo: 'confirmar', titulo, descricao, acao, exigirMotivo })
  return <Modal titulo={conta?.descricao ?? 'Detalhes da conta'} aoFechar={aoFechar} largura="min(900px, calc(100vw - 24px))">
    <EstadoFinanceiro consulta={consulta}>{conta && <div className={css.painel}>
      <div className={css.barra}><SituacaoConta status={conta.status} /><div className={css.acoes}>{podeGerenciar && <>
        <Botao aria-haspopup="dialog" onClick={() => setModal({ tipo: 'editar' })}>Editar conta</Botao>
        {conta.status === 'Rascunho' && <><Botao aria-haspopup="dialog" onClick={() => setModal({ tipo: 'parcela' })}>Adicionar parcela</Botao><Botao variante="primario" aria-haspopup="dialog" onClick={() => confirmar('Emitir conta', 'A conta emitida passa a compor os vencimentos do financeiro.', () => emitirContaFinanceira(lado, id))}>Emitir conta</Botao></>}
        {!['Cancelada', 'Paga'].includes(conta.status) && <Botao aria-haspopup="dialog" onClick={() => confirmar('Cancelar conta', 'O histórico permanece registrado. Informe o motivo do cancelamento.', (motivo) => cancelarContaFinanceira(lado, id, motivo), true)}>Cancelar conta</Botao>}
      </>}</div></div>
      <dl className={css.detalhes}><div><dt>Valor total</dt><dd>{moeda(conta.valorTotal)}</dd></div><div><dt>Pendente</dt><dd>{moeda(conta.pendente)}</dd></div><div><dt>Categoria</dt><dd>{cadastros.dados?.categorias.find((item) => item.id === conta.categoriaFinanceiraId)?.nome ?? 'Categoria indisponível'}</dd></div><div><dt>Centro de custo</dt><dd>{cadastros.dados?.centros.find((item) => item.id === conta.centroCustoId)?.nome ?? 'Sem centro de custo'}</dd></div></dl>
      {conta.observacoes && <p>{conta.observacoes}</p>}
      {conta.parcelas.map((parcela) => <section key={parcela.id} className={css.parcela} aria-label={`Parcela ${parcela.numero}`}>
        <div className={css.barra}><h3>Parcela {parcela.numero} · {dataCurta(parcela.dataVencimento)}</h3><span className={css.valor}>{moeda(parcela.valor)} · Saldo {moeda(parcela.saldo)}</span></div>
        <div className={css.acoes}>{parcela.saldo > 0 && !['Rascunho', 'Cancelada'].includes(conta.status) && <Botao variante="primario" aria-haspopup="dialog" onClick={() => setModal({ tipo: 'pagamento', parcela })}>{lado === 'pagar' ? 'Registrar pagamento' : 'Registrar recebimento'}</Botao>}
          {podeGerenciar && conta.status === 'Rascunho' && conta.parcelas.length > 1 && <Botao aria-haspopup="dialog" onClick={() => confirmar('Remover parcela', `Remover a parcela ${parcela.numero} de ${moeda(parcela.valor)} do rascunho?`, () => removerParcelaFinanceira(lado, id, parcela.id))}>Remover parcela</Botao>}
        </div>
        {parcela.pagamentos.length ? <ul className={css.historico}>{parcela.pagamentos.map((pagamento) => <li key={pagamento.id}><div className={css.barra}><span>{dataCurta(pagamento.dataPagamento)} · {nomeDoMetodoCaixa(pagamento.metodo)} · {moeda(pagamento.valor)} · {pagamento.status}</span>{podeGerenciar && pagamento.status !== 'Estornado' && <Botao aria-haspopup="dialog" onClick={() => confirmar('Estornar registro', 'O estorno corrige o registro financeiro e o Caixa. Informe o motivo.', (motivo) => estornarParcelaFinanceira(lado, parcela.id, pagamento.id, motivo), true)}>Estornar</Botao>}</div>{pagamento.observacao && <p className={css.apoio}>{pagamento.observacao}</p>}{pagamento.motivoEstorno && <p className={css.apoio}>Motivo: {pagamento.motivoEstorno}</p>}</li>)}</ul> : <p className={css.apoio}>Nenhum pagamento registrado nesta parcela.</p>}
      </section>)}
    </div>}</EstadoFinanceiro>
    {modal?.tipo === 'editar' && <EstadoCadastroEmModal consulta={cadastros} aoFechar={() => setModal(null)}><ModalContaFinanceira lado={lado} conta={conta} cadastros={cadastros.dados} aoFechar={() => setModal(null)} aoGravar={atualizado} /></EstadoCadastroEmModal>}
    {modal?.tipo === 'parcela' && <ModalParcelaFinanceira lado={lado} conta={conta} aoFechar={() => setModal(null)} aoGravar={atualizado} />}
    {modal?.tipo === 'pagamento' && <ModalPagamentoFinanceiro lado={lado} conta={conta} parcela={modal.parcela} aoFechar={() => setModal(null)} aoGravar={atualizado} />}
    {modal?.tipo === 'confirmar' && <ModalConfirmacaoFinanceira {...modal} aoFechar={() => setModal(null)} aoGravar={atualizado} />}
  </Modal>
}

function PeriodoFinanceiro({ periodo, aoMudar }) {
  return <><CampoTexto rotulo="De" tipo="date" required value={periodo.inicio} onChange={(e) => aoMudar({ ...periodo, inicio: e.target.value })} /><CampoTexto rotulo="Até" tipo="date" required min={periodo.inicio} value={periodo.fim} onChange={(e) => aoMudar({ ...periodo, fim: e.target.value })} /></>
}

function ExtratoFinanceiro() {
  const { acoes } = useAcessoModulos()
  const [rascunho, setRascunho] = useState(periodoFinanceiro)
  const [periodo, setPeriodo] = useState(rascunho)
  const carregar = useCallback(() => obterExtratoFinanceiro(periodo, acoes), [periodo, acoes])
  const consulta = useConsultaFinanceira(carregar)
  const [linha, setLinha] = useState(null)
  const linhas = consulta.dados ?? []
  const total = (lado) => linhas.filter((item) => !item.estornado && item.lado === lado).reduce((soma, item) => soma + Math.round(item.valor * 100), 0) / 100
  return <div className={css.painel}>
    <form className={css.filtros} onSubmit={(e) => { e.preventDefault(); setPeriodo(rascunho) }}><PeriodoFinanceiro periodo={rascunho} aoMudar={setRascunho} /><Botao tipo="submit">Consultar extrato</Botao></form>
    <EstadoFinanceiro consulta={consulta}><div className={css.painel}>
      <p className={css.aviso}>Movimentos do Caixa e pagamentos das contas acessíveis ao seu perfil. Registros vinculados ao Caixa aparecem uma única vez; estornos permanecem no histórico.</p>
      <div className={css.indicadores}><Indicador nome="Entradas do período" valor={total('receber')} /><Indicador nome="Saídas do período" valor={total('pagar')} /><Indicador nome="Resultado dos movimentos" valor={total('receber') - total('pagar')} /></div>
      {!linhas.length && <Vazio titulo="Sem movimentos no período">Escolha outro intervalo para consultar.</Vazio>}
      <ul className={css.lista}>{linhas.map((item) => <li key={item.id} className={css.linha}><div><strong>{item.descricao}</strong><p className={css.apoio}>{dataCurta(item.em)} · {nomeDoMetodoCaixa(item.metodo)} · {item.lado === 'pagar' ? 'Saída' : 'Entrada'}{item.estornado ? ' · Estornado' : ''}</p></div><span className={css.valor}>{moeda(item.valor)}</span><Botao aria-haspopup="dialog" aria-label={`Detalhes de ${item.descricao}`} onClick={() => setLinha(item)}>Detalhes</Botao></li>)}</ul>
    </div></EstadoFinanceiro>
    {linha && <Modal titulo="Movimento financeiro" aoFechar={() => setLinha(null)}><dl className={css.detalhes}><div><dt>Descrição</dt><dd>{linha.descricao}</dd></div><div><dt>Data</dt><dd>{dataCurta(linha.em)}</dd></div><div><dt>Valor</dt><dd>{moeda(linha.valor)}</dd></div><div><dt>Situação</dt><dd>{linha.estornado ? 'Estornado' : 'Registrado'}</dd></div></dl>{linha.contaId && <a className={css.atalho} href={hashDoModulo('financeiro', linha.lado)} onClick={() => setLinha(null)}>Consultar conta a {linha.lado}</a>}</Modal>}
  </div>
}

function RelatoriosFinanceiros() {
  const [rascunho, setRascunho] = useState(() => ({ ...periodoFinanceiro(), periodicidade: 'Mensal', categoriaId: '', centroCustoId: '' }))
  const [filtros, setFiltros] = useState(rascunho)
  const carregar = useCallback(() => obterRelatorioFinanceiro(filtros), [filtros])
  const consulta = useConsultaFinanceira(carregar)
  const cadastros = useConsultaFinanceira(carregarCadastros)
  const [aberto, setAberto] = useState(false)
  const [exportando, setExportando] = useState(false)
  const [erroExportacao, setErroExportacao] = useState(null)
  const campo = (nome, valor) => setRascunho((atual) => ({ ...atual, [nome]: valor }))
  const tabela = <div className={css.tabelaRolavel}><table className={css.tabela}><caption className="sr">Fluxo de caixa previsto e realizado de {filtros.inicio} a {filtros.fim}</caption><thead><tr>{['Período', 'A pagar', 'A receber', 'Pago', 'Recebido', 'Saldo realizado'].map((nome) => <th scope="col" key={nome}>{nome}</th>)}</tr></thead><tbody>{consulta.dados?.map((item) => <tr key={item.inicioBucket}><th scope="row">{item.rotulo}</th>{[item.previstoPagar, item.previstoReceber, item.realizadoPagar, item.realizadoReceber, item.realizadoReceber - item.realizadoPagar].map((valor, indice) => <td key={indice}>{moeda(valor)}</td>)}</tr>)}</tbody></table></div>
  const exportar = async () => {
    setExportando(true); setErroExportacao(null)
    try {
      const { baixarCsv } = await import('../../../componentes/baixarCsv')
      baixarCsv(`financeiro-${filtros.inicio}-${filtros.fim}.csv`, ['Período', 'A pagar', 'A receber', 'Pago', 'Recebido'], consulta.dados.map((item) => [item.rotulo, item.previstoPagar, item.previstoReceber, item.realizadoPagar, item.realizadoReceber]))
    } catch (erro) { setErroExportacao(erro.message) } finally { setExportando(false) }
  }
  return <div className={css.painel}>
    <form className={css.filtros} onSubmit={(e) => { e.preventDefault(); setFiltros(rascunho) }}><PeriodoFinanceiro periodo={rascunho} aoMudar={setRascunho} />
      <CampoSelecao rotulo="Agrupar por" value={rascunho.periodicidade} onChange={(e) => campo('periodicidade', e.target.value)} opcoes={['Diario', 'Semanal', 'Mensal'].map((valor) => ({ valor, rotulo: { Diario: 'Dia', Semanal: 'Semana', Mensal: 'Mês' }[valor] }))} />
      <CampoSelecao rotulo="Categoria" value={rascunho.categoriaId} onChange={(e) => campo('categoriaId', e.target.value)} opcoes={[{ valor: '', rotulo: 'Todas' }, ...(cadastros.dados?.categorias ?? []).map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
      <CampoSelecao rotulo="Centro de custo" value={rascunho.centroCustoId} onChange={(e) => campo('centroCustoId', e.target.value)} opcoes={[{ valor: '', rotulo: 'Todos' }, ...(cadastros.dados?.centros ?? []).map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
      <Botao tipo="submit">Gerar relatório</Botao>
    </form><ErroFinanceiro erro={cadastros.erro} />
    <EstadoFinanceiro consulta={consulta}><div className={css.painel}><div className={css.barra}><p className={css.aviso}>Fluxo de caixa por vencimento e pagamentos.</p><div className={css.acoes}><Botao disabled={!consulta.dados?.length} aria-haspopup="dialog" onClick={() => setAberto(true)}>Ampliar relatório</Botao><Botao disabled={!consulta.dados?.length || exportando} onClick={exportar}>Exportar CSV</Botao></div></div>{tabela}<ErroFinanceiro erro={erroExportacao} /></div></EstadoFinanceiro>
    {aberto && <Modal titulo="Relatório de fluxo de caixa" aoFechar={() => setAberto(false)} largura="min(1100px, calc(100vw - 24px))">{tabela}</Modal>}
  </div>
}

function CadastrosFinanceiros({ tipo }) {
  const { acoes } = useAcessoModulos()
  const categoria = tipo === 'categorias'
  const podeGerenciar = acoes[categoria ? 'gerenciarCategoriasFinanceiras' : 'gerenciarCentrosCusto'] === true
  const consulta = useConsultaFinanceira(categoria ? listarCategoriasFinanceiras : listarCentrosFinanceiros)
  const [modal, setModal] = useState(null)
  const [busca, setBusca] = useState('')
  const itens = consulta.dados?.filter((item) => item.nome.toLocaleLowerCase('pt-BR').includes(busca.toLocaleLowerCase('pt-BR'))) ?? []
  return <div className={css.painel}><div className={css.barra}><CampoTexto rotulo="Buscar cadastro" value={busca} onChange={(e) => setBusca(e.target.value)} />{podeGerenciar && <Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => setModal({ tipo: 'editar' })}>{categoria ? 'Nova categoria' : 'Novo centro de custo'}</Botao>}</div>
    <EstadoFinanceiro consulta={consulta}><ul className={css.lista}>{itens.map((item) => <li key={item.id} className={css.linha}><div><strong>{item.nome}</strong><p className={css.apoio}>{categoria ? item.tipo : item.codigo} · {(categoria ? item.ativa : item.ativo) ? 'Ativo' : 'Inativo'}</p></div>{podeGerenciar && <div className={css.acoes}><Botao aria-haspopup="dialog" aria-label={`Editar ${item.nome}`} onClick={() => setModal({ tipo: 'editar', item })}>Editar</Botao>{(categoria ? item.ativa : item.ativo) && <Botao aria-haspopup="dialog" aria-label={`Inativar ${item.nome}`} onClick={() => setModal({ tipo: 'inativar', item })}>Inativar</Botao>}</div>}</li>)}</ul>{!itens.length && <Vazio titulo="Nenhum cadastro encontrado">Cadastre ou revise a busca.</Vazio>}</EstadoFinanceiro>
    {modal?.tipo === 'editar' && <ModalCadastroFinanceiro tipo={tipo} item={modal.item} aoFechar={() => setModal(null)} aoGravar={consulta.recarregar} />}
    {modal?.tipo === 'inativar' && <ModalConfirmacaoFinanceira titulo="Inativar cadastro" descricao={`Inativar ${modal.item.nome}? As contas existentes conservam seu histórico.`} acao={() => inativarCadastroFinanceiro(categoria ? tipo : 'centros-custo', modal.item.id)} aoFechar={() => setModal(null)} aoGravar={consulta.recarregar} />}
  </div>
}

export function PainelFinanceiro({ tela }) {
  const { acoes } = useAcessoModulos()
  const permitido = tela === 'pagar' ? acoes.visualizarContasPagar : tela === 'receber' ? acoes.visualizarContasReceber : acoes.visualizarContasPagar || acoes.visualizarContasReceber
  if (!permitido) return <Vazio titulo="Seu perfil não pode consultar estas contas">Consulte o Caixa do dia ou peça à dona para revisar seu acesso.</Vazio>
  if (['pagar', 'receber'].includes(tela)) return <ContasFinanceiras key={tela} lado={tela} />
  if (tela === 'extrato') return <ExtratoFinanceiro />
  if (tela === 'relatorios') return <RelatoriosFinanceiros />
  if (['categorias', 'centros'].includes(tela)) return <CadastrosFinanceiros key={tela} tipo={tela} />
  return <ResumoFinanceiro />
}
