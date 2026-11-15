import { useEffect, useId, useRef, useState } from 'react'
import { adicionarEnderecoCliente, adicionarTelefoneCliente, obterCadastro, removerEnderecoCliente, removerTelefoneCliente } from '../../../aplicacao/cadastros'
import { listarPedidosDoCliente, obterPedido } from '../../../aplicacao/pedidosCadastro'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { Pilula } from '../../../componentes/Pilula'
import { telefoneNaTela } from '../../../dominio/cadastros'
import { aplicarNaFicha, corpoDoEndereco, corpoDoTelefone, ENDERECO_VAZIO, enderecoVaiComoPadrao, erroDoTelefoneExtra, errosDoEndereco, linhaDoEndereco, mensagemDaFicha, resumoDePedidos } from '../../../dominio/fichaCliente'
import { dataHora, moeda } from '../../../dominio/formato'
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

export function FichaCliente({ cliente, detalhe, contexto, podeEditar }) {
  const ids = { pedidos: useId(), enderecos: useId(), telefones: useId(), form: useId() }
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
  const resumo = resumoDePedidos(cliente)

  const abrir = (acao, alvo = null) => { falhar(null); avisar(null); definirErros({}); definirForm(acao === 'telefone' ? { numero: '' } : ENDERECO_VAZIO); marcarWhatsapp(false); escolher({ acao, alvo }) }
  const fechar = () => { if (!emVoo.current) escolher(null) }
  const mudar = (chave, valor) => { definirForm(atual => ({ ...atual, [chave]: valor })); definirErros(atual => ({ ...atual, [chave]: null })) }

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
      if (!montado.current) return
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
    {sucesso && <p className={css.sucesso} role="status">{sucesso}</p>}
    {!intencao && erro && <p className={css.erro} role="alert">{erro}</p>}
    <section aria-labelledby={ids.pedidos}>
      <div className={css.barra}><h3 id={ids.pedidos}>Pedidos</h3></div>
      <p><strong>{resumo.contagem}</strong>{resumo.ultimo && <> · {resumo.ultimo}</>}</p>
      {vePedidos && (pedidos.erro ? <div className={css.erro} role="alert"><p>{pedidos.erro}</p><Botao onClick={pedidos.recarregar}>Tentar de novo</Botao></div>
        : pedidos.carregando ? <p className={css.legenda}>Carregando pedidos…</p>
        : pedidos.itens.length > 0 && <ul className={css.itensFicha} aria-label="Últimos pedidos">{pedidos.itens.map(p => <li key={p.id}>
          <span><strong>{tituloDoPedido(p)}</strong><small>{[p.criadoEm && dataHora(p.criadoEm), rotuloDaSituacao(p.status), moeda(p.total)].filter(Boolean).join(' · ')}</small></span>
          <Botao disabled={Boolean(abrindo)} onClick={() => verPedido(p)} aria-label={`Abrir ${tituloDoPedido(p)}`}>{abrindo === p.id ? 'Abrindo…' : 'Abrir'}</Botao>
        </li>)}</ul>)}
    </section>
    <section aria-labelledby={ids.enderecos}>
      <div className={css.barra}><h3 id={ids.enderecos}>Endereços</h3>{podeEditar && <Botao icone="mais" onClick={() => abrir('endereco')}>Adicionar endereço</Botao>}</div>
      {listas.enderecos.length === 0 ? <p className={css.legenda}>Nenhum endereço cadastrado.</p>
        : <ul className={css.itensFicha}>{listas.enderecos.map(e => { const linha = linhaDoEndereco(e); return <li key={e.id}>
          <span><strong>{linha.principal}</strong>{linha.detalhe && <small>{linha.detalhe}</small>}</span>
          <span className={css.acoes}>{e.padrao && <Pilula fina>Padrão</Pilula>}{podeEditar && <Botao variante="texto" aria-label={`Remover endereço ${linha.principal}`} onClick={() => abrir('removerEndereco', e)}>Remover</Botao>}</span>
        </li> })}</ul>}
    </section>
    <section aria-labelledby={ids.telefones}>
      <div className={css.barra}><h3 id={ids.telefones}>Telefones extras</h3>{podeEditar && <Botao icone="mais" onClick={() => abrir('telefone')}>Adicionar telefone</Botao>}</div>
      {listas.telefones.length === 0 ? <p className={css.legenda}>Nenhum telefone extra.</p>
        : <ul className={css.itensFicha}>{listas.telefones.map(t => <li key={t.id}>
          <span><strong>{telefoneNaTela(t.numero) || t.numero}</strong></span>
          <span className={css.acoes}>{t.whatsapp && <Pilula fina>WhatsApp</Pilula>}{podeEditar && <Botao variante="texto" aria-label={`Remover telefone ${telefoneNaTela(t.numero) || t.numero}`} onClick={() => abrir('removerTelefone', t)}>Remover</Botao>}</span>
        </li>)}</ul>}
    </section>
    {intencao && <Modal titulo={TITULO[intencao.acao]} largura={intencao.acao === 'endereco' ? 'md' : 'sm'} aoFechar={ocupado ? undefined : fechar} segurarFechamento={ocupado}
      rodape={<><Botao disabled={ocupado} onClick={fechar}>Cancelar</Botao><Botao variante={intencao.acao.startsWith('remover') ? 'perigo' : 'primario'} disabled={ocupado} {...(intencao.acao.startsWith('remover') ? { onClick: confirmar } : { tipo: 'submit', form: ids.form })}>{ocupado ? 'Salvando…' : CONFIRMAR[intencao.acao]}</Botao></>}>
      {intencao.acao === 'endereco' && <form id={ids.form} ref={formRef} onSubmit={confirmar} className={css.formulario} noValidate>
        {CAMPOS_ENDERECO.map(([chave, rotulo, obrigatorio]) => <CampoTexto key={chave} rotulo={rotulo} obrigatorio={obrigatorio} value={form[chave] ?? ''} erro={erros[chave]} disabled={ocupado}
          inputMode={chave === 'cep' ? 'numeric' : undefined} autoComplete={chave === 'cep' ? 'postal-code' : undefined} onChange={e => mudar(chave, e.target.value)} />)}
      </form>}
      {intencao.acao === 'telefone' && <form id={ids.form} ref={formRef} onSubmit={confirmar} noValidate>
        <CampoTexto rotulo="Telefone" obrigatorio tipo="tel" inputMode="tel" value={form.numero ?? ''} erro={erros.numero} disabled={ocupado} onChange={e => mudar('numero', e.target.value)} />
        <label className={css.check}><input type="checkbox" checked={whatsapp} disabled={ocupado} onChange={e => marcarWhatsapp(e.target.checked)} />Tem WhatsApp</label>
      </form>}
      {intencao.acao === 'removerEndereco' && <p>{linhaDoEndereco(intencao.alvo).principal} sai da ficha. Para mudar um endereço, remova e adicione de novo.</p>}
      {intencao.acao === 'removerTelefone' && <p>{telefoneNaTela(intencao.alvo.numero) || intencao.alvo.numero} sai da ficha.</p>}
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </Modal>}
    {pedidoAberto && <DetalhePedido dados={pedidoAberto} aoFechar={() => abrirPedido(null)} />}
  </div>
}
