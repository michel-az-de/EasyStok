// Fixture de UI: dados de teste, sem impressora nem API de produção.
import React, { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ImprimirPedido } from '../src/componentes/ImprimirPedido'
import { DOCUMENTOS, PAPEIS } from '../src/dominio/impressos'
import '../src/estilos/tokens.css'
import '../src/estilos/base.css'

function Prova() {
  const [lento, setLento] = useState(false)
  const [preparadas, setPreparadas] = useState(0)
  const [confirmadas, setConfirmadas] = useState(0)
  const [dados, setDados] = useState('Pedido 0412 · 2 unidades de Bolo da Casa · cliente Mariana')
  const [registros, setRegistros] = useState([])
  async function preparar(opcoes, sinal) {
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
    <p><button onClick={() => setDados('Pedido alterado depois da impressão · 7 unidades')}>Alterar pedido de teste</button></p>
    <output>Preparadas: {preparadas} · Confirmadas: {confirmadas}</output><p>Dados atuais: {dados}</p>
    <ImprimirPedido documentos={DOCUMENTOS} papeis={PAPEIS} papelPadrao={() => 'a4'} preparar={preparar}
      abrirImpressao={async () => 'dialogo'}
      historico={async () => registros.map(({ html: _html, ...i }) => i)}
      confirmar={async ({ id }) => { setConfirmadas((n) => n + 1); setRegistros((lista) => lista.map((i) => i.id === id ? { ...i, status: 'impressa' } : i)) }} />
  </main>
}
createRoot(document.getElementById('root')).render(<Prova />)
