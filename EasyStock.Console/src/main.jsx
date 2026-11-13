import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './estilos/base.css'
import { App } from './app/App'
import { AtualizacaoDoConsole } from './app/AtualizacaoDoConsole'
import { FaixaDeFalha } from './app/FaixaDeFalha'
import { LimiteDeErro } from './componentes/LimiteDeErro'
import { ProvedorDeAviso } from './componentes/Aviso'
import { tokensDaGeometria } from './dominio/paineis'
import { instalarFalhaGlobal } from './infra/falhaGlobal'

// #1872: a geometria que o JS usa (o trilho) vira token CSS aqui, antes do primeiro desenho.
for (const [nome, valor] of Object.entries(tokensDaGeometria())) document.documentElement.style.setProperty(nome, valor)

// #2393: promessa solta e erro de handler não derrubam a tela, mas viram uma faixa discreta.
instalarFalhaGlobal(window)

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <FaixaDeFalha />
    <AtualizacaoDoConsole />
    {/* #2393: exceção em qualquer parte da raiz mostra "Recarregar" em vez de tela branca. */}
    <LimiteDeErro parte="Console" recarregarPagina>
      {/* #2146: o Aviso (snackbar) vale para todas as rotas, então mora na raiz. */}
      <ProvedorDeAviso>
        <App />
      </ProvedorDeAviso>
    </LimiteDeErro>
  </StrictMode>,
)
