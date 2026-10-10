package com.lajur.app.ui

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.lajur.app.update.AppUpdateManifest
import com.lajur.app.update.AppUpdatePolicy
import com.lajur.app.update.AppUpdateState

/** Debug-only synthetic host; callbacks never download or install anything. */
class UpdateSmokeActivity : ComponentActivity() {
    val updateState = mutableStateOf(AppUpdateState(checked = true, release = AppUpdateManifest(
        1, 100006, "1.0.6", "com.lajur.app", 26,
        "https://github.com/quenzvezda/lajur/releases/download/v1.0.6/lajur-1.0.6.apk",
        "a".repeat(64), 54_000_000, AppUpdatePolicy.CERTIFICATE, "b".repeat(40),
        "https://github.com/quenzvezda/lajur/releases/tag/v1.0.6")))
    val dark = mutableStateOf(true)
    var downloads = 0
    var cancellations = 0
    var installations = 0

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme(colorScheme = if (dark.value) darkColorScheme() else lightColorScheme()) {
                Surface(Modifier.fillMaxSize()) {
                    Column(Modifier.padding(24.dp)) {
                        Text("Pengaturan", style = MaterialTheme.typography.headlineMedium)
                        AppUpdateSection(updateState.value, canInstall = false, versionName = "1.0.5",
                            onCheck = {}, onDownload = { downloads++ }, onCancel = { cancellations++ },
                            onAutoCheck = {}, onNotes = {}, onInstall = { installations++ })
                    }
                }
            }
        }
    }
}
