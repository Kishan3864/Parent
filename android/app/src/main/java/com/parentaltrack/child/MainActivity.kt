package com.parentaltrack.child

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import com.parentaltrack.child.ui.AppNavHost
import com.parentaltrack.child.ui.MainViewModel
import com.parentaltrack.child.ui.ParentalTrackTheme

/** The single, always-visible launcher activity. There is no hidden or alternative entry point. */
class MainActivity : ComponentActivity() {

    private val viewModel: MainViewModel by viewModels { MainViewModel.Factory }

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        setContent {
            ParentalTrackTheme {
                AppNavHost(
                    viewModel = viewModel,
                    onExitApp = { finishAndRemoveTask() },
                )
            }
        }
    }
}
