package com.casadababa.app.printer;

public final class PrinterClassifierTest {
    public static void main(String[] args) {
        // #2193: a MTP-II e a OIA aparecem como impressora pelo nome, mesmo sem classe IMAGING.
        check(PrinterClassifier.printerClass(0x1F00, "MTP-II_DC53"));
        check(PrinterClassifier.printerClass(0x1F00, "OIA-8381-B"));
        check(PrinterClassifier.printerClass(0x1F00, "wtp05"));
        check(PrinterClassifier.printerClass(PrinterClassifier.MAJOR_IMAGING, "Sem nome"));
        check(!PrinterClassifier.printerClass(0x0200, "moto g56 5G"));
        check(!PrinterClassifier.printerClass(0x1F00, null));
        // Fone e relógio somem; telefone e sem classe aparecem com aviso.
        check(PrinterClassifier.hidden(PrinterClassifier.MAJOR_AUDIO_VIDEO));
        check(PrinterClassifier.hidden(PrinterClassifier.MAJOR_WEARABLE));
        check(!PrinterClassifier.hidden(0x1F00));
        check(!PrinterClassifier.hidden(PrinterClassifier.MAJOR_IMAGING));
        // Só BLE não conecta por SPP.
        check(!PrinterClassifier.supported(PrinterClassifier.DEVICE_TYPE_LE));
        check(PrinterClassifier.supported(PrinterClassifier.DEVICE_TYPE_CLASSIC));
        check(PrinterClassifier.supported(PrinterClassifier.DEVICE_TYPE_DUAL));
        check(PrinterClassifier.supported(PrinterClassifier.DEVICE_TYPE_UNKNOWN));
        check("le".equals(PrinterClassifier.typeName(2)));
        check("dual".equals(PrinterClassifier.typeName(3)));
        check("unknown".equals(PrinterClassifier.typeName(9)));
        // Impressora vem antes de qualquer outro pareado.
        check(PrinterClassifier.compare(true, true, "z", false, true, "a") < 0);
        check(PrinterClassifier.compare(true, false, "a", true, true, "b") > 0);
        check(PrinterClassifier.compare(true, true, "MTP", true, true, "oia") < 0);
        System.out.println("PASS: 20 printer classifier assertions; SPP connect requires device validation");
    }
    private static void check(boolean valid) { if (!valid) throw new AssertionError(); }
}
