package com.casadababa.app;

import android.app.AlertDialog;
import android.webkit.JsPromptResult;
import android.webkit.JsResult;
import android.webkit.WebView;
import android.widget.EditText;

import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeWebChromeClient;

/**
 * O cliente do Capacitor fixa os rótulos em inglês nos diálogos de alert, confirm e prompt do JavaScript.
 * Este cliente repete o comportamento original com os rótulos em português; arquivo, câmera,
 * geolocalização e permissões continuam com a classe base.
 */
public class PilotChromeClient extends BridgeWebChromeClient {
    private final Bridge bridge;

    public PilotChromeClient(Bridge bridge) {
        super(bridge);
        this.bridge = bridge;
    }

    @Override
    public boolean onJsAlert(WebView view, String url, String message, final JsResult result) {
        if (bridge.getActivity().isFinishing()) return true;
        new AlertDialog.Builder(view.getContext())
            .setMessage(message)
            .setPositiveButton(view.getContext().getString(R.string.dialog_ok), (dialog, button) -> {
                dialog.dismiss();
                result.confirm();
            })
            .setOnCancelListener(dialog -> {
                dialog.dismiss();
                result.cancel();
            })
            .create().show();
        return true;
    }

    @Override
    public boolean onJsConfirm(WebView view, String url, String message, final JsResult result) {
        if (bridge.getActivity().isFinishing()) return true;
        new AlertDialog.Builder(view.getContext())
            .setMessage(message)
            .setPositiveButton(view.getContext().getString(R.string.dialog_ok), (dialog, button) -> {
                dialog.dismiss();
                result.confirm();
            })
            .setNegativeButton(view.getContext().getString(R.string.dialog_cancel), (dialog, button) -> {
                dialog.dismiss();
                result.cancel();
            })
            .setOnCancelListener(dialog -> {
                dialog.dismiss();
                result.cancel();
            })
            .create().show();
        return true;
    }

    @Override
    public boolean onJsPrompt(WebView view, String url, String message, String defaultValue, final JsPromptResult result) {
        if (bridge.getActivity().isFinishing()) return true;
        final EditText input = new EditText(view.getContext());
        if (defaultValue != null) input.setText(defaultValue);
        new AlertDialog.Builder(view.getContext())
            .setMessage(message)
            .setView(input)
            .setPositiveButton(view.getContext().getString(R.string.dialog_ok), (dialog, button) -> {
                dialog.dismiss();
                result.confirm(input.getText().toString().trim());
            })
            .setNegativeButton(view.getContext().getString(R.string.dialog_cancel), (dialog, button) -> {
                dialog.dismiss();
                result.cancel();
            })
            .setOnCancelListener(dialog -> {
                dialog.dismiss();
                result.cancel();
            })
            .create().show();
        return true;
    }
}
