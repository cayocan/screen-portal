plugins {
    id("com.android.library")
}

android {
    namespace = "com.screenportal.video"
    // Igual ao compileSdk que a Unity usa no unityLibrary (android-34); maior que isso quebra o
    // CheckAarMetadata do build da Unity.
    compileSdk = 34
    // Mesma NDK que a Unity 6000.3.19f1 embarca (PlaybackEngines/AndroidPlayer/NDK).
    ndkVersion = "27.2.12479018"
    // tools/build-native.ps1 passa -PspNdkPath=<NDK da Unity>; sem ele, o AGP procura no SDK.
    providers.gradleProperty("spNdkPath").orNull?.let { ndkPath = it }

    defaultConfig {
        minSdk = 32 // igual ao Player Settings da Unity
        ndk { abiFilters += "arm64-v8a" }
        externalNativeBuild {
            cmake { arguments += listOf("-DANDROID_STL=c++_static") }
        }
        consumerProguardFiles("consumer-rules.pro")
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    buildTypes {
        release { isMinifyEnabled = false }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
        // O .aar não leva a kotlin-stdlib: sem asserções geradas que chamem kotlin.jvm.internal.Intrinsics.
        freeCompilerArgs.addAll("-Xno-param-assertions", "-Xno-call-assertions", "-Xno-receiver-assertions")
    }
}
