package com.casadababa.app.updater;

import android.app.job.JobInfo;
import android.app.job.JobParameters;
import android.app.job.JobScheduler;
import android.app.job.JobService;
import android.content.ComponentName;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;

/** Extends the short FCM callback into a network-constrained Android job. Never activates code. */
public class UpdateCheckJob extends JobService {
    private static final int JOB_ID = 2065001;
    private final Handler main = new Handler(Looper.getMainLooper());
    private JobParameters running;

    static void schedule(Context context) {
        try {
            JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
            JobInfo job = new JobInfo.Builder(JOB_ID, new ComponentName(context, UpdateCheckJob.class))
                .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                .setBackoffCriteria(30000, JobInfo.BACKOFF_POLICY_EXPONENTIAL)
                .setPersisted(true).build();
            if (scheduler == null || scheduler.schedule(job) != JobScheduler.RESULT_SUCCESS)
                UpdatePush.error(context, "O Android não conseguiu agendar a atualização recebida por push.");
        } catch (RuntimeException error) {
            UpdatePush.error(context, "O Android não permitiu agendar a atualização em segundo plano.");
        }
    }

    @Override public boolean onStartJob(JobParameters params) {
        running = params;
        OtaManager.get(this).check(true, status -> main.post(() -> {
            if (running != params) return;
            running = null;
            announce(status.optString("pendingVersion", ""));
            boolean retry = status.optBoolean("checking") || status.optBoolean("trial")
                || !status.optBoolean("checkSucceeded");
            jobFinished(params, retry);
        }));
        return true;
    }

    /** O push só prepara o pacote; sem este aviso o usuário não sabe que precisa reabrir o app (#2195). */
    private void announce(String pendingVersion) {
        android.content.SharedPreferences prefs = getSharedPreferences("cdb-update-push", MODE_PRIVATE);
        if (!UpdatePolicy.shouldAnnounceContent(pendingVersion, prefs.getString("announcedVersion", ""))) return;
        try {
            if (com.casadababa.app.AppNotifications.contentReady(this, pendingVersion))
                prefs.edit().putString("announcedVersion", pendingVersion).apply();
        } catch (RuntimeException ignored) { /* Aviso é conveniência; o pacote continua preparado para a próxima abertura. */ }
    }

    @Override public boolean onStopJob(JobParameters params) {
        if (running == params) running = null;
        return true;
    }
}
