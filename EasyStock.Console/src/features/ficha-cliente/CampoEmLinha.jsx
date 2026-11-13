import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { Icone } from '../../componentes/Icone'
import { mascaraTelefone, telefoneDoE164 } from '../../dominio/formato'
import { useGravacaoDoAjuste, useRascunhoDoAjuste } from '../../aplicacao/rascunhosDosAjustes'
import { ERRO_IDENTIDADE_DA_EDICAO, identidadeDaEdicao, mesmaIdentidadeDaEdicao } from '../../dominio/identidadeDaEdicao'
import { ConferirDestinoDaEdicao } from './ConferirDestinoDaEdicao'
import css from './cliente.module.css'

const SEGUNDOS_SALVOU = 2
const FALHA_AO_SALVAR = 'Não foi possível salvar. Tente de novo.'

// Edição em linha de nome, telefone e endereço (seção 2 da direção visual).
// Em repouso o valor é um botão com cara de texto; o toque vira campo com
// dois botões de ícone. `tipo="telefone"` aplica a máscara enquanto digita
// (mascaraTelefone é puro, dominio/formato.js: feature pode importar dominio
// direto, diferente de componentes/CampoMascarado.jsx). `validar` decide se
// salva; devolve a mensagem de erro ou `null`.
//
// #1586: `aoSalvar` pode devolver a promessa da ida ao EasyStok. O campo espera
// ("Salvando…") e só então mostra "Salvo"; recusa (`false` ou `{ erro }`) fica no
// próprio campo, com o texto digitado. Sem promessa (demonstração), vale como salvo.
//
// #2258 (auditoria T12, T13): com erro o campo continua visível (contorno vermelho),
// recebe o foco de volta e a mensagem fica ligada a ele por aria-describedby.
// `dica` vira o placeholder e `limite` o maxLength (nome: "Nome e sobrenome", 80).
export function CampoEmLinha({
  rotulo, valor, vazio = 'Sem valor', tipo = 'texto', validar, aoSalvar, dica, limite, aoMudarSujo, chaveRascunho, conversa,
}) {
  const idCampo = useId()
  const idErro = `${idCampo}-erro`
  const chave = chaveRascunho ?? idCampo
  const [edicao, setEdicao] = useRascunhoDoAjuste(chave)
  const editando = edicao !== null
  const rascunho = edicao?.valor ?? ''
  const erro = edicao?.erro ?? null
  const erroDoVinculo = erro === ERRO_IDENTIDADE_DA_EDICAO && conversa && !mesmaIdentidadeDaEdicao(edicao?.identidade, conversa)
  const setRascunho = (valor) => setEdicao(valor === null ? null : { ...edicao, valor, erro: null })
  const setErro = (mensagem) => setEdicao({ ...edicao, valor: rascunho, erro: mensagem })
  const [salvou, setSalvou] = useState(false)
  // #2365: texto e trava pertencem ao cliente/campo, inclusive após remontar no celular.
  const [salvando, iniciarGravacao, estaGravando] = useGravacaoDoAjuste(chave)
  const inputRef = useRef(null)
  // #1920: a API guarda o telefone em E.164 ("+5511987654321"). Mostrado cru, e a máscara aplicada
  // por cima dele ao editar virava "(55) 11987-6541". Mostra, edita e compara já na máscara da tela.
  const exibido = tipo === 'telefone' && valor ? telefoneDoE164(valor) : (valor ?? '')

  // #2258 (T17): rascunho diferente do valor salvo conta como digitado; o modal pergunta antes de descartar.
  const sujo = editando && rascunho !== exibido
  // Efeito de layout, como o do cadastro (BlocoCliente): o aviso chega à janela antes do próximo evento.
  useLayoutEffect(() => { aoMudarSujo?.(sujo) }, [aoMudarSujo, sujo])
  useLayoutEffect(() => () => aoMudarSujo?.(false), [aoMudarSujo])

  useEffect(() => {
    if (!editando) return
    // No remonte, showModal ainda dará o foco inicial; antecipá-lo dispara o onBlur e salva sozinho.
    if (inputRef.current?.closest('dialog')?.open === false) return
    inputRef.current?.focus()
    inputRef.current?.select()
  }, [editando])

  // O erro devolve o foco ao campo: quem digitou corrige sem caçar onde está.
  useEffect(() => {
    if (erro && editando) inputRef.current?.focus()
  }, [erro, editando])

  useEffect(() => {
    if (!salvou) return undefined
    const t = setTimeout(() => setSalvou(false), SEGUNDOS_SALVOU * 1000)
    return () => clearTimeout(t)
  }, [salvou])

  function abrir() {
    setEdicao({ valor: exibido, erro: null, ...(conversa ? { identidade: identidadeDaEdicao(conversa) } : {}) })
  }

  function cancelar() {
    if (estaGravando()) return
    setRascunho(null)
  }

  function concluir(resultado) {
    if (resultado === false || resultado?.erro) { setErro(resultado?.erro || FALHA_AO_SALVAR); return }
    setRascunho(null)
    setSalvou(true)
  }

  function confirmar() {
    if (estaGravando()) return
    if (conversa && !mesmaIdentidadeDaEdicao(edicao?.identidade, conversa)) { setErro(ERRO_IDENTIDADE_DA_EDICAO); return }
    const mensagem = validar?.(rascunho) ?? null
    if (mensagem) { setErro(mensagem); return }
    if (rascunho === exibido) { setRascunho(null); return }
    const ida = aoSalvar(rascunho, edicao?.identidade)
    if (!ida?.then) { concluir(ida); return }
    const liberar = iniciarGravacao()
    setErro(null)
    ida.then((resultado) => { liberar?.(); concluir(resultado) }, (falha) => { liberar?.(); concluir({ erro: falha?.message || FALHA_AO_SALVAR }) })
  }

  function aoDigitar(evento) {
    const bruto = evento.target.value
    setRascunho(tipo === 'telefone' ? mascaraTelefone(bruto) : bruto)
  }

  function aoTeclar(evento) {
    if (evento.key === 'Enter') confirmar()
    if (evento.key === 'Escape') cancelar()
  }

  if (!editando) {
    return (
      <div className={css.campoEmLinha}>
        <button type="button" className={css.valorBotao} onClick={abrir} aria-label={`Editar ${rotulo}`}>
          <span className={valor ? undefined : css.valorVazio}>{exibido || vazio}</span>
          <Icone nome="lapis" tamanho={20} />
        </button>
        {salvou && (
          <output className={css.salvo}>
            <Icone nome="check" tamanho={20} /> Salvo
          </output>
        )}
      </div>
    )
  }

  return (
    <div className={css.campoEmLinha}>
      <div className={css.edicaoEmLinha}>
        <label className="sr" htmlFor={idCampo}>{rotulo}</label>
        <input
          id={idCampo}
          ref={inputRef}
          className={[css.controleEmLinha, erro ? css.controleComErro : ''].filter(Boolean).join(' ')}
          value={rascunho}
          onChange={aoDigitar}
          onKeyDown={aoTeclar}
          onBlur={confirmar}
          readOnly={salvando}
          aria-busy={salvando || undefined}
          aria-invalid={Boolean(erro) || undefined}
          aria-describedby={erro ? idErro : undefined}
          placeholder={dica}
          maxLength={limite}
        />
        <BotaoIcone rotulo="Salvar" icone="check" disabled={salvando} onClick={confirmar} />
        <BotaoIcone rotulo="Desfazer" icone="x" disabled={salvando} onMouseDown={(e) => e.preventDefault()} onClick={cancelar} />
      </div>
      {salvando && <output className={css.salvandoEmLinha}>Salvando…</output>}
      {erro && !erroDoVinculo && <p id={idErro} className={css.erroEmLinha} role="alert">{erro}</p>}
      {conversa && <ConferirDestinoDaEdicao idMensagem={erroDoVinculo ? idErro : undefined} identidade={edicao?.identidade} conversa={conversa} aoConferir={identidade => setEdicao({ ...edicao, identidade, erro: null })} />}
    </div>
  )
}
