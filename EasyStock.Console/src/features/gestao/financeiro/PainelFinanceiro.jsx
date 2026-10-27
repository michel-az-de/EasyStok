import { useCallback, useEffect, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto, CampoSelecao } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { MarcaIcone } from '../../../componentes/MarcaIcone'
import { Vazio } from '../../../componentes/Vazio'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useConsultaFinanceira, listarContasFinanceiras, listarTodasContasFinanceiras, obterContaFinanceira, obterResumoFinanceiro, obterExtratoFinanceiro, obterRelatorioFinanceiro, listarCategoriasFinanceiras, listarCentrosFinanceiros, emitirContaFinanceira, cancelarContaFinanceira, estornarParcelaFinanceira, removerParcelaFinanceira, inativarCadastroFinanceiro, reativarCadastroFinanceiro } from '../../../aplicacao/useFinanceiroApi'
import { STATUS_CONTAS, nomeStatusConta, tomDoStatusConta, nomeStatusPagamento, palavrasDoLado, statusDaConta, diaFinanceiro, diaMais, instanteFinanceiro, periodoFinanceiro, mesFinanceiro, CHIPS_CONTAS, CHIP_PADRAO, planoDoChip, filtrosDaApi, ordenarPorVencimento, paginaDaLista, paginaValida, TAMANHO_PAGINA, proximaParcela, parcelaEmAberto, valorDaLinha, valorLancado, podeEditarConta, podeRegistrarNaConta, hashDoChip, chipDaHash } from '../../../dominio/financeiro'
import { moeda } from '../../../dominio/formato'
import { nomeDoMetodoCaixa } from '../../../dominio/caixa'
import { hashDoModulo } from '../../../dominio/modulos'
import { ModalContaFinanceira, ModalPagamentoFinanceiro, ModalConfirmacaoFinanceira, ModalCadastroFinanceiro, ModalParcelaFinanceira, ErroFinanceiro } from './ModaisFinanceiras'
import css from './financeiro.module.css'

const dataCurta = (valor) => diaFinanceiro(valor).split('-').reverse().join('/')
const carregarCadastros = async () => { const [categorias, centros] = await Promise.all([listarCategoriasFinanceiras(), listarCentrosFinanceiros()]); return { categorias, centros } }
const pagoNaConta = (conta) => conta.totalPago ?? conta.totalRecebido
const podeVer = (acoes, lado) => acoes[lado === 'pagar' ? 'visualizarContasPagar' : 'visualizarContasReceber'] === true
const podeGerenciarLado = (acoes, lado) => acoes[lado === 'pagar' ? 'gerenciarContasPagar' : 'gerenciarContasReceber'] === true
// No celular o resumo ao lado iria para depois da lista: o toque abre o detalhe direto (#2095, C9).
const telaEstreita = () => globalThis.matchMedia?.('(max-width: 759px)').matches === true
function quandoVence(parcela) {
  if (!parcela) return null
  const dia = diaFinanceiro(parcela.dataVencimento), hoje = diaFinanceiro()
  if (dia === hoje) return 'Vence hoje'
  return `${dia < hoje ? 'Venceu' : 'Vence'} ${dataCurta(parcela.dataVencimento).slice(0, 5)}`
}

function EstadoFinanceiro({ consulta, children }) {
  if (consulta.erro) return <div className={css.painel}><ErroFinanceiro erro={consulta.erro} /><Botao onClick={consulta.recarregar}>Tentar novamente</Botao></div>
  if (consulta.carregando && !consulta.dados) return <Vazio titulo="Carregando financeiro">Aguarde um instante.</Vazio>
  return <div aria-busy={consulta.carregando}>{children}</div>
}
function Indicador({ nome, valor, detalhe }) { return <div className={css.indicador}><span>{nome}</span><strong>{moeda(valor)}</strong>{detalhe && <small>{detalhe}</small>}</div> }
function SituacaoConta({ status, lado }) { return <Pilula tom={tomDoStatusConta(status)}>{nomeStatusConta(status, lado)}</Pilula> }

// ------------------------------------------------------------------ visão geral: o que vence
function ListaDoDia({ titulo, lado, chip, contas, podeGerenciar, aoPagar, vazio }) {
  const palavras = palavrasDoLado(lado)
  return <section className={css.blocoDia} aria-label={`${titulo} a ${lado}`}>
    <div className={css.barra}><h3>{titulo}{contas.length ? ` · ${moeda(contas.reduce((soma, conta) => soma + Math.round(conta.pendente * 100), 0) / 100)}` : ''}</h3><a className={css.verLista} href={hashDoChip(lado, chip)}>{contas.length ? `Ver ${contas.length} na lista` : 'Abrir a lista'}</a></div>
    {contas.length ? <ul className={css.itensDia}>{contas.slice(0, 5).map((conta) => {
      const parcela = proximaParcela(conta)
      return <li key={conta.id} className={css.itemDia}>
        <div><strong>{conta.descricao}</strong><small>{quandoVence(parcela)}</small></div>
        <span className={css.valor}><small>Falta</small> {moeda(conta.pendente)}</span>
        {podeGerenciar && parcela && <Botao variante="primario" aria-haspopup="dialog" aria-label={`${palavras.verbo} ${conta.descricao}`} onClick={() => aoPagar({ lado, conta, parcela })}>{palavras.verbo}</Botao>}
      </li>
    })}</ul> : <p className={css.apoio}>{vazio}</p>}
  </section>
}

function ResumoFinanceiro() {
  const { acoes } = useAcessoModulos()
  const verPagar = podeVer(acoes, 'pagar'), verReceber = podeVer(acoes, 'receber')
  const carregar = useCallback(async () => {
    const hoje = diaFinanceiro()
    const lados = [verPagar && 'pagar', verReceber && 'receber'].filter(Boolean)
    // Uma consulta por lado cobre hoje e os próximos 7 dias; o recorte é o mesmo dos chips da lista.
    const filtro = { vencimentoDe: instanteFinanceiro(hoje), vencimentoAte: instanteFinanceiro(diaMais(hoje, 7), true) }
    const [resumo, ...listas] = await Promise.all([obterResumoFinanceiro(), ...lados.map((lado) => listarTodasContasFinanceiras(lado, filtro))])
    const porLado = Object.fromEntries(lados.map((lado, indice) => {
      const ordenadas = ordenarPorVencimento(listas[indice])
      return [lado, { hoje: ordenadas.filter(planoDoChip('hoje', hoje).local), semana: ordenadas.filter(planoDoChip('semana', hoje).local) }]
    }))
    return { resumo, porLado }
  }, [verPagar, verReceber])
  const consulta = useConsultaFinanceira(carregar)
  const [pagamento, setPagamento] = useState(null)
  const resumo = consulta.dados?.resumo
  return <EstadoFinanceiro consulta={consulta}>{resumo && <div className={css.painel}>
    <div className={css.barra}><p className={css.aviso}>O que vence hoje e nos próximos 7 dias. Toque num bloco para ver a lista.</p><Botao icone="refresh-cw" onClick={consulta.recarregar}>Atualizar</Botao></div>
    <div className={css.dia}>{Object.entries(consulta.dados.porLado).map(([lado, listas]) => {
      const vencido = lado === 'pagar' ? resumo.totalVencidoPagar : resumo.totalVencidoReceber
      return <section key={lado} className={css.ladoDia} aria-label={`Contas a ${lado}`}>
        <h2>{lado === 'pagar' ? 'A pagar' : 'A receber'}</h2>
        <a className={`${css.cartaoVencidas} ${vencido > 0 ? css.temVencida : ''}`} href={hashDoChip(lado, 'vencidas')}><span>Vencidas</span><strong>{moeda(vencido)}</strong><small>{vencido > 0 ? 'Ver as vencidas' : 'Nada vencido'}</small></a>
        <ListaDoDia titulo="Vence hoje" lado={lado} chip="hoje" contas={listas.hoje} podeGerenciar={podeGerenciarLado(acoes, lado)} aoPagar={setPagamento} vazio={`Nada a ${lado} hoje.`} />
        <ListaDoDia titulo="Próximos 7 dias" lado={lado} chip="semana" contas={listas.semana} podeGerenciar={podeGerenciarLado(acoes, lado)} aoPagar={setPagamento} vazio={`Nada a ${lado} nos próximos 7 dias.`} />
      </section>
    })}</div>
    <div className={css.indicadores}>
      <Indicador nome="A pagar nos próximos 30 dias" valor={resumo.totalAVencer30dPagar} detalhe={`${resumo.qtdContasPagarAVencer30d} ${resumo.qtdContasPagarAVencer30d === 1 ? 'conta' : 'contas'}`} />
      <Indicador nome="A receber nos próximos 30 dias" valor={resumo.totalAVencer30dReceber} detalhe={`${resumo.qtdContasReceberAVencer30d} ${resumo.qtdContasReceberAVencer30d === 1 ? 'conta' : 'contas'}`} />
      <Indicador nome="Pago no mês" valor={resumo.totalPagoMes} />
      <Indicador nome="Recebido no mês" valor={resumo.totalRecebidoMes} />
    </div>
    <div className={css.atalhos}>{[['pagar', 'Contas a pagar', 'caixa'], ['receber', 'Contas a receber', 'financeiro'], ['caixa', 'Caixa do dia', 'caixa'], ['extrato', 'Extrato', 'receita'], ['relatorios', 'Relatórios', 'grade']].filter(([id]) => !['pagar', 'receber'].includes(id) || podeVer(acoes, id)).map(([id, nome, icone]) => <a key={id} className={css.atalho} href={hashDoModulo('financeiro', id)}><MarcaIcone nome={icone} tamanho={28} />{nome}</a>)}</div>
    {pagamento && <ModalPagamentoFinanceiro lado={pagamento.lado} conta={pagamento.conta} parcela={pagamento.parcela} aoFechar={() => setPagamento(null)} aoGravar={consulta.recarregar} />}
  </div>}</EstadoFinanceiro>
}

// ------------------------------------------------------------------ listas de contas
const SEM_AVANCADO = { status: '', vencimentoDe: '', vencimentoAte: '' }
const semVazios = (objeto) => Object.fromEntries(Object.entries(objeto).filter(([, valor]) => valor !== '' && valor != null))

function ContasFinanceiras({ lado }) {
  const { acoes } = useAcessoModulos()
  const podeGerenciar = podeGerenciarLado(acoes, lado)
  const palavras = palavrasDoLado(lado)
  const [chip, setChip] = useState(() => chipDaHash(globalThis.location?.hash) ?? CHIP_PADRAO)
  const [busca, setBusca] = useState('')
  const [buscaAplicada, setBuscaAplicada] = useState('')
  const [avancado, setAvancado] = useState(SEM_AVANCADO)
  const [avancadoAplicado, setAvancadoAplicado] = useState(SEM_AVANCADO)
  const [pagina, setPagina] = useState(1)
  // A conta selecionada fica na lista depois de paga: a dona vê "Pago" e o foco volta à linha.
  const manter = useRef(null)
  const carregar = useCallback(async () => {
    const plano = planoDoChip(chip)
    const filtros = { ...plano.api, ...semVazios(filtrosDaApi({ busca: buscaAplicada, ...avancadoAplicado })) }
    if (!plano.local) { const resposta = await listarContasFinanceiras(lado, { ...filtros, page: pagina, pageSize: TAMANHO_PAGINA }); return { itens: ordenarPorVencimento(resposta.itens), total: resposta.total } }
    const todas = ordenarPorVencimento((await listarTodasContasFinanceiras(lado, filtros)).filter((conta) => plano.local(conta) || conta.id === manter.current))
    return { itens: paginaDaLista(todas, pagina), total: todas.length }
  }, [lado, chip, buscaAplicada, avancadoAplicado, pagina])
  const consulta = useConsultaFinanceira(carregar)
  const cadastros = useConsultaFinanceira(carregarCadastros)
  const [nova, setNova] = useState(false)
  const [selecionadaId, setSelecionadaId] = useState(null)
  const [detalheId, setDetalheId] = useState(null)
  const [pagamento, setPagamento] = useState(null)
  const selecionada = consulta.dados?.itens.find((item) => item.id === selecionadaId)
  useEffect(() => { manter.current = selecionadaId }, [selecionadaId])
  const total = consulta.dados?.total
  // Busca filtra enquanto digita (com um respiro curto); o Enter aplica na hora.
  useEffect(() => { const espera = setTimeout(() => { setBuscaAplicada(busca); setPagina(1) }, 350); return () => clearTimeout(espera) }, [busca])
  useEffect(() => { if (total !== undefined && paginaValida(pagina, total) !== pagina) setPagina(1) }, [pagina, total])
  useEffect(() => {
    const aoMudarHash = () => { const doLink = chipDaHash(window.location.hash); if (doLink) { setChip(doLink); setAvancado(SEM_AVANCADO); setAvancadoAplicado(SEM_AVANCADO); setPagina(1) } }
    window.addEventListener('hashchange', aoMudarHash)
    return () => window.removeEventListener('hashchange', aoMudarHash)
  }, [])
  const escolherChip = (id) => {
    setChip(id); setAvancado(SEM_AVANCADO); setAvancadoAplicado(SEM_AVANCADO); setPagina(1)
    try { history.replaceState(null, '', hashDoChip(lado, id)) } catch { /* a rota segue a mesma */ }
  }
  const campo = (nome, valor) => setAvancado((atual) => ({ ...atual, [nome]: valor }))
  const selecionar = (conta) => { setSelecionadaId(conta.id); if (telaEstreita()) setDetalheId(conta.id) }
  const comAvancado = Object.values(avancadoAplicado).some(Boolean)
  const nomeDoChip = CHIPS_CONTAS.find((item) => item.id === chip)?.rotulo
  return <div className={css.painel}>
    <div className={css.barra}>
      <fieldset className={css.chips}><legend className="sr">Mostrar contas</legend>{CHIPS_CONTAS.map((item) => <button key={item.id} type="button" className={`${css.chip} ${chip === item.id && !comAvancado ? css.chipAtivo : ''}`} aria-pressed={chip === item.id && !comAvancado} onClick={() => escolherChip(item.id)}>{item.rotulo}</button>)}</fieldset>
      <div className={css.acoes}><Botao onClick={consulta.recarregar} icone="refresh-cw">Atualizar</Botao>{podeGerenciar && <Botao variante="primario" icone="mais" aria-haspopup="dialog" onClick={() => setNova(true)}>Nova conta a {lado}</Botao>}</div>
    </div>
    <div className={css.ferramentas}>
      <form className={css.busca} onSubmit={(evento) => { evento.preventDefault(); setBuscaAplicada(busca); setPagina(1) }}>
        <CampoTexto rotulo="Buscar conta" rotuloOculto placeholder="Buscar conta" tipo="search" value={busca} onChange={(e) => setBusca(e.target.value)} />
      </form>
      <details className={css.maisFiltros} open={comAvancado || undefined}>
        <summary>Mais filtros{comAvancado ? ' (ligados)' : ''}</summary>
        <form className={css.filtros} onSubmit={(evento) => { evento.preventDefault(); setAvancadoAplicado(avancado); setChip('todas'); setPagina(1) }}>
          <CampoSelecao rotulo="Situação" value={avancado.status} onChange={(e) => campo('status', e.target.value)} opcoes={[{ valor: '', rotulo: 'Todas' }, ...STATUS_CONTAS.map((valor) => ({ valor, rotulo: nomeStatusConta(valor, lado) }))]} />
          <CampoTexto rotulo="Vencimento de" tipo="date" value={avancado.vencimentoDe} onChange={(e) => campo('vencimentoDe', e.target.value)} />
          <CampoTexto rotulo="Vencimento até" tipo="date" min={avancado.vencimentoDe || undefined} value={avancado.vencimentoAte} onChange={(e) => campo('vencimentoAte', e.target.value)} />
          <Botao tipo="submit" icone="busca">Filtrar</Botao>
          {comAvancado && <Botao onClick={() => escolherChip(CHIP_PADRAO)}>Limpar filtros</Botao>}
        </form>
      </details>
      <p className={css.aviso} aria-live="polite">{consulta.dados ? `${total} ${total === 1 ? 'conta' : 'contas'}${comAvancado ? ' com os filtros' : ` · ${nomeDoChip}`}` : 'Consulte vencimentos, parcelas e pagamentos.'}</p>
    </div>
    <EstadoFinanceiro consulta={consulta}>
      <div className={css.comContexto}>
        <div className={css.painel}>
          {consulta.dados?.itens.length === 0 && <Vazio titulo="Nenhuma conta aqui">{chip === 'todas' && !comAvancado && !buscaAplicada ? 'Cadastre a primeira conta.' : 'Troque o recorte acima ou limpe a busca.'}</Vazio>}
          <ul className={css.lista}>{consulta.dados?.itens.map((conta) => {
            const parcela = proximaParcela(conta)
            const linha = valorDaLinha(conta, lado)
            const parcelas = conta.parcelas.length
            return <li key={conta.id} className={`${css.linha} ${conta.id === selecionadaId ? css.linhaSelecionada : ''}`}>
              <button type="button" className={css.selecionar} aria-pressed={conta.id === selecionadaId} onClick={() => selecionar(conta)}><strong>{conta.descricao}</strong><small><SituacaoConta status={statusDaConta(conta)} lado={lado} />{conta.status !== 'Cancelada' && parcela ? ` · ${quandoVence(parcela)}` : ''}{parcelas > 1 ? ` · ${parcelas} parcelas` : ''}</small></button>
              <span className={css.valorLinha}><small>{linha.rotulo}</small><span className={css.valor}>{moeda(linha.valor)}</span></span>
              <div className={css.acoesLinha}>
                {podeRegistrarNaConta(conta, podeGerenciar) && <Botao variante="primario" aria-haspopup="dialog" aria-label={`${palavras.verbo} ${conta.descricao}`} onClick={() => { setSelecionadaId(conta.id); setPagamento({ conta, parcela }) }}>{palavras.verbo}</Botao>}
                <Botao aria-label={`Abrir conta ${conta.descricao}`} aria-haspopup="dialog" onClick={() => { setSelecionadaId(conta.id); setDetalheId(conta.id) }}>Abrir</Botao>
              </div>
            </li>
          })}</ul>
          {total > TAMANHO_PAGINA && <div className={css.paginacao}><Botao disabled={pagina === 1 || consulta.carregando} onClick={() => setPagina((atual) => atual - 1)}>Anterior</Botao><span>Página {pagina} de {Math.ceil(total / TAMANHO_PAGINA)}</span><Botao disabled={pagina * TAMANHO_PAGINA >= total || consulta.carregando} onClick={() => setPagina((atual) => atual + 1)}>Próxima</Botao></div>}
        </div>
        <aside className={css.contexto} aria-label="Resumo da conta selecionada">{selecionada ? <>
          <h2>{selecionada.descricao}</h2><SituacaoConta status={statusDaConta(selecionada)} lado={lado} />
          <dl className={css.detalhes}>{selecionada.status === 'Cancelada'
            ? <div><dt>Valor lançado</dt><dd>{moeda(valorLancado(selecionada))}</dd></div>
            : <><div><dt>Total</dt><dd>{moeda(selecionada.valorTotal)}</dd></div><div><dt>{palavras.feito}</dt><dd>{moeda(pagoNaConta(selecionada))}</dd></div><div><dt>Falta</dt><dd>{moeda(selecionada.pendente)}</dd></div></>}
            <div><dt>Emissão</dt><dd>{dataCurta(selecionada.dataEmissao)}</dd></div></dl>
          {podeRegistrarNaConta(selecionada, podeGerenciar) && <Botao variante="primario" aria-haspopup="dialog" onClick={() => setPagamento({ conta: selecionada, parcela: proximaParcela(selecionada) })}>{palavras.verbo} a próxima parcela</Botao>}
          <Botao aria-haspopup="dialog" onClick={() => setDetalheId(selecionada.id)}>Parcelas e histórico</Botao>
        </> : <><h2>Conta selecionada</h2><p>Toque numa conta para ver o resumo. Use {palavras.verbo} na linha para registrar a próxima parcela.</p></>}</aside>
      </div>
    </EstadoFinanceiro>
    {nova && <EstadoCadastroEmModal consulta={cadastros} aoFechar={() => setNova(false)}><ModalContaFinanceira lado={lado} cadastros={cadastros.dados} aoFechar={() => setNova(false)} aoGravar={consulta.recarregar} aoCriar={(conta) => { setSelecionadaId(conta.id); setDetalheId(conta.id) }} /></EstadoCadastroEmModal>}
    {pagamento && <ModalPagamentoFinanceiro lado={lado} conta={pagamento.conta} parcela={pagamento.parcela} aoFechar={() => setPagamento(null)} aoGravar={consulta.recarregar} />}
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
  const palavras = palavrasDoLado(lado)
  const [modal, setModal] = useState(null)
  const atualizado = async () => { await Promise.all([consulta.recarregar(), aoGravar()]) }
  const confirmar = (titulo, descricao, acao, exigirMotivo = false, rotulos = {}) => setModal({ tipo: 'confirmar', titulo, descricao, acao, exigirMotivo, ...rotulos })
  const cancelada = conta?.status === 'Cancelada'
  return <Modal titulo={conta?.descricao ?? 'Detalhes da conta'} aoFechar={aoFechar} largura="min(900px, calc(100vw - 24px))">
    <EstadoFinanceiro consulta={consulta}>{conta && <div className={css.painel}>
      <div className={css.barra}><SituacaoConta status={statusDaConta(conta)} lado={lado} /><div className={css.acoes}>{podeGerenciar && <>
        {podeEditarConta(conta, podeGerenciar) && <Botao aria-haspopup="dialog" onClick={() => setModal({ tipo: 'editar' })}>Editar conta</Botao>}
        {conta.status === 'Rascunho' && <><Botao aria-haspopup="dialog" onClick={() => setModal({ tipo: 'parcela' })}>Adicionar parcela</Botao><Botao variante="primario" aria-haspopup="dialog" onClick={() => confirmar('Emitir conta', 'A conta emitida passa a compor os vencimentos do financeiro.', () => emitirContaFinanceira(lado, id))}>Emitir conta</Botao></>}
        {!['Cancelada', 'Paga'].includes(conta.status) && <Botao aria-haspopup="dialog" onClick={() => confirmar('Cancelar conta', `A conta sai dos vencimentos e deixa de aceitar ${palavras.registros}. O que já foi ${palavras.feito.toLowerCase()} continua no histórico; para devolver um valor, estorne o registro na parcela. Escreva o motivo do cancelamento.`, (motivo) => cancelarContaFinanceira(lado, id, motivo), true, { salvar: 'Cancelar a conta', fechar: 'Voltar' })}>Cancelar conta</Botao>}
      </>}</div></div>
      <dl className={css.detalhes}>{cancelada
        ? <><div><dt>Valor lançado</dt><dd>{moeda(valorLancado(conta))}</dd></div><div><dt>Situação</dt><dd>Cancelada: nada mais a {lado}</dd></div></>
        : <><div><dt>Valor total</dt><dd>{moeda(conta.valorTotal)}</dd></div><div><dt>Falta {lado}</dt><dd>{moeda(conta.pendente)}</dd></div></>}
        <div><dt>Categoria</dt><dd>{cadastros.dados?.categorias.find((item) => item.id === conta.categoriaFinanceiraId)?.nome ?? 'Categoria indisponível'}</dd></div><div><dt>Centro de custo</dt><dd>{cadastros.dados?.centros.find((item) => item.id === conta.centroCustoId)?.nome ?? 'Sem centro de custo'}</dd></div></dl>
      {conta.observacoes && <p>{conta.observacoes}</p>}
      {conta.parcelas.map((parcela) => {
        const parcelaCancelada = cancelada || parcela.status === 'Cancelada'
        return <section key={parcela.id} className={css.parcela} aria-label={`Parcela ${parcela.numero}`}>
          <div className={css.barra}><h3>Parcela {parcela.numero} · {parcelaCancelada ? 'Cancelada' : `vence ${dataCurta(parcela.dataVencimento)}`}</h3><span className={css.valor}>{moeda(parcela.valor)}{parcelaCancelada ? '' : parcela.saldo > 0 ? ` · Falta ${moeda(parcela.saldo)}` : ` · ${palavras.feito}`}</span></div>
          <div className={css.acoes}>{podeGerenciar && parcelaEmAberto(parcela) && !['Rascunho', 'Cancelada'].includes(conta.status) && <Botao variante="primario" aria-haspopup="dialog" onClick={() => setModal({ tipo: 'pagamento', parcela })}>{lado === 'pagar' ? 'Registrar pagamento' : 'Registrar recebimento'}</Botao>}
            {podeGerenciar && conta.status === 'Rascunho' && conta.parcelas.length > 1 && <Botao aria-haspopup="dialog" onClick={() => confirmar('Remover parcela', `Remover a parcela ${parcela.numero} de ${moeda(parcela.valor)} do rascunho?`, () => removerParcelaFinanceira(lado, id, parcela.id))}>Remover parcela</Botao>}
          </div>
          {parcela.pagamentos.length ? <ul className={css.historico}>{parcela.pagamentos.map((pagamento) => <li key={pagamento.id}><div className={css.barra}><span>{dataCurta(pagamento.dataPagamento)} · {nomeDoMetodoCaixa(pagamento.metodo)} · {moeda(pagamento.valor)} · {nomeStatusPagamento(pagamento.status)}</span>{podeGerenciar && pagamento.status !== 'Estornado' && <Botao aria-haspopup="dialog" onClick={() => confirmar('Estornar registro', 'O estorno corrige o registro financeiro e o Caixa. Informe o motivo.', (motivo) => estornarParcelaFinanceira(lado, parcela.id, pagamento.id, motivo), true)}>Estornar</Botao>}</div>{pagamento.observacao && <p className={css.apoio}>{pagamento.observacao}</p>}{pagamento.motivoEstorno && <p className={css.apoio}>Motivo: {pagamento.motivoEstorno}</p>}</li>)}</ul> : <p className={css.apoio}>Nenhum {palavras.registro} registrado nesta parcela.</p>}
        </section>
      })}
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
  const [rascunho, setRascunho] = useState(() => ({ ...mesFinanceiro(), periodicidade: 'Mensal', categoriaId: '', centroCustoId: '' }))
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
    <EstadoFinanceiro consulta={consulta}><ul className={css.lista}>{itens.map((item) => <li key={item.id} className={css.linha}><div><strong>{item.nome}</strong><p className={css.apoio}>{categoria ? item.tipo : item.codigo} · {(categoria ? item.ativa : item.ativo) ? 'Ativo' : 'Inativo'}</p></div>{podeGerenciar && <div className={css.acoes}><Botao aria-haspopup="dialog" aria-label={`Editar ${item.nome}`} onClick={() => setModal({ tipo: 'editar', item })}>Editar</Botao>{(categoria ? item.ativa : item.ativo) ? <Botao aria-haspopup="dialog" aria-label={`Inativar ${item.nome}`} onClick={() => setModal({ tipo: 'inativar', item })}>Inativar</Botao> : <Botao aria-haspopup="dialog" aria-label={`Reativar ${item.nome}`} onClick={() => setModal({ tipo: 'reativar', item })}>Reativar</Botao>}</div>}</li>)}</ul>{!itens.length && <Vazio titulo="Nenhum cadastro encontrado">Cadastre ou revise a busca.</Vazio>}</EstadoFinanceiro>
    {modal?.tipo === 'editar' && <ModalCadastroFinanceiro tipo={tipo} item={modal.item} aoFechar={() => setModal(null)} aoGravar={consulta.recarregar} />}
    {modal?.tipo === 'reativar' && <ModalConfirmacaoFinanceira titulo="Reativar cadastro" descricao={`Reativar ${modal.item.nome}? Volta a aparecer na escolha das contas novas.`} acao={() => reativarCadastroFinanceiro(categoria ? tipo : 'centros-custo', modal.item.id)} aoFechar={() => setModal(null)} aoGravar={consulta.recarregar} />}
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
