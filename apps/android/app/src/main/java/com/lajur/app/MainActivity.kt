package com.lajur.app

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.Build
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.view.WindowCompat
import androidx.fragment.app.FragmentActivity
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import android.view.WindowManager
import com.lajur.app.security.BiometricUnlockController
import com.lajur.app.ui.LajurApp
import com.lajur.app.ui.LajurViewModel
import com.lajur.app.ui.theme.LajurTheme
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

class MainActivity : FragmentActivity() {
    private val viewModel: LajurViewModel by viewModels {
        LajurViewModel.Factory((application as LajurApplication).container)
    }
    private lateinit var biometricController: BiometricUnlockController

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        applyScreenProtection(enabled = true)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        biometricController = BiometricUnlockController(this)
        enableEdgeToEdge()
        acceptSharedImage(intent)
        setContent {
            LajurTheme {
                LajurApp(viewModel, biometricController::authenticate)
            }
        }
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.CREATED) {
                viewModel.appLockSettings
                    .map { it.screenProtectionEnabled }
                    .distinctUntilChanged()
                    .collect(::applyScreenProtection)
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        acceptSharedImage(intent)
    }

    override fun onStart() {
        super.onStart()
        viewModel.onAppForegrounded()
    }

    override fun onStop() {
        viewModel.onAppBackgrounded()
        super.onStop()
    }

    private fun applyScreenProtection(enabled: Boolean) {
        if (enabled) {
            window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            setRecentsScreenshotEnabled(!enabled)
        }
    }

    @Suppress("DEPRECATION")
    private fun acceptSharedImage(intent: Intent?) {
        if (intent?.action != Intent.ACTION_SEND || intent.type?.startsWith("image/") != true) return
        val uri = if (android.os.Build.VERSION.SDK_INT >= 33) {
            intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            intent.getParcelableExtra(Intent.EXTRA_STREAM)
        }
        uri?.let { viewModel.onSharedImage(it.toString()) }
    }
}
