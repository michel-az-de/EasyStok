package com.casadababa.app.printer;

import java.util.Locale;
import java.util.regex.Pattern;

/**
 * Classifica um aparelho pareado sem depender do Android, para o seletor
 * mostrar todo pareado plausível em vez de esconder térmica sem UUID em cache.
 * Constantes espelham BluetoothDevice.DEVICE_TYPE_* e BluetoothClass.Device.Major.*.
 */
public final class PrinterClassifier {
    public static final int DEVICE_TYPE_UNKNOWN = 0;
    public static final int DEVICE_TYPE_CLASSIC = 1;
    public static final int DEVICE_TYPE_LE = 2;
    public static final int DEVICE_TYPE_DUAL = 3;

    public static final int MAJOR_AUDIO_VIDEO = 0x0400;
    public static final int MAJOR_IMAGING = 0x0600;
    public static final int MAJOR_WEARABLE = 0x0700;

    private static final Pattern PRINTER_NAME =
        Pattern.compile("(MTP|WTP|OIA|PT-|POS|PRINTER|RPP|MPT)", Pattern.CASE_INSENSITIVE);

    private PrinterClassifier() {}

    public static String typeName(int type) {
        switch (type) {
            case DEVICE_TYPE_CLASSIC: return "classic";
            case DEVICE_TYPE_LE: return "le";
            case DEVICE_TYPE_DUAL: return "dual";
            default: return "unknown";
        }
    }

    /** Fone e relógio nunca entram na lista; o resto aparece com aviso. */
    public static boolean hidden(int majorClass) {
        return majorClass == MAJOR_AUDIO_VIDEO || majorClass == MAJOR_WEARABLE;
    }

    public static boolean printerClass(int majorClass, String name) {
        if (majorClass == MAJOR_IMAGING) return true;
        return name != null && PRINTER_NAME.matcher(name).find();
    }

    /** O app imprime por SPP clássico: aparelho só BLE não conecta. */
    public static boolean supported(int type) {
        return type != DEVICE_TYPE_LE;
    }

    /** Impressora primeiro, depois suportado, depois nome. */
    public static int compare(boolean printerA, boolean supportedA, String nameA,
                              boolean printerB, boolean supportedB, String nameB) {
        if (printerA != printerB) return printerA ? -1 : 1;
        if (supportedA != supportedB) return supportedA ? -1 : 1;
        String a = nameA == null ? "" : nameA.toLowerCase(Locale.ROOT);
        String b = nameB == null ? "" : nameB.toLowerCase(Locale.ROOT);
        return a.compareTo(b);
    }

    public static final String LE_ONLY_MESSAGE =
        "Este aparelho só tem Bluetooth Low Energy; o app imprime por Bluetooth clássico (SPP)";
}
