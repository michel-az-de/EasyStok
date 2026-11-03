package com.casadababa.app.printer;

import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothSocket;

import java.io.IOException;
import java.io.OutputStream;
import java.lang.reflect.Method;
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
        OutputStream o = out;
        return s != null && o != null && s.isConnected();
    }

    /** Uma tentativa de abrir o socket RFCOMM; cada etapa tem nome para o erro. */
    interface SocketAttempt {
        BluetoothSocket open() throws Exception;
    }

    /**
     * #2193: seguro, inseguro e, por fim, canal 1 por reflexão. Térmica barata
     * (MTP-II e similares) às vezes não publica o SPP no SDP, e só o canal fixo conecta.
     */
    public void connect(final BluetoothAdapter adapter, final BluetoothDevice device, final UUID sppUuid, final ResultCallback cb) {
        io.execute(new Runnable() {
            @Override public void run() {
                closeQuietly();
                String[] names = { "seguro", "inseguro", "canal 1" };
                SocketAttempt[] attempts = {
                    new SocketAttempt() { public BluetoothSocket open() throws Exception {
                        return device.createRfcommSocketToServiceRecord(sppUuid); } },
                    new SocketAttempt() { public BluetoothSocket open() throws Exception {
                        return device.createInsecureRfcommSocketToServiceRecord(sppUuid); } },
                    new SocketAttempt() { public BluetoothSocket open() throws Exception {
                        Method m = device.getClass().getMethod("createRfcommSocket", int.class);
                        return (BluetoothSocket) m.invoke(device, 1); } }
                };
                StringBuilder trail = new StringBuilder();
                BluetoothSocket s = null;
                for (int i = 0; i < attempts.length && s == null; i++) {
                    try { if (adapter != null) adapter.cancelDiscovery(); } catch (Exception ignore) {}
                    BluetoothSocket candidate = null;
                    try {
                        candidate = attempts[i].open();
                        candidate.connect();
                        s = candidate;
                    } catch (SecurityException ex) {
                        closeSocket(candidate);
                        cb.onError("Sem permissão BLUETOOTH_CONNECT");
                        return;
                    } catch (Exception ex) {
                        closeSocket(candidate);
                        Throwable cause = ex.getCause() != null && ex instanceof java.lang.reflect.InvocationTargetException ? ex.getCause() : ex;
                        String msg = cause.getMessage() != null ? cause.getMessage() : cause.getClass().getSimpleName();
                        if (trail.length() > 0) trail.append("; ");
                        trail.append(names[i]).append(": ").append(msg);
                    }
                }
                if (s == null) {
                    cb.onError("Falha ao conectar (" + trail + "). Confira se a impressora está ligada, perto e sem outro aparelho conectado.");
                    return;
                }
                try {
                    OutputStream o = s.getOutputStream();
                    socket = s;
                    out = o;
                    cb.onSuccess();
                } catch (IOException ex) {
                    closeSocket(s);
                    closeQuietly();
                    cb.onError("Falha ao abrir stream: " + ex.getMessage());
                }
            }
        });
    }

    private static void closeSocket(BluetoothSocket s) {
        if (s != null) try { s.close(); } catch (IOException ignore) {}
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
                    // Socket morto (broken pipe / EPIPE acontece quando a impressora
                    // saiu de alcance, entrou em idle ou foi desligada). Fecha o
                    // socket zumbi pra forçar reconnect na próxima chamada — sem
                    // isso, isConnected() continua mentindo "true" e todo write
                    // falha igual ate o app ser reaberto.
                    closeQuietly();
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
