package com.parentaltrack.child.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController

/** Every destination in the app. There are no hidden screens. */
object Routes {
    const val CONSENT = "consent"
    const val PAIRING = "pairing"
    const val PERMISSIONS = "permissions"
    const val STATUS = "status"
}

/**
 * Navigation graph. The start destination is decided once, from the first loaded state:
 * consent -> pairing -> permissions -> status. From anywhere in the flow the status screen (which
 * carries the stop control) stays reachable in one tap.
 */
@Composable
fun AppNavHost(
    viewModel: MainViewModel,
    onExitApp: () -> Unit,
    modifier: Modifier = Modifier,
    navController: NavHostController = rememberNavController(),
) {
    val state by viewModel.state.collectAsStateWithLifecycle()

    if (state.isLoading) {
        Surface(modifier = modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        }
        return
    }

    // Captured once: later state changes must not yank the user to another screen mid-task.
    val startDestination = rememberSaveable { startDestinationFor(state) }

    NavHost(
        navController = navController,
        startDestination = startDestination,
        modifier = modifier,
    ) {
        composable(Routes.CONSENT) {
            ConsentScreen(
                onAccept = {
                    viewModel.acceptConsent()
                    navController.navigate(Routes.PAIRING) {
                        popUpTo(Routes.CONSENT) { inclusive = true }
                    }
                },
                onDecline = onExitApp,
            )
        }

        composable(Routes.PAIRING) {
            PairingScreen(
                pairing = state.pairing,
                canReturnToStatus = state.isPaired,
                onSubmit = viewModel::pair,
                onErrorDismissed = viewModel::clearPairingError,
                onPaired = {
                    viewModel.onPairingHandled()
                    navController.navigate(Routes.PERMISSIONS) {
                        popUpTo(Routes.PAIRING) { inclusive = true }
                    }
                },
                onBackToStatus = { navController.navigateToStatus() },
            )
        }

        composable(Routes.PERMISSIONS) {
            PermissionScreen(
                permissions = state.permissions,
                onRefresh = viewModel::refresh,
                onContinue = { navController.navigateToStatus() },
            )
        }

        composable(Routes.STATUS) {
            StatusScreen(
                state = state,
                onRefresh = viewModel::refresh,
                onStartSharing = viewModel::startSharing,
                onStopSharing = viewModel::stopSharing,
                onDismissStartFailure = viewModel::dismissStartFailure,
                onDismissServiceError = viewModel::dismissServiceError,
                onOpenPermissions = { navController.navigate(Routes.PERMISSIONS) },
                onUnpair = viewModel::unpair,
                onPairAgain = {
                    navController.navigate(Routes.PAIRING) {
                        popUpTo(Routes.STATUS) { inclusive = true }
                    }
                },
            )
        }
    }
}

/** Replaces any earlier status entry so the back stack cannot grow while the user toggles screens. */
private fun NavHostController.navigateToStatus() {
    navigate(Routes.STATUS) {
        launchSingleTop = true
        popUpTo(Routes.STATUS) { inclusive = true }
    }
}

private fun startDestinationFor(state: UiState): String = when {
    !state.consentAccepted -> Routes.CONSENT
    !state.isPaired -> Routes.PAIRING
    !state.permissions.canTrack || !state.permissions.backgroundLocationGranted -> Routes.PERMISSIONS
    else -> Routes.STATUS
}
