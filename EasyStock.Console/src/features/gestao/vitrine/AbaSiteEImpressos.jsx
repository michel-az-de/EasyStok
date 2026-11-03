// Site e impressos (#2070, só no modo API): cria o site se ainda não existe; depois, subtítulo, cor,
// WhatsApp de pedidos, pedido mínimo, frete grátis e um logo que vale para os impressos e o site.
// Ligar ou tirar o site do ar, endereço próprio, mensagem fora da área de entrega, loja padrão e
// campos fiscais não aparecem aqui, e o PUT devolve o que a tela não mostra exatamente como veio.
// Hoje só o WhatsApp e o logo mudam algo (impressos): o resto fica guardado e a tela diz isso.
import { useId, useRef, useState } from 'react'
import { useEnvio, useLeitura, usePendenciaAvisada } from '../../../aplicacao/formularioDeAjuste'
import {
  carregarSite, criarSite, enviarLogoDoSite, salvarSite, urlDoLogo, usarLogoDaLojaNoSite,
} from '../../../aplicacao/siteEImpressos'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { CampoReais } from '../../../componentes/CampoReais'
import { lerMoeda } from '../../../dominio/formato'
import {
  EXEMPLO_DE_COR, LIMITES_DO_SITE, TIPOS_DE_LOGO, errosDaCriacao, errosDoSite, formularioDoSite, logoDosImpressos, motivoDoArquivoDeLogo,
  siteComOutroLogo, siteMudou, sugerirEnderecoDoSite,
} from '../../../dominio/vitrineDoSite'
import { Erros, FalhaDeLeitura, Pendencia, Retorno } from '../PartesDoAjuste'
import css from '../formularioDeAjuste.module.css'

// O site dos clientes acha a vitrine por este código (o `slug`); o link que o cliente abre é o
// domínio do site, que esta tela não mostra.
const ROTULOS_DA_CRIACAO = { tituloPublico: 'Nome do site', slug: 'Código do site' }
const ROTULOS_DO_SITE = {
  subtituloPublico: 'Subtítulo', corPrimaria: 'Cor principal', whatsappPedidos: 'WhatsApp de pedidos',
  pedidoMinimoCentavos: 'Pedido mínimo para entrega', freteGratisCentavos: 'Frete grátis acima de',
}

export function AbaSiteEImpressos({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const [carga, recarregar] = useLeitura(carregarSite)
  const [criacaoPendente, setCriacaoPendente] = useState(false)
  const [sitePendente, setSitePendente] = useState(false)
  usePendenciaAvisada(criacaoPendente || sitePendente, aoMudarPendencia)

  if (carga.estado === 'carregando') return <output className={css.descricao}>Carregando o site…</output>
  if (carga.estado === 'erro') return <FalhaDeLeitura mensagem={`O site não carregou: ${carga.mensagem}`} aoTentarDeNovo={recarregar} />
  const { vitrine, configuracao, loja, totalDeLojas } = carga.dados
  if (!vitrine) {
    return <CriarSite compacto={compacto} aoMudarPendencia={setCriacaoPendente} aoCriado={() => { aoSalvo?.(); return recarregar() }} />
  }
  return <ConfigurarSite compacto={compacto} configuracao={configuracao} loja={loja} totalDeLojas={totalDeLojas}
    aoMudarPendencia={setSitePendente} aoSalvo={aoSalvo} />
}

const CRIACAO_VAZIA = { tituloPublico: '', slug: '' }

function CriarSite({ compacto, aoMudarPendencia, aoCriado }) {
  const id = useId()
  const [form, setForm] = useState(CRIACAO_VAZIA)
  const [enderecoEscolhido, setEnderecoEscolhido] = useState(false)
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const salvando = situacao.estado === 'salvando'
  const pendente = Boolean(form.tituloPublico.trim() || form.slug.trim())
  usePendenciaAvisada(pendente, aoMudarPendencia)

  function mudarNome(evento) {
    const tituloPublico = evento.target.value
    limpar(); setErros({})
    setForm((atual) => ({ tituloPublico, slug: enderecoEscolhido ? atual.slug : sugerirEnderecoDoSite(tituloPublico) }))
  }
  function mudarEndereco(evento) {
    const slug = evento.target.value.toLowerCase()
    limpar(); setErros({})
    setEnderecoEscolhido(true)
    setForm((atual) => ({ ...atual, slug }))
  }

  async function criar(evento) {
    evento.preventDefault()
    if (salvando) return
    const encontrados = errosDaCriacao(form)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok } = await enviar(() => criarSite(form))
    if (!ok) return
    setForm(CRIACAO_VAZIA)
    setEnderecoEscolhido(false)
    await aoCriado()
  }

  return (
    <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={criar}>
      <h3 id={`${id}-titulo`}>{compacto ? 'Criar o site' : 'A loja ainda não tem site'}</h3>
      {!compacto && <p className={css.descricao}>Escolha o nome e o código. Os dois ficam assim depois de criados.</p>}
      <p className={css.descricao}>O site nasce fora do ar: quem coloca no ar é o suporte.</p>
      <Pendencia visivel={pendente && !salvando} acao="Crie o site ou apague o que digitou antes de sair." />
      <fieldset className={css.campos} disabled={salvando}>
        <legend className="sr">Novo site</legend>
        <CampoTexto rotulo={ROTULOS_DA_CRIACAO.tituloPublico} required maxLength={LIMITES_DO_SITE.tituloPublico}
          dica="Aparece no topo do site." value={form.tituloPublico} onChange={mudarNome} />
        <CampoTexto rotulo={ROTULOS_DA_CRIACAO.slug} required maxLength={LIMITES_DO_SITE.slug} autoCapitalize="none" spellCheck={false}
          dica="O site dos clientes acha a loja por este código: letras minúsculas, números e hífen." value={form.slug} onChange={mudarEndereco} />
      </fieldset>
      <Erros erros={erros} rotulos={ROTULOS_DA_CRIACAO} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando}>{salvando ? 'Criando…' : 'Criar o site'}</Botao>
        <Retorno situacao={situacao} salvo="Site criado, ainda fora do ar." />
      </div>
    </form>
  )
}

function ConfigurarSite({ compacto, configuracao, loja, totalDeLojas, aoMudarPendencia, aoSalvo }) {
  const id = useId()
  const [gravado, setGravado] = useState(configuracao)
  // O logo da loja é o que os impressos usam; a aba Impressão pode trocá-lo sem passar pelo site.
  const [logoDaLoja, setLogoDaLoja] = useState(loja?.logoUrl ?? null)
  // O logo lê a marca gravada depois do upload, não a da renderização em que o arquivo foi escolhido.
  const gravadoRef = useRef(configuracao)
  const gravar = (config) => { gravadoRef.current = config; setGravado(config) }
  const [form, setForm] = useState(() => formularioDoSite(configuracao))
  const [erros, setErros] = useState({})
  const [situacao, enviar, limpar] = useEnvio()
  const [enviandoLogo, setEnviandoLogo] = useState(false)
  const salvando = situacao.estado === 'salvando'
  const pendente = siteMudou(form, gravado)
  // Os dois PUTs do site não correm juntos: um levaria de volta o que o outro acabou de gravar. O logo
  // subindo também conta como pendência, para ninguém sair da tela no meio do envio.
  usePendenciaAvisada(pendente || enviandoLogo, aoMudarPendencia)

  const editar = (campo, valor) => {
    limpar(); setErros({})
    setForm((atual) => ({ ...atual, [campo]: valor }))
  }
  const mudar = (campo) => (evento) => editar(campo, evento.target.value)

  async function salvar(evento) {
    evento.preventDefault()
    if (!pendente || salvando || enviandoLogo) return
    const encontrados = errosDoSite(form, gravado)
    setErros(encontrados)
    if (Object.keys(encontrados).length > 0) return
    const { ok, resultado } = await enviar(async () => {
      const confirmado = await salvarSite(gravadoRef.current, form)
      if (!confirmado) throw new Error('O EasyStok não confirmou o site. Tente de novo.')
      return confirmado
    })
    if (!ok) return
    gravar(resultado)
    setForm(formularioDoSite(resultado))
    aoSalvo?.()
  }

  function desfazer() {
    setForm(formularioDoSite(gravado))
    setErros({})
    limpar()
  }

  function logoSalvo({ configuracao: config, logoDaLoja: logo }) {
    gravar(config)
    setLogoDaLoja(logo)
    aoSalvo?.()
  }

  const idAindaNao = `${id}-ainda-nao`
  return (
    <div className={css.aba}>
      {!compacto && (
        <p className={css.descricao}>
          {gravado.tituloPublico ? `Site “${gravado.tituloPublico}”` : 'Site sem nome'}, código {gravado.slug}. O nome e o código ficam como foram criados.
        </p>
      )}
      <p className={css.descricao}>
        {gravado.ativo ? 'No ar: os clientes já abrem o site.' : 'Fora do ar: os clientes ainda não abrem o site. Quem coloca o site no ar é o suporte.'}
      </p>
      <LogoDoSite gravado={gravado} loja={loja} totalDeLojas={totalDeLojas} logoDaLoja={logoDaLoja}
        lerGravado={() => gravadoRef.current} bloqueado={salvando} aoMudarEnvio={setEnviandoLogo} aoSalvar={logoSalvo} />
      <form className={css.secao} aria-labelledby={`${id}-titulo`} onSubmit={salvar}>
        <h3 id={`${id}-titulo`}>Site</h3>
        <Pendencia visivel={pendente && !salvando} />
        <fieldset className={css.campos} disabled={salvando}>
          <legend className="sr">Configuração do site</legend>
          <CampoTexto rotulo={ROTULOS_DO_SITE.whatsappPedidos} tipo="tel" inputMode="tel" maxLength={LIMITES_DO_SITE.whatsappPedidos}
            dica="Com DDD. Sai nos impressos dos pedidos." value={form.whatsappPedidos} onChange={mudar('whatsappPedidos')} />
          {/* Gravados pela API, mas o menu público e o fechamento do pedido ainda não os leem. */}
          <p id={idAindaNao} className={`${css.aviso} ${css.inteiro}`}>
            Subtítulo, cor, pedido mínimo e frete grátis ficam guardados, mas o site e o fechamento do pedido ainda não usam: por enquanto o cliente não vê nem é cobrado por eles.
          </p>
          <div className={css.inteiro}>
            <CampoTexto rotulo={ROTULOS_DO_SITE.subtituloPublico} maxLength={LIMITES_DO_SITE.subtituloPublico} aria-describedby={idAindaNao}
              value={form.subtituloPublico} onChange={mudar('subtituloPublico')} />
          </div>
          <CampoTexto rotulo={ROTULOS_DO_SITE.corPrimaria} maxLength={LIMITES_DO_SITE.corPrimaria} placeholder={EXEMPLO_DE_COR}
            autoCapitalize="none" spellCheck={false} dica={`Código da cor, como ${EXEMPLO_DE_COR}.`} aria-describedby={idAindaNao}
            value={form.corPrimaria} onChange={mudar('corPrimaria')} />
          <CampoReais rotulo={ROTULOS_DO_SITE.pedidoMinimoCentavos} centavos={form.pedidoMinimoCentavos} ler={lerMoeda}
            aria-describedby={idAindaNao} aoMudar={(centavos) => editar('pedidoMinimoCentavos', centavos ?? 0)} />
          <CampoReais rotulo={ROTULOS_DO_SITE.freteGratisCentavos} centavos={form.freteGratisCentavos} ler={lerMoeda}
            aria-describedby={idAindaNao} aoMudar={(centavos) => editar('freteGratisCentavos', centavos)} />
        </fieldset>
        <Erros erros={erros} rotulos={ROTULOS_DO_SITE} />
        <div className={css.acoes}>
          <Botao variante="primario" tipo="submit" className={css.toque} disabled={!pendente || salvando || enviandoLogo}>Salvar o site</Botao>
          <Botao variante="texto" className={css.toque} disabled={!pendente || salvando} onClick={desfazer}>Desfazer edição</Botao>
          {enviandoLogo && pendente && <output className={css.retorno}>Aguarde o logo terminar de subir para salvar.</output>}
          <Retorno situacao={situacao} />
        </div>
      </form>
    </div>
  )
}

function LogoDoSite({ gravado, loja, totalDeLojas, logoDaLoja, lerGravado, bloqueado, aoMudarEnvio, aoSalvar }) {
  const id = useId()
  const arquivoRef = useRef(null)
  const [situacao, setSituacao] = useState({ estado: 'ocioso', mensagem: null })
  const enviando = situacao.estado === 'enviando' || situacao.estado === 'alinhando'

  // Um envio ou alinhamento por vez, e nunca junto com o Salvar do site.
  async function gravarLogo(estado, chamada, salvo) {
    setSituacao({ estado, mensagem: null })
    aoMudarEnvio(true)
    try {
      aoSalvar(await chamada())
      setSituacao({ estado: 'salvo', mensagem: salvo })
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
    } finally {
      aoMudarEnvio(false)
    }
  }

  function escolher(evento) {
    const arquivo = evento.target.files?.[0]
    // O mesmo arquivo pode ser escolhido de novo depois de um erro.
    evento.target.value = ''
    if (!arquivo || enviando || bloqueado) return undefined
    const motivo = motivoDoArquivoDeLogo(arquivo)
    if (motivo) { setSituacao({ estado: 'erro', mensagem: motivo }); return undefined }
    return gravarLogo('enviando', () => enviarLogoDoSite(loja, lerGravado, arquivo), 'Logo salvo nos impressos e no site.')
  }

  function usarNoSite() {
    if (enviando || bloqueado) return undefined
    return gravarLogo('alinhando', async () => {
      const configuracao = await usarLogoDaLojaNoSite(logoDaLoja, lerGravado)
      if (!configuracao) throw new Error('O EasyStok não confirmou o site. Tente de novo.')
      return { configuracao, logoDaLoja }
    }, 'O site agora usa o mesmo logo dos impressos.')
  }

  const imagem = urlDoLogo(logoDosImpressos(logoDaLoja, gravado.logoUrl))
  const outroNoSite = siteComOutroLogo(logoDaLoja, gravado.logoUrl)
  return (
    <section className={css.secao} aria-labelledby={`${id}-logo`}>
      <h3 id={`${id}-logo`}>Logo</h3>
      <p className={css.descricao}>PNG ou JPG até 5 MB. O mesmo logo vai para os impressos e para o site. Na térmica só sai em preto.</p>
      {totalDeLojas > 1 && loja && (
        <p className={css.aviso}>Esta empresa tem {totalDeLojas} lojas ativas: o logo vai para {loja.nome || 'a primeira'}.</p>
      )}
      <div className={css.logo}>
        {imagem
          ? <img className={css.previa} src={imagem} alt={`Logo de ${gravado.tituloPublico || loja?.nome || 'a loja'}`} />
          : <p className={css.semPrevia}>O logo aparece aqui depois do envio.</p>}
        <div className={css.acoes}>
          <input ref={arquivoRef} type="file" accept={TIPOS_DE_LOGO.join(',')} className="sr" aria-label="Arquivo do logo" tabIndex={-1} onChange={escolher} />
          <Botao icone="imagem" className={css.toque} disabled={enviando || bloqueado} onClick={() => arquivoRef.current?.click()}>
            {situacao.estado === 'enviando' ? 'Enviando…' : imagem ? 'Trocar logo' : 'Enviar logo'}
          </Botao>
          {outroNoSite && (
            <Botao className={css.toque} disabled={enviando || bloqueado} onClick={usarNoSite}>
              {situacao.estado === 'alinhando' ? 'Gravando no site…' : 'Usar este logo no site'}
            </Botao>
          )}
        </div>
      </div>
      {outroNoSite && <p className={css.aviso}>O site ainda não usa este logo. Os impressos já saem com ele.</p>}
      {situacao.estado === 'salvo' && <output className={css.retorno}>{situacao.mensagem}</output>}
      {situacao.estado === 'erro' && <p className={css.erro} role="alert">{situacao.mensagem}</p>}
    </section>
  )
}
