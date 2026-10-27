'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const root = path.resolve(__dirname, '..');
const res = path.join(root, 'android/app/src/main/res');
const read = relative => fs.readFileSync(path.join(res, relative), 'utf8');
const original = fs.readFileSync(path.resolve(root, '../../EasyStock.Console/src/assets/casa-da-baba.png'));

test('marca Android é cópia exata do PNG oficial do Console', () => {
  const bundled = fs.readFileSync(path.join(res, 'drawable-nodpi/casa_da_baba_logo.png'));
  const sha = bytes => createHash('sha256').update(bytes).digest('hex');
  assert.equal(sha(bundled), sha(original));
  assert.equal(bundled.readUInt32BE(16), 420);
  assert.equal(bundled.readUInt32BE(20), 280);
});

test('launcher e roundIcon têm marca nos caminhos legados e adaptativos', () => {
  const manifest = fs.readFileSync(path.join(root, 'android/app/src/main/AndroidManifest.xml'), 'utf8');
  for (const icon of ['ic_launcher', 'ic_launcher_round']) {
    assert.ok(manifest.includes(`@mipmap/${icon}`));
    const legacy = read(`mipmap-anydpi/${icon}.xml`);
    const adaptive = read(`mipmap-anydpi-v26/${icon}.xml`);
    assert.match(legacy, /<layer-list/);
    assert.match(legacy, /@drawable\/casa_da_baba_(foreground|logo)/);
    assert.match(adaptive, /<adaptive-icon/);
    assert.match(adaptive, /<foreground android:drawable="@drawable\/casa_da_baba_foreground"/);
    assert.ok(![legacy, adaptive].some(xml => /@mipmap\/ic_launcher_foreground|@drawable\/ic_launcher_foreground/.test(xml)), 'template Capacitor não pode continuar conectado ao launcher');
  }
});

test('composições preservam proporção da marca e fundo circular legado', () => {
  const ratio = original.readUInt32BE(16) / original.readUInt32BE(20);
  for (const file of ['drawable/casa_da_baba_foreground.xml', 'drawable/casa_da_baba_splash.xml', 'mipmap-anydpi/ic_launcher_round.xml']) {
    const xml = read(file);
    const item = xml.match(/<item android:width="([\d.]+)dp" android:height="([\d.]+)dp" android:gravity="center">\s*<bitmap android:src="@drawable\/casa_da_baba_logo"/);
    assert.ok(item, file + ': marca precisa ficar centralizada');
    assert.ok(Math.abs(Number(item[1]) / Number(item[2]) - ratio) < 0.0001, file + ': não distorcer a marca');
    if (file.includes('_round')) {
      assert.match(xml, /android:shape="oval"/);
      const diameter = Number(xml.match(/<size android:width="([\d.]+)dp"/)[1]);
      assert.ok(Math.hypot(Number(item[1]), Number(item[2])) <= diameter, 'cantos opacos do PNG precisam caber no fundo circular');
    }
  }
});

test('tema de abertura usa a marca e deixa de referenciar splash Capacitor', () => {
  const styles = read('values/styles.xml');
  const launch = styles.match(/<style name="AppTheme.NoActionBarLaunch"[\s\S]*?<\/style>/)[0];
  assert.match(launch, /name="android:background">@drawable\/casa_da_baba_splash</);
  assert.match(launch, /name="windowSplashScreenAnimatedIcon">@mipmap\/ic_launcher</);
  assert.match(launch, /name="windowSplashScreenBackground">@color\/ic_launcher_background</);
  assert.ok(!launch.includes('@drawable/splash<'));
});
