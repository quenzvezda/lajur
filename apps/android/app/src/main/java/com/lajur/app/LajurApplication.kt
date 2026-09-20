package com.lajur.app

import android.app.Application

class LajurApplication : Application() {
    val container: AppContainer by lazy { AppContainer(this) }
}
