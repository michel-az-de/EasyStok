package com.casadababa.app;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.text.InputType;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.webkit.JsPromptResult;
import android.webkit.JsResult;
import android.webkit.WebView;
import android.widget.EditText;
import android.widget.FrameLayout;

import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeWebChromeClient;

/**
 * O cliente do Capacitor fixa os rótulos em inglês nos diálogos de alert, confirm e prompt do JavaScript.
 * Este cliente repete o comportamento original com os rótulos em português; arquivo, câmera,
 * geolocalização e permissões continuam com a classe base.
 * #2886: os três diálogos usam o tema PilotDialog (cor da marca no claro e no escuro) e o prompt
 * abre o teclado já focado, com tudo selecionado; valor padrão de até 4 dígitos pede teclado numérico.
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
        new AlertDialog.Builder(view.getContext(), R.style.PilotDialog)
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
        new AlertDialog.Builder(view.getContext(), R.style.PilotDialog)
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
        input.setSingleLine(true);
        input.setImeOptions(EditorInfo.IME_ACTION_DONE);
        if (defaultValue != null && defaultValue.matches("\\d{1,4}")) input.setInputType(InputType.TYPE_CLASS_NUMBER);
        if (defaultValue != null) input.setText(defaultValue);
        input.setSelectAllOnFocus(true);
        final FrameLayout box = new FrameLayout(view.getContext());
        final int inset = view.getResources().getDimensionPixelSize(R.dimen.dialog_input_inset);
        box.setPadding(inset, inset / 3, inset, 0);
        box.addView(input);
        final AlertDialog dialog = new AlertDialog.Builder(view.getContext(), R.style.PilotDialog)
            .setMessage(message)
            .setView(box)
            .setPositiveButton(view.getContext().getString(R.string.dialog_ok), (d, button) -> {
                d.dismiss();
                result.confirm(input.getText().toString().trim());
            })
            .setNegativeButton(view.getContext().getString(R.string.dialog_cancel), (d, button) -> {
                d.dismiss();
                result.cancel();
            })
            .setOnCancelListener(d -> {
                d.dismiss();
                result.cancel();
            })
            .create();
        input.setOnEditorActionListener((field, action, event) -> {
            if (action != EditorInfo.IME_ACTION_DONE) return false;
            dialog.getButton(DialogInterface.BUTTON_POSITIVE).performClick();
            return true;
        });
        if (dialog.getWindow() != null)
            dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE);
        dialog.show();
        input.requestFocus();
        return true;
    }
}
