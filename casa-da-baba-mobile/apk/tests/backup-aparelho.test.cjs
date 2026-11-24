'use strict';
// #3257 (P3 da #3114): o APK leva o backup automático e o alerta de dado só local do PWA.
const { lerPwaMontada } = require('../scripts/pwa-montagem.cjs');
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const raiz = path.resolve(__dirname, '..');
const pwa = path.resolve(raiz, '../../EasyStock.Api/wwwroot/pwa');

test('o APK registra o plugin que grava o backup fora do app', () => {
  const main = fs.readFileSync(path.join(raiz, 'android/app/src/main/java/com/casadababa/app/MainActivity.java'), 'utf8');
  assert.match(main, /registerPlugin\(PublicBackupPlugin\.class\);/);
});

test('o conteúdo do APK checa dado só local no health check e o banner aparece no piloto', () => {
  const app = lerPwaMontada(path.join(pwa, 'index.html'));
  assert.match(app, /avaliarDadoSoNoAparelho\(\)/, 'o health check do PWA montado avalia o dado só local');
  assert.match(fs.readFileSync(path.join(pwa, 'sync.js'), 'utf8'), /function dadoSoNoAparelho\(/);
  const css = fs.readFileSync(path.join(raiz, 'pilot.css'), 'utf8');
  assert.match(css, /body\.pilot-app \.health-banner\.warn \{/, 'o alerta tem estilo no piloto');
  assert.doesNotMatch(css, /\.health-banner[^{]*\{[^}]*display:\s*none/, 'o piloto não esconde o banner');
});
