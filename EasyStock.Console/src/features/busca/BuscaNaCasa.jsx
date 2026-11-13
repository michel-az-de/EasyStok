import { useEffect, useRef, useState } from 'react'
import { buscarNaCasa, contextoDaBusca, obterCadastro, obterPedidoDaBusca } from '../../aplicacao/buscaNaCasa'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { BuscaComSugestoes } from '../../componentes/BuscaComSugestoes'
import { Botao } from '../../componentes/Botao'
import { Modal } from '../../componentes/Modal'
import { LIMITE_BUSCA_CASA, nomesDaBusca } from '../../dominio/buscaNaCasa'
import { buscarDestinosDoConsole, modulosDoHall } from '../../dominio/modulos'
import css from './busca.module.css'

export function BuscaNaCasa({ aoFechar, aoAbrirConversa, renderizarCadastro, renderizarPedido }) {
  const acesso = useAcessoModulos()
  const [contexto] = useState(contextoDaBusca)
  const [termo, mudar] = useState('')
  const [consulta, definir] = useState({ resultados: [], indisponiveis: [], carregando: false })
  const [erro, falhar] = useState(null)
  const [abrindo, abrir] = useState(null)
  const [editor, editar] = useState(null)
  const [pedido, mostrarPedido] = useState(null)
  const leitura = useRef(null)
  const buscando = useRef(null)
  const [tentativa, tentar] = useState(0)
  const pode = acesso.permite
  const acoes = acesso.acoes
  useEffect(() => {
    const controle = new AbortController()
    buscando.current = controle
    definir({ resultados: [], indisponiveis: [], carregando: Boolean(termo.trim()) })
    if (!termo.trim()) return () => controle.abort()
    const espera = setTimeout(() => {
      buscarNaCasa(termo, { permite: pode, acoes }, contexto, controle.signal).then(r => {
        if (!controle.signal.aborted) definir({ ...r, carregando: false })
      }).catch(e => { if (!controle.signal.aborted) { falhar(e.message); definir({ resultados: [], indisponiveis: [], carregando: false }) } })
    }, 200)
    return () => { clearTimeout(espera); controle.abort() }
  }, [termo, contexto, pode, acoes, tentativa])
  useEffect(() => () => leitura.current?.abort(), [])
  const telas = buscarDestinosDoConsole(modulosDoHall({ fonteApi: true, acoes }).filter(m => pode(m.id)), termo)
    .map(d => ({ id: `telas:${d.href}`, tipo: 'telas', rotulo: d.tela.rotulo, descricao: `Tela · ${d.modulo.nome}`, href: d.href }))
  const resultados = [...consulta.resultados, ...telas]

  async function escolher(resultado) {
    if (abrindo) return
    falhar(null)
    if (resultado.tipo === 'telas') { window.location.hash = resultado.href; aoFechar(); return }
    if (resultado.tipo === 'conversas') { aoAbrirConversa(resultado.item); aoFechar(); return }
    const controle = new AbortController(); leitura.current?.abort(); leitura.current = controle
    abrir(resultado.id)
    try {
      if (resultado.tipo === 'pedidos') {
        const dados = await obterPedidoDaBusca(resultado.item.id, contexto, controle.signal)
        if (!controle.signal.aborted) mostrarPedido(dados)
      } else {
        const dados = await obterCadastro(resultado.tipo, resultado.item.id, contexto, controle.signal)
        if (!controle.signal.aborted) editar({ tela: resultado.tipo, dados, inicial: dados })
      }
    } catch (e) { if (!controle.signal.aborted) falhar(e.message) }
    finally { if (!controle.signal.aborted) abrir(null) }
  }
  return <>
    <Modal titulo="Buscar na Casa" descricao="Pessoas, conversas, pedidos, produtos e telas que seu perfil pode abrir." aoFechar={aoFechar} largura="md" focarCampo>
      <div className={css.busca}>
        <p>Até {LIMITE_BUSCA_CASA} resultados por categoria. Pessoas: cadastros ativos. Refine a busca para encontrar outro registro.</p>
        <BuscaComSugestoes valor={termo} aoMudar={valor => { buscando.current?.abort(); leitura.current?.abort(); abrir(null); falhar(null); definir({ resultados: [], indisponiveis: [], carregando: Boolean(valor.trim()) }); mudar(valor) }} opcoes={resultados.map(r => ({ ...r, item: r }))} aoEscolher={escolher}
          carregando={consulta.carregando} minimo={1} rotulo="Buscar na Casa" rotuloLista="Resultados da busca na Casa" textoCarregando="Consultando a loja…" placeholder="Nome, telefone, ID ou tela" />
        {abrindo && <p role="status">Abrindo o registro…</p>}
        {erro && <p role="alert">{erro}</p>}
        {consulta.indisponiveis.length > 0 && <div role="alert"><p>Parte da busca não está disponível:</p><ul>{consulta.indisponiveis.map(e => <li key={e.tipo}>{nomesDaBusca[e.tipo]}: {e.mensagem}</li>)}</ul><Botao onClick={() => tentar(t => t + 1)}>Tentar novamente</Botao></div>}
        {termo.trim() && !consulta.carregando && resultados.length === 0 && <p role="status">Nenhum resultado nas categorias consultadas.</p>}
      </div>
    </Modal>
    {editor && renderizarCadastro({ tela: editor.tela, editor, aoMudar: editar, contexto, podeEditar: editor.tela === 'clientes' || acoes.editarCadastrosProdutos === true,
      aoFechar: () => editar(null), aoSalvo: () => { editar(null); tentar(t => t + 1) } })}
    {/* #2478: o mesmo detalhe da tela Pedidos de Cadastros, injetado pelo App. */}
    {pedido && renderizarPedido({ dados: pedido, aoFechar: () => mostrarPedido(null) })}
  </>
}
