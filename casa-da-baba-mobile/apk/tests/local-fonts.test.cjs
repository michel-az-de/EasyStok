'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const fonts = path.join(root, 'assets/fonts');
// Originais baixados sem conversão de https://raw.githubusercontent.com/google/fonts/main/ofl/:
// manrope/Manrope%5Bwght%5D.ttf; fraunces/Fraunces%5BSOFT,WONK,opsz,wght%5D.ttf;
// fraunces/Fraunces-Italic%5BSOFT,WONK,opsz,wght%5D.ttf.
// As duas licenças são os OFL.txt dos mesmos diretórios, com nome da família no pacote.
const files = {
  'Manrope.ttf': '3ae11c49db0455a3cc33e37d380f20fdb8c7f8b41dc07625c177e3d87a9d6ae6',
  'Fraunces.ttf': '177ff6c0f14e5550a3c624247cd1189611d4eb65d000b14944c63d967958abbb',
  'Fraunces-Italic.ttf': 'b24448c43702fac4ee856781d461a0dfba8d8e594b6e8e190234b75fed2c0e01',
};
const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function table(font, tag) {
  assert.equal(font.readUInt32BE(0), 0x00010000, 'arquivo precisa ser TrueType');
  for (let i = 0; i < font.readUInt16BE(4); i++) {
    const offset = 12 + i * 16;
    if (font.toString('ascii', offset, offset + 4) === tag) {
      const start = font.readUInt32BE(offset + 8);
      return font.subarray(start, start + font.readUInt32BE(offset + 12));
    }
  }
  assert.fail('Tabela da fonte ausente: ' + tag);
}
test('fontes oficiais mantêm bytes, eixos ópticos, pesos e itálico originais', () => {
  for (const [name, hash] of Object.entries(files)) {
    const font = fs.readFileSync(path.join(fonts, name));
    assert.equal(digest(font), hash, name);
    const fvar = table(font, 'fvar'), axes = {};
    for (let i = 0; i < fvar.readUInt16BE(8); i++) {
      const offset = fvar.readUInt16BE(4) + i * fvar.readUInt16BE(10);
      axes[fvar.toString('ascii', offset, offset + 4)] = [fvar.readInt32BE(offset + 4) / 65536, fvar.readInt32BE(offset + 12) / 65536];
    }
    if (name === 'Manrope.ttf') assert.deepEqual(axes, { wght: [200, 800] });
    else assert.deepEqual(axes, { opsz: [9, 144], wght: [100, 900], SOFT: [0, 100], WONK: [0, 1] });
    assert.equal(Boolean(table(font, 'head').readUInt16BE(44) & 2), name.includes('Italic'));
  }
});
test('cada família inclui licença OFL e CSS local preserva estilo e faixa de peso', () => {
  for (const family of ['Fraunces', 'Manrope']) {
    const license = fs.readFileSync(path.join(fonts, family + '-OFL.txt'), 'utf8');
    assert.match(license, new RegExp('Copyright.*' + family));
    assert.match(license, /SIL OPEN FONT LICENSE Version 1\.1/);
  }
  const css = fs.readFileSync(path.join(fonts, 'fonts.css'), 'utf8');
  assert.equal((css.match(/@font-face/g) || []).length, 3);
  assert.match(css, /font-family: 'Manrope';\s*font-style: normal;\s*font-weight: 200 800;/);
  for (const style of ['normal', 'italic']) assert.match(css, new RegExp("font-family: 'Fraunces';\\s*font-style: " + style + ';\\s*font-weight: 100 900;'));
  assert.equal((css.match(/font-display: swap;/g) || []).length, 3);
  assert.doesNotMatch(css, /https?:\/\//);
});
test('empacotador real copia fontes/licenças sem rede e preserva PWA e estilos fonte', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-local-fonts-'));
  assert.equal(path.dirname(path.resolve(tmp)), path.resolve(os.tmpdir()));
  try {
    const fixtureRoot = path.join(tmp, 'casa-da-baba-mobile/apk');
    const fixturePwa = path.join(tmp, 'EasyStock.Api/wwwroot/pwa/index.html');
    const originalPwa = fs.readFileSync(path.resolve(root, '../../EasyStock.Api/wwwroot/pwa/index.html'));
    const originalCss = fs.readFileSync(path.join(root, 'pilot.css'));
    const copy = (from, to) => { fs.mkdirSync(path.dirname(to), { recursive: true }); fs.copyFileSync(from, to); };
    for (const name of ['package.json', 'pilot.css', 'pilot.js', 'pilot-ui.js', 'pilot-settings.js', 'scripts/copy-web.cjs', 'scripts/app-markup.cjs', 'scripts/ota-contract.cjs']) copy(path.join(root, name), path.join(fixtureRoot, name));
    fs.mkdirSync(path.dirname(fixturePwa), { recursive: true }); fs.writeFileSync(fixturePwa, originalPwa);
    copy(path.resolve(root, '../../EasyStock.Console/src/assets/casa-da-baba.png'), path.join(tmp, 'EasyStock.Console/src/assets/casa-da-baba.png'));
    fs.cpSync(fonts, path.join(fixtureRoot, 'assets/fonts'), { recursive: true });
    fs.cpSync(path.join(root, 'assets/guia'), path.join(fixtureRoot, 'assets/guia'), { recursive: true });
    execFileSync(process.execPath, [path.join(fixtureRoot, 'scripts/copy-web.cjs')], { encoding: 'utf8' });
    const web = path.join(fixtureRoot, 'web');
    const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
    assert.doesNotMatch(html, /https:\/\/fonts\.(googleapis|gstatic)\.com/);
    assert.match(html, /href="fonts\/fonts\.css\?pilot=/);
    for (const name of fs.readdirSync(fonts)) assert.deepEqual(fs.readFileSync(path.join(web, 'fonts', name)), fs.readFileSync(path.join(fonts, name)), name);
    assert.deepEqual(fs.readFileSync(fixturePwa), originalPwa);
    assert.deepEqual(fs.readFileSync(path.join(web, 'pilot.css')), originalCss);
    // Falha antes de apagar a distribuição anterior se uma fonte obrigatória faltar.
    fs.unlinkSync(path.join(fixtureRoot, 'assets/fonts/Manrope.ttf'));
    assert.throws(() => execFileSync(process.execPath, [path.join(fixtureRoot, 'scripts/copy-web.cjs')], { stdio: 'pipe' }), /Fonte local obrigatória ausente/);
    assert.equal(fs.readFileSync(path.join(web, 'index.html'), 'utf8'), html);
  } finally { fs.rmSync(tmp, { recursive: true, force: true }); }
});
