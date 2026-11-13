package com.casadababa.app;

/** Server time seen through the HTTP Date header; signatures use it, so a wrong phone clock does not lock out push. */
final class NoticeClock {
    /** Matches PushSignature.Fresh on the API: issuedAt must be within five minutes of the server clock. */
    static final long WINDOW = 300000L;
    private static final long MARGIN = 60000L;
    private NoticeClock() {}

    /** A missing Date header keeps the previous offset. Second precision is enough for a five-minute window. */
    static long offset(long serverDate, long localAtResponse, long previous) {
        return serverDate > 0 ? serverDate - localAtResponse : previous;
    }

    static boolean skewed(long offset) { return Math.abs(offset) > 2 * MARGIN; }

    /** Keeps registrations monotonic while the server can still accept them; a value saved with a wrong clock is dropped. */
    static long issuedAt(long serverNow, long previous) {
        long next = previous + 1;
        return next > serverNow && next < serverNow + WINDOW - MARGIN ? next : serverNow;
    }
}
