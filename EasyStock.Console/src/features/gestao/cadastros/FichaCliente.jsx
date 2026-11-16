import { useEffect, useId, useRef, useState } from 'react'
import { adicionarEnderecoCliente, adicionarTelefoneCliente, obterCadastro, removerEnderecoCliente, removerTelefoneCliente } from '../../../aplicacao/cadastros'
import { listarPedidosDoCliente, obterPedido } from '../../../aplicacao/pedidosCadastro'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { telefoneNaTela } from '../../../dominio/cadastros'
import { aplicarNaFicha, corpoDoEndereco, corpoDoTelefone, ENDERECO_VAZIO, enderecoVaiComoPadrao, erroDoTelefoneExtra, errosDoEndereco, linhaDoEndereco, mensagemDaFicha, numerosDoRelacionamento, resumoDePedidos, telefonesDaFicha } from '../../../dominio/fichaCliente'
import { dataHora, mascaraCep, moeda } from '../../../dominio/formato'
import { rotuloDaSituacao, tituloDoPedido } from '../../../dominio/pedidosCadastro'
import { DetalhePedido } from './DetalhePedido'
import css from './cadastros.module.css'

// #2495 (S6, I4): ficha do cliente existente no editor de Cadastros. Grava sozinha, fora do formulário: adicionar
// ou remover endereço e telefone não mexe no que a pessoa digitou acima nem marca o cadastro como alterado.
const CAMPOS_ENDERECO = [
  ['logradouro', 'Rua ou avenida', true], ['numero', 'Número'], ['complemento', 'Complemento'], ['bairro', 'Bairro'],
  ['cidade', 'Cidade'], ['estado', 'UF'], ['cep', 'CEP'], ['referencia', 'Ponto de referência'],
]
const TITULO = { endereco: 'Novo endereço', telefone: 'Novo telefone', removerEndereco: 'Remover endereço?', removerTelefone: 'Remover telefone?' }
const CONFIRMAR = { endereco: 'Adicionar endereço', telefone: 'Adicionar telefone', removerEndereco: 'Remover', removerTelefone: 'Remover' }
const SUCESSO = { endereco: 'Endereço adicionado.', telefone: 'Telefone adicionado.', removerEndereco: 'Endereço removido.', removerTelefone: 'Telefone removido.' }
// #2734 (A14): CEP e telefone ganham a máscara enquanto a pessoa digita, a mesma do campo Telefone do cadastro.
// O endereço também tem um campo `numero` (o da casa): a máscara de telefone só vale no formulário de telefone.
const comMascara = (acao, chave, valor) => (chave === 'cep' ? mascaraCep(valor) : acao === 'telefone' && chave === 'numero' ? telefoneNaTela(valor) : valor)
const pedirFechamento = (evento) => evento.currentTarget.closest('dialog')?.dispatchEvent(new Event('cancel', { cancelable: true }))

// Carregando é derivado (a resposta guardada é de outra chave), sem setState síncrono no efeito.
function usePedidosDoCliente(clienteId, { empresaId, identidade }, habilitado) {
  const [revisao, recarregar] = useState(0)
  const chave = `${clienteId}:${empresaId}:${revisao}`
  const [estado, definir] = useState({ chave: null, itens: [], erro: null })
  useEffect(() => {
    if (!habilitado) return undefined
    const controlador = new AbortController()
    listarPedidosDoCliente(clienteId, { empresaId, identidade }, controlador.signal).then(
      itens => { if (!controlador.signal.aborted) definir({ chave, itens, erro: null }) },
      e => { if (!controlador.signal.aborted) definir({ chave, itens: [], erro: e.message }) },
    )
    return () => controlador.abort()
  }, [chave, clienteId, empresaId, identidade, habilitado])
  const atual = estado.chave === chave
  return { itens: atual ? estado.itens : [], erro: atual ? estado.erro : null, carregando: habilitado && !atual, recarregar: () => recarregar(r => r + 1) }
}

// #2734: `relacionamento` é o dossiê já lido pelo editor (valor total, favorito e conversas recentes); sem
// `aoAbrirConversa` (cadastro com mudança por salvar) as conversas ficam listadas, mas não abrem.
// `conversasNoAtendimento` são as que o Atendimento já carregou. Vem por prop: este editor também abre pela
// Buscar na Casa, fora do AtendimentoProvider, e lá as conversas ficam só como registro.
// `aoGravar` avisa quem mostra a mesma pessoa (a Ficha do Atendimento) de que endereço ou telefone mudou.
export function FichaCliente({ cliente, detalhe, contexto, podeEditar, relacionamento, conversasNoAtendimento, aoAbrirConversa, aoGravar }) {
  const ids = { pedidos: useId(), enderecos: useId(), telefones: useId(), conversas: useId(), form: useId() }
  const { acoes } = useAcessoModulos()
  const vePedidos = acoes.verPedidos === true
  const pedidos = usePedidosDoCliente(cliente.id, contexto, vePedidos)
  const [listas, definirListas] = useState({ enderecos: detalhe?.enderecos ?? [], telefones: detalhe?.telefones ?? [] })
  const [intencao, escolher] = useState(null)
  const [form, definirForm] = useState(ENDERECO_VAZIO)
  const [whatsapp, marcarWhatsapp] = useState(false)
  const [erros, definirErros] = useState({})
  const [erro, falhar] = useState(null)
  const [ocupado, ocupar] = useState(false)
  const [sucesso, avisar] = useState(null)
  const [pedidoAberto, abrirPedido] = useState(null)
  const [abrindo, marcarAbrindo] = useState(null)
  const emVoo = useRef(false)
  const formRef = useRef(null)
  const leitura = useRef(0)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false } }, [])
  // #2734: removido o endereço ou o telefone, o botão que tinha o foco sai da tela. O foco vai para o controle da
  // mesma seção; sem isto ele caía no X do cadastro. Roda depois da limpeza do Modal, que devolve o foco antes.
  const botaoEndereco = useRef(null)
  const botaoTelefone = useRef(null)
  const focoPendente = useRef(null)
  useEffect(() => {
    const alvo = focoPendente.current
    if (!alvo) return
    focoPendente.current = null
    alvo.current?.focus()
  })
  const resumo = resumoDePedidos(cliente)
  const numeros = numerosDoRelacionamento(relacionamento)
  const telefones = telefonesDaFicha(listas.telefones)
  const recentes = relacionamento?.conversas ?? []
  // Só abre daqui a conversa que o Atendimento já carregou; as outras ficam como registro.
  const noAtendimento = new Set(conversasNoAtendimento ?? [])
  const digitando = Boolean(intencao) && !intencao.acao.startsWith('remover')
  const comTexto = digitando && Object.values(form).some(valor => String(valor ?? '').trim())

  const abrir = (acao, alvo = null) => { falhar(null); avisar(null); definirErros({}); definirForm(acao === 'telefone' ? { numero: '' } : ENDERECO_VAZIO); marcarWhatsapp(false); escolher({ acao, alvo }) }
  const fechar = () => { if (!emVoo.current) escolher(null) }
  const mudar = (chave, valor) => { definirForm(atual => ({ ...atual, [chave]: comMascara(intencao?.acao, chave, valor) })); definirErros(atual => ({ ...atual, [chave]: null })) }

  async function confirmar(evento) {
    evento?.preventDefault()
    if (emVoo.current || !intencao) return
    const { acao, alvo } = intencao
    const problemas = acao === 'endereco' ? errosDoEndereco(form) : acao === 'telefone' && erroDoTelefoneExtra(form.numero) ? { numero: erroDoTelefoneExtra(form.numero) } : {}
    if (Object.keys(problemas).length) {
      definirErros(problemas)
      requestAnimationFrame(() => formRef.current?.querySelector('[aria-invalid="true"]')?.focus())
      return
    }
    emVoo.current = true; ocupar(true); falhar(null)
    const corpo = acao === 'endereco' ? corpoDoEndereco(form, contexto.empresaId, enderecoVaiComoPadrao(listas.enderecos))
      : acao === 'telefone' ? corpoDoTelefone(form.numero, whatsapp, contexto.empresaId) : null
    try {
      const resposta = acao === 'endereco' ? await adicionarEnderecoCliente(cliente.id, corpo, contexto)
        : acao === 'telefone' ? await adicionarTelefoneCliente(cliente.id, corpo, contexto)
        : acao === 'removerEndereco' ? await removerEnderecoCliente(cliente.id, alvo.id, contexto)
        : await removerTelefoneCliente(cliente.id, alvo.id, contexto)
      // A Ficha do Atendimento relê a pessoa mesmo que este editor já tenha fechado.
      aoGravar?.()
      if (!montado.current) return
      if (acao === 'removerEndereco') focoPendente.current = botaoEndereco
      if (acao === 'removerTelefone') focoPendente.current = botaoTelefone
      escolher(null); avisar(SUCESSO[acao])
      definirListas(atual => aplicarNaFicha(atual, acao, alvo, corpo, resposta?.id))
      sincronizar()
    } catch (e) { if (montado.current) falhar(mensagemDaFicha(e)) }
    finally { emVoo.current = false; if (montado.current) ocupar(false) }
  }

  // A gravação já valeu: a recarga só traz o que o servidor guardou. Se falhar, a lista local fica; vale a última leitura.
  async function sincronizar() {
    const minha = ++leitura.current
    try {
      const atual = await obterCadastro('clientes', cliente.id, contexto)
      if (montado.current && minha === leitura.current) definirListas({ enderecos: atual.detalhe?.enderecos ?? [], telefones: atual.detalhe?.telefones ?? [] })
    } catch { /* a lista local já reflete a gravação */ }
  }

  async function verPedido(item) {
    if (abrindo) return
    marcarAbrindo(item.id); falhar(null)
    try { const dados = await obterPedido(item.id, contexto); if (montado.current) abrirPedido(dados) }
    catch (e) { if (montado.current) { avisar(null); falhar(e.message) } }
    finally { if (montado.current) marcarAbrindo(null) }
  }

  return <div className={css.ficha}>
    {/* #2734: a região de aviso fica sempre na tela; o leitor de tela só anuncia o que muda dentro dela. */}
    <div role="status" className={css.vivo}>{sucesso && <p className={css.sucesso}>{sucesso}</p>}</div>
    {!intencao && erro && <p className={css.erro} role="alert">{erro}</p>}
    <section aria-labelledby={ids.pedidos}>
      <div className={css.barra}><h3 id={ids.pedidos}>Pedidos</h3></div>
      <p><strong>{resumo.contagem}</strong>{resumo.ultimo && <> · {resumo.ultimo}</>}</p>
      {numeros.length > 0 && <p>{numeros.join(' · ')}</p>}
      {vePedidos && (pedidos.erro ? <div className={css.erro} role="alert"><p>{pedidos.erro}</p><Botao onClick={pedidos.recarregar}>Tentar de novo</Botao></div>
        : pedidos.carregando ? <p className={css.legenda}>Carregando pedidos…</p>
        : pedidos.itens.length > 0 && <ul className={css.itensFicha} aria-label="Últimos pedidos">{pedidos.itens.map(p => <li key={p.id}>
          <span><strong>{tituloDoPedido(p)}</strong><small>{[p.criadoEm && dataHora(p.criadoEm), rotuloDaSituacao(p.status), moeda(p.total)].filter(Boolean).join(' · ')}</small></span>
          <Botao disabled={Boolean(abrindo)} onClick={() => verPedido(p)} aria-label={`Abrir ${tituloDoPedido(p)}`}>{abrindo === p.id ? 'Abrindo…' : 'Abrir'}</Botao>
        </li>)}</ul>)}
    </section>
    <section aria-labelledby={ids.enderecos}>
      <div className={css.barra}><h3 id={ids.enderecos}>Endereços</h3>{podeEditar && <Botao ref={botaoEndereco} icone="mais" onClick={() => abrir('endereco')}>Adicionar endereço</Botao>}</div>
      {listas.enderecos.length === 0 ? <Vazio variante="linha" titulo="Nenhum endereço cadastrado." />
        : <ul className={css.itensFicha}>{listas.enderecos.map(e => { const linha = linhaDoEndereco(e); return <li key={e.id}>
          <span><strong>{linha.principal}</strong>{linha.detalhe && <small>{linha.detalhe}</small>}</span>
          <span className={css.acoes}>{e.padrao && <Pilula fina>Padrão</Pilula>}{podeEditar && <Botao variante="texto" aria-label={`Remover endereço ${linha.principal}`} onClick={() => abrir('removerEndereco', e)}>Remover</Botao>}</span>
        </li> })}</ul>}
    </section>
    <section aria-labelledby={ids.telefones}>
      <div className={css.barra}><h3 id={ids.telefones}>Telefones</h3>{podeEditar && <Botao ref={botaoTelefone} icone="mais" onClick={() => abrir('telefone')}>Adicionar telefone</Botao>}</div>
      {telefones.length === 0 ? <Vazio variante="linha" titulo="Nenhum telefone extra." />
        : <ul className={css.itensFicha}>{telefones.map(t => { const numero = telefoneNaTela(t.numero) || t.numero; return <li key={t.id}>
          <span><strong>{numero}</strong></span>
          <span className={css.acoes}>{t.principal && <Pilula fina>Principal</Pilula>}{t.whatsapp && <Pilula fina>WhatsApp</Pilula>}{podeEditar && t.removivel && <Botao variante="texto" aria-label={`Remover telefone ${numero}`} onClick={() => abrir('removerTelefone', t)}>Remover</Botao>}</span>
        </li> })}</ul>}
      {telefones.some(t => t.principal) && <p className={css.legenda}>O telefone principal identifica a pessoa no Atendimento e não sai por esta lista.</p>}
    </section>
    {relacionamento && <section aria-labelledby={ids.conversas}>
      <div className={css.barra}><h3 id={ids.conversas}>Conversas recentes</h3></div>
      {recentes.length === 0 ? <Vazio variante="linha" titulo="Nenhuma conversa ainda." />
        : <ul className={css.itensFicha}>{recentes.map(c => <li key={c.id}>
          <span>{[c.canal, c.estado, c.em].filter(Boolean).join(' · ')}</span>
          {noAtendimento.has(c.id) && <Botao disabled={!aoAbrirConversa} onClick={() => aoAbrirConversa?.(c.id)} aria-label={`Abrir conversa de ${[c.canal, c.em].filter(Boolean).join(', ')}`}>Abrir conversa</Botao>}
        </li>)}</ul>}
      {/* #2734: conversa sem botão tem explicação; pela Buscar na Casa nenhuma abre daqui. */}
      {recentes.some(c => !noAtendimento.has(c.id)) && <p className={css.legenda}>{conversasNoAtendimento
        ? 'Só a conversa que está na lista do Atendimento abre daqui. As outras ficam como registro.'
        : 'Aqui as conversas ficam como registro. Para abrir uma, use o Atendimento.'}</p>}
      {!aoAbrirConversa && recentes.some(c => noAtendimento.has(c.id)) && <p className={css.legenda}>Salve ou descarte as mudanças do cadastro para abrir uma conversa.</p>}
    </section>}
    {intencao && <Modal titulo={TITULO[intencao.acao]} largura={intencao.acao === 'endereco' ? 'md' : 'sm'} focarCampo={digitando} aoFechar={ocupado ? undefined : fechar} segurarFechamento={ocupado} confirmarDescarte={comTexto && !ocupado}
      rodape={<><Botao disabled={ocupado} onClick={digitando ? pedirFechamento : fechar}>Cancelar</Botao><Botao variante={intencao.acao.startsWith('remover') ? 'perigo' : 'primario'} disabled={ocupado} {...(intencao.acao.startsWith('remover') ? { onClick: confirmar } : { tipo: 'submit', form: ids.form })}>{ocupado ? 'Salvando…' : CONFIRMAR[intencao.acao]}</Botao></>}>
      {intencao.acao === 'endereco' && <form id={ids.form} ref={formRef} onSubmit={confirmar} className={css.formulario} noValidate>
        {CAMPOS_ENDERECO.map(([chave, rotulo, obrigatorio]) => <CampoTexto key={chave} rotulo={rotulo} obrigatorio={obrigatorio} value={form[chave] ?? ''} erro={erros[chave]} disabled={ocupado}
          inputMode={chave === 'cep' ? 'numeric' : undefined} autoComplete={chave === 'cep' ? 'postal-code' : undefined} onChange={e => mudar(chave, e.target.value)} />)}
      </form>}
      {intencao.acao === 'telefone' && <form id={ids.form} ref={formRef} onSubmit={confirmar} noValidate>
        <CampoTexto rotulo="Telefone" obrigatorio tipo="tel" inputMode="tel" value={form.numero ?? ''} erro={erros.numero} disabled={ocupado} onChange={e => mudar('numero', e.target.value)} />
        <label className={css.check}><input type="checkbox" checked={whatsapp} disabled={ocupado} onChange={e => marcarWhatsapp(e.target.checked)} />Tem WhatsApp</label>
      </form>}
      {intencao.acao === 'removerEndereco' && <p>{linhaDoEndereco(intencao.alvo).principal} sai do cadastro. Para mudar um endereço, remova e adicione de novo.</p>}
      {intencao.acao === 'removerTelefone' && <p>{telefoneNaTela(intencao.alvo.numero) || intencao.alvo.numero} sai do cadastro.</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>}
    {pedidoAberto && <DetalhePedido dados={pedidoAberto} aoFechar={() => abrirPedido(null)} />}
  </div>
}
