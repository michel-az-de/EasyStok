import { useEffect, useMemo, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../componentes/Campo'
import { Icone } from '../../componentes/Icone'
import { Modal } from '../../componentes/Modal'
import { Pilula } from '../../componentes/Pilula'
import { Vazio } from '../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../aplicacao/contextos'
import { contextoDePrevia, textoDaRegra } from '../../dominio/automacao'
import { canalDaConversa } from '../../dominio/canal'
import { textoDaCobranca } from '../../dominio/cobranca'
import { faixaDaJanela } from '../../dominio/entrega'
import { descreverProximaAbertura } from '../../dominio/funcionamento'
import { permissaoDeEscrita } from '../../dominio/janela'
import { preencherModelo, primeiroNome } from '../../dominio/mensagem'
import {
  ID_ITEM_COBRANCA_PIX, agruparPorCategoria, avisoDeFaltando, filtrarBiblioteca, gerarAtalho, listarBiblioteca,
  resolverVariaveis,
} from '../../dominio/respostas'
import css from './respostas.module.css'

// Variáveis que uma resposta pronta aceita (dominio/respostas.js,
// resolverVariaveis): chip por variável em vez do texto corrido "Variáveis:
// {nome}, {pedido}" (achado 2, pendência 21 da banca 64).
const VARIAVEIS_RESPOSTA = ['nome', 'pedido']

// Texto que a Thatiane manda agora, resolvido pela conversa aberta: automática
// usa a MESMA conta de dominio/automacao.js (contextoDePrevia + textoDaRegra)
// que features/automacoes/ModalAutomacoes.jsx já usa, então a prévia nunca
// diverge entre as duas telas. Pronta usa resolverVariaveis (nome e pedido,
// que avisa em vez de inventar quando falta valor) mais a mesma `faixa` já
// calculada para o contexto automático (só "Confirmar janela de entrega"
// usa `{faixa}`, e ela não pode ficar crua na tela por causa disso).
// Cobrança (Pix ou cartão por link): texto pleno vem de dominio/cobranca.js,
// dependente do pedido E da cobrança de fato geradas nesta conversa (valor,
// link, copia e cola mudam a cada pedido). Sem os dois, não existe texto real
// para mostrar nem enviar; a tela diz isso em vez de inventar um exemplo.
function resolverTextoCobranca(conversa, cardapio) {
  const cobranca = conversa?.pedido?.cobranca
  if (!cobranca) {
    return {
      texto: 'Sem cobrança gerada nesta conversa agora. O texto muda por pedido '
        + '(valor, link e copia e cola) e só existe depois de gerar a cobrança na ficha.',
      faltando: [],
      semCobranca: true,
    }
  }
  return { texto: textoDaCobranca(cobranca, conversa.pedido, cardapio), faltando: [], semCobranca: false }
}

function resolverTexto(item, texto, conversa, contextoAutomatico, cardapio) {
  if (item.id === ID_ITEM_COBRANCA_PIX) return resolverTextoCobranca(conversa, cardapio)
  if (item.tipo === 'automatica') return { texto: textoDaRegra({ texto }, contextoAutomatico), faltando: [] }
  return resolverVariaveis(texto, conversa, { faixa: contextoAutomatico.faixa })
}

function ItemDaBiblioteca({
  item, conversa, contextoAutomatico, cardapio, podeEscrever, focoInicial,
  aoEnviar, aoUsar, aoSalvar, aoArquivar, aoAlternarAtiva,
}) {
  const emFoco = item.id === focoInicial
  const [aberto, setAberto] = useState(emFoco)
  const [rascunho, setRascunho] = useState(null)

  // Achado 2 (banca 10): Pix e as automáticas da esteira entram na
  // biblioteca visíveis e reenviáveis, mas sem edição de texto (mesmo corte
  // já usado em "Modelos aprovados" nesta tela): o texto delas é gerado por
  // pedido ou é um passo fixo da esteira, não um campo solto para editar.
  const editavel = item.editavel !== false

  const valor = rascunho ?? { titulo: item.titulo, categoria: item.categoria, texto: item.texto }
  const editando = editavel
    && (valor.texto !== item.texto || valor.titulo !== item.titulo || valor.categoria !== item.categoria)
  const mudar = (campo, novo) => setRascunho({ ...valor, [campo]: novo })
  const { texto: previa, faltando, semCobranca } = resolverTexto(item, valor.texto, conversa, contextoAutomatico, cardapio)
  const aviso = avisoDeFaltando(faltando)
  const semConteudoParaEnviar = Boolean(semCobranca)

  function salvar() {
    aoSalvar(item, { titulo: valor.titulo.trim(), categoria: valor.categoria.trim(), texto: valor.texto.trim() })
    setRascunho(null)
  }

  return (
    <li id={`biblioteca-item-${item.id}`} className={`${css.item} ${item.arquivada ? css.arquivada : ''}`}>
      <div className={css.linhaItem}>
        <span className={css.identidade}>
          {item.tipo === 'automatica' && <Pilula tom="neutro" icone="raio">Automática</Pilula>}
          {item.arquivada && <Pilula tom="neutro">Arquivada</Pilula>}
          <strong>{item.titulo}</strong>
          <span className={css.atalho}>{item.atalho}</span>
        </span>
        {item.tipo === 'automatica' && item.regraId && !item.regraId.startsWith('esteira-') && item.regraId !== ID_ITEM_COBRANCA_PIX && (
          <label className={css.chave}>
            <input
              type="checkbox"
              aria-label={`${item.titulo}, ${item.ativa ? 'ligada' : 'desligada'}`}
              checked={item.ativa}
              onChange={() => aoAlternarAtiva(item)}
            />
            {item.ativa ? 'ligada' : 'desligada'}
          </label>
        )}
        {editavel && (
          <Botao variante="texto" icone="lapis" aria-expanded={aberto} onClick={() => setAberto((v) => !v)}>
            {aberto ? 'Fechar' : 'Editar'}
          </Botao>
        )}
      </div>

      {/* Achado 3, P1 (banca 10): o gatilho de cada automática (quando ela
          dispara) só existia no modal Automáticas do trilho; aqui reaproveita
          o mesmo `item.descricao`, sem reescrever nada. */}
      {item.descricao && <p className={css.descricaoItem}>{item.descricao}</p>}

      {(!editavel || !aberto) && (
        <>
          <p className={css.previaFechada}>{previa}</p>
          {aviso && <p className={css.aviso}><Icone nome="alerta" /> {aviso}</p>}
        </>
      )}

      {editavel && aberto && (
        <div className={css.edicao}>
          {item.tipo === 'pronta' && (
            <div className={css.parIdentidade}>
              <CampoTexto rotulo="Título" value={valor.titulo} onChange={(e) => mudar('titulo', e.target.value)} />
              <CampoTexto rotulo="Categoria" value={valor.categoria} onChange={(e) => mudar('categoria', e.target.value)} />
            </div>
          )}
          <CampoArea
            rotulo={`Texto de ${item.titulo}`}
            value={valor.texto}
            onChange={(e) => mudar('texto', e.target.value)}
          />
          <p className={css.variaveis}>
            <span>Variáveis:</span>
            {VARIAVEIS_RESPOSTA.map((v) => <span key={v} className={css.tokenVariavel}>{`{${v}}`}</span>)}
          </p>
          <p className={css.rotuloPrevia}>Fica assim, com esta conversa:</p>
          <p className={css.bolha}>{previa}</p>
          {aviso && (
            <p className={css.aviso}><Icone nome="alerta" /> {aviso}</p>
          )}
          <div className={css.acoesEdicao}>
            <Botao variante="primario" disabled={!editando} onClick={salvar}>Salvar</Botao>
            <Botao disabled={!editando} onClick={() => setRascunho(null)}>Desfazer</Botao>
          </div>
        </div>
      )}

      <div className={css.acoesItem}>
        <Botao
          icone="copy"
          disabled={semConteudoParaEnviar}
          title={semConteudoParaEnviar ? 'Sem cobrança gerada nesta conversa.' : undefined}
          onClick={() => aoUsar(item, previa)}
        >
          Usar no campo
        </Botao>
        <Botao
          icone="enviar"
          disabled={!podeEscrever || semConteudoParaEnviar}
          title={semConteudoParaEnviar
            ? 'Sem cobrança gerada nesta conversa.'
            : (!podeEscrever ? 'Janela fechada para texto livre. Use um modelo aprovado.' : undefined)}
          onClick={() => aoEnviar(item, previa)}
        >
          Enviar
        </Botao>
        {item.tipo === 'pronta' && (
          <Botao icone={item.arquivada ? 'undo-2' : 'archive'} onClick={() => aoArquivar(item)}>
            {item.arquivada ? 'Restaurar' : 'Arquivar'}
          </Botao>
        )}
      </div>
    </li>
  )
}

function FormularioNovaResposta({ aoIncluir, aoCancelar }) {
  const [titulo, setTitulo] = useState('')
  const [categoria, setCategoria] = useState('')
  const [texto, setTexto] = useState('')
  const [erro, setErro] = useState(null)

  function confirmar() {
    if (!titulo.trim()) { setErro('Dê um título.'); return }
    if (!texto.trim()) { setErro('Escreva o texto.'); return }
    aoIncluir({ titulo: titulo.trim(), categoria: categoria.trim() || 'Geral', texto: texto.trim() })
  }

  return (
    <li className={`${css.item} ${css.itemNovo}`}>
      <CampoTexto rotulo="Título da resposta nova" value={titulo} onChange={(e) => setTitulo(e.target.value)} placeholder="Ex.: Aviso de chuva" />
      <p className={css.atalhoPrevia}>Atalho: {gerarAtalho(titulo)}</p>
      <CampoTexto rotulo="Categoria" value={categoria} onChange={(e) => setCategoria(e.target.value)} placeholder="Ex.: Entrega" />
      <CampoArea rotulo="Texto" value={texto} onChange={(e) => setTexto(e.target.value)} />
      <p className={css.variaveis}>
        <span>Variáveis:</span>
        {VARIAVEIS_RESPOSTA.map((v) => <span key={v} className={css.tokenVariavel}>{`{${v}}`}</span>)}
      </p>
      {erro && <p className={css.aviso} role="alert"><Icone nome="alerta" /> {erro}</p>}
      <div className={css.acoesEdicao}>
        <Botao variante="primario" icone="plus" onClick={confirmar}>Cadastrar</Botao>
        <Botao onClick={aoCancelar}>Cancelar</Botao>
      </div>
    </li>
  )
}

// Biblioteca inteira do composer (rodada 7, pedido do dono 24/09/2026):
// resposta pronta e automática do sistema, uma tela só, com busca, envio,
// edição ali mesmo e cadastro sem trocar de tela. Automática edita e liga
// pela MESMA porta de features/automacoes/ModalAutomacoes.jsx
// (editarRegra/alternarRegra): nunca uma cópia do texto.
//
// Auto-suficiente de propósito (só `aoFechar` de fora, mesmo molde de
// ModalAutomacoes): quem abre é App.jsx (Composicao), não Composer.jsx, pois
// feature nenhuma importa outra feature (ferramentas/verificar-camadas.mjs) e
// o Composer só expõe o gancho `aoAbrirBiblioteca`.
export function ModalBiblioteca({ aoFechar, focoInicial = null }) {
  const {
    selecionada: conversa, agora, funcionamento, regras, rascunho,
  } = useAtendimento()
  const {
    respostasProntas, janelas, canais, modelos, cardapio,
  } = useCatalogo()
  const {
    enviar, definirRascunho, editarRegra, alternarRegra, incluirRespostaPronta, editarRespostaPronta,
    alternarArquivamentoRespostaPronta,
  } = useAcoes()
  const [busca, setBusca] = useState('')
  const [mostrarArquivadas, setMostrarArquivadas] = useState(false)
  const [criando, setCriando] = useState(false)

  // Mesma conta de PainelAtendimento.jsx: quem decide se o texto livre sai
  // agora é o canal mais a janela, nunca um estado próprio desta tela.
  const canal = canalDaConversa(canais, conversa)
  const { pode: podeEscrever, ofereceModelo } = permissaoDeEscrita(conversa, agora, canal)
  const novidade = cardapio.find((i) => i.estoque > 0)

  const faixa = faixaDaJanela(janelas, conversa?.pedido?.janela)
  const contextoAutomatico = {
    ...contextoDePrevia(conversa, faixa, agora),
    abre: descreverProximaAbertura(agora, funcionamento),
    // Só a automática "Esteira: pedido saiu para entrega" usa {entregador};
    // sem entregador resolvido ainda, entra um texto genérico na prévia (o
    // envio de verdade do passo, avisoDoPasso, já trava sem nome real).
    entregador: conversa?.pedido?.entregador?.nome ?? 'quem for entregar',
  }

  const itens = useMemo(
    () => listarBiblioteca({ respostasProntas, regras, incluirArquivadas: mostrarArquivadas }),
    [respostasProntas, regras, mostrarArquivadas],
  )
  const filtrados = filtrarBiblioteca(itens, busca)
  const grupos = agruparPorCategoria(filtrados)

  // Item D (banca 10): clicar na etiqueta "automática" de um balão manda
  // aqui o id do item (features/atendimento/Balao.jsx via
  // dominio/respostas.js#itemDaBibliotecaPelaRegra). Rola até ele em vez de
  // filtrar a lista, para ela ainda ver o resto da biblioteca ao redor.
  useEffect(() => {
    if (!focoInicial) return
    document.getElementById(`biblioteca-item-${focoInicial}`)?.scrollIntoView({ block: 'center' })
  }, [focoInicial])

  function enviarItem(item, texto) {
    enviar(conversa.id, texto, item.tipo === 'automatica' ? { automatica: true, regra: item.regraId } : {})
    aoFechar()
  }

  function usarItem(item, texto) {
    definirRascunho(conversa.id, rascunho ? `${rascunho.trimEnd()} ${texto}` : texto)
    aoFechar()
  }

  function enviarModelo(modelo) {
    enviar(
      conversa.id,
      preencherModelo(modelo, [primeiroNome(conversa.nome), novidade?.nome ?? 'a novidade da casa']),
      { modelo: true },
    )
    aoFechar()
  }

  function salvarItem(item, dados) {
    if (item.tipo === 'automatica') editarRegra(item.regraId, { texto: dados.texto })
    else editarRespostaPronta(item.id, { titulo: dados.titulo, categoria: dados.categoria, texto: dados.texto })
  }

  function arquivarItem(item) { alternarArquivamentoRespostaPronta(item.id) }
  function alternarAtivaItem(item) { alternarRegra(item.regraId) }

  function incluirNova(dados) {
    incluirRespostaPronta(dados)
    setCriando(false)
  }

  return (
    <Modal
      titulo="Respostas"
      descricao="Respostas prontas e mensagens automáticas da casa, num lugar só."
      aoFechar={aoFechar}
      largura="640px"
      rodape={<Botao onClick={aoFechar}>Fechar</Botao>}
    >
      <div className={css.barra}>
        <span className={css.campoBusca}>
          <Icone nome="lupa" tamanho={20} />
          <label className="sr" htmlFor="biblioteca-busca">Buscar resposta, atalho ou categoria</label>
          <input
            id="biblioteca-busca"
            type="search"
            className={css.entradaBusca}
            placeholder="Buscar por título, atalho ou texto"
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
          />
        </span>
        <Botao
          variante="texto"
          className={css.toqueAcao}
          aria-pressed={mostrarArquivadas}
          onClick={() => setMostrarArquivadas((v) => !v)}
        >
          {mostrarArquivadas ? 'Ocultar arquivadas' : 'Mostrar arquivadas'}
        </Botao>
        <Botao
          variante="primario"
          icone="plus"
          className={css.toqueAcao}
          aria-expanded={criando}
          onClick={() => setCriando((v) => !v)}
        >
          Nova resposta
        </Botao>
      </div>

      <ul className={css.listaCategorias}>
        {criando && <FormularioNovaResposta aoIncluir={incluirNova} aoCancelar={() => setCriando(false)} />}

        {grupos.map((grupo) => (
          <li key={grupo.categoria} className={css.grupo}>
            <p className={css.tituloGrupo}>{grupo.categoria}</p>
            <ul className={css.itensDoGrupo}>
              {grupo.itens.map((item) => (
                <ItemDaBiblioteca
                  key={item.id}
                  item={item}
                  conversa={conversa}
                  contextoAutomatico={contextoAutomatico}
                  cardapio={cardapio}
                  podeEscrever={podeEscrever}
                  focoInicial={focoInicial}
                  aoEnviar={enviarItem}
                  aoUsar={usarItem}
                  aoSalvar={salvarItem}
                  aoArquivar={arquivarItem}
                  aoAlternarAtiva={alternarAtivaItem}
                />
              ))}
            </ul>
          </li>
        ))}

        {!criando && grupos.length === 0 && (
          <li>
            <Vazio
              titulo={busca ? 'Nada com esse termo' : 'Nenhuma resposta aqui'}
              acao={<Botao variante="primario" icone="plus" onClick={() => setCriando(true)}>Nova resposta</Botao>}
            >
              {busca ? 'Tente outra palavra, ou cadastre esta como resposta nova.' : 'Cadastre a primeira resposta da casa.'}
            </Vazio>
          </li>
        )}
      </ul>

      {/* Modelo aprovado (Meta) é texto fixo, sem edição nem arquivo: existe só
          para furar janela fechada (dominio/janela.js, ofereceModelo). Mesma
          conta de PainelAtendimento.jsx (preencherModelo + novidade), sem
          duplicar em dois lugares o que é lido daqui pra frente. */}
      {ofereceModelo && modelos.length > 0 && (
        <div className={css.blocoModelos}>
          <p className={css.tituloGrupo}>Modelos aprovados</p>
          <ul className={css.itensDoGrupo}>
            {modelos.map((modelo) => (
              <li key={modelo.nome} className={css.item}>
                <div className={css.linhaItem}>
                  <span className={css.identidade}>
                    <strong>{modelo.nome}</strong>
                    <span className={css.atalho}>{modelo.categoria}</span>
                  </span>
                </div>
                <p className={css.previaFechada}>{modelo.texto}</p>
                <div className={css.acoesItem}>
                  <Botao icone="enviar" onClick={() => enviarModelo(modelo)}>Enviar</Botao>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Modal>
  )
}
