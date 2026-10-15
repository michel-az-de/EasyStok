import { useState } from 'react'
import { useIntegracoesApi } from '../../../aplicacao/useIntegracoesApi'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Pilula } from '../../../componentes/Pilula'
import { ORIGEM, rotuloDoCampo, textoDoUltimoTeste } from '../../../dominio/chavesIntegracao'
import css from './AbaIntegracoes.module.css'

// Aba Integrações no modo API (F16, #1246): uma por integração do EasyStok. O segredo só se
// escreve: some do campo ao salvar e nunca volta da API (só a máscara dos últimos 4). O que é da
// FMA (WhatsApp inteiro, segredo do webhook do Mercado Pago) aparece só como leitura.
const AMBIENTES = [{ valor: 'producao', rotulo: 'Produção' }, { valor: 'sandbox', rotulo: 'Sandbox (teste)' }]

function vazio(campos) {
  return Object.fromEntries((campos ?? []).map((c) => [c, '']))
}

function CartaoIntegracao({ item, ocupado, acoes }) {
  const [editando, setEditando] = useState(false)
  const [valores, setValores] = useState(() => vazio(item.campos))
  const [ambiente, setAmbiente] = useState(item.ambiente ?? 'producao')
  const [resultado, setResultado] = useState(null)
  const preenchido = (item.campos ?? []).every((c) => valores[c]?.trim())

  const fechar = () => { setValores(vazio(item.campos)); setEditando(false) }
  const rodar = async (acao) => {
    setResultado(null)
    try {
      const r = await acao()
      if (r && typeof r.ok === 'boolean') setResultado({ ok: r.ok, texto: r.mensagem })
    } catch (e) {
      setResultado({ ok: false, texto: e.message })
    }
  }
  const salvar = () => rodar(async () => {
    await acoes.salvar(item.provider, valores, item.escolheAmbiente ? ambiente : null)
    fechar()
    return acoes.testar(item.provider)
  })

  return (
    <li className={css.cartao}>
      <div className={css.cabecalho}>
        <strong className={css.nomeProvedor}>{item.nome}</strong>
        <Pilula tom={item.ativo ? 'ok' : 'neutro'} fina>{item.ativo ? 'Ligado' : 'Desligado'}</Pilula>
      </div>
      {item.alerta === 'parada' && <Pilula tom="perigo" fina>Parada: o último teste falhou</Pilula>}
      {item.alerta === 'vencendo' && <Pilula tom="aviso" fina>A chave vence em menos de 7 dias</Pilula>}

      <div className={css.credencial}>
        <p className={css.motivo}>
          {item.temCredencial
            ? `${ORIGEM[item.origem] ?? 'Chave'}${item.mascara ? ` terminada em ${item.mascara}` : ''}${item.ambiente === 'sandbox' ? ' (sandbox)' : ''}`
            : 'Sem chave'}
          {item.numero ? ` · número ${item.numero}` : ''}
        </p>
        <p className={css.motivo}>{textoDoUltimoTeste(item)}</p>
        {resultado && (
          <p className={css.motivo} role="status">{resultado.ok ? 'Teste ok' : 'Teste falhou'}: {resultado.texto}</p>
        )}

        {item.lojaGrava && !editando && (
          <Botao variante="texto" onClick={() => setEditando(true)}>
            {item.origem === 'loja' ? 'Trocar a chave da loja' : 'Cadastrar a chave da loja'}
          </Botao>
        )}
        {editando && (
          <div className={css.formularioCredencial}>
            {(item.campos ?? []).map((campo) => (
              <CampoTexto
                key={campo}
                rotulo={`${rotuloDoCampo(campo)} da ${item.nome}`}
                tipo="password"
                autoComplete="off"
                value={valores[campo]}
                onChange={(e) => setValores((v) => ({ ...v, [campo]: e.target.value }))}
              />
            ))}
            {item.escolheAmbiente && (
              <CampoSelecao rotulo="Ambiente" opcoes={AMBIENTES} value={ambiente} onChange={(e) => setAmbiente(e.target.value)} />
            )}
            <div className={css.botoesCredencial}>
              <Botao variante="primario" onClick={salvar} disabled={!preenchido || ocupado === item.provider}>Salvar e testar</Botao>
              <Botao variante="texto" onClick={fechar}>Cancelar</Botao>
            </div>
          </div>
        )}
      </div>

      <div className={css.rodapeCartao}>
        {item.temCredencial && (
          <Botao onClick={() => rodar(() => acoes.testar(item.provider))} disabled={ocupado === item.provider}>
            {ocupado === item.provider ? 'Testando…' : 'Testar'}
          </Botao>
        )}
        {item.origem === 'loja' && (
          <Botao variante="texto" onClick={() => rodar(() => acoes.desativar(item.provider))} disabled={ocupado === item.provider}>
            Desativar a chave da loja
          </Botao>
        )}
        {item.geridoPelaFma && <p className={css.motivo}>Gerido pela FMA: {item.geridoPelaFma}</p>}
      </div>
    </li>
  )
}

export function AbaIntegracoesApi() {
  const { lista, erro, ocupado, ...acoes } = useIntegracoesApi()

  if (erro && !lista) return <div className={css.aba}><p role="alert">Não deu para ler as integrações: {erro}</p></div>
  if (!lista) return <div className={css.aba}><p>Carregando integrações…</p></div>

  return (
    <div className={css.aba}>
      <p className={css.intro}>
        Chaves de cada integração da loja, guardadas cifradas no EasyStok. Testar não cobra nem faz pedido.
        A cada 15 minutos o EasyStok testa sozinho e avisa no topo se alguma parar.
      </p>
      <ul className={css.lista}>
        {lista.map((item) => <CartaoIntegracao key={item.provider} item={item} ocupado={ocupado} acoes={acoes} />)}
        <li className={css.cartao}>
          <div className={css.cabecalho}>
            <strong className={css.nomeProvedor}>Entregador próprio</strong>
            <Pilula tom="ok" fina>Ligado</Pilula>
          </div>
          <Pilula tom="ok" fina>Padrão do despacho</Pilula>
          <p className={css.motivo}>Única forma de despacho até a Lalamove entrar no despacho (F17).</p>
        </li>
      </ul>
    </div>
  )
}
