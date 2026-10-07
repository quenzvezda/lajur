package com.lajur.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import com.lajur.app.R

@Composable
internal fun MainNavigationBar(selected: MainTab, hasPendingImage: Boolean, onSelect: (MainTab) -> Unit) {
    NavigationBar {
        MainTab.entries.forEach { tab ->
            NavigationBarItem(
                selected = selected == tab,
                onClick = { onSelect(tab) },
                modifier = Modifier.testTag("nav-${tab.name.lowercase()}"),
                label = { Text(tab.label, maxLines = 1) },
                icon = {
                    BadgedBox(badge = { if (tab == MainTab.SCAN && hasPendingImage) Badge() }) {
                        val scan = tab == MainTab.SCAN
                        Box(
                            Modifier.size(if (scan) 42.dp else 28.dp).then(
                                if (scan) Modifier.background(MaterialTheme.colorScheme.primary, RoundedCornerShape(14.dp))
                                else Modifier
                            ), contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                painterResource(when (tab) {
                                    MainTab.HOME -> R.drawable.ic_nav_home
                                    MainTab.HISTORY -> R.drawable.ic_nav_history
                                    MainTab.SCAN -> R.drawable.ic_nav_scan
                                    MainTab.INSIGHTS -> R.drawable.ic_nav_insights
                                    MainTab.SETTINGS -> R.drawable.ic_nav_settings
                                }), contentDescription = null, modifier = Modifier.size(24.dp),
                                tint = if (scan) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                },
            )
        }
    }
}
