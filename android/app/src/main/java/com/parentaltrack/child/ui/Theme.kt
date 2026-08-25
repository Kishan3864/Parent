package com.parentaltrack.child.ui

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp

private val Teal40 = Color(0xFF00696E)
private val Teal90 = Color(0xFF9CF1F5)
private val Teal10 = Color(0xFF002022)
private val Slate40 = Color(0xFF4A6365)
private val Slate90 = Color(0xFFCCE8EA)
private val Slate10 = Color(0xFF051F21)
private val Amber40 = Color(0xFF6B5D00)
private val Amber90 = Color(0xFFF7E360)
private val Amber10 = Color(0xFF201C00)
private val Red40 = Color(0xFFBA1A1A)
private val Red90 = Color(0xFFFFDAD6)
private val Red80 = Color(0xFFFFB4AB)
private val Red20 = Color(0xFF690005)
private val Red10 = Color(0xFF410002)
private val Surface = Color(0xFFFAFDFC)
private val SurfaceDark = Color(0xFF0E1415)
private val OnSurface = Color(0xFF191C1D)
private val OnSurfaceDark = Color(0xFFE0E3E3)

private val LightColors = lightColorScheme(
    primary = Teal40,
    onPrimary = Color.White,
    primaryContainer = Teal90,
    onPrimaryContainer = Teal10,
    secondary = Slate40,
    onSecondary = Color.White,
    secondaryContainer = Slate90,
    onSecondaryContainer = Slate10,
    tertiary = Amber40,
    onTertiary = Color.White,
    tertiaryContainer = Amber90,
    onTertiaryContainer = Amber10,
    error = Red40,
    onError = Color.White,
    errorContainer = Red90,
    onErrorContainer = Red10,
    background = Surface,
    onBackground = OnSurface,
    surface = Surface,
    onSurface = OnSurface,
)

private val DarkColors = darkColorScheme(
    primary = Teal90,
    onPrimary = Teal10,
    primaryContainer = Color(0xFF004F53),
    onPrimaryContainer = Teal90,
    secondary = Slate90,
    onSecondary = Slate10,
    secondaryContainer = Color(0xFF324B4D),
    onSecondaryContainer = Slate90,
    tertiary = Amber90,
    onTertiary = Amber10,
    tertiaryContainer = Color(0xFF514600),
    onTertiaryContainer = Amber90,
    error = Red80,
    onError = Red20,
    errorContainer = Color(0xFF93000A),
    onErrorContainer = Red90,
    background = SurfaceDark,
    onBackground = OnSurfaceDark,
    surface = SurfaceDark,
    onSurface = OnSurfaceDark,
)

/**
 * Material 3 theme. Uses the wallpaper-derived dynamic palette on API 31+ and falls back to a
 * fixed teal palette (light and dark) everywhere else.
 */
@Composable
fun ParentalTrackTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = true,
    content: @Composable () -> Unit,
) {
    val colorScheme = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            val context = LocalContext.current
            if (darkTheme) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        }

        darkTheme -> DarkColors
        else -> LightColors
    }
    MaterialTheme(colorScheme = colorScheme, content = content)
}

/**
 * Shared layout atoms used by every screen, kept next to the theme so the four screens stay
 * visually consistent without a separate design-system module.
 */
@Composable
fun SectionCard(
    title: String,
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    Card(modifier = modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(text = title, style = MaterialTheme.typography.titleMedium)
            content()
        }
    }
}

/** A plain bullet list. Text is selectable-sized for readability, not decoration. */
@Composable
fun BulletList(items: List<String>, modifier: Modifier = Modifier) {
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        items.forEach { item ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(text = "•", style = MaterialTheme.typography.bodyMedium)
                Text(text = item, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}
