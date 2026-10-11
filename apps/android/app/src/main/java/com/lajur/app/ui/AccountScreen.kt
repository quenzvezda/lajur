package com.lajur.app.ui

import android.app.Activity
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Context
import android.os.Build
import android.os.PersistableBundle
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountRoute(viewModel: LajurViewModel, signedIn: Boolean, initialMode: String, onBack: () -> Unit) {
    val state by viewModel.account.collectAsStateWithLifecycle()
    var mode by remember { mutableStateOf(initialMode) }
    var server by remember { mutableStateOf(viewModel.defaultServerUrl) }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var confirmation by remember { mutableStateOf("") }
    var currentPassword by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    val context = LocalContext.current
    DisposableEffect(Unit) {
        viewModel.setAccountScreenVisible(true)
        val window = (context as? Activity)?.window
        val alreadySecure = window?.attributes?.flags?.and(WindowManager.LayoutParams.FLAG_SECURE) != 0
        window?.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        onDispose {
            viewModel.setAccountScreenVisible(false)
            if (!alreadySecure) window?.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
        }
    }
    LaunchedEffect(state.busy) {
        if (!state.busy) { password = ""; confirmation = ""; currentPassword = ""; code = "" }
    }
    val leave = { if (!state.busy) { viewModel.clearAccountResult(); onBack() } }
    BackHandler { leave() }
    Scaffold(topBar = { TopAppBar(title = { Text(if (signedIn) "Keamanan akun" else if (mode == "register") "Buat akun" else "Pulihkan akun") },
        navigationIcon = { TextButton(onClick = leave, enabled = !state.busy) { Text("Kembali") } }) }) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).imePadding().verticalScroll(rememberScrollState()).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            state.message?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
            state.error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            if (state.codes.isNotEmpty() || state.invitation != null) {
                val content = state.codes.joinToString("\n").ifEmpty { state.invitation.orEmpty() }
                Text(if (state.codes.isNotEmpty()) "Simpan kode pemulihan" else "Bagikan kode undangan", style = MaterialTheme.typography.titleLarge)
                Text(if (state.codes.isNotEmpty()) "Hanya ditampilkan sekarang. Simpan di password manager atau tempat aman di luar HP ini. Siapa pun yang memiliki kode dapat mengganti password Anda."
                    else "Undangan hanya membuka akun baru; pengundang tidak mendapat akses ke buku pengguna lain.")
                SelectionContainer { Text(content, style = MaterialTheme.typography.bodyLarge) }
                OutlinedButton(onClick = { copyAccountSecret(context, content) }, modifier = Modifier.fillMaxWidth()) { Text("Salin kode") }
                Button(onClick = leave, modifier = Modifier.fillMaxWidth()) { Text("Sudah disimpan, selesai") }
            } else if (!signedIn && state.message != null) {
                Button(onClick = leave, modifier = Modifier.fillMaxWidth()) { Text("Kembali masuk") }
            } else {
                if (!signedIn) {
                    OutlinedTextField(server, { server = it }, label = { Text("Alamat server") }, singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                    OutlinedTextField(username, { username = it }, label = { Text("Username") }, singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                    OutlinedTextField(code, { code = it }, label = { Text(if (mode == "register") "Kode undangan" else "Kode pemulihan") }, singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                } else {
                    OutlinedTextField(currentPassword, { currentPassword = it }, label = { Text("Password saat ini") }, visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password), singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                    Text("Kode pemulihan baru membatalkan set lama. Simpan di tempat aman.")
                    OutlinedButton(onClick = { viewModel.createRecoveryCodes(currentPassword) }, enabled = !state.busy && currentPassword.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("Buat kode pemulihan baru") }
                    OutlinedButton(onClick = { viewModel.createInvitation(currentPassword) }, enabled = !state.busy && currentPassword.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("Buat undangan · 7 hari") }
                    HorizontalDivider()
                    Text("Ubah password", style = MaterialTheme.typography.titleLarge)
                }
                OutlinedTextField(password, { password = it }, label = { Text(if (mode == "register" && !signedIn) "Password · minimal 12 karakter" else "Password baru · minimal 12 karakter") }, visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password), singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(confirmation, { confirmation = it }, label = { Text("Ulangi password") }, visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password), singleLine = true, enabled = !state.busy, modifier = Modifier.fillMaxWidth())
                if (signedIn || mode == "recover") Text("Password diganti akan mengakhiri seluruh sesi. Data keuangan dan antrean lokal tetap tersimpan.")
                Button(onClick = {
                    if (signedIn) viewModel.changeAccountPassword(currentPassword, password)
                    else if (mode == "register") viewModel.registerAccount(server, username, password, code)
                    else viewModel.recoverAccount(server, username, code, password)
                }, enabled = !state.busy && validNewAccountPassword(password, confirmation) &&
                    (if (signedIn) currentPassword.isNotBlank() else username.isNotBlank() && code.isNotBlank()), modifier = Modifier.fillMaxWidth()) {
                    if (state.busy) CircularProgressIndicator(Modifier.size(20.dp))
                    else Text(if (!signedIn && mode == "register") "Daftar" else "Ganti password")
                }
            }
        }
    }
}

private fun copyAccountSecret(context: Context, content: String) {
    val clip = ClipData.newPlainText("Kode akun Lajur", content)
    if (Build.VERSION.SDK_INT >= 33) clip.description.extras = PersistableBundle().apply {
        putBoolean(ClipDescription.EXTRA_IS_SENSITIVE, true)
    }
    (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(clip)
}
