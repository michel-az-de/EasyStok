import { useEffect, useId, useState } from 'react'
import { Botao } from './Botao'
import { CampoArea, CampoTexto } from './Campo'
import { SelosDeVariaveis } from './EditorDeAutomatica'
import { algumPassaDoLimite } from './limiteDoCampo'
import css from './formResposta.module.css'

// Formulário de resposta pronta, um só para a Gestão (Respostas) e para o painel do chat (#3208): título, atalho,
// texto e categoria, com a mesma validação e os mesmos limites do servidor (RespostaPronta.*TamanhoMaximo).
// `regras` vem de dominio/respostas (REGRAS_DO_FORM): componente não importa dominio, o dado chega por parâmetro.
// `aoSalvar(dados)` pode devolver promessa; `erro` é o motivo que o servidor recusou, mostrado aqui com role="alert".
// Limites: o campo não corta, avisa (`limite`) e trava o salvar. #2792: Atalho e Texto também avisam.
const TITULO_MAXIMO = 120
const TEXTO_MAXIMO = 4096

// #2792: o motivo do Salvar pede só o que falta.
function faltando(titulo, texto) {
  const semTitulo = !titulo.trim()
  const semTexto = !texto.trim()
  if (semTitulo && semTexto) return 'Preencha o título e o texto.'
  return semTitulo ? 'Preencha o título.' : semTexto ? 'Preencha o texto.' : undefined
}

export function FormResposta({ inicial, aoSalvar, aoCancelar, regras, categorias = [], erro = null, className = '', aoMudarAlterado, pedidoDescarte = 0 }) {
  const { atalhoMaximo: ATALHO_MAXIMO, categoriaMaxima: CATEGORIA_MAXIMA, gerarAtalho, normalizarCategoria, variaveis } = regras
  const [titulo, setTitulo] = useState(inicial?.titulo ?? '')
  const [atalho, setAtalho] = useState(inicial?.atalho ?? '')
  const [texto, setTexto] = useState(inicial?.texto ?? '')
  const [categoria, setCategoria] = useState(inicial?.categoria ?? '')
  const [salvando, setSalvando] = useState(false)
  const [descartando, setDescartando] = useState(false)
  const idSugestoes = useId()
  // Esc ou clique fora no painel (`pedidoDescarte` sobe) fazem a mesma pergunta do Cancelar, sem apagar nada.
  const [pedidoVisto, setPedidoVisto] = useState(pedidoDescarte)
  // #2792: sem atalho digitado, o servidor recebe o gerado do título, que já nasce dentro de 40 (gerarAtalho) e
  // aparece no placeholder: a pessoa vê o atalho exato antes de salvar.
  const atalhoGerado = gerarAtalho(titulo)
  const passaDoLimite = algumPassaDoLimite([titulo, TITULO_MAXIMO], [atalho, ATALHO_MAXIMO], [texto, TEXTO_MAXIMO], [categoria, CATEGORIA_MAXIMA])
  const pronto = titulo.trim() && texto.trim() && !passaDoLimite
  // Mesma pergunta do Modal (`confirmarDescarte`): com algo digitado, Cancelar não descarta calado.
  const alterado = titulo !== (inicial?.titulo ?? '') || atalho !== (inicial?.atalho ?? '')
    || texto !== (inicial?.texto ?? '') || categoria !== (inicial?.categoria ?? '')

  if (pedidoDescarte !== pedidoVisto) { setPedidoVisto(pedidoDescarte); if (alterado) setDescartando(true) }

  // O painel do chat precisa saber se há algo digitado para não fechar por Esc ou clique fora (#3208).
  useEffect(() => {
    aoMudarAlterado?.(alterado)
    return () => aoMudarAlterado?.(false)
  }, [alterado, aoMudarAlterado])

  function salvar(evento) {
    evento.preventDefault()
    if (salvando || !pronto) return
    setSalvando(true)
    const dados = { titulo: titulo.trim(), atalho: atalho.trim() || atalhoGerado, texto: texto.trim(), categoria: normalizarCategoria(categoria) }
    Promise.resolve(aoSalvar(dados)).finally(() => setSalvando(false))
  }

  return (
    <form className={`${css.form} ${className}`} onSubmit={salvar}>
      <div className={css.campos}>
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
      <SelosDeVariaveis variaveis={variaveis} />
      <CampoTexto
        rotulo="Categoria"
        value={categoria}
        disabled={salvando}
        limite={CATEGORIA_MAXIMA}
        list={categorias.length ? idSugestoes : undefined}
        dica="Opcional. Agrupa a resposta na lista do chat."
        onChange={(e) => setCategoria(e.target.value)}
      />
      {categorias.length > 0 && (
        <datalist id={idSugestoes}>
          {categorias.map((c) => <option key={c} value={c} />)}
        </datalist>
      )}
      </div>
      {erro && <p className={css.erro} role="alert">{erro}</p>}
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
      <div className={`${css.acoes} ${css.barra}`}>
        <Botao variante="primario" tipo="submit" icone="check" disabled={!pronto || salvando}
          motivo={passaDoLimite ? 'Algum campo passa do limite de caracteres.' : faltando(titulo, texto)}>{salvando ? 'Salvando…' : 'Salvar'}</Botao>
        <Botao variante="texto" disabled={salvando || descartando} onClick={() => (alterado ? setDescartando(true) : aoCancelar())}>Cancelar</Botao>
      </div>
    </form>
  )
}
