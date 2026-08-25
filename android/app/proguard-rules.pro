# R8 configuration for the release build (minify + resource shrinking are on).
#
# Room, WorkManager, OkHttp and Retrofit all ship consumer rules inside their artifacts. The
# rules below cover the two things R8 cannot infer on its own: kotlinx.serialization's generated
# serializers, and the reflective type information Retrofit reads from suspend functions.

# --- Keep enough generic signature data for Retrofit's return types -----------------------
-keepattributes Signature, InnerClasses, EnclosingMethod
-keepattributes RuntimeVisibleAnnotations, RuntimeVisibleParameterAnnotations
-keepattributes AnnotationDefault

# --- Retrofit -----------------------------------------------------------------------------
# Retrofit builds the API implementation reflectively from the interface's annotations.
-keepclassmembers,allowshrinking,allowobfuscation interface * {
    @retrofit2.http.* <methods>;
}
-dontwarn org.codehaus.mojo.animal_sniffer.IgnoreJRERequirement
-dontwarn javax.annotation.**
-dontwarn kotlin.Unit
-dontwarn retrofit2.KotlinExtensions
-dontwarn retrofit2.KotlinExtensions$*

# --- OkHttp -------------------------------------------------------------------------------
# OkHttp probes for optional TLS providers that are not on the classpath here.
-dontwarn okhttp3.internal.platform.**
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**

# --- kotlinx.serialization ----------------------------------------------------------------
# Official rules: keep the compiler-generated serializer() entry points reachable.
-if @kotlinx.serialization.Serializable class **
-keepclassmembers class <1> {
    static <1>$Companion Companion;
}
-if @kotlinx.serialization.Serializable class ** {
    static **$* *;
}
-keepclassmembers class <2>$<3> {
    kotlinx.serialization.KSerializer serializer(...);
}
-if @kotlinx.serialization.Serializable class ** {
    public static ** INSTANCE;
}
-keepclassmembers class <1> {
    public static <1> INSTANCE;
    kotlinx.serialization.KSerializer serializer(...);
}
-keepclasseswithmembers class com.parentaltrack.child.data.remote.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# --- WorkManager --------------------------------------------------------------------------
# Workers are instantiated by name; the two-argument constructor must survive.
-keep public class * extends androidx.work.ListenableWorker {
    public <init>(android.content.Context, androidx.work.WorkerParameters);
}

# --- androidx.security-crypto / Tink ------------------------------------------------------
# Tink registers its key managers reflectively and references compile-only annotations.
-keep class com.google.crypto.tink.** { *; }
-dontwarn com.google.errorprone.annotations.**
-dontwarn javax.annotation.concurrent.**
