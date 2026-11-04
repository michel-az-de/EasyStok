'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const css = fs.readFileSync(path.join(__dirname, '..', 'pilot.css'), 'utf8');
const rule = selector => {
  const start = css.indexOf(selector + ' {');
  assert.ok(start >= 0, selector + ' ausente');
  return css.slice(start, css.indexOf('}', start));
};

test('alvo de toque do APK segue os 48dp do Android em todos os controles', () => {
  assert.match(css, /--pilot-control:\s*48px/);
  const small = [...css.matchAll(/(?:min-)?(?:height|width):\s*(4[0-7])px/g)].map(match => match[0]);
  assert.deepEqual(small, [], 'controles abaixo de 48px');
});

test('comando padronizado não rebaixa as ações principais de 52px e 16px', () => {
  const command = rule('body.pilot-app .pilot-command');
  assert.doesNotMatch(command, /min-height:[^;]*!important/);
  assert.doesNotMatch(command, /font-size:[^;]*!important/);
  assert.match(rule("body.pilot-app .phone .k-card .k-primary-actions .k-advance"), /min-height:\s*52px/);
});
