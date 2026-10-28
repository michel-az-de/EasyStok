package com.casadababa.app;

import android.os.Bundle;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebView;
import androidx.activity.OnBackPressedCallback;

import com.casadababa.app.printer.ThermalPrinterPlugin;
import com.casadababa.app.backup.PublicBackupPlugin;
import com.casadababa.app.updater.OtaManager;
import com.casadababa.app.updater.PilotUpdaterPlugin;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeActivity;
import com.getcapacitor.ServerPath;
import com.getcapacitor.WebViewListener;

public class MainActivity extends BridgeActivity {
    private boolean recreatedActivity;
    private boolean noticeOpened;
    private final Handler startupHandler = new Handler(Looper.getMainLooper());
    private final Runnable startupDeadline = this::hideStartupOverlay;
    private View startupOverlay;
    private boolean backPending;
    private int backRequest;
    private final Runnable backDeadline = () -> finishBack(backRequest, false);

    @Override
    public void onCreate(Bundle savedInstanceState) {
        recreatedActivity = savedInstanceState != null;
        registerPlugin(PilotAppPlugin.class);
        registerPlugin(ThermalPrinterPlugin.class);
        registerPlugin(PublicBackupPlugin.class);
        registerPlugin(PilotUpdaterPlugin.class);
        super.onCreate(savedInstanceState);
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override public void handleOnBackPressed() { requestAppBack(); }
        });
        AppNotifications.initialize(this);
    }

    private void requestAppBack() {
        // IME e dialogs nativos recebem Back antes do dispatcher da Activity.
        if (backPending) return;
        backPending = true;
        final int request = ++backRequest;
        startupHandler.postDelayed(backDeadline, 1500);
        try {
            WebView web = bridge == null ? null : bridge.getWebView();
            if (web == null) { finishBack(request, false); return; }
            web.evaluateJavascript("(function(){try{var back=window.cdbHandleNativeBack||window.cdbHandleBack;return typeof back==='function'&&back()===true;}catch(e){return false;}})()",
                result -> finishBack(request, "true".equals(result)));
        } catch (RuntimeException unavailable) { finishBack(request, false); }
    }

    private void finishBack(int request, boolean handled) {
        if (!backPending || request != backRequest) return;
        backPending = false;
        startupHandler.removeCallbacks(backDeadline);
        // Renderer indisponível não prende a pessoa nem destrói os dados/formulários.
        if (!handled && !isFinishing() && !isDestroyed()) moveTaskToBack(true);
    }

    @Override
    public void onResume() {
        super.onResume();
        if (noticeOpened) { noticeOpened = false; return; }
        if (!AppNotifications.openNotice(this)) AppNotifications.offerPermission(this, false);
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        noticeOpened = AppNotifications.openNotice(this);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode == AppNotifications.PERMISSION_REQUEST) AppNotifications.subscribe(this, false);
    }

    @Override
    protected void load() {
        showStartupOverlay();
        bridgeBuilder.addWebViewListener(new WebViewListener() {
            @Override
            public void onPageLoaded(WebView webView) {
                if (startupOverlay == null) return;
                // Page load alone can precede painting. API 23 is also our minSdk.
                try {
                    webView.postVisualStateCallback(0, new WebView.VisualStateCallback() {
                        @Override
                        public void onComplete(long requestId) { hideStartupOverlay(); }
                    });
                } catch (RuntimeException unavailable) { hideStartupOverlay(); }
            }

            @Override
            public void onReceivedError(WebView webView) { hideStartupOverlay(); }

            @Override
            public void onReceivedHttpError(WebView webView) { hideStartupOverlay(); }
        });
        String path = OtaManager.get(this).beginBoot(recreatedActivity);
        bridgeBuilder.setServerPath(path == null
            ? new ServerPath(ServerPath.PathType.ASSET_PATH, Bridge.DEFAULT_WEB_ASSET_DIR)
            : new ServerPath(ServerPath.PathType.BASE_PATH, path));
        super.load();
    }

    private void showStartupOverlay() {
        ViewGroup content = findViewById(android.R.id.content);
        if (content == null || startupOverlay != null) return;
        startupOverlay = getLayoutInflater().inflate(R.layout.startup_loading, content, false);
        content.addView(startupOverlay);
        // Feedback must never become a permanent gate if the renderer cannot finish.
        startupHandler.postDelayed(startupDeadline, 15000);
    }

    private void hideStartupOverlay() {
        startupHandler.removeCallbacks(startupDeadline);
        if (startupOverlay == null) return;
        if (startupOverlay.getParent() instanceof ViewGroup) {
            ((ViewGroup) startupOverlay.getParent()).removeView(startupOverlay);
        }
        startupOverlay = null;
    }

    @Override
    public void onDestroy() {
        backPending = false;
        startupHandler.removeCallbacks(backDeadline);
        hideStartupOverlay();
        super.onDestroy();
    }
}
