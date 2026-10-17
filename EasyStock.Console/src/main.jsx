import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './estilos/base.css'
import { App } from './app/App'
import { AtualizacaoDoConsole } from './app/AtualizacaoDoConsole'
import { tokensDaGeometria } from './dominio/paineis'

// #1872: a geometria que o JS usa (o trilho) vira token CSS aqui, antes do primeiro desenho.
for (const [nome, valor] of Object.entries(tokensDaGeometria())) document.documentElement.style.setProperty(nome, valor)

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <AtualizacaoDoConsole />
    <App />
  </StrictMode>,
)
