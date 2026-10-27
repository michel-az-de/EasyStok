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

test('feedback nativo reutiliza marca oficial e progresso indeterminado, sem janela sobre o Android', () => {
  const layout = read('layout/startup_loading.xml');
  assert.match(layout, /android:background="@drawable\/casa_da_baba_splash"/);
  assert.match(layout, /<ProgressBar[\s\S]*?android:indeterminate="true"/);
  assert.match(layout, /android:text="@string\/startup_loading"/);
  assert.match(layout, /android:accessibilityLiveRegion="polite"/);
  assert.match(read('values/startup.xml'), /Abrindo Casa da Baba/);
  const activity = fs.readFileSync(path.join(root, 'android/app/src/main/java/com/casadababa/app/MainActivity.java'), 'utf8');
  assert.match(activity, /content\.addView\(startupOverlay\)/);
  assert.doesNotMatch(activity, /TYPE_APPLICATION_OVERLAY|SYSTEM_ALERT_WINDOW|setWebViewClient|evaluateJavascript/);
});

test('overlay acompanha API real do Capacitor, sai na primeira pintura, erro ou limite e limpa lifecycle', () => {
  const activity = fs.readFileSync(path.join(root, 'android/app/src/main/java/com/casadababa/app/MainActivity.java'), 'utf8');
  const capacitor = path.join(root, 'node_modules/@capacitor/android/capacitor/src/main/java/com/getcapacitor');
  const listener = fs.readFileSync(path.join(capacitor, 'WebViewListener.java'), 'utf8');
  assert.match(fs.readFileSync(path.join(capacitor, 'Bridge.java'), 'utf8'), /public Builder addWebViewListener\(WebViewListener/);
  for (const event of ['onPageLoaded', 'onReceivedError', 'onReceivedHttpError']) {
    assert.ok(listener.includes('public void ' + event + '(WebView'));
    assert.ok(activity.includes('public void ' + event + '(WebView'));
  }
  const load = activity.slice(activity.indexOf('protected void load()'), activity.indexOf('private void showStartupOverlay()'));
  assert.ok(load.indexOf('showStartupOverlay();') < load.indexOf('OtaManager.get(this).beginBoot(recreatedActivity)'));
  assert.ok(load.indexOf('bridgeBuilder.addWebViewListener(') < load.indexOf('super.load();'));
  assert.match(load, /postVisualStateCallback\(0, new WebView\.VisualStateCallback/);
  assert.match(load, /onComplete\(long requestId\) \{ hideStartupOverlay\(\); \}/);
  assert.match(activity, /postDelayed\(startupDeadline, 15000\)/);
  assert.match(activity, /startupDeadline = this::hideStartupOverlay/);
  assert.match(activity, /removeCallbacks\(startupDeadline\)/);
  assert.match(activity, /removeView\(startupOverlay\)/);
  assert.match(activity, /onDestroy\(\) \{\s*hideStartupOverlay\(\);\s*super\.onDestroy\(\);/);
});
