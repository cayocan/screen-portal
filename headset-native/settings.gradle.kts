pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
    plugins {
        // Mesma versão do AGP dos templates da Unity 6000.3.19f1 (Tools/GradleTemplates).
        id("com.android.library") version "9.0.0"
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "screenportal-native"
include(":screenportal")
