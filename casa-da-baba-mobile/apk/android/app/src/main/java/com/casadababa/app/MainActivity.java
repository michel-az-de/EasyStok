package com.casadababa.app;

import android.os.Bundle;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebView;

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

    @Override
    public void onCreate(Bundle savedInstanceState) {
        recreatedActivity = savedInstanceState != null;
        registerPlugin(PilotAppPlugin.class);
        registerPlugin(ThermalPrinterPlugin.class);
        registerPlugin(PublicBackupPlugin.class);
        registerPlugin(PilotUpdaterPlugin.class);
        super.onCreate(savedInstanceState);
        AppNotifications.initialize(this);
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
        hideStartupOverlay();
        super.onDestroy();
    }
}
