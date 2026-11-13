package com.casadababa.app;

import android.app.job.JobInfo;
import android.app.job.JobParameters;
import android.app.job.JobScheduler;
import android.app.job.JobService;
import android.content.ComponentName;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;

/** Android retries the durable outbox after connectivity returns, without a WebView. */
public class NoticeSyncJob extends JobService {
    private static final int ID = 2091001;
    private static final Object SCHEDULING = new Object();
    private static long generation;
    private static boolean active;
    private final Handler main = new Handler(Looper.getMainLooper());
    private JobParameters running;
    public static void schedule(Context context) {
        synchronized (SCHEDULING) {
            generation++;
            if (active) return;
            try {
                JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
                if (scheduler == null) return;
                for (JobInfo pending : scheduler.getAllPendingJobs()) if (pending.getId() == ID) return;
                scheduler.schedule(new JobInfo.Builder(ID, new ComponentName(context, NoticeSyncJob.class))
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPersisted(true)
                    .setBackoffCriteria(30000, JobInfo.BACKOFF_POLICY_EXPONENTIAL).build());
            } catch (RuntimeException failure) {
                try { NoticeManager.get(context).error("O Android não agendou as confirmações. Abra o sino e toque em Atualizar avisos."); }
                catch (Exception ignored) { }
            }
        }
    }
    @Override public boolean onStartJob(JobParameters params) {
        final long started;
        synchronized (SCHEDULING) { running = params; active = true; started = generation; }
        try {
            NoticeManager.get(this).refresh((status, success) -> main.post(() -> {
                synchronized (SCHEDULING) {
                    if (running != params) return;
                    running = null; active = false;
                    // A push received after the final flush must not lose its pending upload.
                    jobFinished(params, !success || started != generation);
                }
            }), false);
        } catch (Exception failure) {
            synchronized (SCHEDULING) { running = null; active = false; jobFinished(params, true); }
        }
        return true;
    }
    @Override public boolean onStopJob(JobParameters params) {
        synchronized (SCHEDULING) { if (running == params) { running = null; active = false; } }
        return true;
    }
}
