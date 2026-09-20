package com.lajur.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import com.lajur.app.ui.LajurApp
import com.lajur.app.ui.LajurViewModel
import com.lajur.app.ui.theme.LajurTheme

class MainActivity : ComponentActivity() {
    private val viewModel: LajurViewModel by viewModels {
        LajurViewModel.Factory((application as LajurApplication).container)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            LajurTheme {
                LajurApp(viewModel)
            }
        }
    }
}
