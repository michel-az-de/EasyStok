package com.casadababa.app.printer;

import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothSocket;

import java.io.IOException;
import java.io.OutputStream;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Lógica pura de I/O Bluetooth Classic SPP. Toda chamada bloqueante
 * (connect/write) roda numa thread única dedicada — evita ANR e
 * race conditions com a main thread.
 */
public class ThermalPrinterImpl {
    private final ExecutorService io = Executors.newSingleThreadExecutor();
    private volatile BluetoothSocket socket;
    private volatile OutputStream out;

    public interface ResultCallback {
        void onSuccess();
        void onError(String msg);
    }

    public boolean isConnected() {
        BluetoothSocket s = socket;
        return s != null && s.isConnected();
    }

    public void connect(final BluetoothDevice device, final UUID sppUuid, final ResultCallback cb) {
        io.execute(new Runnable() {
            @Override public void run() {
                closeQuietly();
                BluetoothSocket s = null;
                IOException primary = null;
                try {
                    s = device.createRfcommSocketToServiceRecord(sppUuid);
                    s.connect();
                } catch (IOException ex) {
                    primary = ex;
                    if (s != null) try { s.close(); } catch (IOException ignore) {}
                    s = null;
                } catch (SecurityException ex) {
                    cb.onError("Sem permissão BLUETOOTH_CONNECT");
                    return;
                } catch (Exception ex) {
                    cb.onError(ex.getMessage() != null ? ex.getMessage() : "Erro inesperado ao conectar");
                    return;
                }
                if (s == null) {
                    // Fallback: insecure RFCOMM — alguns devices sem PIN exigem
                    try {
                        s = device.createInsecureRfcommSocketToServiceRecord(sppUuid);
                        s.connect();
                    } catch (IOException ex2) {
                        if (s != null) try { s.close(); } catch (IOException ignore) {}
                        String msg = primary != null ? primary.getMessage() : ex2.getMessage();
                        cb.onError("Falha ao conectar: " + (msg != null ? msg : "device pode estar desligado ou fora de alcance"));
                        return;
                    } catch (SecurityException ex2) {
                        cb.onError("Sem permissão BLUETOOTH_CONNECT");
                        return;
                    } catch (Exception ex2) {
                        cb.onError(ex2.getMessage() != null ? ex2.getMessage() : "Erro inesperado");
                        return;
                    }
                }
                try {
                    OutputStream o = s.getOutputStream();
                    socket = s;
                    out = o;
                    cb.onSuccess();
                } catch (IOException ex) {
                    closeQuietly();
                    cb.onError("Falha ao abrir stream: " + ex.getMessage());
                }
            }
        });
    }

    public void write(final byte[] data, final ResultCallback cb) {
        io.execute(new Runnable() {
            @Override public void run() {
                OutputStream o = out;
                if (o == null) { cb.onError("Não conectado"); return; }
                try {
                    o.write(data);
                    o.flush();
                    cb.onSuccess();
                } catch (IOException ex) {
                    cb.onError("Falha ao enviar: " + ex.getMessage());
                }
            }
        });
    }

    public void disconnect() {
        io.execute(new Runnable() {
            @Override public void run() { closeQuietly(); }
        });
    }

    private void closeQuietly() {
        OutputStream o = out;
        BluetoothSocket s = socket;
        out = null;
        socket = null;
        if (o != null) try { o.close(); } catch (IOException ignore) {}
        if (s != null) try { s.close(); } catch (IOException ignore) {}
    }
}
