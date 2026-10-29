import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './estilos/base.css'
import { App } from './app/App'
import { AtualizacaoDoConsole } from './app/AtualizacaoDoConsole'
import { ProvedorDeAviso } from './componentes/Aviso'
import { tokensDaGeometria } from './dominio/paineis'

// #1872: a geometria que o JS usa (o trilho) vira token CSS aqui, antes do primeiro desenho.
for (const [nome, valor] of Object.entries(tokensDaGeometria())) document.documentElement.style.setProperty(nome, valor)

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <AtualizacaoDoConsole />
    {/* #2146: o Aviso (snackbar) vale para todas as rotas, então mora na raiz. */}
    <ProvedorDeAviso>
      <App />
    </ProvedorDeAviso>
  </StrictMode>,
)
