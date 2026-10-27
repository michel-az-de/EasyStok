import { useId, useRef, useState } from 'react'
import { Modal } from '../../../componentes/Modal'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto, CampoSelecao, CampoArea } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { moeda, lerMoeda, moedaAltaDemais } from '../../../dominio/formato'
import { METODOS_CAIXA, nomeDoMetodoCaixa } from '../../../dominio/caixa'
import { diaFinanceiro, instanteFinanceiro, parcelasFinanceiras, motivoContaInvalida, palavrasDoLado, mensagemFinanceira } from '../../../dominio/financeiro'
import { useOperacaoFinanceira, salvarContaFinanceira, registrarParcelaFinanceira, salvarCadastroFinanceiro, adicionarParcelaFinanceira } from '../../../aplicacao/useFinanceiroApi'
import css from './financeiro.module.css'

export function DinheiroFinanceiro({ rotulo, centavos, aoMudar, disabled, ...props }) {
  // #2005 (console-04): em reais, como o caixa ("100" é R$ 100,00). Campo vazio vale zero.
  return <CampoReais rotulo={rotulo} centavos={centavos} ler={lerMoeda} disabled={disabled} erro={moedaAltaDemais(centavos) ? 'Valor alto demais' : null} aoMudar={(valor) => aoMudar(valor ?? 0)} {...props} />
}
// #2095 (F4): texto técnico do servidor vira uma frase humana.
export function ErroFinanceiro({ erro }) { return erro ? <p role="alert" className={css.erro}>{mensagemFinanceira(erro)}</p> : null }

function FormularioFinanceiro({ titulo, aoFechar, operacao, aoEnviar, children, salvar = 'Salvar', fechar = 'Cancelar', valido = true, motivo = null }) {
  const id = useId()
  return <Modal titulo={titulo} aoFechar={operacao.ocupado ? undefined : aoFechar} largura="min(720px, calc(100vw - 24px))" rodape={<>{!valido && motivo && <output className={css.motivo}>{motivo}</output>}<Botao disabled={operacao.ocupado} onClick={aoFechar}>{fechar}</Botao><Botao tipo="submit" form={id} variante="primario" disabled={!valido || operacao.ocupado}>{operacao.ocupado ? 'Salvando…' : salvar}</Botao></>}>
    <form id={id} className={css.formulario} onSubmit={(evento) => { evento.preventDefault(); if (valido && !operacao.ocupado) aoEnviar() }}>
      <fieldset disabled={operacao.ocupado} className={css.campos}>{children}</fieldset>
      <ErroFinanceiro erro={operacao.erro} />
    </form>
  </Modal>
}

export function ModalContaFinanceira({ lado, conta, cadastros, aoFechar, aoGravar, aoCriar }) {
  const [descricao, setDescricao] = useState(conta?.descricao ?? '')
  const [categoria, setCategoria] = useState(conta?.categoriaFinanceiraId ?? '')
  const [centro, setCentro] = useState(conta?.centroCustoId ?? '')
  const [observacoes, setObservacoes] = useState(conta?.observacoes ?? '')
  const [centavos, setCentavos] = useState(0)
  const [quantidade, setQuantidade] = useState(1)
  const [vencimento, setVencimento] = useState(diaFinanceiro())
  const [emissao, setEmissao] = useState(diaFinanceiro())
  const [emitir, setEmitir] = useState(true)
  // #2095 (C7): "Já foi paga/recebida" registra a parcela única no mesmo gesto.
  const [jaFeito, setJaFeito] = useState(false)
  const [metodo, setMetodo] = useState('pix')
  const [dataFeito, setDataFeito] = useState(diaFinanceiro())
  const [referencia] = useState(() => crypto.randomUUID())
  const criada = useRef(null)
  const palavras = palavrasDoLado(lado)
  const operacao = useOperacaoFinanceira(aoGravar)
  const tipo = lado === 'pagar' ? 'Despesa' : 'Receita'
  const categorias = cadastros.categorias.filter((item) => (item.ativa && [tipo, 'Ambas'].includes(item.tipo)) || item.id === categoria)
  const parcelas = parcelasFinanceiras(centavos, quantidade, vencimento)
  const registrarJunto = !conta && jaFeito && quantidade === 1 && emitir
  const motivo = conta
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
      // Se o registro falhar, a conta já existe: a nova tentativa só registra, sem duplicar a conta.
      if (!criada.current) criada.current = await salvarContaFinanceira(lado, corpo, conta?.id)
      const parcela = criada.current?.parcelas?.[0]
      if (registrarJunto && parcela) {
        try { await registrarParcelaFinanceira(lado, criada.current.id, parcela.id, { valor: parcela.saldo ?? parcela.valor, metodo, dataPagamento: instanteFinanceiro(dataFeito), observacao: '', gatewayProvedor: 'Manual', gatewayTransactionId: referencia }) }
        catch (erro) { throw new Error(`A conta foi salva, mas o ${palavras.registro} não foi registrado: ${mensagemFinanceira(erro.message)} Tente de novo.`) }
      }
    })
    if (ok) { aoFechar(); if (!conta && criada.current?.id) aoCriar?.(criada.current) }
  }
  return <FormularioFinanceiro titulo={conta ? 'Editar conta' : `Nova conta a ${lado}`} aoFechar={aoFechar} operacao={operacao} aoEnviar={salvar} valido={valido} motivo={motivo}>
    <CampoTexto rotulo="Descrição" required maxLength={300} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
    <div className={css.grade}>
      <CampoSelecao rotulo="Categoria financeira" required value={categoria} onChange={(e) => setCategoria(e.target.value)} opcoes={[{ valor: '', rotulo: 'Escolha a categoria' }, ...categorias.map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
      <CampoSelecao rotulo="Centro de custo" value={centro} onChange={(e) => setCentro(e.target.value)} opcoes={[...(!conta?.centroCustoId ? [{ valor: '', rotulo: 'Sem centro de custo' }] : []), ...cadastros.centros.filter((item) => item.ativo || item.id === centro).map((item) => ({ valor: item.id, rotulo: item.nome }))]} />
    </div>
    {!categorias.length && <p className={css.aviso}>Cadastre uma categoria de {tipo.toLowerCase()} em Categorias financeiras para criar esta conta.</p>}
    {!conta && <>
      <div className={css.grade}><DinheiroFinanceiro rotulo="Valor total" centavos={centavos} aoMudar={setCentavos} /><CampoTexto rotulo="Data de emissão" tipo="date" required value={emissao} onChange={(e) => setEmissao(e.target.value)} /></div>
      <div className={css.grade}><CampoTexto rotulo="Quantidade de parcelas" tipo="number" min={1} max={60} required value={quantidade} onChange={(e) => setQuantidade(Number(e.target.value))} /><CampoTexto rotulo="Primeiro vencimento" tipo="date" required min={emissao || undefined} value={vencimento} onChange={(e) => setVencimento(e.target.value)} /></div>
      {vencimento && emissao && vencimento < emissao && <ErroFinanceiro erro="O primeiro vencimento não pode ser antes da emissão." />}
      <ol className={css.parcelas}>{parcelas.map((item) => <li key={item.numero}>Parcela {item.numero}: {moeda(item.valor)} · {diaFinanceiro(item.dataVencimento).split('-').reverse().join('/')}</li>)}</ol>
      <label className={css.escolha}><input type="checkbox" checked={emitir} onChange={(e) => { setEmitir(e.target.checked); if (!e.target.checked) setJaFeito(false) }} /> Emitir a conta ao salvar</label>
      {quantidade === 1 && emitir && <label className={css.escolha}><input type="checkbox" checked={jaFeito} onChange={(e) => setJaFeito(e.target.checked)} /> {palavras.jaFeito}</label>}
      {registrarJunto && <div className={css.grade}><CampoSelecao rotulo={palavras.forma} value={metodo} onChange={(e) => setMetodo(e.target.value)} opcoes={METODOS_CAIXA.map((item) => ({ valor: item, rotulo: nomeDoMetodoCaixa(item) }))} /><CampoTexto rotulo={palavras.data} tipo="date" required max={diaFinanceiro()} value={dataFeito} onChange={(e) => setDataFeito(e.target.value)} /></div>}
    </>}
    <CampoArea rotulo="Observações" rows={3} value={observacoes} onChange={(e) => setObservacoes(e.target.value)} />
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
