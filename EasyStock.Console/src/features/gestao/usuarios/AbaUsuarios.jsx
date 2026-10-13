// Configurações › Usuários (#1753, M7.2, só no modo API): quem tem acesso à casa, com que perfil,
// e as quatro coisas que a dona faz com isso: convidar, trocar o perfil, desativar e reativar.
// Convite é por link no e-mail: a dona nunca define a senha de ninguém (DM7-3).
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAtendimento } from '../../../aplicacao/contextos'
import {
  convidarUsuario, desativarUsuario, listarPerfis, listarUsuarios, reativarUsuario, reenviarConvite, trocarPerfil,
} from '../../../aplicacao/usuarios'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { haQuantoTempo } from '../../../dominio/formato'
import {
  SITUACAO, acoesDoUsuario, erroDoConvite, fraseDeFalha, fraseDeRetorno, instanteDaApi, ordenarPerfis, resumoDoPerfil,
  situacaoDoUsuario,
} from '../../../dominio/usuarios'
import css from './usuarios.module.css'

const VAZIO = { nome: '', email: '', perfilId: '' }
const ultimoAcesso = (ms, agora) => {
  if (!ms) return 'Nunca entrou'
  const tempo = haQuantoTempo(ms, agora)
  return tempo === 'agora' ? 'Entrou agora há pouco' : `Último acesso há ${tempo}`
}
const primeiroControle = (elemento) => elemento?.querySelector('select:not(:disabled), button:not(:disabled)')

function Convite({ perfis, aoConvidar, aoCancelar }) {
  const formulario = useRef(null)
  const [campos, setCampos] = useState(VAZIO)
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  // Digitar em qualquer campo tira o erro: ele falava do que estava escrito antes.
  const trocar = (campo) => (evento) => { setErro(null); setCampos((atual) => ({ ...atual, [campo]: evento.target.value })) }
  const opcoes = [{ valor: '', rotulo: 'Escolha o perfil' }, ...perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))]
  const resumo = resumoDoPerfil(perfis.find((p) => p.perfilId === campos.perfilId)?.nome)

  useEffect(() => { formulario.current?.querySelector('input')?.focus() }, [])

  async function enviar(evento) {
    evento.preventDefault()
    if (enviando) return
    const invalido = erroDoConvite(campos)
    if (invalido) { setErro(invalido); return }
    setErro(null)
    setEnviando(true)
    try {
      // Deu certo: quem chamou fecha o formulário, então não há mais estado para mexer aqui.
      await aoConvidar(campos)
    } catch (falha) {
      setErro(`O convite não saiu: ${falha.message}`)
      setEnviando(false)
    }
  }

  return (
    <form ref={formulario} className={css.convite} onSubmit={enviar} aria-label="Convidar pessoa" aria-busy={enviando} noValidate>
      <CampoTexto rotulo="Nome" autoComplete="off" value={campos.nome} onChange={trocar('nome')} readOnly={enviando} />
      <CampoTexto rotulo="E-mail" tipo="email" autoComplete="off" value={campos.email} onChange={trocar('email')} readOnly={enviando} dica="O convite vai para este e-mail" />
      <CampoSelecao rotulo="Perfil" opcoes={opcoes} value={campos.perfilId} onChange={trocar('perfilId')} disabled={enviando} dica={resumo} />
      <div className={css.botoesDoConvite}>
        <Botao variante="primario" tipo="submit" disabled={enviando}>{enviando ? 'Enviando…' : 'Enviar convite'}</Botao>
        <Botao variante="texto" disabled={enviando} onClick={aoCancelar}>Cancelar</Botao>
      </div>
      {erro && <p className={css.erroDoConvite} role="alert">{erro}</p>}
    </form>
  )
}

function Pessoa({ usuario, perfis, meuId, agora, ocupado, aoAgir, aoAbrirConfirmacao }) {
  const linha = useRef(null)
  const cancelar = useRef(null)
  const jaAbriu = useRef(false)
  const [confirmando, setConfirmando] = useState(false)
  // Escolher no select não troca nada: seta do teclado num select fechado já mudava o perfil e derrubava a sessão da pessoa.
  const [perfilEscolhido, setPerfilEscolhido] = useState(null)
  const situacao = situacaoDoUsuario(usuario)
  const pode = acoesDoUsuario(usuario, meuId)
  const eu = usuario.usuarioId === meuId
  const ultimo = instanteDaApi(usuario.ultimoAcessoEm)
  const aberta = confirmando || perfilEscolhido !== null
  // O perfil atual sempre aparece, mesmo que não esteja mais entre os que se pode dar.
  const opcoes = perfis.some((p) => p.perfilId === usuario.perfilId) || !usuario.perfilId
    ? perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))
    : [{ valor: usuario.perfilId, rotulo: usuario.perfil }, ...perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))]

  // Teclado: a confirmação abre com o foco em quem desiste; ao desistir, o foco volta para a linha.
  // Quando a ação segue, a linha está ocupada (nada habilitado) e quem devolve o foco é a lista.
  useEffect(() => {
    if (aberta) { jaAbriu.current = true; cancelar.current?.focus(); return }
    if (jaAbriu.current) primeiroControle(linha.current)?.focus()
  }, [aberta])

  function escolherPerfil(evento) {
    const valor = evento.target.value
    if (!valor || valor === usuario.perfilId) { setPerfilEscolhido(null); return }
    setConfirmando(false)
    setPerfilEscolhido(valor)
    aoAbrirConfirmacao()
  }

  return (
    <li ref={linha} className={css.pessoa} data-usuario={usuario.usuarioId} data-desativado={!usuario.ativo || undefined}>
      <div className={css.quem}>
        <strong className={css.nome}>{usuario.nome}{eu && <span className={css.voce}> (você)</span>}</strong>
        <span className={css.email} title={usuario.email}>{usuario.email}</span>
      </div>
      <div className={css.perfil}>
        {pode.trocarPerfil ? (
          <CampoSelecao
            rotulo={`Perfil de ${usuario.nome}`} rotuloOculto className={css.seletor}
            opcoes={usuario.perfilId ? opcoes : [{ valor: '', rotulo: 'Sem perfil' }, ...opcoes]}
            value={perfilEscolhido ?? usuario.perfilId ?? ''} disabled={ocupado}
            onChange={escolherPerfil}
          />
        ) : <span className={css.perfilFixo}>{usuario.perfil ?? 'Sem perfil'}</span>}
      </div>
      <div className={css.situacao}>
        {situacao !== SITUACAO.ativo && <Pilula tom={situacao.tom} fina>{situacao.rotulo}</Pilula>}
        <span className={css.acesso}>{ultimoAcesso(ultimo, agora)}</span>
      </div>
      <div className={css.acoes}>
        {pode.reenviarConvite && !aberta && (
          <Botao variante="texto" disabled={ocupado} onClick={() => aoAgir('reenviar', usuario)}>Reenviar convite</Botao>
        )}
        {pode.desativar && !aberta && (
          <Botao variante="texto" disabled={ocupado} onClick={() => { setConfirmando(true); aoAbrirConfirmacao() }}>Desativar</Botao>
        )}
        {confirmando && (
          <>
            <Botao variante="perigo" disabled={ocupado} onClick={() => { setConfirmando(false); aoAgir('desativar', usuario) }}>
              Desativar {usuario.nome.split(' ')[0]}
            </Botao>
            <Botao ref={cancelar} variante="texto" disabled={ocupado} onClick={() => setConfirmando(false)}>Manter</Botao>
          </>
        )}
        {perfilEscolhido !== null && (
          <>
            <Botao variante="primario" disabled={ocupado} onClick={() => { setPerfilEscolhido(null); aoAgir('perfil', usuario, perfilEscolhido) }}>
              Salvar perfil
            </Botao>
            <Botao ref={cancelar} variante="texto" disabled={ocupado} onClick={() => setPerfilEscolhido(null)}>Cancelar</Botao>
          </>
        )}
        {pode.reativar && <Botao variante="texto" disabled={ocupado} onClick={() => aoAgir('reativar', usuario)}>Reativar</Botao>}
      </div>
    </li>
  )
}

export function AbaUsuarios() {
  const { sessao, agora } = useAtendimento()
  const meuId = sessao?.usuario?.id ?? null
  const titulo = useRef(null)
  const lista = useRef(null)
  const botaoConvidar = useRef(null)
  const jaConvidou = useRef(false)
  const [estado, setEstado] = useState({ carregando: true, carregada: false, usuarios: [], perfis: [], erro: null })
  const [retorno, setRetorno] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const [convidando, setConvidando] = useState(false)
  // Para onde o foco volta depois de uma ação: a linha da pessoa, ou o título quando a linha mudou de lugar.
  const [foco, setFoco] = useState(null)

  const carregar = useCallback(async (sinal) => {
    try {
      const [usuarios, perfis] = await Promise.all([listarUsuarios(sinal), listarPerfis(sinal)])
      setEstado({ carregando: false, carregada: true, usuarios: usuarios ?? [], perfis: ordenarPerfis(perfis ?? []), erro: null })
    } catch (falha) {
      if (sinal?.aborted) return
      setEstado((atual) => ({ ...atual, carregando: false, erro: falha.message }))
    }
  }, [])

  useEffect(() => {
    const controle = new AbortController()
    carregar(controle.signal)
    return () => controle.abort()
  }, [carregar])

  useEffect(() => {
    if (!foco) return
    const linha = foco.usuarioId && lista.current?.querySelector(`[data-usuario="${foco.usuarioId}"]`)
    const alvo = (linha && primeiroControle(linha)) || titulo.current
    alvo?.focus()
  }, [foco])

  // Fechou o formulário do convite: o foco volta para o botão que o abriu.
  useEffect(() => {
    if (convidando) { jaConvidou.current = true; return }
    if (jaConvidou.current) botaoConvidar.current?.focus()
  }, [convidando])

  const convidar = useCallback(async (campos) => {
    await convidarUsuario(campos)
    setConvidando(false)
    setRetorno({ tom: 'ok', texto: fraseDeRetorno('convite', campos) })
    await carregar()
  }, [carregar])

  const agir = useCallback(async (acao, usuario, valor) => {
    setOcupado(true)
    setRetorno(null)
    try {
      if (acao === 'perfil') await trocarPerfil(usuario.usuarioId, valor)
      else if (acao === 'reenviar') await reenviarConvite(usuario.usuarioId)
      else if (acao === 'desativar') await desativarUsuario(usuario.usuarioId)
      else await reativarUsuario(usuario.usuarioId)
      const perfil = estado.perfis.find((p) => p.perfilId === valor)?.nome
      setRetorno({ tom: 'ok', texto: fraseDeRetorno(acao, usuario, perfil) })
    } catch (falha) {
      setRetorno({ tom: 'erro', texto: fraseDeFalha(acao, falha) })
    } finally {
      await carregar()
      setOcupado(false)
      // Desativar e reativar mudam a linha de lugar: o foco vai para o título da lista.
      setFoco({ usuarioId: acao === 'desativar' || acao === 'reativar' ? null : usuario.usuarioId })
    }
  }, [carregar, estado.perfis])

  const limparRetorno = useCallback(() => setRetorno(null), [])
  const recarregar = () => { setEstado((a) => ({ ...a, carregando: !a.carregada })); carregar() }

  // Quem pode entrar vem primeiro; desativados ficam no fim, à vista para reativar.
  const pessoas = useMemo(
    () => [...estado.usuarios].sort((a, b) => Number(b.ativo) - Number(a.ativo) || a.nome.localeCompare(b.nome, 'pt-BR')),
    [estado.usuarios],
  )

  if (estado.carregando) return <output className={css.descricao}>Carregando as pessoas…</output>
  if (estado.erro && !estado.carregada) {
    return (
      <Vazio titulo="A lista não carregou" role="alert" acao={<Botao onClick={recarregar}>Tentar de novo</Botao>}>
        {estado.erro}
      </Vazio>
    )
  }

  return (
    <div className={css.aba}>
      <section className={css.secao} aria-labelledby="titulo-pessoas">
        <div className={css.cabecalho}>
          <h3 id="titulo-pessoas" ref={titulo} tabIndex={-1}>Quem tem acesso</h3>
          {!convidando && (
            <Botao ref={botaoConvidar} variante="primario" disabled={ocupado} onClick={() => setConvidando(true)}>Convidar pessoa</Botao>
          )}
        </div>
        {convidando && <Convite perfis={estado.perfis} aoConvidar={convidar} aoCancelar={() => setConvidando(false)} />}
        {/* Sempre montado e com a altura reservada: a lista não pula quando o aviso chega. */}
        <div className={css.aviso}>
          <output className={css.retorno} data-tom={retorno?.tom} role={retorno?.tom === 'erro' ? 'alert' : undefined}>{retorno?.texto}</output>
        </div>
        {estado.erro && (
          <p className={css.desatualizada} role="alert">
            A lista pode estar desatualizada.
            <Botao variante="texto" onClick={recarregar}>Atualizar</Botao>
          </p>
        )}
        <ul ref={lista} className={css.pessoas}>
          {pessoas.map((usuario) => (
            <Pessoa
              key={usuario.usuarioId} usuario={usuario} perfis={estado.perfis} meuId={meuId} agora={agora} ocupado={ocupado}
              aoAgir={agir} aoAbrirConfirmacao={limparRetorno}
            />
          ))}
        </ul>
      </section>
    </div>
  )
}
