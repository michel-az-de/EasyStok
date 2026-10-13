// Configurações › Usuários (#1753, M7.2, só no modo API): quem tem acesso à casa, com que perfil,
// e as quatro coisas que a dona faz com isso: convidar, trocar o perfil, desativar e reativar.
// Convite é por link no e-mail: a dona nunca define a senha de ninguém (DM7-3).
import { useCallback, useEffect, useId, useMemo, useState } from 'react'
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
  acoesDoUsuario, erroDoConvite, instanteDaApi, ordenarPerfis, resumoDoPerfil, situacaoDoUsuario,
} from '../../../dominio/usuarios'
import css from './usuarios.module.css'

const VAZIO = { nome: '', email: '', perfilId: '' }
const ultimoAcesso = (ms, agora) => {
  if (!ms) return 'Nunca entrou'
  const tempo = haQuantoTempo(ms, agora)
  return tempo === 'agora' ? 'Entrou agora há pouco' : `Último acesso há ${tempo}`
}

function Convite({ perfis, aoConvidar }) {
  const id = useId()
  const [campos, setCampos] = useState(VAZIO)
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const trocar = (campo) => (evento) => setCampos((atual) => ({ ...atual, [campo]: evento.target.value }))
  const opcoes = [{ valor: '', rotulo: 'Escolha o perfil' }, ...perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))]
  const resumo = resumoDoPerfil(perfis.find((p) => p.perfilId === campos.perfilId)?.nome)

  async function enviar(evento) {
    evento.preventDefault()
    if (enviando) return
    const invalido = erroDoConvite(campos)
    if (invalido) { setErro(invalido); return }
    setErro(null)
    setEnviando(true)
    try {
      await aoConvidar(campos)
      setCampos(VAZIO)
    } catch (falha) {
      setErro(`O convite não saiu: ${falha.message}`)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section className={css.secao} aria-labelledby={`${id}-titulo`}>
      <h3 id={`${id}-titulo`}>Convidar pessoa</h3>
      <p className={css.descricao}>Ela recebe um link por e-mail e escolhe a própria senha.</p>
      <form className={css.convite} onSubmit={enviar} aria-busy={enviando} noValidate>
        <CampoTexto rotulo="Nome" autoComplete="off" value={campos.nome} onChange={trocar('nome')} readOnly={enviando} />
        <CampoTexto rotulo="E-mail" tipo="email" autoComplete="off" value={campos.email} onChange={trocar('email')} readOnly={enviando} />
        <CampoSelecao rotulo="Perfil" opcoes={opcoes} value={campos.perfilId} onChange={trocar('perfilId')} disabled={enviando} dica={resumo} />
        <Botao variante="primario" tipo="submit" className={css.enviar} disabled={enviando}>
          {enviando ? 'Enviando…' : 'Enviar convite'}
        </Botao>
      </form>
      {erro && <p className={css.erro} role="alert">{erro}</p>}
    </section>
  )
}

function Pessoa({ usuario, perfis, meuId, agora, ocupado, aoAgir }) {
  const [confirmando, setConfirmando] = useState(false)
  const situacao = situacaoDoUsuario(usuario)
  const pode = acoesDoUsuario(usuario, meuId)
  const eu = usuario.usuarioId === meuId
  const ultimo = instanteDaApi(usuario.ultimoAcessoEm)
  // O perfil atual sempre aparece, mesmo que não esteja mais entre os que se pode dar.
  const opcoes = perfis.some((p) => p.perfilId === usuario.perfilId) || !usuario.perfilId
    ? perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))
    : [{ valor: usuario.perfilId, rotulo: usuario.perfil }, ...perfis.map((p) => ({ valor: p.perfilId, rotulo: p.nome }))]

  return (
    <li className={css.pessoa} data-desativado={!usuario.ativo || undefined}>
      <div className={css.quem}>
        <strong className={css.nome}>{usuario.nome}{eu && <span className={css.voce}> (você)</span>}</strong>
        <span className={css.email}>{usuario.email}</span>
      </div>
      <div className={css.perfil}>
        {pode.trocarPerfil ? (
          <CampoSelecao
            rotulo={`Perfil de ${usuario.nome}`} rotuloOculto
            opcoes={usuario.perfilId ? opcoes : [{ valor: '', rotulo: 'Sem perfil' }, ...opcoes]}
            value={usuario.perfilId ?? ''} disabled={ocupado}
            onChange={(evento) => evento.target.value && aoAgir('perfil', usuario, evento.target.value)}
          />
        ) : <span className={css.perfilFixo}>{usuario.perfil ?? 'Sem perfil'}</span>}
      </div>
      <div className={css.situacao}>
        <Pilula tom={situacao.tom} fina>{situacao.rotulo}</Pilula>
        <span className={css.acesso}>{ultimoAcesso(ultimo, agora)}</span>
      </div>
      <div className={css.acoes}>
        {pode.reenviarConvite && !confirmando && (
          <Botao variante="texto" disabled={ocupado} onClick={() => aoAgir('reenviar', usuario)}>Reenviar convite</Botao>
        )}
        {pode.desativar && !confirmando && (
          <Botao variante="texto" disabled={ocupado} onClick={() => setConfirmando(true)}>Desativar</Botao>
        )}
        {confirmando && (
          <>
            <Botao variante="perigo" disabled={ocupado} onClick={() => { setConfirmando(false); aoAgir('desativar', usuario) }}>
              Desativar {usuario.nome.split(' ')[0]}
            </Botao>
            <Botao variante="texto" disabled={ocupado} onClick={() => setConfirmando(false)}>Manter</Botao>
          </>
        )}
        {pode.reativar && <Botao variante="texto" disabled={ocupado} onClick={() => aoAgir('reativar', usuario)}>Reativar</Botao>}
      </div>
    </li>
  )
}

const FEITO = {
  convite: (u) => `Convite enviado para ${u.email}. Chega em até um minuto.`,
  reenviar: (u) => `Convite reenviado para ${u.email}. O link anterior deixou de valer.`,
  perfil: (u, perfil) => `${u.nome} agora é ${perfil}. Ela precisa entrar de novo.`,
  desativar: (u) => `${u.nome} não entra mais. Dá para reativar quando quiser.`,
  reativar: (u) => `${u.nome} voltou a ter acesso.`,
}
const FALHOU = {
  reenviar: 'O convite não foi reenviado',
  perfil: 'O perfil não mudou',
  desativar: 'O acesso continua ativo',
  reativar: 'O acesso continua desativado',
}

export function AbaUsuarios() {
  const { sessao, agora } = useAtendimento()
  const meuId = sessao?.usuario?.id ?? null
  const [estado, setEstado] = useState({ carregando: true, usuarios: [], perfis: [], erro: null })
  const [retorno, setRetorno] = useState(null)
  const [ocupado, setOcupado] = useState(false)

  const carregar = useCallback(async (sinal) => {
    try {
      const [usuarios, perfis] = await Promise.all([listarUsuarios(sinal), listarPerfis(sinal)])
      setEstado({ carregando: false, usuarios: usuarios ?? [], perfis: ordenarPerfis(perfis ?? []), erro: null })
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

  const convidar = useCallback(async (campos) => {
    await convidarUsuario(campos)
    setRetorno({ tom: 'ok', texto: FEITO.convite(campos) })
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
      setRetorno({ tom: 'ok', texto: FEITO[acao](usuario, perfil) })
    } catch (falha) {
      setRetorno({ tom: 'erro', texto: `${FALHOU[acao]}: ${falha.message}` })
    } finally {
      await carregar()
      setOcupado(false)
    }
  }, [carregar, estado.perfis])

  // Quem pode entrar vem primeiro; desativados ficam no fim, à vista para reativar.
  const pessoas = useMemo(
    () => [...estado.usuarios].sort((a, b) => Number(b.ativo) - Number(a.ativo) || a.nome.localeCompare(b.nome, 'pt-BR')),
    [estado.usuarios],
  )

  if (estado.carregando) return <output className={css.descricao}>Carregando as pessoas…</output>
  if (estado.erro && pessoas.length === 0) {
    return (
      <Vazio titulo="A lista não carregou" role="alert" acao={<Botao onClick={() => { setEstado((a) => ({ ...a, carregando: true })); carregar() }}>Tentar de novo</Botao>}>
        {estado.erro}
      </Vazio>
    )
  }

  return (
    <div className={css.aba}>
      <Convite perfis={estado.perfis} aoConvidar={convidar} />
      <section className={css.secao} aria-labelledby="titulo-pessoas">
        <h3 id="titulo-pessoas">Quem tem acesso</h3>
        {retorno && (retorno.tom === 'erro'
          ? <p className={css.erro} role="alert">{retorno.texto}</p>
          : <output className={css.retorno}>{retorno.texto}</output>)}
        <ul className={css.pessoas}>
          {pessoas.map((usuario) => (
            <Pessoa key={usuario.usuarioId} usuario={usuario} perfis={estado.perfis} meuId={meuId} agora={agora} ocupado={ocupado} aoAgir={agir} />
          ))}
        </ul>
      </section>
    </div>
  )
}
