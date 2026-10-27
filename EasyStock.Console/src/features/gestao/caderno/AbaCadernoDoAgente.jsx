// Caderno do agente (#2070, S54, só no modo API): o que a loja sabe e o agente do atendimento usa ao
// responder. Lista, cria, edita e remove (arquiva) trechos.
import { useEffect, useId, useRef, useState } from 'react'
import { carregarCaderno, removerTrecho, salvarTrecho } from '../../../aplicacao/cadernoDoAgente'
import { useEnvio, useLeitura, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import { Botao } from '../../../componentes/Botao'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import {
  LIMITES_DO_CADERNO, errosDoTrecho, formularioDoTrecho, tamanhoDoNucleo, trechoMudou,
} from '../../../dominio/cadernoDoAgente'
import { Erros, FalhaDeLeitura, Pendencia, Retorno } from '../PartesDoAjuste'
import css from '../formularioDeAjuste.module.css'

const ROTULOS = { titulo: 'Título', texto: 'Texto', palavrasChave: 'Palavras-chave', nucleo: 'Núcleo' }
const numero = (n) => n.toLocaleString('pt-BR')

export function AbaCadernoDoAgente({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const id = useId()
  const [carga, recarregar] = useLeitura(carregarCaderno)
  const [edicao, setEdicao] = useState(null)
  const [edicaoPendente, setEdicaoPendente] = useState(false)
  const [removendo, setRemovendo] = useState(null)
  const [remocao, enviarRemocao, limparRemocao] = useEnvio()
  usePendenciaAvisada(edicaoPendente, aoMudarPendencia)
  const trechos = carga.dados ?? []
  const ocupado = edicao !== null || remocao.estado === 'salvando'

  // Teclado e leitor de tela: abrir leva o foco ao formulário ou à confirmação; fechar devolve ao
  // botão de origem. O alvo espera a renderização em que ele já existe.
  const focoPendente = useRef(null)
  useEffect(() => {
    const alvo = focoPendente.current
    if (!alvo) return
    focoPendente.current = null
    document.getElementById(alvo)?.focus()
  })
  const ids = {
    caderno: `${id}-titulo`, novo: `${id}-novo`, confirmar: `${id}-confirmar`,
    editar: (trecho) => `${id}-editar-${trecho.id}`, remover: (trecho) => `${id}-remover-${trecho.id}`,
  }

  function abrirEdicao(trecho) {
    limparRemocao()
    setRemovendo(null)
    setEdicao({ trecho, origem: trecho ? ids.editar(trecho) : ids.novo })
  }
  function fecharEdicao() {
    focoPendente.current = edicao?.origem ?? null
    setEdicao(null)
  }
  function pedirRemocao(trecho) {
    limparRemocao()
    focoPendente.current = ids.confirmar
    setRemovendo(trecho.id)
  }
  function manter(trecho) {
    focoPendente.current = ids.remover(trecho)
    setRemovendo(null)
  }

  async function aoSalvarTrecho() {
    fecharEdicao()
    aoSalvo?.()
    await recarregar()
  }

  async function remover(trecho) {
    const { ok } = await enviarRemocao(() => removerTrecho(trecho.id))
    if (!ok) return
    focoPendente.current = ids.caderno
    setRemovendo(null)
    aoSalvo?.()
    await recarregar()
  }

  return (
    <div className={css.aba}>
      <section className={css.secao} aria-labelledby={ids.caderno}>
        <h3 id={ids.caderno} tabIndex={-1}>Caderno do agente</h3>
        <p className={css.descricao}>
          O agente do atendimento consulta este caderno para responder os clientes.
          {!compacto && ' Trecho do núcleo vai em toda resposta. Os outros ficam num índice pelo título e pelas palavras-chave, e o agente lê o texto quando o assunto aparece.'}
        </p>
        {carga.estado === 'ok' && (
          <p className={css.descricao}>Núcleo: {numero(tamanhoDoNucleo(trechos, null, null))} de {numero(LIMITES_DO_CADERNO.nucleo)} caracteres.</p>
        )}
        {edicao === null && carga.estado === 'ok' && (
          <div className={css.acoes}>
            <Botao id={ids.novo} variante="primario" icone="plus" className={css.toque} disabled={ocupado} onClick={() => abrirEdicao(null)}>Novo trecho</Botao>
          </div>
        )}
      </section>
      {carga.estado === 'carregando' && <output className={css.descricao}>Carregando o caderno…</output>}
      {carga.estado === 'erro' && <FalhaDeLeitura mensagem={`O caderno não carregou: ${carga.mensagem}`} aoTentarDeNovo={recarregar} />}
      {edicao !== null && (
        <EdicaoDoTrecho key={edicao.trecho?.id ?? 'novo'} trecho={edicao.trecho} trechos={trechos}
          aoMudarPendencia={setEdicaoPendente} aoCancelar={fecharEdicao} aoSalvo={aoSalvarTrecho} />
      )}
      {carga.estado === 'ok' && trechos.length === 0 && edicao === null && (
        <Vazio titulo="O caderno está vazio">Comece pelo que os clientes mais perguntam: entrega, pagamento, alergias.</Vazio>
      )}
      <Retorno situacao={remocao} salvo="Trecho removido do caderno." falhou="Não foi possível remover o trecho." />
      {carga.estado === 'ok' && trechos.length > 0 && (
        <ul className={css.lista} aria-label="Trechos do caderno">
          {trechos.map((trecho) => (
            <li key={trecho.id} className={css.cartao}>
              <div className={css.acoes}>
                <h4>{trecho.titulo}</h4>
                {trecho.nucleo && <Pilula tom="info" fina>Núcleo</Pilula>}
              </div>
              {trecho.palavrasChave && <p className={css.descricao}>Palavras-chave: {trecho.palavrasChave}</p>}
              <p className={css.texto}>{trecho.texto}</p>
              {removendo === trecho.id ? (
                <fieldset className={css.confirmacao}>
                  <legend className="sr">Remover {trecho.titulo}</legend>
                  <p className={css.descricao}>Remover “{trecho.titulo}” do caderno? O agente deixa de usar este trecho.</p>
                  <Botao id={ids.confirmar} variante="perigo" className={css.toque} disabled={remocao.estado === 'salvando'} onClick={() => remover(trecho)}>Remover do caderno</Botao>
                  <Botao variante="texto" className={css.toque} disabled={remocao.estado === 'salvando'} onClick={() => manter(trecho)}>Manter</Botao>
                </fieldset>
              ) : (
                <div className={css.acoes}>
                  <Botao id={ids.editar(trecho)} className={css.toque} disabled={ocupado} aria-label={`Editar: ${trecho.titulo}`}
                    onClick={() => abrirEdicao(trecho)}>Editar</Botao>
                  <Botao id={ids.remover(trecho)} variante="texto" className={css.toque} disabled={ocupado} aria-label={`Remover: ${trecho.titulo}`}
                    onClick={() => pedirRemocao(trecho)}>Remover</Botao>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function EdicaoDoTrecho({ trecho, trechos, aoMudarPendencia, aoCancelar, aoSalvo }) {
  const id = useId()
  const [form, setForm] = useState(() => formularioDoTrecho(trecho))
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = trechoMudou(form, trecho)
  usePendenciaAvisada(pendente, aoMudarPendencia)
  const nucleo = tamanhoDoNucleo(trechos, { ...form, nucleo: true }, trecho?.id ?? null)
  // Aberto pelo botão "Novo trecho" ou "Editar", que some ou fica longe: o foco vem para o título.
  useEffect(() => { document.getElementById(`${id}-titulo`)?.focus() }, [id])

  const mudar = (campo, lerValor = (evento) => evento.target.value) => (evento) => {
    const valor = lerValor(evento)
    limpar(); setErros({})
    setForm((atual) => ({ ...atual, [campo]: valor }))
  }

  async function salvar(evento) {
    evento.preventDefault()
    if (!pendente || salvando) return
    const encontrados = errosDoTrecho(form, trechos, trecho?.id ?? null)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok } = await enviar(() => salvarTrecho(form, trecho))
    if (ok) await aoSalvo()
  }

  return (
    <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={salvar}>
      <h3 id={`${id}-titulo`} tabIndex={-1}>{trecho ? `Editar “${trecho.titulo}”` : 'Novo trecho'}</h3>
      <Pendencia visivel={pendente && !salvando} />
      <fieldset className={css.campos} disabled={salvando}>
        <legend className="sr">Trecho do caderno</legend>
        <div className={css.inteiro}>
          <CampoTexto rotulo={ROTULOS.titulo} required maxLength={LIMITES_DO_CADERNO.titulo} value={form.titulo} onChange={mudar('titulo')} />
        </div>
        <div className={css.inteiro}>
          <CampoArea rotulo={ROTULOS.texto} required maxLength={LIMITES_DO_CADERNO.texto} rows={6}
            dica={`${numero(form.texto.length)} de ${numero(LIMITES_DO_CADERNO.texto)} caracteres.`} value={form.texto} onChange={mudar('texto')} />
        </div>
        <div className={css.inteiro}>
          <CampoTexto rotulo={ROTULOS.palavrasChave} maxLength={LIMITES_DO_CADERNO.palavrasChave}
            dica="Separe por vírgula. O agente acha o trecho por elas." value={form.palavrasChave} onChange={mudar('palavrasChave')} />
        </div>
        <label className={`${css.chave} ${css.inteiro}`}>
          <input type="checkbox" checked={form.nucleo} aria-describedby={`${id}-nucleo`} onChange={mudar('nucleo', (evento) => evento.target.checked)} />
          Vai em toda resposta (núcleo)
        </label>
        <p id={`${id}-nucleo`} className={`${css.descricao} ${css.inteiro}`}>
          Com este trecho no núcleo, ele fica com {numero(nucleo)} de {numero(LIMITES_DO_CADERNO.nucleo)} caracteres.
        </p>
      </fieldset>
      <Erros erros={erros} rotulos={ROTULOS} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>Salvar trecho</Botao>
        <Botao variante="texto" className={css.toque} disabled={salvando} onClick={aoCancelar}>Cancelar</Botao>
        <Retorno situacao={situacao} />
      </div>
    </form>
  )
}
