package com.lajur.app.ui

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.lightColorScheme
import androidx.lifecycle.ViewModelProvider
import com.lajur.app.AppContainer

/** Debug-only, non-exported host for opt-in local account flow tests. */
class AccountSmokeActivity : ComponentActivity() {
    lateinit var viewModel: LajurViewModel
        private set
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        viewModel = ViewModelProvider(this, LajurViewModel.Factory(AppContainer(this)))[LajurViewModel::class.java]
        setContent {
            MaterialTheme(colorScheme = lightColorScheme()) {
                Surface { AccountRoute(viewModel, false, intent.getStringExtra("mode") ?: "register") { finish() } }
            }
        }
    }
}
