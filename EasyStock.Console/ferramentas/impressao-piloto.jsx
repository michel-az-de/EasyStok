// Fixture de UI: dados de teste, sem impressora nem API de produção.
import React, { useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ImprimirPedido } from '../src/componentes/ImprimirPedido'
import { DOCUMENTOS, PAPEIS } from '../src/dominio/impressos'
import '../src/estilos/tokens.css'
import '../src/estilos/base.css'

function Prova() {
  const [lento, setLento] = useState(false)
  const [preparadas, setPreparadas] = useState(0)
  const [confirmadas, setConfirmadas] = useState(0)
  const [dados, setDados] = useState('Pedido A7F3C21B · 2 unidades de Bolo da Casa · cliente Mariana Souza')
  const [registros, setRegistros] = useState([])
  const [cenario, setCenario] = useState('normal')
  const falhou = useRef(false)
  const [ultimaTentativa, setUltimaTentativa] = useState(null)
  async function preparar(opcoes, sinal) {
    setUltimaTentativa({ ...opcoes })
    if (cenario === 'reimpressao' && opcoes.originalId && !falhou.current) {
      falhou.current = true
      throw new Error('Falha de leitura simulada. O original continua guardado.')
    }
    await new Promise((ok, nao) => {
      const prazo = setTimeout(ok, lento ? 2500 : 50)
      sinal.addEventListener('abort', () => { clearTimeout(prazo); nao(new DOMException('Cancelado', 'AbortError')) }, { once: true })
    })
    const original = registros.find((i) => i.id === opcoes.originalId)
    let htmlRenderizado = null
    if (!original && opcoes.modelo === 'a4' && opcoes.codigo === 'barras') {
      const resposta = await fetch('/ferramentas/pedido-a4-barras.generated.html', { signal: sinal })
      if (resposta.ok) htmlRenderizado = await resposta.text()
    }
    const registro = { ...opcoes, id: crypto.randomUUID(), versao: 'fixture-1', criadaEm: new Date().toISOString(), status: 'preparada',
      html: original?.html ?? htmlRenderizado ?? `<!doctype html><html lang="pt-BR"><head><title>Pedido de teste</title><style>@page{size:A4;margin:15mm}body{font-family:Georgia;padding:20px;color:#422814}</style></head><body><h1>Casa da Baba</h1><h2>${dados}</h2><p>Dados de teste para conferir a tela. Código selecionado: ${opcoes.codigo}.</p></body></html>` }
    setPreparadas((n) => n + 1)
    setRegistros((lista) => [registro, ...lista])
    return registro
  }
  return <main style={{ padding: 32 }}>
    <h1>Prova local do piloto</h1><p>Dados de teste e diálogo simulado, sem envio à impressora. A validação de servidor roda nos testes PostgreSQL e da API.</p>
    <label><input type="checkbox" checked={lento} onChange={(e) => setLento(e.target.checked)} /> Simular preparação lenta (2,5 s)</label>
    <p><label>Cenário de teste <select value={cenario} onChange={(e) => { setCenario(e.target.value); falhou.current = false }}>
      <option value="normal">Fluxo normal</option><option value="reimpressao">Falhar uma vez na reimpressão</option>
      <option value="historico">Falhar uma vez no histórico</option><option value="confirmacao">Falhar uma vez na confirmação</option>
    </select></label></p>
    <button onClick={() => setRegistros([{ id: 'original-58', documento: 'cozinha', modelo: 'cupom-58', codigo: 'barras', perfil: 'Escolher no diálogo do sistema', versao: 'fixture-original', criadaEm: new Date().toISOString(), status: 'impressa', html: '<!doctype html><html lang="pt-BR"><body><h1>Original guardado: 2 unidades</h1></body></html>' }])}>Adicionar original de cozinha 58 mm</button>
    <p><button onClick={() => setDados('Pedido alterado depois da impressão · 7 unidades')}>Alterar pedido de teste</button></p>
    <output>Preparadas: {preparadas} · Confirmadas: {confirmadas}</output><p>Dados atuais: {dados}</p>
    <p>Última tentativa: {JSON.stringify(ultimaTentativa)}</p>
    <ImprimirPedido contexto="#A7F3C21B" clienteNome="Mariana Souza" documentos={DOCUMENTOS} papeis={PAPEIS} papelPadrao={() => 'a4'} preparar={preparar}
      abrirImpressao={async () => 'dialogo'}
      historico={async () => {
        if (cenario === 'historico' && !falhou.current) { falhou.current = true; throw new Error('Histórico indisponível neste teste.') }
        return registros.map(({ html: _html, ...i }) => i)
      }}
      confirmar={async ({ id }) => {
        if (cenario === 'confirmacao' && !falhou.current) { falhou.current = true; throw new Error('Sem resposta do servidor neste teste.') }
        setConfirmadas((n) => n + 1); setRegistros((lista) => lista.map((i) => i.id === id ? { ...i, status: 'impressa' } : i))
      }} />
  </main>
}
createRoot(document.getElementById('root')).render(<Prova />)
