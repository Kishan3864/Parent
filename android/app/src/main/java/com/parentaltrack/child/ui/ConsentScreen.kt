package com.parentaltrack.child.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.parentaltrack.child.R

/**
 * First-launch disclosure. Nothing is requested, stored or started before this is accepted.
 *
 * The wording is deliberately complete: what is collected, who sees it, that it runs continuously
 * in the background behind a permanent notification, that it can be stopped at any time, and an
 * explicit list of what the app never touches.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConsentScreen(
    onAccept: () -> Unit,
    onDecline: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = { TopAppBar(title = { Text(stringResource(R.string.consent_title)) }) },
        bottomBar = {
            Surface(tonalElevation = 3.dp) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Button(onClick = onAccept, modifier = Modifier.fillMaxWidth()) {
                        Text(stringResource(R.string.consent_accept))
                    }
                    TextButton(onClick = onDecline, modifier = Modifier.fillMaxWidth()) {
                        Text(stringResource(R.string.consent_decline))
                    }
                    Text(
                        text = stringResource(R.string.consent_declined_message),
                        style = MaterialTheme.typography.bodySmall,
                        textAlign = TextAlign.Center,
                    )
                }
            }
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
                text = stringResource(R.string.consent_body),
                style = MaterialTheme.typography.bodyLarge,
            )

            Card(modifier = Modifier.fillMaxWidth()) {
                BulletList(
                    items = listOf(
                        stringResource(R.string.consent_point_location),
                        stringResource(R.string.consent_point_background),
                        stringResource(R.string.consent_point_notification),
                        stringResource(R.string.consent_point_stop),
                    ),
                    modifier = Modifier.padding(16.dp),
                )
            }

            SectionCard(title = "What is collected") {
                BulletList(
                    listOf(
                        "Where this device is: the latitude and longitude of each fix",
                        "How accurate that fix is, in metres",
                        "The time the fix was taken",
                        "The battery level and whether the phone is charging",
                        "A device id created when this phone is paired, plus its make, model, " +
                            "Android version and app version",
                    ),
                )
            }

            SectionCard(title = "What is never collected") {
                Text(
                    text = stringResource(R.string.consent_point_data),
                    style = MaterialTheme.typography.bodyMedium,
                )
                BulletList(
                    listOf(
                        "No calls or call history, and no SMS or messages of any kind",
                        "No microphone, camera, photos or screen contents",
                        "No contacts, files or documents",
                        "No browsing history, search history or keystrokes",
                    ),
                )
                Text(
                    text = "The app does not ask Android for any of these permissions, so it " +
                        "could not collect them even if it tried. Location history is deleted " +
                        "from the server automatically after 90 days.",
                    style = MaterialTheme.typography.bodyMedium,
                )
            }

            Text(
                text = stringResource(R.string.consent_footer),
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}
