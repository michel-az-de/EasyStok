'use strict';
const assert = require('node:assert/strict');
const { createRequire } = require('node:module');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

// Carrega a fachada JSX e suas dependências reais. Não mantém uma segunda
// tabela de desenhos esperados nem precisa gerar/substituir o dist do Console.
async function loadConsoleIcons() {
  const root = path.resolve(__dirname, '../../../../EasyStock.Console');
  const fromConsole = createRequire(path.join(root, 'package.json'));
  const { createServer } = await import(pathToFileURL(fromConsole.resolve('vite')).href);
  const { createElement } = fromConsole('react');
  const { renderToStaticMarkup } = fromConsole('react-dom/server');
  const server = await createServer({
    root, configFile: false, logLevel: 'silent', appType: 'custom',
    server: { middlewareMode: true, hmr: false, watch: null },
    optimizeDeps: { noDiscovery: true, include: [] },
    oxc: { jsx: { runtime: 'automatic' } },
  });
  try {
    const { Icone } = await server.ssrLoadModule('/src/componentes/Icone.jsx');
    const { MarcaIcone } = await server.ssrLoadModule('/src/componentes/MarcaIcone.jsx');
    const render = (Component, name) => {
      const svg = renderToStaticMarkup(createElement(Component, { nome: name, tamanho: 24 }));
      assert.match(svg, /^<svg\b/, 'Ícone canônico ausente: ' + name);
      return svg;
    };
    return {
      svg: (name) => render(Icone, name),
      brandSvg: (name) => render(MarcaIcone, name),
      close: () => server.close(),
    };
  } catch (error) { await server.close(); throw error; }
}

// Compara toda a geometria, inclusive círculos/retângulos/linhas do Lucide.
// Ordem de atributos e a grafia HTML de fechamento não mudam o desenho.
function geometry(svg) {
  const shapes = [...svg.matchAll(/<(path|circle|rect|line|polyline|polygon|ellipse)\b([^>]*?)(?:\/?>)/g)]
    .map(([, tag, attributes]) => ({ tag, attributes: Object.fromEntries(
      [...attributes.matchAll(/([\w:-]+)="([^"]*)"/g)].map(([, name, value]) => [name, value]),
    ) }));
  assert.ok(shapes.length, 'SVG sem geometria');
  return shapes;
}

module.exports = { loadConsoleIcons, geometry };
