package com.casadababa.app.printer;

import android.Manifest;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothClass;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothManager;
import android.content.Context;
import android.os.Build;
import android.os.ParcelUuid;
import android.util.Base64;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

import java.util.Set;
import java.util.UUID;

@CapacitorPlugin(
    name = "ThermalPrinter",
    permissions = {
        @Permission(alias = "bluetooth", strings = {
            Manifest.permission.BLUETOOTH_CONNECT,
            Manifest.permission.BLUETOOTH_SCAN
        })
    }
)
public class ThermalPrinterPlugin extends Plugin {
    private static final UUID SPP_UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB");
    private final ThermalPrinterImpl impl = new ThermalPrinterImpl();

    @PluginMethod
    public void requestPermissions(PluginCall call) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (getPermissionState("bluetooth") != PermissionState.GRANTED) {
                requestPermissionForAlias("bluetooth", call, "permsCallback");
                return;
            }
        }
        JSObject ret = new JSObject();
        ret.put("granted", true);
        call.resolve(ret);
    }

    @PermissionCallback
    private void permsCallback(PluginCall call) {
        boolean granted = getPermissionState("bluetooth") == PermissionState.GRANTED;
        JSObject ret = new JSObject();
        ret.put("granted", granted);
        if (granted) {
            call.resolve(ret);
        } else {
            call.reject("Permissão Bluetooth negada");
        }
    }

    @PluginMethod
    public void isEnabled(PluginCall call) {
        try {
            BluetoothAdapter adapter = getAdapter();
            JSObject ret = new JSObject();
            ret.put("enabled", adapter != null && adapter.isEnabled());
            call.resolve(ret);
        } catch (SecurityException ex) {
            call.reject("Sem permissão pra checar Bluetooth", ex);
        } catch (Exception ex) {
            call.reject(safeMsg(ex), ex);
        }
    }

    @PluginMethod
    public void listPaired(PluginCall call) {
        try {
            BluetoothAdapter adapter = getAdapter();
            if (adapter == null) {
                call.reject("Bluetooth não suportado neste dispositivo");
                return;
            }
            if (!adapter.isEnabled()) {
                call.reject("Bluetooth desligado — ligue nas Configurações");
                return;
            }
            Set<BluetoothDevice> bonded = adapter.getBondedDevices();
            JSArray devices = new JSArray();
            if (bonded != null) {
                for (BluetoothDevice d : bonded) {
                    if (!looksLikePrinter(d)) continue;
                    JSObject dev = new JSObject();
                    String addr = d.getAddress();
                    String name = null;
                    try { name = d.getName(); } catch (SecurityException ignore) {}
                    dev.put("address", addr);
                    dev.put("id", addr);
                    dev.put("name", name != null ? name : addr);
                    devices.put(dev);
                }
            }
            JSObject ret = new JSObject();
            ret.put("devices", devices);
            call.resolve(ret);
        } catch (SecurityException ex) {
            call.reject("Sem permissão BLUETOOTH_CONNECT — Configurações > Apps > Casa da Baba > Permissões > Dispositivos próximos", ex);
        } catch (Exception ex) {
            call.reject(safeMsg(ex), ex);
        }
    }

    /**
     * Heurística pra filtrar dispositivos que claramente não são impressoras.
     * - Se UUIDs estão disponíveis: exige SPP (00001101-...).
     * - Se UUIDs não foram cacheados: exclui apenas pela classe (audio/wearable/peripheral).
     * Mantém impressora térmica genérica visível e tira fones BLE-only que crashavam connect.
     */
    private boolean looksLikePrinter(BluetoothDevice d) {
        try {
            ParcelUuid[] uuids = d.getUuids();
            if (uuids != null && uuids.length > 0) {
                for (ParcelUuid pu : uuids) {
                    if (pu != null && pu.getUuid().equals(SPP_UUID)) return true;
                }
                return false;
            }
            BluetoothClass bc = d.getBluetoothClass();
            if (bc != null) {
                int major = bc.getMajorDeviceClass();
                if (major == BluetoothClass.Device.Major.AUDIO_VIDEO) return false;
                if (major == BluetoothClass.Device.Major.PERIPHERAL) return false;
                if (major == BluetoothClass.Device.Major.WEARABLE) return false;
                if (major == BluetoothClass.Device.Major.PHONE) return false;
                if (major == BluetoothClass.Device.Major.COMPUTER) return false;
            }
            return true;
        } catch (Exception ex) {
            return true;
        }
    }

    @PluginMethod
    public void connect(final PluginCall call) {
        final String address = call.getString("address");
        if (address == null || address.isEmpty()) {
            call.reject("address obrigatório");
            return;
        }
        try {
            BluetoothAdapter adapter = getAdapter();
            if (adapter == null) { call.reject("Bluetooth não suportado"); return; }
            if (!adapter.isEnabled()) { call.reject("Bluetooth desligado"); return; }
            try { adapter.cancelDiscovery(); } catch (Exception ignore) {}
            BluetoothDevice device = adapter.getRemoteDevice(address);
            impl.connect(device, SPP_UUID, new ThermalPrinterImpl.ResultCallback() {
                @Override public void onSuccess() {
                    JSObject ret = new JSObject();
                    ret.put("connected", true);
                    call.resolve(ret);
                }
                @Override public void onError(String msg) { call.reject(msg); }
            });
        } catch (SecurityException ex) {
            call.reject("Sem permissão BLUETOOTH_CONNECT", ex);
        } catch (Exception ex) {
            call.reject(safeMsg(ex), ex);
        }
    }

    @PluginMethod
    public void isConnected(PluginCall call) {
        JSObject ret = new JSObject();
        ret.put("connected", impl.isConnected());
        call.resolve(ret);
    }

    @PluginMethod
    public void write(final PluginCall call) {
        String b64 = call.getString("base64");
        if (b64 == null) { call.reject("base64 obrigatório"); return; }
        final byte[] data;
        try {
            data = Base64.decode(b64, Base64.DEFAULT);
        } catch (Exception ex) {
            call.reject("base64 inválido", ex);
            return;
        }
        impl.write(data, new ThermalPrinterImpl.ResultCallback() {
            @Override public void onSuccess() { call.resolve(); }
            @Override public void onError(String msg) { call.reject(msg); }
        });
    }

    @PluginMethod
    public void disconnect(PluginCall call) {
        impl.disconnect();
        call.resolve();
    }

    private BluetoothAdapter getAdapter() {
        Context ctx = getContext();
        BluetoothManager bm = (BluetoothManager) ctx.getSystemService(Context.BLUETOOTH_SERVICE);
        return bm != null ? bm.getAdapter() : null;
    }

    private static String safeMsg(Throwable t) {
        if (t == null) return "Erro desconhecido";
        String m = t.getMessage();
        return m != null ? m : t.getClass().getSimpleName();
    }
}
