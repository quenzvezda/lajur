package com.lajur.app.ui

import android.os.SystemClock
import androidx.activity.compose.BackHandler
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner

/** Toolbar and system Back share the same route and exit policy. */
@Composable
internal fun rememberAppBackAction(
    screen: AppScreen,
    onNavigate: (AppScreen) -> Unit,
    onExit: () -> Unit,
    onExitPrompt: () -> Unit,
    clockMillis: () -> Long = SystemClock::elapsedRealtime,
): () -> Unit {
    val exitGate = remember { ExitBackPressGate() }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    DisposableEffect(lifecycle) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) exitGate.reset()
        }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer) }
    }
    LaunchedEffect(screen) { exitGate.reset() }
    val action: () -> Unit = {
        val target = screen.backTarget()
        if (target != null) onNavigate(target)
        else if (exitGate.press(clockMillis())) onExit()
        else onExitPrompt()
    }
    // Register before child screens: modal sheets and the workbook busy guard take priority.
    BackHandler(onBack = action)
    return action
}
