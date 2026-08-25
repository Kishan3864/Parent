package com.parentaltrack.child.ui

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.parentaltrack.child.R

/**
 * Staged permission flow, in the order Android requires: notifications, then foreground location,
 * then — only once foreground is granted — background location as a separate request, then the
 * optional battery-optimisation exemption. Every stage explains itself before the system dialog
 * appears, and all states are re-read on ON_RESUME so returning from Settings updates the screen.
 */
@SuppressLint("InlinedApi") // Permission constants are inlined; each use is guarded by SDK_INT.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PermissionScreen(
    permissions: PermissionState,
    onRefresh: () -> Unit,
    onContinue: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    var actionError by remember { mutableStateOf<String?>(null) }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) onRefresh()
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    val notificationLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { onRefresh() }

    val foregroundLocationLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions(),
    ) { onRefresh() }

    val backgroundLocationLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { onRefresh() }

    val settingsLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { onRefresh() }

    val settingsFallbackMessage = "This phone did not open that settings screen. Open Settings, " +
        "find this app, and change the permission there."

    // Tries each intent in turn: some OEM builds are missing one of these settings screens.
    fun openSettings(candidates: List<Intent>) {
        actionError = null
        for (intent in candidates) {
            if (runCatching { settingsLauncher.launch(intent) }.isSuccess) return
        }
        actionError = settingsFallbackMessage
    }

    // The on-device wording for the background option ("Allow all the time" on stock Android).
    val backgroundOptionLabel = remember {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            context.packageManager.backgroundPermissionOptionLabel.toString()
        } else {
            ""
        }
    }

    val grantedLabel = stringResource(R.string.permission_granted)
    val deniedLabel = stringResource(R.string.permission_denied)

    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.permission_title)) },
                actions = {
                    TextButton(onClick = onContinue) {
                        Text(stringResource(R.string.permission_skip))
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
            Text(
                text = stringResource(R.string.permission_intro),
                style = MaterialTheme.typography.bodyLarge,
            )

            if (permissions.notificationsRequired) {
                StageCard(
                    step = "Step 1",
                    title = stringResource(R.string.permission_notifications_title),
                    granted = permissions.notificationsGranted,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    rationale = stringResource(R.string.permission_notifications_rationale),
                ) {
                    Button(
                        onClick = {
                            notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                        },
                    ) {
                        Text(stringResource(R.string.permission_notifications_action))
                    }
                    OutlinedButton(onClick = { openSettings(listOf(appDetailsIntent(context))) }) {
                        Text(stringResource(R.string.permission_open_settings))
                    }
                }
            }

            StageCard(
                step = if (permissions.notificationsRequired) "Step 2" else "Step 1",
                title = stringResource(R.string.permission_location_title),
                granted = permissions.foregroundLocationGranted,
                grantedLabel = grantedLabel,
                deniedLabel = deniedLabel,
                rationale = stringResource(R.string.permission_location_rationale),
            ) {
                Button(
                    onClick = {
                        foregroundLocationLauncher.launch(
                            arrayOf(
                                Manifest.permission.ACCESS_FINE_LOCATION,
                                Manifest.permission.ACCESS_COARSE_LOCATION,
                            ),
                        )
                    },
                ) {
                    Text(stringResource(R.string.permission_location_action))
                }
                OutlinedButton(onClick = { openSettings(listOf(appDetailsIntent(context))) }) {
                    Text(stringResource(R.string.permission_open_settings))
                }
            }

            if (permissions.backgroundLocationRequired) {
                val needsSettingsDeepLink = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R
                // On API 30+ there is no system dialog, so the rationale has to name the exact
                // option the device shows inside its own settings screen.
                val settingsHint = when {
                    !needsSettingsDeepLink -> null
                    backgroundOptionLabel.isNotBlank() -> stringResource(
                        R.string.permission_background_settings_hint,
                        backgroundOptionLabel,
                    )

                    else -> stringResource(R.string.permission_background_settings_hint_generic)
                }
                val backgroundRationale =
                    stringResource(R.string.permission_background_rationale) +
                        (if (settingsHint == null) "" else "\n\n$settingsHint")

                StageCard(
                    step = if (permissions.notificationsRequired) "Step 3" else "Step 2",
                    title = stringResource(R.string.permission_background_title),
                    granted = permissions.backgroundLocationGranted,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    enabled = permissions.foregroundLocationGranted,
                    disabledNote = stringResource(R.string.permission_warning_location_missing),
                    rationale = backgroundRationale,
                ) {
                    Button(
                        onClick = {
                            if (needsSettingsDeepLink) {
                                openSettings(listOf(appDetailsIntent(context)))
                            } else {
                                backgroundLocationLauncher.launch(
                                    Manifest.permission.ACCESS_BACKGROUND_LOCATION,
                                )
                            }
                        },
                        enabled = permissions.foregroundLocationGranted,
                    ) {
                        Text(
                            stringResource(
                                if (needsSettingsDeepLink) {
                                    R.string.permission_open_settings
                                } else {
                                    R.string.permission_background_action
                                },
                            ),
                        )
                    }
                }
            }

            StageCard(
                step = "Optional",
                title = stringResource(R.string.permission_battery_title),
                granted = permissions.batteryOptimisationIgnored,
                grantedLabel = grantedLabel,
                deniedLabel = deniedLabel,
                rationale = stringResource(R.string.permission_battery_rationale) + "\n\n" +
                    stringResource(R.string.permission_battery_optional),
            ) {
                Button(
                    onClick = {
                        openSettings(
                            listOf(
                                batteryExemptionIntent(context),
                                Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS),
                            ),
                        )
                    },
                ) {
                    Text(stringResource(R.string.permission_battery_action))
                }
                OutlinedButton(
                    onClick = {
                        openSettings(
                            listOf(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)),
                        )
                    },
                ) {
                    Text(stringResource(R.string.permission_open_settings))
                }
            }

            actionError?.let { message ->
                Card(
                    colors = CardDefaults.cardColors(
                        containerColor = MaterialTheme.colorScheme.errorContainer,
                        contentColor = MaterialTheme.colorScheme.onErrorContainer,
                    ),
                ) {
                    Text(
                        text = message,
                        modifier = Modifier.padding(16.dp),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }
            }

            SectionCard(title = "What is granted, and what it costs") {
                if (permissions.notificationsRequired) {
                    PermissionSummaryRow(
                        label = stringResource(R.string.status_permission_notifications),
                        granted = permissions.notificationsGranted,
                        grantedLabel = grantedLabel,
                        deniedLabel = deniedLabel,
                        costIfMissing = stringResource(
                            R.string.permission_warning_notifications_missing,
                        ),
                    )
                }
                PermissionSummaryRow(
                    label = stringResource(R.string.status_permission_location),
                    granted = permissions.foregroundLocationGranted,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    costIfMissing = stringResource(R.string.permission_warning_location_missing),
                )
                PermissionSummaryRow(
                    label = "Precise location",
                    granted = permissions.fineLocationGranted,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    costIfMissing = stringResource(R.string.permission_warning_coarse_only),
                )
                if (permissions.backgroundLocationRequired) {
                    PermissionSummaryRow(
                        label = stringResource(R.string.status_permission_background),
                        granted = permissions.backgroundLocationGranted,
                        grantedLabel = grantedLabel,
                        deniedLabel = deniedLabel,
                        costIfMissing = stringResource(
                            R.string.permission_warning_background_missing,
                        ),
                    )
                }
                PermissionSummaryRow(
                    label = stringResource(R.string.status_permission_battery),
                    granted = permissions.batteryOptimisationIgnored,
                    grantedLabel = grantedLabel,
                    deniedLabel = deniedLabel,
                    costIfMissing = stringResource(R.string.permission_battery_optional),
                )
            }

            Button(onClick = onContinue, modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.permission_continue))
            }
        }
    }
}

@Composable
private fun StageCard(
    step: String,
    title: String,
    granted: Boolean,
    grantedLabel: String,
    deniedLabel: String,
    rationale: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    disabledNote: String? = null,
    actions: @Composable () -> Unit,
) {
    Card(modifier = modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = step, style = MaterialTheme.typography.labelLarge)
                Text(
                    text = if (granted) grantedLabel else deniedLabel,
                    style = MaterialTheme.typography.labelLarge,
                    color = if (granted) {
                        MaterialTheme.colorScheme.primary
                    } else {
                        MaterialTheme.colorScheme.error
                    },
                )
            }
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
            Text(text = rationale, style = MaterialTheme.typography.bodyMedium)
            if (!enabled && disabledNote != null) {
                Text(
                    text = disabledNote,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                actions()
            }
        }
    }
}

@Composable
private fun PermissionSummaryRow(
    label: String,
    granted: Boolean,
    grantedLabel: String,
    deniedLabel: String,
    costIfMissing: String,
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
                text = costIfMissing,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

private fun appDetailsIntent(context: Context): Intent = Intent(
    Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
    Uri.fromParts("package", context.packageName, null),
)

// The direct request is the flow users expect; devices that refuse it fall back to the
// system-wide battery optimisation list, which never needs a permission of its own.
@SuppressLint("BatteryLife")
private fun batteryExemptionIntent(context: Context): Intent = Intent(
    Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
    Uri.parse("package:${context.packageName}"),
)
