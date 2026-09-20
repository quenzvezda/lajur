package com.lajur.app.security

import android.os.Build
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity

class BiometricUnlockController(private val activity: FragmentActivity) {
    var isPromptShowing: Boolean = false
        private set

    fun authenticate(
        allowDeviceCredential: Boolean,
        onSuccess: () -> Unit,
        onError: (String) -> Unit,
    ) {
        if (isPromptShowing) return
        val authenticators = authenticators(allowDeviceCredential)
        val availability = BiometricManager.from(activity).canAuthenticate(authenticators)
        if (availability != BiometricManager.BIOMETRIC_SUCCESS) {
            onError(availabilityMessage(availability))
            return
        }

        val prompt = BiometricPrompt(
            activity,
            ContextCompat.getMainExecutor(activity),
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                    isPromptShowing = false
                    onSuccess()
                }

                override fun onAuthenticationError(errorCode: Int, errString: CharSequence) {
                    isPromptShowing = false
                    onError(errString.toString())
                }
            },
        )
        val promptInfo = BiometricPrompt.PromptInfo.Builder()
            .setTitle("Buka Lajur")
            .setSubtitle("Konfirmasi identitas untuk melihat data keuangan")
            .setAllowedAuthenticators(authenticators)
            .setConfirmationRequired(false)
            .apply {
                if (!allowDeviceCredential) setNegativeButtonText("Batal")
            }
            .build()

        isPromptShowing = true
        prompt.authenticate(promptInfo)
    }

    private fun authenticators(allowDeviceCredential: Boolean): Int {
        if (!allowDeviceCredential) return BiometricManager.Authenticators.BIOMETRIC_STRONG
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            BiometricManager.Authenticators.BIOMETRIC_STRONG or
                BiometricManager.Authenticators.DEVICE_CREDENTIAL
        } else {
            // Android 10 and older cannot combine STRONG with DEVICE_CREDENTIAL.
            BiometricManager.Authenticators.BIOMETRIC_WEAK or
                BiometricManager.Authenticators.DEVICE_CREDENTIAL
        }
    }

    private fun availabilityMessage(code: Int): String = when (code) {
        BiometricManager.BIOMETRIC_ERROR_NONE_ENROLLED ->
            "Atur sidik jari atau kunci layar perangkat terlebih dahulu."
        BiometricManager.BIOMETRIC_ERROR_NO_HARDWARE ->
            "Perangkat ini tidak memiliki autentikator yang didukung."
        BiometricManager.BIOMETRIC_ERROR_HW_UNAVAILABLE ->
            "Autentikasi perangkat sedang tidak tersedia."
        else -> "Autentikasi perangkat tidak dapat digunakan (kode $code)."
    }
}
