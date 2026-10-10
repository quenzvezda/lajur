package com.lajur.app.ui

import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Button
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.lajur.app.BuildConfig
import com.lajur.app.update.AppUpdateState
import java.util.Locale

@Composable
internal fun AppUpdateRoute(viewModel: LajurViewModel, showAutoPreference: Boolean = false) {
    val state by viewModel.appUpdates.collectAsStateWithLifecycle()
    val context = LocalContext.current
    var canInstall by remember { mutableStateOf(context.packageManager.canRequestPackageInstalls()) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        canInstall = context.packageManager.canRequestPackageInstalls()
    }
    AppUpdateSection(state = state, showAutoPreference = showAutoPreference,
        canInstall = canInstall,
        onCheck = { viewModel.checkAppUpdates() },
        onDownload = viewModel::downloadAppUpdate,
        onCancel = viewModel::cancelAppUpdateDownload,
        onAutoCheck = viewModel::setAutomaticUpdates,
        onNotes = { state.release?.let { release ->
            runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(release.releaseUrl))) }
                .onFailure { viewModel.appUpdateInstallError() }
        } },
        onInstall = {
            canInstall = context.packageManager.canRequestPackageInstalls()
            if (!canInstall) {
                runCatching { permission.launch(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))) }
                    .onFailure { viewModel.appUpdateInstallError() }
            } else {
                viewModel.installAppUpdate { intent ->
                    runCatching { context.startActivity(intent) }.onFailure { viewModel.appUpdateInstallError() }
                }
            }
        })
}

@Composable
internal fun AppUpdateSection(state: AppUpdateState, showAutoPreference: Boolean = false,
    canInstall: Boolean = true, versionName: String = BuildConfig.VERSION_NAME,
    onCheck: () -> Unit, onDownload: () -> Unit, onCancel: () -> Unit,
    onAutoCheck: (Boolean) -> Unit, onNotes: () -> Unit, onInstall: () -> Unit) {
    Column(Modifier.fillMaxWidth().testTag("app-update-section"), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Versi aplikasi", style = MaterialTheme.typography.titleMedium)
                Text("Lajur $versionName", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            TextButton(onClick = onCheck, enabled = !state.checking && state.progress == null,
                modifier = Modifier.testTag("app-update-check")) {
                Text(if (state.checking) "Memeriksa…" else "Cek pembaruan")
            }
        }
        if (showAutoPreference && !BuildConfig.DEBUG) Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Cek otomatis setiap hari", Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
            Switch(state.autoCheck, onCheckedChange = onAutoCheck)
        }
        state.release?.let { release ->
            Text("Versi ${release.versionName} tersedia · ${String.format(Locale.forLanguageTag("id-ID"), "%.1f", release.sizeBytes / 1048576.0)} MB",
                style = MaterialTheme.typography.bodyMedium)
            state.incompatibility?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
            when {
                state.progress != null -> {
                    Text(if (state.progress == 100) "Memverifikasi APK…" else "Mengunduh… ${state.progress}%", style = MaterialTheme.typography.bodySmall)
                    LinearProgressIndicator(progress = { state.progress / 100f }, modifier = Modifier.fillMaxWidth())
                    TextButton(onClick = onCancel) { Text("Batalkan unduhan") }
                }
                state.ready -> {
                    Text("APK terverifikasi dan siap dipasang.", style = MaterialTheme.typography.bodySmall)
                    Button(onClick = onInstall, enabled = !state.checking, modifier = Modifier.testTag("app-update-install")) {
                        Text(if (canInstall) "Pasang pembaruan" else "Izinkan pemasangan")
                    }
                }
                else -> Row(verticalAlignment = Alignment.CenterVertically) {
                    if (state.incompatibility == null) Button(onClick = onDownload, enabled = !state.checking, modifier = Modifier.testTag("app-update-download")) { Text("Unduh APK") }
                    TextButton(onClick = onNotes) { Text("Catatan rilis") }
                }
            }
        }
        if (state.checked && state.release == null && state.error == null) Text("Aplikasi sudah versi terbaru.", style = MaterialTheme.typography.bodySmall)
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
    }
}
