import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { automacoesEditaveis, respostasEditaveis } from '../../../dominio/acessoModulos'
import {
  VARIAVEIS, VARIAVEIS_DA_API, analisarAutomatica, avisoDaAutomaticaDeEntrada, bloqueioDaAutomatica, exemploDaPrevia, previaDoServidor,
} from '../../../dominio/automacao'
import { EditorDeAutomatica, SelosDeVariaveis, TextoDaPrevia } from '../../../componentes/EditorDeAutomatica'
import { plural } from '../../../dominio/formato'
import { algumPassaDoLimite } from '../../../componentes/limiteDoCampo'
import { ATALHO_MAXIMO, gerarAtalho, normalizarBusca } from '../../../dominio/respostas'
import { hashDoModulo, NOME_RESPOSTAS_AUTOMATICAS } from '../../../dominio/modulos'
import { SecaoMensagensPadrao } from './SecaoMensagensPadrao'
import css from './abaRespostas.module.css'

// Mesmo editor na janela contextual do Balcão e na rota direta de Atendimento.

// A ação do modo API devolve `false` quando não gravou (o aviso já foi para a faixa);
// a da demonstração não devolve nada. Nos dois casos sem `false`, o formulário fecha.
const depois = (resultado, fechar) => Promise.resolve(resultado).then((ok) => { if (ok !== false) fechar() })

// Limites do servidor (RespostaPronta.*TamanhoMaximo): o campo não corta, avisa (`limite`) e trava o salvar.
// #2792: Atalho e Texto também avisam; antes só o Título, e os outros dois só falhavam no servidor.
const TITULO_MAXIMO = 120
const TEXTO_MAXIMO = 4096

// #2783: o editor da automática e as peças da prévia vivem em componentes/EditorDeAutomatica; daqui saem como sempre.
export { SelosDeVariaveis }
export function TextoPreenchido({ texto, exemplo, fonteApi, ...resto }) {
  return <TextoDaPrevia partes={previaDoServidor(texto, exemplo, fonteApi ? VARIAVEIS_DA_API : VARIAVEIS)} {...resto} />
}

// #2792: o motivo do Salvar pede só o que falta.
function faltando(titulo, texto) {
  const semTitulo = !titulo.trim()
  const semTexto = !texto.trim()
  if (semTitulo && semTexto) return 'Preencha o título e o texto.'
  return semTitulo ? 'Preencha o título.' : semTexto ? 'Preencha o texto.' : undefined
}

function FormResposta({ inicial, aoSalvar, aoCancelar }) {
  const [titulo, setTitulo] = useState(inicial?.titulo ?? '')
  const [atalho, setAtalho] = useState(inicial?.atalho ?? '')
  const [texto, setTexto] = useState(inicial?.texto ?? '')
  const [salvando, setSalvando] = useState(false)
  const [descartando, setDescartando] = useState(false)
  // #2792: sem atalho digitado, o servidor recebe o gerado do título, que já nasce dentro de 40 (gerarAtalho) e
  // aparece no placeholder: a pessoa vê o atalho exato antes de salvar.
  const atalhoGerado = gerarAtalho(titulo)
  const passaDoLimite = algumPassaDoLimite([titulo, TITULO_MAXIMO], [atalho, ATALHO_MAXIMO], [texto, TEXTO_MAXIMO])
  const pronto = titulo.trim() && texto.trim() && !passaDoLimite
  // Mesma pergunta do Modal (`confirmarDescarte`): com algo digitado, Cancelar não descarta calado.
  const alterado = titulo !== (inicial?.titulo ?? '') || atalho !== (inicial?.atalho ?? '') || texto !== (inicial?.texto ?? '')

  function salvar(evento) {
    evento.preventDefault()
    if (salvando || !pronto) return
    setSalvando(true)
    const dados = { titulo: titulo.trim(), atalho: atalho.trim() || atalhoGerado, texto: texto.trim() }
    Promise.resolve(aoSalvar(dados)).finally(() => setSalvando(false))
  }

  return (
    <form className={css.form} onSubmit={salvar}>
      <div className={css.duas}>
        <CampoTexto
          disabled={salvando} rotulo="Título" value={titulo} limite={TITULO_MAXIMO}
          onChange={(e) => setTitulo(e.target.value)} autoFocus
        />
        <CampoTexto
          rotulo="Atalho"
          value={atalho}
          disabled={salvando}
          limite={ATALHO_MAXIMO}
          dica="Sem preencher, o atalho é criado a partir do título."
          placeholder={titulo ? atalhoGerado : '/pix'}
          onChange={(e) => setAtalho(e.target.value.replace(/\s/g, '-'))}
        />
      </div>
      <CampoArea disabled={salvando} rotulo="Texto" rows={3} value={texto} limite={TEXTO_MAXIMO} onChange={(e) => setTexto(e.target.value)} />
      <SelosDeVariaveis variaveis={VARIAVEIS_DA_API} />
      {descartando && (
        <div className={css.descarte} role="alert">
          <p>Descartar o que foi digitado?</p>
          <div className={css.acoes}>
            {/* eslint-disable-next-line jsx-a11y/no-autofocus -- o foco vai para a opção que não perde nada */}
            <Botao autoFocus onClick={() => setDescartando(false)}>Continuar editando</Botao>
            <Botao variante="perigo" onClick={aoCancelar}>Descartar</Botao>
          </div>
        </div>
      )}
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" icone="check" disabled={!pronto || salvando}
          motivo={passaDoLimite ? 'Algum campo passa do limite de caracteres.' : faltando(titulo, texto)}>{salvando ? 'Salvando…' : 'Salvar'}</Botao>
        <Botao variante="texto" disabled={salvando || descartando} onClick={() => (alterado ? setDescartando(true) : aoCancelar())}>Cancelar</Botao>
      </div>
    </form>
  )
}

function SecaoRespostas() {
  const { respostasProntas } = useCatalogo()
  const { incluirRespostaPronta, editarRespostaPronta, alternarArquivamentoRespostaPronta } = useAcoes()
  const { fonteApi } = useAtendimento()
  const { acoes } = useAcessoModulos()
  // #1992: criar, editar e arquivar é da gerência (a API recusa o Operador); a atendente usa pelo / no campo.
  const editavel = respostasEditaveis({ fonteApi, acoes })
  const [busca, setBusca] = useState('')
  const buscaRef = useRef(null)
  const [verArquivadas, setVerArquivadas] = useState(false)
  // 'nova', o id da resposta em edição, ou null.
  const [editando, setEditando] = useState(null)
  const listaRef = useRef(null)
  const novaRef = useRef(null)
  // Quem fechou o formulário ('nova' ou o id): ao fechar, o foco volta ao botão dele, não ao início da página.
  const voltarFoco = useRef(null)
  const fechar = (id) => { voltarFoco.current = id; setEditando(null) }
  // Arquivar e restaurar gravam pela API: um por vez, com retorno para quem lê só a tela.
  const [gravando, setGravando] = useState(null)
  const [aviso, setAviso] = useState('')

  useEffect(() => {
    if (editando !== null || voltarFoco.current === null) return
    const id = voltarFoco.current
    voltarFoco.current = null
    const alvo = id === 'nova' ? novaRef.current : listaRef.current?.querySelector(`[data-editar="${id}"]`)
    alvo?.focus()
  }, [editando])

  // #2734: enquanto grava, o botão fica desabilitado e perde o foco; arquivada, a linha ainda sai da lista. Ao fim,
  // o foco volta ao botão da linha, ou a "Nova resposta" quando a linha saiu. Quem já foi a outro controle fica lá.
  const focoAlternado = useRef(null)
  useEffect(() => {
    const id = focoAlternado.current
    if (id === null || gravando !== null) return
    focoAlternado.current = null
    const ativo = document.activeElement
    if (ativo && ativo !== document.body && !ativo.disabled) return
    const alvo = listaRef.current?.querySelector(`[data-alternar="${id}"]`) ?? novaRef.current
    alvo?.focus()
  })

  function alternarArquivo(r) {
    if (gravando) return
    setGravando(r.id); setAviso(''); focoAlternado.current = r.id
    Promise.resolve(alternarArquivamentoRespostaPronta(r.id))
      .then((ok) => { if (ok !== false) setAviso(`Resposta ${r.titulo} ${r.arquivada ? 'restaurada' : 'arquivada'}.`) })
      .finally(() => setGravando(null))
  }

  const lista = useMemo(() => {
    const alvo = normalizarBusca(busca)
    return (respostasProntas ?? [])
      .filter((r) => verArquivadas || !r.arquivada)
      .filter((r) => !alvo || [r.titulo, r.atalho, r.texto].some((c) => normalizarBusca(c).includes(alvo)))
      .sort((a, b) => Number(a.arquivada) - Number(b.arquivada) || a.titulo.localeCompare(b.titulo, 'pt-BR'))
  }, [respostasProntas, busca, verArquivadas])
  const ativas = (respostasProntas ?? []).filter((r) => !r.arquivada).length

  return (
    <section className={css.secao} aria-labelledby="gestao-respostas">
      <header className={css.topo}>
        <div>
          <h3 id="gestao-respostas">Respostas prontas</h3>
          <p className={css.descricao}>{plural(ativas, 'resposta ativa', 'respostas ativas')}. Em Conversas, digite / no campo da mensagem ou toque em Respostas.</p>
          {!editavel && <p className={css.descricao}>Somente a gerência pode criar, editar e arquivar respostas prontas.</p>}
        </div>
        {editavel && <Botao ref={novaRef} variante="primario" icone="plus" onClick={() => setEditando('nova')} disabled={editando !== null}>Nova resposta</Botao>}
      </header>

      <div className={css.filtros}>
        <div className={css.filtroBusca}>
          <CampoTexto ref={buscaRef} disabled={editando !== null} tipo="search" rotulo="Buscar resposta pronta"
            placeholder="Título, atalho ou trecho da resposta" value={busca} onChange={(e) => setBusca(e.target.value)} />
          {busca && <div className={css.retornoBusca}>
            <output className={css.descricao}>{plural(lista.length, 'resposta encontrada', 'respostas encontradas')}</output>
            <Botao variante="texto" icone="x" disabled={editando !== null} onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Limpar busca</Botao>
          </div>}
        </div>
        <label className={css.chave}>
          <input type="checkbox" disabled={editando !== null} checked={verArquivadas} onChange={(e) => setVerArquivadas(e.target.checked)} />
          Mostrar arquivadas
        </label>
      </div>
      <p className={css.descricao} role="status">{aviso}</p>

      {editando === 'nova' && (
        <FormResposta
          aoSalvar={(dados) => depois(incluirRespostaPronta(dados), () => fechar('nova'))}
          aoCancelar={() => fechar('nova')}
        />
      )}

      {lista.length === 0 ? (
        <Vazio icone={busca ? 'search' : 'respostas'} titulo={busca ? 'Nada com essa busca' : 'Nenhuma resposta pronta ainda'}>
          {busca ? 'Busque outro título, atalho ou trecho.' : editavel ? 'Use Nova resposta para salvar mensagens frequentes.' : 'Peça à gerência para cadastrar respostas prontas.'}
        </Vazio>
      ) : (
        <ul className={css.lista} ref={listaRef}>
          {lista.map((r) => (
            <li key={r.id} className={`${css.linha} ${r.arquivada ? css.arquivada : ''}`}>
              {editando === r.id ? (
                <FormResposta
                  inicial={r}
                  aoSalvar={(dados) => depois(editarRespostaPronta(r.id, dados), () => fechar(r.id))}
                  aoCancelar={() => fechar(r.id)}
                />
              ) : (
                <>
                  <div className={css.resumo}>
                    <span className={css.titulo}>{r.titulo}</span>
                    <span className={css.atalho}>{r.atalho}</span>
                    {r.arquivada && <span className={css.marca}>arquivada</span>}
                    <span className={css.texto}>{r.texto}</span>
                  </div>
                  {editavel && <div className={css.acoesLinha}>
                    {!r.arquivada && <Botao variante="texto" disabled={editando !== null || gravando !== null} icone="lapis" data-editar={r.id} aria-label={`Editar resposta ${r.titulo}`} onClick={() => setEditando(r.id)}>Editar</Botao>}
                    <Botao variante="texto" disabled={editando !== null || gravando !== null} data-alternar={r.id} aria-label={`${r.arquivada ? 'Restaurar' : 'Arquivar'} resposta ${r.titulo}`} icone={r.arquivada ? 'undo-2' : 'archive'} onClick={() => alternarArquivo(r)}>
                      {r.arquivada ? 'Restaurar' : 'Arquivar'}
                    </Botao>
                  </div>}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function LinhaAutomatica({ regra, fonteApi, emFoco, editavel, exemplo }) {
  const { alternarRegra, editarRegra } = useAcoes()
  const { sincronizacao } = useAtendimento()
  const linhaRef = useRef(null)
  const [rascunho, setRascunho] = useState(null)
  const [salvando, setSalvando] = useState(false)
  const editando = rascunho !== null
  // #2476 (U8): variável que o EasyStok não preenche sairia escrita para o cliente: não salva nem liga.
  const bloqueio = fonteApi && !editando ? bloqueioDaAutomatica(regra.texto, editavel) : null
  const idBloqueio = useId()
  const idExemplo = useId()
  const naoLiga = !regra.ativa && fonteApi && Boolean(bloqueioDaAutomatica(regra.texto))
  // #1474: no modo API a saudação de Horários e mensagens sai sempre na primeira mensagem.
  const avisoEntrada = fonteApi ? avisoDaAutomaticaDeEntrada(regra) : null

  useEffect(() => {
    if (!emFoco) return
    const quadro = requestAnimationFrame(() => {
      linhaRef.current?.focus({ preventScroll: true })
      linhaRef.current?.scrollIntoView({ block: 'center' })
    })
    return () => cancelAnimationFrame(quadro)
  }, [emFoco])

  // A chave da linha fica parada enquanto o editor grava (o motivo de uma recusa aparece dentro dele).
  const gravar = (texto) => {
    setSalvando(true)
    return Promise.resolve(editarRegra(regra.id, { texto })).finally(() => setSalvando(false))
  }

  return (
    <li ref={linhaRef} tabIndex={emFoco ? -1 : undefined}
      className={`${css.linha} ${regra.ativa ? '' : css.desligada} ${emFoco ? css.emFoco : ''}`}>
      <div className={css.resumo}>
        <span className={css.titulo}>{regra.nome}</span>
        <label className={`${css.chave} ${css.chaveLinha}`}>
          <input
            type="checkbox"
            aria-label={`${regra.nome}, ${regra.ativa ? 'ligada' : 'desligada'}`}
            checked={regra.ativa}
            disabled={salvando || !editavel || naoLiga}
            aria-describedby={naoLiga ? idBloqueio : undefined}
            onChange={() => alternarRegra(regra.id)}
          />
          {regra.ativa ? 'Ligada' : 'Desligada'}
        </label>
        <span className={css.quando}>{regra.descricao}</span>
        {!editando && (
          regra.texto
            ? (
              <span className={css.textoDaLinha}>
                <span id={idExemplo} className={css.rotuloPrevia}>Exemplo de como o cliente lê</span>
                <TextoPreenchido texto={regra.texto} exemplo={exemplo} fonteApi={fonteApi} className={css.textoInteiro} rotuloId={idExemplo} />
              </span>
            )
            : !avisoEntrada && <span className={css.semTexto}>Sem texto: não sai nada para o cliente.</span>
        )}
        {!editando && avisoEntrada && <span className={css.semTexto}>{avisoEntrada}</span>}
        {bloqueio && <span id={idBloqueio} className={css.alerta}>{bloqueio}</span>}
      </div>

      {editando ? (
        <EditorDeAutomatica
          regra={regra} inicial={rascunho} editavel={editavel}
          analisar={(texto) => analisarAutomatica({ regra, texto, fonteApi, editavel, valores: exemplo })}
          avisoApi={sincronizacao?.aviso ?? null} idBloqueio={idBloqueio} aoSalvar={gravar}
          aoSalvou={() => setRascunho(null)} aoCancelar={() => setRascunho(null)} autoFocus
        />
      ) : editavel && (
        <div className={css.acoesLinha}>
          <Botao variante="texto" icone="lapis" onClick={() => setRascunho(regra.texto || regra.sugestao || '')}>
            {regra.texto ? 'Editar texto' : 'Escrever texto'}
          </Botao>
        </div>
      )}
    </li>
  )
}

function SecaoAutomaticas({ focoInicial }) {
  const { regras, fonteApi, cargaDasRegras, agora, funcionamento } = useAtendimento()
  const exemplo = useMemo(() => exemploDaPrevia({ agora, funcionamento, origem: window.location.origin }), [agora, funcionamento])
  const { acoes } = useAcessoModulos()
  // #1966: ligar, desligar e editar é da gerência do atendimento (a API recusa o Operador).
  const editavel = automacoesEditaveis({ fonteApi, acoes })
  // #1474: lista vazia não é "carregando": separa a espera, o erro e o vazio de verdade.
  const vazio = cargaDasRegras?.estado === 'carregando'
    ? 'Carregando as respostas automáticas…'
    : cargaDasRegras?.estado === 'erro'
      ? `Não foi possível carregar as respostas automáticas. ${cargaDasRegras.mensagem ?? ''} Recarregue a página para tentar de novo.`.replace('  ', ' ')
      : 'Nenhuma resposta automática configurada para esta loja.'
  return (
    <section className={css.secao} aria-labelledby="gestao-automaticas">
      <header className={css.topo}>
        <div>
          <h3 id="gestao-automaticas">Mensagens que saem sozinhas</h3>
          <p className={css.descricao}>
            Saem sozinhas, só até 24 h depois da última mensagem do cliente e nunca para cliente bloqueado.
            Na conversa aparecem com a etiqueta automática.
          </p>
          {!editavel && (
            <p className={css.descricao}>Somente a gerência pode ativar, desativar e editar mensagens automáticas.</p>
          )}
        </div>
      </header>
      {regras.length === 0 ? (
        <p className={css.vazio} role={cargaDasRegras?.estado === 'erro' ? 'alert' : undefined}>{vazio}</p>
      ) : (
        <ul className={css.lista}>
          {regras.map((r) => <LinhaAutomatica key={r.id} regra={r} fonteApi={fonteApi} editavel={editavel} exemplo={exemplo} emFoco={focoInicial === `regra-${r.id}`} />)}
        </ul>
      )}
    </section>
  )
}

// #2476 (U8): em Cadastros › Modelos só as respostas prontas; as automáticas têm lugar próprio e não se repetem.
export function AbaRespostas({ focoInicial = null, comAutomaticas = true }) {
  return (
    <div className={css.aba}>
      {comAutomaticas && <SecaoAutomaticas focoInicial={focoInicial} />}
      <SecaoRespostas />
      <SecaoMensagensPadrao />
      {!comAutomaticas && (
        <p className={css.descricao}>
          As mensagens que saem sozinhas ficam em <a href={hashDoModulo('atendimento', 'respostas')}>{NOME_RESPOSTAS_AUTOMATICAS}</a>.
        </p>
      )}
    </div>
  )
}
