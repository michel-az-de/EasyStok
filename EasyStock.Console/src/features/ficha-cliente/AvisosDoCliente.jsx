// Canais de aviso da Ficha (decisão 46, "Encerrar" v2, pedido do dono 24/09): e-mail e
// SMS, cada um ligado ou não.
//
// Modo demonstração: grava em `cliente.avisos` pela ação genérica EDITAR_DADO_CLIENTE,
// como sempre foi. Modo API (F02, S38): lê e grava o consentimento do cadastro no
// EasyStok (`clienteId`); o erro aparece aqui do lado, e a caixa volta ao que a API tem.
import { useEffect, useState } from 'react'
import { useAcoes, useAtendimento } from '../../aplicacao/contextos'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { podeVerAvisosDoCliente } from '../../dominio/acessoModulos'
import css from './cliente.module.css'

const CANAIS = [
  { aviso: 'email', rotulo: 'E-mail' },
  { aviso: 'sms', rotulo: 'SMS' },
]

function Caixas({ avisos, aoMudar, desabilitado }) {
  return CANAIS.map(({ aviso, rotulo }) => (
    <label key={aviso} className={css.avisoCanal}>
      <input
        type="checkbox"
        checked={Boolean(avisos?.[aviso])}
        disabled={desabilitado}
        onChange={(e) => aoMudar(aviso, e.target.checked)}
      />
      {rotulo}
    </label>
  ))
}

function AvisosDemonstracao({ conversa }) {
  const { agora } = useAtendimento()
  const { editarDadoCliente } = useAcoes()
  const avisos = conversa.cliente.avisos
  return (
    <Caixas
      avisos={avisos}
      aoMudar={(aviso, ligado) => editarDadoCliente(conversa.id, 'avisos', { ...avisos, [aviso]: ligado }, agora)}
    />
  )
}

function AvisosApi({ clienteId }) {
  const { carregarAvisos, definirAvisoCliente } = useAcoes()
  const [avisos, setAvisos] = useState(null)
  const [erro, setErro] = useState(null)
  const [gravando, setGravando] = useState(false)
  // #1761: falha de carga ganha "Tentar de novo"; a tentativa refaz a leitura.
  const [naoCarregou, setNaoCarregou] = useState(false)
  const [tentativa, setTentativa] = useState(0)

  // `key={clienteId}` remonta este componente ao trocar de cliente: nasce vazio e carrega.
  useEffect(() => {
    let vivo = true
    carregarAvisos(clienteId)
      .then((lidos) => { if (vivo) { setAvisos(lidos); setErro(null); setNaoCarregou(false) } })
      .catch((e) => { if (vivo) { setErro(`Avisos não carregaram: ${e.message}`); setNaoCarregou(true) } })
    return () => { vivo = false }
  }, [clienteId, carregarAvisos, tentativa])

  async function mudar(aviso, ligado) {
    const antes = avisos
    setAvisos({ ...avisos, [aviso]: ligado })
    setErro(null)
    setGravando(true)
    try {
      setAvisos(await definirAvisoCliente(clienteId, aviso, ligado))
    } catch (e) {
      setAvisos(antes)
      setErro(`Não gravou: ${e.message}`)
    } finally {
      setGravando(false)
    }
  }

  return (
    <>
      <Caixas avisos={avisos} aoMudar={mudar} desabilitado={!avisos || gravando} />
      {erro && <output className={css.avisoErro} role="alert">{erro}</output>}
      {naoCarregou && (
        <button type="button" className={css.tentarDeNovo} onClick={() => { setErro(null); setNaoCarregou(false); setTentativa((t) => t + 1) }}>
          Tentar de novo
        </button>
      )}
    </>
  )
}

export function AvisosDoCliente({ conversa }) {
  const { fonteApi } = useAtendimento()
  if (!fonteApi) return <AvisosDemonstracao conversa={conversa} />
  if (!conversa.clienteId) {
    return <span className={css.avisoCanal}>Cadastre o cliente para registrar avisos.</span>
  }
  return <AvisosApiDoPerfil clienteId={conversa.clienteId} />
}

// #1761: a rota é só do administrador. Para os outros perfis a Ficha diz isso sem pedir a leitura
// (voltava 403 a cada conversa aberta) e sem caixas que não respondem.
function AvisosApiDoPerfil({ clienteId }) {
  const { acoes } = useAcessoModulos()
  if (!podeVerAvisosDoCliente(acoes)) {
    return <span className={css.avisoCanal}>Só o administrador da loja vê e muda os avisos do cliente.</span>
  }
  return <AvisosApi key={clienteId} clienteId={clienteId} />
}
