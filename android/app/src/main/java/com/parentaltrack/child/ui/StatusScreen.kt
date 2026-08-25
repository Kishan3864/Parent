package com.parentaltrack.child.ui

import android.text.format.DateUtils
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ElevatedCard
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.parentaltrack.child.R
import java.text.DateFormat
import java.util.Date

/**
 * The dashboard the person carrying this phone sees. It says plainly whether location is being
 * shared and with whom, when the last fix and the last successful upload happened, and how much is
 * still queued. The stop control is the first thing on the screen and is never hidden or disabled
 * while sharing is on.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StatusScreen(
    state: UiState,
    onRefresh: () -> Unit,
    onStartSharing: () -> Unit,
    onStopSharing: () -> Unit,
    onDismissStartFailure: () -> Unit,
    onDismissServiceError: () -> Unit,
    onOpenPermissions: () -> Unit,
    onUnpair: () -> Unit,
    onPairAgain: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val lifecycleOwner = LocalLifecycleOwner.current
    var showUnpairDialog by remember { mutableStateOf(false) }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) onRefresh()
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    val canStart = state.isPaired && state.permissions.canTrack
    val blockedReason: String? = when {
        canStart -> null
        !state.isPaired -> stringResource(R.string.status_error_not_paired)
        !state.permissions.foregroundLocationGranted ->
            stringResource(R.string.status_error_location_permission)

        else -> null
    }
    val startFailure: String? = state.startFailure?.let { blocker ->
        when (blocker) {
            SharingBlocker.CONSENT_MISSING ->
                "Sharing cannot start until the notice shown when this app first opened is accepted."

            SharingBlocker.NOT_PAIRED -> stringResource(R.string.status_error_not_paired)
            SharingBlocker.LOCATION_PERMISSION ->
                stringResource(R.string.status_error_location_permission)

            SharingBlocker.SERVICE_START_FAILED ->
                stringResource(R.string.status_error_service_start)
        }
    }
    val neverLabel = stringResource(R.string.status_never)
    val grantedLabel = stringResource(R.string.permission_granted)
    val deniedLabel = stringResource(R.string.permission_denied)

    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.status_title)) },
                actions = {
                    TextButton(onClick = onRefresh) {
                        Text(stringResource(R.string.status_refresh))
                    }
                },
            )
        },
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            ElevatedCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(modifier = Modifier.padding(end = 16.dp)) {
                            Text(
                                text = stringResource(
                                    if (state.trackingEnabled) {
                                        R.string.status_sharing_on
                                    } else {
                                        R.string.status_sharing_off
                                    },
                                ),
                                style = MaterialTheme.typography.headlineSmall,
                                fontWeight = FontWeight.SemiBold,
                            )
                            Text(
                                text = stringResource(R.string.status_sharing_switch),
                                style = MaterialTheme.typography.bodyMedium,
                            )
                        }
                        Switch(
                            checked = state.trackingEnabled,
                            onCheckedChange = { wantsOn ->
                                if (wantsOn) onStartSharing() else onStopSharing()
                            },
                            // Stopping is always possible; only starting can be blocked.
                            enabled = state.trackingEnabled || canStart,
                        )
                    }
                    if (!state.trackingEnabled && blockedReason != null) {
                        Text(
                            text = blockedReason,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                }
            }

            if (startFailure != null) {
                Banner(
                    container = MaterialTheme.colorScheme.errorContainer,
                    content = MaterialTheme.colorScheme.onErrorContainer,
                    title = "Sharing did not start",
                    body = startFailure,
                    actionLabel = "Dismiss",
                    onAction = onDismissStartFailure,
                )
            }

            // A failure the service recorded on its own — a foreground start Android refused after
            // a reboot or a START_STICKY re-delivery — is only visible here (contract §5.3).
            if (startFailure == null && state.lastServiceError != null) {
                Banner(
                    container = MaterialTheme.colorScheme.errorContainer,
                    content = MaterialTheme.colorScheme.onErrorContainer,
                    title = "Sharing stopped unexpectedly",
                    body = stringResource(R.string.status_error_service_start),
                    actionLabel = "Dismiss",
                    onAction = onDismissServiceError,
                )
            }

            if (state.revokedByParent) {
                Banner(
                    container = MaterialTheme.colorScheme.errorContainer,
                    content = MaterialTheme.colorScheme.onErrorContainer,
                    title = stringResource(R.string.status_revoked_title),
                    body = stringResource(R.string.status_revoked_body),
                    actionLabel = stringResource(R.string.pairing_submit),
                    onAction = onPairAgain,
                )
            } else if (!state.isPaired) {
                Banner(
                    container = MaterialTheme.colorScheme.errorContainer,
                    content = MaterialTheme.colorScheme.onErrorContainer,
                    title = stringResource(R.string.status_not_paired),
                    body = stringResource(R.string.pairing_body),
                    actionLabel = stringResource(R.string.pairing_submit),
                    onAction = onPairAgain,
                )
            }

            if (state.isPaired && state.permissions.backgroundLocationRequired &&
                !state.permissions.backgroundLocationGranted
            ) {
                Banner(
                    container = MaterialTheme.colorScheme.tertiaryContainer,
                    content = MaterialTheme.colorScheme.onTertiaryContainer,
                    title = stringResource(R.string.status_permission_background),
                    body = stringResource(R.string.permission_warning_background_missing),
                    actionLabel = stringResource(R.string.status_fix_permissions),
                    onAction = onOpenPermissions,
                )
            }

            val pairingSummary = when {
                state.childName != null ->
                    stringResource(R.string.status_paired_with, state.childName)

                state.isPaired -> "Paired with a parent account"
                else -> stringResource(R.string.status_not_paired)
            }

            SectionCard(title = "Details") {
                Text(text = pairingSummary, style = MaterialTheme.typography.bodyMedium)
                DetailRow(
                    label = stringResource(R.string.status_last_fix),
                    value = formatMoment(state.lastFixAtMillis, state.nowMillis, neverLabel),
                )
                DetailRow(
                    label = stringResource(R.string.status_last_upload),
                    value = formatMoment(state.lastUploadAtMillis, state.nowMillis, neverLabel),
                )
                DetailRow(
                    label = stringResource(R.string.status_pending_queue),
                    value = stringResource(
                        R.string.status_pending_count,
                        state.pendingUploadCount,
                    ),
                )
            }

            SectionCard(title = stringResource(R.string.status_permissions_header)) {
                PermissionLine(
                    label = stringResource(R.string.status_permission_location),
                    granted = state.permissions.foregroundLocationGranted,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    warning = stringResource(R.string.permission_warning_location_missing),
                )
                if (state.permissions.foregroundLocationGranted &&
                    !state.permissions.fineLocationGranted
                ) {
                    Text(
                        text = stringResource(R.string.permission_warning_coarse_only),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (state.permissions.backgroundLocationRequired) {
                    PermissionLine(
                        label = stringResource(R.string.status_permission_background),
                        granted = state.permissions.backgroundLocationGranted,
                        grantedLabel = grantedLabel,
                        deniedLabel = deniedLabel,
                        warning = stringResource(R.string.permission_warning_background_missing),
                    )
                }
                if (state.permissions.notificationsRequired) {
                    PermissionLine(
                        label = stringResource(R.string.status_permission_notifications),
                        granted = state.permissions.notificationsGranted,
                        grantedLabel = grantedLabel,
                        deniedLabel = deniedLabel,
                        warning = stringResource(
                            R.string.permission_warning_notifications_missing,
                        ),
                    )
                }
                PermissionLine(
                    label = stringResource(R.string.status_permission_battery),
                    granted = state.permissions.batteryOptimisationIgnored,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    warning = stringResource(R.string.permission_battery_optional),
                )
                OutlinedButton(onClick = onOpenPermissions, modifier = Modifier.fillMaxWidth()) {
                    Text(stringResource(R.string.status_fix_permissions))
                }
            }

            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant,
                    contentColor = MaterialTheme.colorScheme.onSurfaceVariant,
                ),
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(
                        text = stringResource(R.string.unpair_button),
                        style = MaterialTheme.typography.titleMedium,
                    )
                    Text(
                        text = stringResource(R.string.unpair_confirm_message),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                    Button(
                        onClick = { showUnpairDialog = true },
                        modifier = Modifier.fillMaxWidth(),
                        enabled = state.isPaired,
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.error,
                            contentColor = MaterialTheme.colorScheme.onError,
                        ),
                    ) {
                        Text(stringResource(R.string.unpair_button))
                    }
                }
            }
        }
    }

    if (showUnpairDialog) {
        AlertDialog(
            onDismissRequest = { showUnpairDialog = false },
            title = { Text(stringResource(R.string.unpair_confirm_title)) },
            text = { Text(stringResource(R.string.unpair_confirm_message)) },
            confirmButton = {
                TextButton(
                    onClick = {
                        showUnpairDialog = false
                        onUnpair()
                    },
                ) {
                    Text(
                        text = stringResource(R.string.unpair_confirm_action),
                        color = MaterialTheme.colorScheme.error,
                    )
                }
            },
            dismissButton = {
                TextButton(onClick = { showUnpairDialog = false }) {
                    Text(stringResource(R.string.unpair_cancel))
                }
            },
        )
    }
}

@Composable
private fun Banner(
    container: Color,
    content: Color,
    title: String,
    body: String,
    actionLabel: String,
    onAction: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = container, contentColor = content),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold,
            )
            Text(text = body, style = MaterialTheme.typography.bodyMedium)
            TextButton(onClick = onAction) { Text(text = actionLabel, color = content) }
        }
    }
}

@Composable
private fun DetailRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(end = 12.dp),
        )
        Text(
            text = value,
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.Medium,
        )
    }
}

@Composable
private fun PermissionLine(
    label: String,
    granted: Boolean,
    grantedLabel: String,
    deniedLabel: String,
    warning: String,
) {
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = label,
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(end = 12.dp),
            )
            Text(
                text = if (granted) grantedLabel else deniedLabel,
                style = MaterialTheme.typography.bodyMedium,
                color = if (granted) {
                    MaterialTheme.colorScheme.primary
                } else {
                    MaterialTheme.colorScheme.error
                },
            )
        }
        if (!granted) {
            Text(
                text = warning,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** "3 minutes ago (14:05)" — relative for scanning, absolute so it can be checked. */
private fun formatMoment(millis: Long?, now: Long, neverLabel: String): String {
    if (millis == null || millis <= 0L) return neverLabel
    val clock = DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(millis))
    val age = now - millis
    val relative = if (age in 0 until DateUtils.MINUTE_IN_MILLIS) {
        "Just now"
    } else {
        DateUtils.getRelativeTimeSpanString(millis, now, DateUtils.MINUTE_IN_MILLIS).toString()
    }
    return "$relative ($clock)"
}
