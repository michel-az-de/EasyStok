// Configurações › Usuários (#1753, M7.2, só no modo API): quem tem acesso à casa, com que perfil,
// e o que se faz com isso: convidar, trocar o perfil, desativar, reativar e (#2070) marcar a dona.
// Convite é por link no e-mail: ninguém define a senha de ninguém (DM7-3).
// #2070: os botões de cada linha são as `acoes` que a API calcula pela hierarquia; a tela não decide.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAtendimento } from '../../../aplicacao/contextos'
import {
  convidarUsuario, desativarUsuario, listarPerfis, listarUsuarios, marcarDona, reativarUsuario, reenviarConvite, trocarPerfil,
} from '../../../aplicacao/usuarios'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { haQuantoTempo } from '../../../dominio/formato'
import {
  CONVITE_VAZIO, DICA_DO_EMAIL_DO_CONVITE, MINIMO_DO_MOTIVO, SITUACAO, acoesDoUsuario, confirmacaoDaMarcaDeDona, ehDona,
  ehSuporteDaSessao, erroDoConvite, erroDoMotivo, estadoDoConvite, fraseDeFalha, fraseDeRetorno, instanteDaApi,
  opcoesDoPerfilDaPessoa, ordenarPerfis, situacaoDoUsuario,
} from '../../../dominio/usuarios'
import css from './usuarios.module.css'

const ultimoAcesso = (ms, agora) => {
  if (!ms) return 'Nunca entrou'
  const tempo = haQuantoTempo(ms, agora)
  return tempo === 'agora' ? 'Entrou agora há pouco' : `Último acesso há ${tempo}`
}
const primeiroControle = (elemento) => elemento?.querySelector('select:not(:disabled), button:not(:disabled)')

export function Convite({ perfis, aoConvidar, aoCancelar }) {
  const formulario = useRef(null)
  const [campos, setCampos] = useState(CONVITE_VAZIO)
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  // Digitar em qualquer campo tira o erro: ele falava do que estava escrito antes.
  const trocar = (campo) => (evento) => { setErro(null); setCampos((atual) => ({ ...atual, [campo]: evento.target.value })) }
  const trocarDona = (evento) => { setErro(null); setCampos((atual) => ({ ...atual, dona: evento.target.checked })) }
  const estado = estadoDoConvite(campos, perfis)

  useEffect(() => { formulario.current?.querySelector('input')?.focus() }, [])

  async function enviar(evento) {
    evento.preventDefault()
    if (enviando) return
    // Vale a marca que a tela mostra: trocar para um perfil sem a opção tira a marca de dona.
    const pedido = { ...campos, dona: estado.comoDona }
    const invalido = erroDoConvite(pedido)
    if (invalido) { setErro(invalido); return }
    setErro(null)
    setEnviando(true)
    try {
      // Deu certo: quem chamou fecha o formulário, então não há mais estado para mexer aqui.
      await aoConvidar(pedido)
    } catch (falha) {
      setErro(fraseDeFalha('convite', falha))
      setEnviando(false)
    }
  }

  return (
    <form ref={formulario} className={css.convite} onSubmit={enviar} aria-label="Convidar pessoa" aria-busy={enviando} noValidate>
      <CampoTexto rotulo="Nome" autoComplete="off" value={campos.nome} onChange={trocar('nome')} readOnly={enviando} />
      <CampoTexto rotulo="E-mail" tipo="email" autoComplete="off" value={campos.email} onChange={trocar('email')} readOnly={enviando} dica={DICA_DO_EMAIL_DO_CONVITE} />
      <CampoSelecao rotulo="Perfil" opcoes={estado.opcoes} value={campos.perfilId} onChange={trocar('perfilId')} disabled={enviando} dica={estado.resumo} />
      {estado.ofereceDona && (
        <div className={css.dona}>
          <div className={css.opcaoDona}>
            <label className={css.marcarDona}>
              <input type="checkbox" checked={estado.comoDona} onChange={trocarDona} disabled={enviando} />
              Convidar como dona
            </label>
            <p className={css.dicaDona}>A dona pode tudo, inclusive marcar outras donas. Use um e-mail que ainda não tenha conta.</p>
          </div>
          {estado.pedeEmailDeNovo && (
            <CampoTexto
              rotulo="Confirme o e-mail" tipo="email" autoComplete="off" value={campos.emailDeNovo} onChange={trocar('emailDeNovo')}
              readOnly={enviando} dica="Digite o mesmo e-mail mais uma vez."
            />
          )}
        </div>
      )}
      <div className={css.botoesDoConvite}>
        <Botao variante="primario" tipo="submit" disabled={enviando}>{enviando ? 'Enviando…' : 'Enviar convite'}</Botao>
        <Botao variante="texto" disabled={enviando} onClick={aoCancelar}>Cancelar</Botao>
      </div>
      {erro && <p className={css.erroDoConvite} role="alert">{erro}</p>}
    </form>
  )
}

export function Pessoa({ usuario, perfis, meuId, agora, ocupado, suporte, aoAgir, aoAbrirConfirmacao }) {
  const linha = useRef(null)
  const painel = useRef(null)
  const cancelar = useRef(null)
  const jaAbriu = useRef(false)
  const [confirmando, setConfirmando] = useState(false)
  // Escolher no select não troca nada: seta do teclado num select fechado já mudava o perfil e derrubava a sessão da pessoa.
  const [perfilEscolhido, setPerfilEscolhido] = useState(null)
  // Marcar ou tirar a marca de dona: a confirmação ocupa a linha inteira, com o motivo quando é o suporte.
  const [marca, setMarca] = useState(null)
  const situacao = situacaoDoUsuario(usuario)
  const pode = acoesDoUsuario(usuario)
  const eu = usuario.usuarioId === meuId
  const ultimo = instanteDaApi(usuario.ultimoAcessoEm)
  const aberta = confirmando || perfilEscolhido !== null || marca !== null
  const confirmacao = marca ? confirmacaoDaMarcaDeDona(marca.acao, usuario, suporte) : null

  // Teclado: a confirmação abre com o foco no motivo, quando há, ou em quem desiste; ao desistir, o foco volta para a linha.
  // Quando a ação segue, a linha está ocupada (nada habilitado) e quem devolve o foco é a lista.
  useEffect(() => {
    if (aberta) {
      jaAbriu.current = true
      const motivo = painel.current?.querySelector('input')
      if (motivo) motivo.focus()
      else cancelar.current?.focus()
      return
    }
    if (jaAbriu.current) primeiroControle(linha.current)?.focus()
  }, [aberta])

  function escolherPerfil(evento) {
    const valor = evento.target.value
    if (!valor || valor === usuario.perfilId) { setPerfilEscolhido(null); return }
    setConfirmando(false)
    setPerfilEscolhido(valor)
    aoAbrirConfirmacao()
  }

  function abrirMarca(acao) {
    setMarca({ acao, motivo: '', erro: null })
    aoAbrirConfirmacao()
  }

  function confirmarMarca() {
    const invalido = confirmacao.pedeMotivo ? erroDoMotivo(marca.motivo) : null
    if (invalido) { setMarca((atual) => ({ ...atual, erro: invalido })); return }
    const { acao, motivo } = marca
    setMarca(null)
    aoAgir(acao, usuario, confirmacao.pedeMotivo ? motivo : null)
  }

  return (
    <li ref={linha} className={css.pessoa} data-usuario={usuario.usuarioId} data-desativado={!usuario.ativo || undefined}>
      <div className={css.quem}>
        <span className={css.identificacao}>
          <strong className={css.nome}>{usuario.nome}{eu && <span className={css.voce}> (você)</span>}</strong>
          {ehDona(usuario) && <Pilula tom="marca">Dona</Pilula>}
        </span>
        <span className={css.email} title={usuario.email}>{usuario.email}</span>
      </div>
      <div className={css.perfil}>
        {pode.trocarPerfil ? (
          <CampoSelecao
            rotulo={`Perfil de ${usuario.nome}`} rotuloOculto className={css.seletor}
            opcoes={opcoesDoPerfilDaPessoa(usuario, perfis)}
            value={perfilEscolhido ?? usuario.perfilId ?? ''} disabled={ocupado || marca !== null}
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
        {pode.marcarDona && !aberta && (
          <Botao variante="texto" disabled={ocupado} onClick={() => abrirMarca('marcarDona')}>Marcar como dona</Botao>
        )}
        {pode.desmarcarDona && !aberta && (
          <Botao variante="texto" disabled={ocupado} onClick={() => abrirMarca('desmarcarDona')}>Tirar a marca de dona</Botao>
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
        {pode.reativar && !aberta && <Botao variante="texto" disabled={ocupado} onClick={() => aoAgir('reativar', usuario)}>Reativar</Botao>}
      </div>
      {confirmacao && (
        <div ref={painel} className={css.confirmacao}>
          <p className={css.textoDaConfirmacao}>{confirmacao.texto}</p>
          {confirmacao.pedeMotivo && (
            <CampoTexto
              rotulo="Motivo do suporte" autoComplete="off" value={marca.motivo} readOnly={ocupado}
              onChange={(evento) => { const motivo = evento.target.value; setMarca((atual) => ({ ...atual, motivo, erro: null })) }}
              dica={`Fica no histórico da casa. Pelo menos ${MINIMO_DO_MOTIVO} caracteres.`}
            />
          )}
          {marca.erro && <p className={css.erro} role="alert">{marca.erro}</p>}
          <div className={css.botoesDaConfirmacao}>
            <Botao variante="primario" disabled={ocupado} onClick={confirmarMarca}>{confirmacao.confirmar}</Botao>
            <Botao ref={cancelar} variante="texto" disabled={ocupado} onClick={() => setMarca(null)}>Cancelar</Botao>
          </div>
        </div>
      )}
    </li>
  )
}

const CHAMADA = {
  perfil: (usuario, perfilId) => trocarPerfil(usuario.usuarioId, perfilId),
  reenviar: (usuario) => reenviarConvite(usuario.usuarioId),
  desativar: (usuario) => desativarUsuario(usuario.usuarioId),
  reativar: (usuario) => reativarUsuario(usuario.usuarioId),
  marcarDona: (usuario, motivo) => marcarDona(usuario.usuarioId, { dona: true, motivo }),
  desmarcarDona: (usuario, motivo) => marcarDona(usuario.usuarioId, { dona: false, motivo }),
}

export function AbaUsuarios() {
  const { sessao, agora } = useAtendimento()
  const meuId = sessao?.usuario?.id ?? null
  const suporte = ehSuporteDaSessao(sessao)
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

  // `valor` é o perfil escolhido (trocar perfil) ou o motivo do suporte (marca de dona).
  const agir = useCallback(async (acao, usuario, valor) => {
    setOcupado(true)
    setRetorno(null)
    try {
      await CHAMADA[acao](usuario, valor)
      const perfil = estado.perfis.find((p) => p.perfilId === valor)?.nome
      // A lista recarrega antes do aviso: "continua no fim desta lista" só é verdade depois disso.
      await carregar()
      setRetorno({ tom: 'ok', texto: fraseDeRetorno(acao, usuario, perfil) })
    } catch (falha) {
      await carregar()
      setRetorno({ tom: 'erro', texto: fraseDeFalha(acao, falha) })
    } finally {
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
              suporte={suporte} aoAgir={agir} aoAbrirConfirmacao={limparRetorno}
            />
          ))}
        </ul>
      </section>
    </div>
  )
}
