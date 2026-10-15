// Servidor estatico minimo para preview do prototipo (sem deps).
// Aceita --port/-p e --host repassados pelo runner (ex.: npm run dev -- --port 7100).
const http = require('http');
const fs = require('fs');
const path = require('path');

function argValue(names, fallback) {
  for (let i = 2; i < process.argv.length; i++) {
    if (names.includes(process.argv[i]) && process.argv[i + 1]) return process.argv[i + 1];
    const eq = names.find(n => process.argv[i].startsWith(n + '='));
    if (eq) return process.argv[i].split('=')[1];
  }
  return fallback;
}

const port = Number(argValue(['--port', '-p', '--PORT'.toLowerCase()], process.env.PORT || 7100));
const host = argValue(['--host', '-h'], process.env.HOST || '127.0.0.1');
const root = __dirname;

const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json' };

http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p === '/') p = '/index.html';
  const file = path.join(root, path.normalize(p).replace(/^([/\\])+/, ''));
  if (!file.startsWith(root)) { res.writeHead(403); return res.end(); }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); return res.end('not found'); }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(port, host, () => console.log(`preview: http://${host}:${port}/`));
