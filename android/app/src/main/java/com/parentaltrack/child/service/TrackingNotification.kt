package com.parentaltrack.child.service

import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.parentaltrack.child.MainActivity
import com.parentaltrack.child.R

/**
 * The permanent notification that makes location sharing visible while it is on
 * (contract §5.3). It carries a Stop sharing action so the child can end sharing
 * without opening the app.
 */
object TrackingNotification {

    const val CHANNEL_ID = "location_tracking"

    private const val REQUEST_CONTENT = 1
    private const val REQUEST_STOP = 2

    fun build(context: Context): Notification {
        ensureChannel(context)
        val text = context.getString(R.string.tracking_notification_text)
        return NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_stat_location)
            .setContentTitle(context.getString(R.string.tracking_notification_title))
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(contentIntent(context))
            .addAction(0, context.getString(R.string.tracking_notification_stop), stopIntent(context))
            .setOngoing(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    fun ensureChannel(context: Context) {
        val channel = NotificationChannelCompat
            .Builder(CHANNEL_ID, NotificationManagerCompat.IMPORTANCE_LOW)
            .setName(context.getString(R.string.tracking_channel_name))
            .setDescription(context.getString(R.string.tracking_channel_description))
            .setShowBadge(false)
            .setVibrationEnabled(false)
            .build()
        NotificationManagerCompat.from(context).createNotificationChannel(channel)
    }

    private fun contentIntent(context: Context): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_MAIN
            addCategory(Intent.CATEGORY_LAUNCHER)
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        return PendingIntent.getActivity(
            context,
            REQUEST_CONTENT,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun stopIntent(context: Context): PendingIntent {
        val intent = Intent(context, LocationTrackingService::class.java)
            .setAction(LocationTrackingService.ACTION_STOP)
        return PendingIntent.getService(
            context,
            REQUEST_STOP,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }
}
