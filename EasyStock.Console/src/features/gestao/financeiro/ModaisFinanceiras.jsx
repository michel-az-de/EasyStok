import { useEffect, useId, useRef, useState } from 'react'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto, CampoSelecao, CampoArea } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { moeda, lerMoeda, moedaAltaDemais } from '../../../dominio/formato'
import { METODOS_CAIXA, nomeDoMetodoCaixa } from '../../../dominio/caixa'
import { diaFinanceiro, instanteFinanceiro, parcelasFinanceiras, motivoContaInvalida, palavrasDoLado, mensagemFinanceira, lerParcelas, corpoDaNovaCategoria, motivoDaNovaCategoria } from '../../../dominio/financeiro'
import { useOperacaoFinanceira, listarCategoriasFinanceiras, salvarContaFinanceira, registrarParcelaFinanceira, salvarCadastroFinanceiro, adicionarParcelaFinanceira } from '../../../aplicacao/useFinanceiroApi'
import css from './financeiro.module.css'

export function DinheiroFinanceiro({ rotulo, centavos, aoMudar, disabled, ...props }) {
  // #2005 (console-04): em reais, como o caixa ("100" é R$ 100,00). Campo vazio vale zero.
  return <CampoReais rotulo={rotulo} centavos={centavos} ler={lerMoeda} disabled={disabled} erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null} aoMudar={(valor) => aoMudar(valor ?? 0)} {...props} />
}
// #2095 (F4): texto técnico do servidor vira uma frase humana.
export function ErroFinanceiro({ erro }) { return erro ? <p role="alert" className={css.erro}>{mensagemFinanceira(erro)}</p> : null }

function FormularioFinanceiro({ titulo, aoFechar, operacao, aoEnviar, children, salvar = 'Salvar', fechar = 'Cancelar', valido = true, motivo = null, digitado = false }) {
  const id = useId()
  // #2258: com algo digitado, Esc, fundo e X perguntam antes de descartar; durante o envio nada fecha.
  return <Modal titulo={titulo} aoFechar={operacao.ocupado ? undefined : aoFechar} segurarFechamento={operacao.ocupado} confirmarDescarte={digitado && !operacao.ocupado} largura="min(720px, calc(100vw - 24px))" rodape={<><Botao disabled={operacao.ocupado} onClick={aoFechar}>{fechar}</Botao><Botao tipo="submit" form={id} variante="primario" motivo={!valido && !operacao.ocupado ? motivo : undefined} disabled={!valido || operacao.ocupado}>{operacao.ocupado ? 'Salvando…' : salvar}</Botao></>}>
    <form id={id} className={css.formulario} onSubmit={(evento) => { evento.preventDefault(); if (valido && !operacao.ocupado) aoEnviar() }}>
      <fieldset disabled={operacao.ocupado} className={css.campos}>{children}</fieldset>
      <ErroFinanceiro erro={operacao.erro} />
    </form>
  </Modal>
}

export function ModalContaFinanceira({ lado, conta, cadastros, aoFechar, aoGravar, aoCriar, aoCriarCategoria }) {
  const [descricao, setDescricao] = useState(conta?.descricao ?? '')
  const [categoria, setCategoria] = useState(conta?.categoriaFinanceiraId ?? '')
  const [centro, setCentro] = useState(conta?.centroCustoId ?? '')
  const [observacoes, setObservacoes] = useState(conta?.observacoes ?? '')
  const [centavos, setCentavos] = useState(0)
  // #2258 (F13): texto de dígitos; o número sai de `lerParcelas`.
  const [parcelasTexto, setParcelasTexto] = useState('1')
  const [vencimento, setVencimento] = useState(diaFinanceiro())
  const [emissao, setEmissao] = useState(diaFinanceiro())
  const [emitir, setEmitir] = useState(true)
  // #2095 (C7): "Já foi paga/recebida" registra a parcela única no mesmo gesto.
  const [jaFeito, setJaFeito] = useState(false)
  const [metodo, setMetodo] = useState('pix')
  const [dataFeito, setDataFeito] = useState(diaFinanceiro())
  const [referencia] = useState(() => crypto.randomUUID())
  const criada = useRef(null)
  // Revisão #2105 (3): criada a conta, os campos dela travam; a nova tentativa só registra o pagamento.
  const [travada, setTravada] = useState(false)
  const palavras = palavrasDoLado(lado)
  const operacao = useOperacaoFinanceira(aoGravar)
  const tipo = lado === 'pagar' ? 'Despesa' : 'Receita'
  const parcelasLidas = lerParcelas(parcelasTexto)
  const quantidade = parcelasLidas.erro ? null : parcelasLidas.quantidade
  // #2258 (F12): categoria criada ali mesmo entra na escolha e vem selecionada.
  const { acoes } = useAcessoModulos()
  const podeCriarCategoria = acoes.gerenciarCategoriasFinanceiras === true
  const [criadas, setCriadas] = useState([])
  const [criandoCategoria, setCriandoCategoria] = useState(false)
  const [nomeNovaCategoria, setNomeNovaCategoria] = useState('')
  const criacao = useOperacaoFinanceira()
  const campoNovaCategoria = useRef(null)
  useEffect(() => { if (criandoCategoria) campoNovaCategoria.current?.focus() }, [criandoCategoria])
  const todasCategorias = [...cadastros.categorias, ...criadas.filter((nova) => !cadastros.categorias.some((item) => item.id === nova.id))]
  const categorias = todasCategorias.filter((item) => (item.ativa && [tipo, 'Ambas'].includes(item.tipo)) || item.id === categoria)
  const criarCategoria = async () => {
    if (motivoDaNovaCategoria(nomeNovaCategoria) || criacao.ocupado) return
    const corpo = corpoDaNovaCategoria(nomeNovaCategoria, lado)
    let nova = null
    const ok = await criacao.executar(async () => {
      const resposta = await salvarCadastroFinanceiro('categorias', corpo)
      nova = resposta?.id ? resposta : (await listarCategoriasFinanceiras()).find((item) => item.nome === corpo.nome && item.tipo === corpo.tipo)
      if (!nova) throw new Error('A categoria foi criada, mas não apareceu na lista. Feche e abra a conta de novo.')
    })
    if (!ok) return
    setCriadas((atual) => [...atual, { ativa: true, ...corpo, ...nova }])
    setCategoria(nova.id); setNomeNovaCategoria(''); setCriandoCategoria(false)
    aoCriarCategoria?.()
  }
  const parcelas = parcelasFinanceiras(centavos, quantidade, vencimento)
  const registrarJunto = !conta && jaFeito && quantidade === 1 && emitir
  // Só some a caixa "Já foi paga" com mais de uma parcela; valor inválido no campo não a esconde.
  const parcelaUnica = !(quantidade > 1)
  const digitado = !conta && !travada && Boolean(descricao.trim() || centavos > 0 || observacoes.trim() || categoria || centro)
  const motivo = travada ? (!dataFeito ? `Informe a ${palavras.data.toLowerCase()}.` : null) : conta
    ? (!descricao.trim() ? 'Escreva a descrição da conta.' : !categoria ? 'Escolha a categoria financeira.' : null)
    : motivoContaInvalida({ descricao, categoria, centavos, quantidade, emissao, vencimento })
      ?? (moedaAltaDemais(centavos) ? 'Valor alto demais.' : registrarJunto && !dataFeito ? `Informe a ${palavras.data.toLowerCase()}.` : null)
  const valido = !motivo && (conta || parcelas.length > 0)
  const salvar = async () => {
    const corpo = { descricao: descricao.trim(), observacoes }
    if (!conta || categoria !== conta.categoriaFinanceiraId) corpo.categoriaFinanceiraId = categoria
    if (!conta || centro !== (conta.centroCustoId ?? '')) corpo.centroCustoId = centro || null
    if (!conta) Object.assign(corpo, { dataEmissao: instanteFinanceiro(emissao), parcelas, emitirAposCriar: emitir, origem: 'Manual' })
    const ok = await operacao.executar(async () => {
      // Se o registro falhar, a conta já existe: a lista recarrega já e a nova tentativa só registra.
      if (!criada.current) {
        criada.current = await salvarContaFinanceira(lado, corpo, conta?.id)
        if (registrarJunto) { setTravada(true); await aoGravar?.() }
      }
      const parcela = criada.current?.parcelas?.[0]
      if (registrarJunto && parcela) {
        try { await registrarParcelaFinanceira(lado, criada.current.id, parcela.id, { valor: parcela.saldo ?? parcela.valor, metodo, dataPagamento: instanteFinanceiro(dataFeito), observacao: '', gatewayProvedor: 'Manual', gatewayTransactionId: referencia }) }
        catch (erro) { throw new Error(`A conta foi salva, mas o ${palavras.registro} não foi registrado: ${mensagemFinanceira(erro.message)} Tente de novo.`) }
      }
    })
    if (ok) fechar()
  }
  // Fechar depois de criar (mesmo se o registro falhou) abre a conta criada para terminar ali.
  const fechar = () => { aoFechar(); if (!conta && criada.current?.id) aoCriar?.(criada.current) }
  return <FormularioFinanceiro titulo={conta ? 'Editar conta' : `Nova conta a ${lado}`} aoFechar={fechar} operacao={operacao} aoEnviar={salvar} valido={valido} motivo={motivo} digitado={digitado} salvar={travada ? `Registrar ${palavras.registro}` : 'Salvar'}>
    {travada && <output className={css.aviso}>A conta já foi criada e aparece na lista. Falta só registrar o {palavras.registro}; para mudar a conta, feche e abra o detalhe.</output>}
    <fieldset disabled={travada} className={css.campos}>
    <CampoTexto rotulo="Descrição" required maxLength={300} erro={descricao && !descricao.trim() ? 'Escreva a descrição da conta.' : null} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
    <div className={css.grade}>
      <div className={css.categoria}>
        <CampoSelecao rotulo="Categoria financeira" required value={categoria} onChange={(e) => setCategoria(e.target.value)} opcoes={[{ valor: '', rotulo: 'Escolha a categoria' }, ...categorias.map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
        {podeCriarCategoria && !criandoCategoria && <Botao variante="texto" onClick={() => setCriandoCategoria(true)}>+ Nova categoria</Botao>}
      </div>
      <CampoSelecao rotulo="Centro de custo (opcional)" value={centro} onChange={(e) => setCentro(e.target.value)} opcoes={[...(!conta?.centroCustoId ? [{ valor: '', rotulo: 'Sem centro de custo' }] : []), ...cadastros.centros.filter((item) => item.ativo || item.id === centro).map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
    </div>
    {podeCriarCategoria && criandoCategoria && <fieldset className={css.novaCategoria}><legend className="sr">Nova categoria</legend>
      <CampoTexto ref={campoNovaCategoria} rotulo={`Nome da nova categoria de ${tipo.toLowerCase()}`} maxLength={100} erro={criacao.erro ? mensagemFinanceira(criacao.erro) : null} value={nomeNovaCategoria} onChange={(e) => setNomeNovaCategoria(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); criarCategoria() } }} />
      <Botao variante="primario" disabled={Boolean(motivoDaNovaCategoria(nomeNovaCategoria)) || criacao.ocupado} onClick={criarCategoria}>{criacao.ocupado ? 'Criando…' : 'Criar categoria'}</Botao>
      <Botao variante="texto" disabled={criacao.ocupado} onClick={() => { setCriandoCategoria(false); setNomeNovaCategoria('') }}>Cancelar</Botao>
    </fieldset>}
    {!categorias.length && !criandoCategoria && <p className={css.aviso}>{podeCriarCategoria ? `Ainda não há categoria de ${tipo.toLowerCase()}. Use "+ Nova categoria" para criar a primeira.` : `Ainda não há categoria de ${tipo.toLowerCase()}. Peça a quem administra a loja para cadastrar uma.`}</p>}
    {!conta && <>
      <div className={css.grade}><DinheiroFinanceiro rotulo="Valor total" centavos={centavos} aoMudar={setCentavos} /><CampoTexto rotulo="Data de emissão" tipo="date" required value={emissao} onChange={(e) => setEmissao(e.target.value)} /></div>
      <div className={css.grade}><CampoTexto rotulo="Quantidade de parcelas" inputMode="numeric" autoComplete="off" dica="De 1 a 60" required erro={parcelasLidas.erro} value={parcelasTexto} onChange={(e) => setParcelasTexto(lerParcelas(e.target.value).texto)} /><CampoTexto rotulo="Primeiro vencimento" tipo="date" required min={emissao || undefined} erro={vencimento && emissao && vencimento < emissao ? 'O primeiro vencimento não pode ser antes da emissão.' : null} value={vencimento} onChange={(e) => setVencimento(e.target.value)} /></div>
      <ol className={css.parcelas}>{parcelas.map((item) => <li key={item.numero}>Parcela {item.numero}: {moeda(item.valor)} · {diaFinanceiro(item.dataVencimento).split('-').reverse().join('/')}</li>)}</ol>
      <label className={css.escolha}><input type="checkbox" checked={emitir} onChange={(e) => { setEmitir(e.target.checked); if (!e.target.checked) setJaFeito(false) }} /> Emitir a conta ao salvar</label>
      {parcelaUnica && emitir && <label className={css.escolha}><input type="checkbox" checked={jaFeito} onChange={(e) => setJaFeito(e.target.checked)} /> {palavras.jaFeito}</label>}
    </>}
    <CampoArea rotulo="Observações (opcional)" rows={3} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
    </fieldset>
    {!conta && <>
      {registrarJunto && <div className={css.grade}><CampoSelecao rotulo={palavras.forma} value={metodo} onChange={(e) => setMetodo(e.target.value)} opcoes={METODOS_CAIXA.map((item) => ({ valor: item, rotulo: nomeDoMetodoCaixa(item) }))} /><CampoTexto rotulo={palavras.data} tipo="date" required max={diaFinanceiro()} value={dataFeito} onChange={(e) => setDataFeito(e.target.value)} /></div>}
    </>}
  </FormularioFinanceiro>
}

export function ModalPagamentoFinanceiro({ lado, conta, parcela, aoFechar, aoGravar }) {
  const [centavos, setCentavos] = useState(Math.round(parcela.saldo * 100))
  const [metodo, setMetodo] = useState('pix')
  const [data, setData] = useState(diaFinanceiro())
  const [observacao, setObservacao] = useState('')
  const [referencia] = useState(() => crypto.randomUUID())
  const operacao = useOperacaoFinanceira(aoGravar)
  const palavras = palavrasDoLado(lado)
  const valido = centavos > 0 && centavos <= Math.round(parcela.saldo * 100) && data && !moedaAltaDemais(centavos)
  const salvar = async () => {
    if (await operacao.executar(() => registrarParcelaFinanceira(lado, conta.id, parcela.id, { valor: centavos / 100, metodo, dataPagamento: instanteFinanceiro(data), observacao, gatewayProvedor: 'Manual', gatewayTransactionId: referencia }))) aoFechar()
  }
  return <FormularioFinanceiro titulo={lado === 'pagar' ? 'Registrar pagamento' : 'Registrar recebimento'} aoFechar={aoFechar} operacao={operacao} aoEnviar={salvar} salvar="Confirmar registro" valido={valido}>
    <p>{conta.descricao} · Parcela {parcela.numero} · Falta {moeda(parcela.saldo)}</p>
    <p className={css.aviso}>Registre o valor já {lado === 'pagar' ? 'pago' : 'recebido'}. A confirmação também gera o movimento correspondente no Caixa.</p>
    <DinheiroFinanceiro rotulo="Valor" centavos={centavos} aoMudar={setCentavos} />
    {centavos > Math.round(parcela.saldo * 100) && <ErroFinanceiro erro="O valor ultrapassa o saldo da parcela." />}
    <div className={css.grade}><CampoSelecao rotulo={palavras.forma} value={metodo} onChange={(e) => setMetodo(e.target.value)} opcoes={METODOS_CAIXA.map((item) => ({ valor: item, rotulo: nomeDoMetodoCaixa(item) }))} /><CampoTexto rotulo={palavras.data} tipo="date" required max={diaFinanceiro()} value={data} onChange={(e) => setData(e.target.value)} /></div>
    <CampoArea rotulo="Observação" rows={2} value={observacao} onChange={(e) => setObservacao(e.target.value)} />
  </FormularioFinanceiro>
}

export function ModalConfirmacaoFinanceira({ titulo, descricao, exigirMotivo = false, acao, aoFechar, aoGravar, salvar = 'Confirmar', fechar = 'Cancelar' }) {
  const [motivo, setMotivo] = useState('')
  const operacao = useOperacaoFinanceira(aoGravar)
  return <FormularioFinanceiro titulo={titulo} aoFechar={aoFechar} operacao={operacao} valido={!exigirMotivo || motivo.trim().length >= 3} motivo="Escreva o motivo (3 letras ou mais)." salvar={salvar} fechar={fechar} aoEnviar={async () => { if (await operacao.executar(() => acao(motivo.trim()))) aoFechar() }}>
    <p>{descricao}</p>{exigirMotivo && <CampoArea rotulo="Motivo" required minLength={3} rows={3} value={motivo} onChange={(e) => setMotivo(e.target.value)} />}
  </FormularioFinanceiro>
}

export function ModalCadastroFinanceiro({ tipo, item, aoFechar, aoGravar }) {
  const categoria = tipo === 'categorias'
  const [nome, setNome] = useState(item?.nome ?? '')
  const [codigo, setCodigo] = useState(item?.codigo ?? '')
  const [tipoCategoria, setTipoCategoria] = useState(item?.tipo ?? 'Despesa')
  const [descricao, setDescricao] = useState(item?.descricao ?? '')
  const operacao = useOperacaoFinanceira(aoGravar)
  return <FormularioFinanceiro titulo={`${item ? 'Editar' : 'Novo cadastro de'} ${categoria ? 'categoria financeira' : 'centro de custo'}`} aoFechar={aoFechar} operacao={operacao} valido={nome.trim() && (categoria || codigo.trim())} aoEnviar={async () => {
    const corpo = categoria ? { nome: nome.trim(), ...(item ? { cor: item.cor, icone: item.icone } : { tipo: tipoCategoria }) } : { nome: nome.trim(), descricao, ...(item ? {} : { codigo: codigo.trim() }) }
    if (await operacao.executar(() => salvarCadastroFinanceiro(tipo === 'centros' ? 'centros-custo' : tipo, corpo, item?.id))) aoFechar()
  }}>
    <CampoTexto rotulo="Nome" required value={nome} onChange={(e) => setNome(e.target.value)} />
    {categoria ? <CampoSelecao rotulo="Tipo" disabled={Boolean(item)} value={tipoCategoria} onChange={(e) => setTipoCategoria(e.target.value)} opcoes={['Despesa', 'Receita', 'Ambas'].map((valor) => ({ valor, rotulo: valor }))} /> : <><CampoTexto rotulo="Código" required disabled={Boolean(item)} value={codigo} onChange={(e) => setCodigo(e.target.value)} /><CampoArea rotulo="Descrição" rows={3} value={descricao} onChange={(e) => setDescricao(e.target.value)} /></>}
  </FormularioFinanceiro>
}

export function ModalParcelaFinanceira({ lado, conta, aoFechar, aoGravar }) {
  const [centavos, setCentavos] = useState(0)
  const [vencimento, setVencimento] = useState(diaFinanceiro())
  const operacao = useOperacaoFinanceira(aoGravar)
  const numero = Math.max(0, ...conta.parcelas.map((item) => item.numero)) + 1
  return <FormularioFinanceiro titulo="Adicionar parcela" aoFechar={aoFechar} operacao={operacao} valido={centavos > 0 && vencimento && !moedaAltaDemais(centavos)} aoEnviar={async () => {
    if (await operacao.executar(() => adicionarParcelaFinanceira(lado, conta.id, { numero, valor: centavos / 100, dataVencimento: instanteFinanceiro(vencimento) }))) aoFechar()
  }}><DinheiroFinanceiro rotulo="Valor da parcela" centavos={centavos} aoMudar={setCentavos} /><CampoTexto rotulo="Vencimento" tipo="date" required value={vencimento} onChange={(e) => setVencimento(e.target.value)} /></FormularioFinanceiro>
}
