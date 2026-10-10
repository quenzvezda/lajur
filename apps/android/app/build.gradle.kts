plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
}

// Signing credentials come only from the trusted release job or the operator's environment.
val releaseStoreFile = providers.environmentVariable("LAJUR_SIGNING_STORE_FILE")
val releaseStorePassword = providers.environmentVariable("LAJUR_SIGNING_STORE_PASSWORD")
val releaseKeyAlias = providers.environmentVariable("LAJUR_SIGNING_KEY_ALIAS")
val releaseKeyPassword = providers.environmentVariable("LAJUR_SIGNING_KEY_PASSWORD")
val releaseVersionCode = providers.environmentVariable("LAJUR_VERSION_CODE").orElse("1").get().toInt()
val releaseVersionName = providers.environmentVariable("LAJUR_VERSION_NAME").orElse("1.0").get()
require(releaseVersionCode in 1..2_100_000_000) { "LAJUR_VERSION_CODE is outside Android's supported range." }
require(releaseVersionName.matches(Regex("[A-Za-z0-9.+_-]{1,100}"))) { "Invalid LAJUR_VERSION_NAME." }
val releaseApiUrl = providers.environmentVariable("LAJUR_RELEASE_API_BASE_URL").orElse("").get()
require(releaseApiUrl.isEmpty() || (releaseApiUrl.startsWith("https://") &&
    releaseApiUrl.matches(Regex("https://[A-Za-z0-9.-]+(?::[0-9]+)?/api/v1/")))) {
    "Release API URL must be an HTTPS /api/v1/ endpoint without credentials."
}

android {
    namespace = "com.lajur.app"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "com.lajur.app"
        minSdk = 26
        targetSdk = 37
        versionCode = releaseVersionCode
        versionName = releaseVersionName

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField(
            "String",
            "LAJUR_API_BASE_URL",
            "\"${providers.gradleProperty("LAJUR_API_BASE_URL").orElse("http://127.0.0.1:8080/api/v1/").get()}\""
        )
    }

    signingConfigs {
        if (releaseStoreFile.isPresent) {
            create("release") {
                storeFile = file(releaseStoreFile.get())
                storePassword = releaseStorePassword.get()
                keyAlias = releaseKeyAlias.get()
                keyPassword = releaseKeyPassword.get()
            }
        }
    }

    buildTypes {
        debug {
            manifestPlaceholders["usesCleartextTraffic"] = "true"
        }
        release {
            buildConfigField("String", "LAJUR_API_BASE_URL", "\"$releaseApiUrl\"")
            if (releaseStoreFile.isPresent) {
                signingConfig = signingConfigs.getByName("release")
            }
            manifestPlaceholders["usesCleartextTraffic"] = "false"
            optimization {
                enable = false
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    sourceSets {
        getByName("test").kotlin.directories.add("src/sharedTest/java")
        getByName("test").resources.directories.add("../../../tests/smart-scan-eval")
        getByName("androidTest").kotlin.directories.add("src/sharedTest/java")
        getByName("androidTest").assets.directories.add("../../../tests/smart-scan-eval")
    }
}

val validateReleaseSigning = tasks.register("validateReleaseSigning") {
    doLast {
        check(releaseStoreFile.isPresent && releaseStorePassword.isPresent &&
            releaseKeyAlias.isPresent && releaseKeyPassword.isPresent) {
            "Signed release requires the four LAJUR_SIGNING_* environment variables."
        }
        check(file(releaseStoreFile.get()).isFile) { "Release keystore does not exist." }
    }
}
tasks.matching { it.name == "preReleaseBuild" }.configureEach {
    dependsOn(validateReleaseSigning)
}

ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    implementation(libs.androidx.work.runtime.ktx)
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.androidx.biometric)
    implementation(libs.androidx.exifinterface)
    implementation(libs.mlkit.text.recognition)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.litert.lm.android)
    implementation(libs.okhttp)
    ksp(libs.androidx.room.compiler)
    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
    androidTestImplementation(libs.androidx.room.testing)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
