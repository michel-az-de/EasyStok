// Copia os woff2 variaveis (latin + latin-ext) do @fontsource-variable para
// wwwroot/fonts. Rode: `npm run fonts:copy`. Os arquivos ficam COMMITADOS como
// assets-fonte (self-host; ver wwwroot/css/fonts.css) — o build/CI NAO regenera.
// Motivo do self-host: o CDN do Google Fonts corre o mesmo risco de firewall
// corporativo que ja derrubou o Alpine via jsDelivr. issue 837.
const fs = require('fs');
const path = require('path');

const src = path.join(__dirname, '..', 'node_modules', '@fontsource-variable');
const dst = path.join(__dirname, '..', 'wwwroot', 'fonts');

const files = [
  ['inter', 'inter-latin-wght-normal.woff2'],
  ['inter', 'inter-latin-ext-wght-normal.woff2'],
  ['fraunces', 'fraunces-latin-wght-normal.woff2'],
  ['fraunces', 'fraunces-latin-ext-wght-normal.woff2'],
  ['jetbrains-mono', 'jetbrains-mono-latin-wght-normal.woff2'],
  ['jetbrains-mono', 'jetbrains-mono-latin-ext-wght-normal.woff2'],
];

fs.mkdirSync(dst, { recursive: true });
for (const [fam, name] of files) {
  fs.copyFileSync(path.join(src, fam, 'files', name), path.join(dst, name));
  console.log('copiada', name);
}
console.log('OK: ' + files.length + ' fontes em wwwroot/fonts');
