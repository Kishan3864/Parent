plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
}

android {
    namespace = "com.parentaltrack.child"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.parentaltrack.child"
        minSdk = 24
        targetSdk = 35
        versionCode = 1
        versionName = "1.0.0"

        // Consumed by android:usesCleartextTraffic in AndroidManifest.xml. Secure by default;
        // only the debug build type relaxes it (see below).
        manifestPlaceholders["usesCleartextTraffic"] = "false"
    }

    buildTypes {
        debug {
            isMinifyEnabled = false
            // Emulator alias for the host machine's loopback interface.
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:5080/\"")
            buildConfigField("boolean", "ALLOW_CLEARTEXT", "true")
            manifestPlaceholders["usesCleartextTraffic"] = "true"
        }
        // The build to sideload onto a child's phone for this MVP. It points at the live server
        // like release does, but is signed with the debug key so it installs without any keystore
        // ceremony - and unlike debug it is neither debuggable nor allowed to use cleartext.
        // Replace it with a properly signed release build before distributing the app.
        create("live") {
            initWith(getByName("debug"))
            isDebuggable = false
            isMinifyEnabled = false
            buildConfigField("String", "API_BASE_URL", "\"https://parent.flexypdf.com/\"")
            buildConfigField("boolean", "ALLOW_CLEARTEXT", "false")
            manifestPlaceholders["usesCleartextTraffic"] = "false"
            matchingFallbacks += listOf("debug")
        }
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
            // The single value to change when pointing the app at a real server.
            buildConfigField("String", "API_BASE_URL", "\"https://parent.flexypdf.com/\"")
            buildConfigField("boolean", "ALLOW_CLEARTEXT", "false")
            manifestPlaceholders["usesCleartextTraffic"] = "false"
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    packaging {
        resources {
            excludes += setOf(
                "/META-INF/{AL2.0,LGPL2.1}",
                "/META-INF/DEPENDENCIES",
                "/META-INF/INDEX.LIST",
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

// Room is the only KSP consumer in this project (no Hilt, no other annotation processors).
ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
    arg("room.generateKotlin", "true")
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.navigation.compose)

    val composeBom = platform(libs.androidx.compose.bom)
    implementation(composeBom)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.foundation)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)

    // Supplies the XML theme parent (Theme.Material3.DayNight.NoActionBar) used by the activity window.
    implementation(libs.google.android.material)

    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)

    implementation(libs.androidx.work.runtime.ktx)
    implementation(libs.androidx.security.crypto)
    implementation(libs.play.services.location)

    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization.converter)
    implementation(libs.okhttp)
    implementation(libs.okhttp.logging.interceptor)
    implementation(libs.kotlinx.serialization.json)

    implementation(libs.kotlinx.coroutines.android)
}
