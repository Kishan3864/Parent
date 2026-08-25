package com.parentaltrack.child.ui

import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import com.parentaltrack.child.R

/** Room for the display dash: "AB3D-9KMP". */
private const val MAX_TYPED_CODE_LENGTH = PAIRING_CODE_LENGTH + 1

/**
 * Enrollment. The parent creates the device on their dashboard and reads out the 8-character code;
 * on success the device token is stored and the permission flow starts.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PairingScreen(
    pairing: PairingUiState,
    canReturnToStatus: Boolean,
    onSubmit: (String) -> Unit,
    onErrorDismissed: () -> Unit,
    onPaired: () -> Unit,
    onBackToStatus: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var code by rememberSaveable { mutableStateOf("") }
    val focusRequester = remember { FocusRequester() }
    val canSubmit = normalisePairingCode(code).length == PAIRING_CODE_LENGTH && !pairing.isSubmitting

    LaunchedEffect(pairing.succeeded) {
        if (pairing.succeeded) onPaired()
    }

    LaunchedEffect(Unit) {
        focusRequester.requestFocus()
    }

    Scaffold(
        modifier = modifier.fillMaxSize(),
        topBar = { TopAppBar(title = { Text(stringResource(R.string.pairing_title)) }) },
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 12.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(
                text = stringResource(R.string.pairing_body),
                style = MaterialTheme.typography.bodyLarge,
            )

            OutlinedTextField(
                value = code,
                onValueChange = { typed ->
                    if (pairing.error != null) onErrorDismissed()
                    code = typed
                        .filter { it.isLetterOrDigit() || it == '-' }
                        .uppercase()
                        .take(MAX_TYPED_CODE_LENGTH)
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .focusRequester(focusRequester),
                enabled = !pairing.isSubmitting,
                label = { Text(stringResource(R.string.pairing_code_label)) },
                placeholder = { Text(stringResource(R.string.pairing_code_hint)) },
                singleLine = true,
                isError = pairing.error != null,
                supportingText = {
                    Text(
                        text = pairing.error?.let { stringResource(it.messageRes()) }
                            ?: stringResource(R.string.pairing_code_helper),
                    )
                },
                keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Characters,
                    imeAction = ImeAction.Done,
                ),
                keyboardActions = KeyboardActions(onDone = { if (canSubmit) onSubmit(code) }),
            )

            Button(
                onClick = { onSubmit(code) },
                modifier = Modifier.fillMaxWidth(),
                enabled = canSubmit,
            ) {
                if (pairing.isSubmitting) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(18.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary,
                    )
                } else {
                    Text(
                        stringResource(
                            if (pairing.error == null) R.string.pairing_submit
                            else R.string.pairing_retry,
                        ),
                    )
                }
            }

            if (pairing.isSubmitting) {
                Text(
                    text = stringResource(R.string.pairing_in_progress),
                    style = MaterialTheme.typography.bodySmall,
                )
            }

            Text(
                text = "Pairing stores an access token on this phone so it can send locations to " +
                    "that parent account, and nothing else. You can unpair later from the main " +
                    "screen.",
                style = MaterialTheme.typography.bodySmall,
            )

            if (canReturnToStatus) {
                TextButton(onClick = onBackToStatus, modifier = Modifier.fillMaxWidth()) {
                    Text("Back to sharing status")
                }
            }
        }
    }
}

@StringRes
private fun PairingError.messageRes(): Int = when (this) {
    PairingError.INCOMPLETE -> R.string.pairing_error_incomplete
    PairingError.INVALID_CODE -> R.string.pairing_error_invalid
    PairingError.NETWORK -> R.string.pairing_error_network
    PairingError.SERVER -> R.string.pairing_error_server
    PairingError.UNKNOWN -> R.string.pairing_error_unknown
}
