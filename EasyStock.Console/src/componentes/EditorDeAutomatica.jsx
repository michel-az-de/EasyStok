import { useId, useState } from 'react'
import { Botao } from './Botao'
import { CampoArea } from './Campo'
import { Pilula } from './Pilula'
import css from './editorDeAutomatica.module.css'

// #3010: onde se edita, cada variável é um selo com o que ela vira; na prévia e na lista o texto sai preenchido.
export function SelosDeVariaveis({ variaveis }) {
  return (
    <ul className={css.variaveis} aria-label="Variáveis que o texto aceita">
      {variaveis.map((v) => (
        <li key={v.chave}><span className={css.selo}>{`{${v.chave}}`}</span> {v.descricao}</li>
      ))}
    </ul>
  )
}

// `rotuloId` liga a bolha ao rótulo "exemplo"; a chave desconhecida ganha o motivo em texto, não só a cor.
export function TextoDaPrevia({ partes, className, rotuloId }) {
  return (
    <span className={className} role={rotuloId ? 'group' : undefined} aria-labelledby={rotuloId}>
      {partes.map((p, i) => (p.tipo === 'erro'
        ? <mark key={i} className={css.previaErro}>{p.texto}<span className={css.oculto}> (variável desconhecida)</span></mark>
        : <span key={i}>{p.texto}</span>))}
    </span>
  )
}

// #2783: o editor da mensagem automática é este, em Respostas e em Automações. A ação do modo API devolve `false`
// quando não gravou (o motivo do servidor vai para a faixa e chega aqui em `avisoApi`); a da demonstração não
// devolve nada. Em erro o rascunho fica e o motivo aparece aqui dentro, não atrás do modal.
// `analisar(texto)` é `analisarAutomatica` (dominio/automacao): variáveis, bloqueio, avisos e prévia do texto digitado.
// `inicial` abre o rascunho com outro texto (a sugestão da regra sem texto).
// `children` (função ou nó) entra ao lado do Salvar; a função recebe a prévia do rascunho (o que "Enviar" manda).
export function EditorDeAutomatica({
  regra, inicial = null, analisar, editavel, avisoApi = null, aoSalvar,
  aoSalvou, aoCancelar = null, autoFocus = false, idBloqueio: idExterno, children,
}) {
  const [rascunho, setRascunho] = useState(inicial ?? regra.texto ?? '')
  // ocioso | salvando | salvo | erro
  const [gravacao, setGravacao] = useState('ocioso')
  const idPrevia = useId()
  const idProprio = useId()
  const idBloqueio = idExterno ?? idProprio
  const alterado = rascunho !== (regra.texto ?? '')
  // #2476 (U8): variável que o EasyStok não preenche sairia escrita para o cliente: não salva nem liga.
  const { variaveis, bloqueio, semValor, avisoEntrada, partes } = analisar(rascunho)
  const previa = partes.map((p) => p.texto).join('')

  const mudar = (novo) => { setGravacao('ocioso'); setRascunho(novo) }
  const desfazer = () => { setRascunho(regra.texto ?? ''); setGravacao('ocioso') }

  const salvar = async () => {
    if (gravacao === 'salvando' || !rascunho.trim() || bloqueio) return
    setGravacao('salvando')
    let ok
    try {
      ok = await aoSalvar(rascunho.trim())
    } catch {
      ok = false
    }
    if (ok === false) { setGravacao('erro'); return }
    setGravacao('salvo')
    aoSalvou?.()
  }

  const salvando = gravacao === 'salvando'
  return (
    <div className={css.editor} data-editor-automatica={regra.id}>
      <CampoArea
        rotulo={`Texto de ${regra.nome}`}
        rotuloOculto={Boolean(aoCancelar)}
        rows={3}
        value={rascunho}
        disabled={salvando}
        readOnly={!editavel}
        onChange={(e) => mudar(e.target.value)}
        autoFocus={autoFocus}
      />
      <SelosDeVariaveis variaveis={variaveis} />
      {bloqueio && <p id={idBloqueio} className={css.alerta} role="alert">{bloqueio}</p>}
      {avisoEntrada && <p className={css.alerta}>{avisoEntrada}</p>}
      {semValor && <p className={css.alerta}>{semValor}</p>}

      {/* Prévia em bolha: é o que o cliente lê, com os valores de exemplo (ou os da conversa, na demonstração). */}
      <p id={idPrevia} className={css.rotuloPrevia}>Como o cliente lê (exemplo)</p>
      <TextoDaPrevia partes={partes} className={css.bolha} rotuloId={idPrevia} />

      {alterado && gravacao !== 'erro' && <p className={css.estado}><Pilula tom="aviso" fina>Alterações não salvas</Pilula></p>}
      {gravacao === 'erro' && (
        <p className={css.erro} role="alert">
          Não foi possível confirmar o salvamento. {avisoApi ?? 'Tente novamente.'} Sua edição foi mantida.
        </p>
      )}
      {gravacao === 'salvo' && !alterado && <output className={css.estado}><Pilula tom="ok" fina>Salvo</Pilula></output>}

      {/* #1966: salvar é da gerência do atendimento; para o Operador o texto fica só para ler. */}
      {editavel && (
        <div className={css.acoes}>
          <Botao variante="primario" icone="check" className={css.toque} disabled={!alterado || !rascunho.trim() || salvando || Boolean(bloqueio)}
            aria-describedby={bloqueio ? idBloqueio : undefined} onClick={salvar}>
            {salvando ? 'Salvando…' : 'Salvar'}
          </Botao>
          {aoCancelar
            ? <Botao variante="texto" className={css.toque} disabled={salvando} onClick={aoCancelar}>Cancelar</Botao>
            : <Botao className={css.toque} disabled={!alterado || salvando} onClick={desfazer}>Desfazer</Botao>}
          {typeof children === 'function' ? children({ previa }) : children}
        </div>
      )}
    </div>
  )
}
