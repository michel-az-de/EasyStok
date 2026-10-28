package com.casadababa.app;

/** A token rotation invalidates in-flight registration, including its persistent success write. */
final class TokenRegistration {
    interface Write { void run() throws Exception; }
    private long generation;
    synchronized long begin() { return generation; }
    synchronized void changed(Write invalidate) throws Exception { generation++; invalidate.run(); }
    synchronized boolean complete(long started, Write persist) throws Exception {
        if (started != generation) return false;
        persist.run();
        return true;
    }
}
