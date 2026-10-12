package com.lajur.app.ui

import com.lajur.app.data.CachedInsight

data class InsightState(
    val ownerKey: String? = null,
    val cached: CachedInsight? = null,
    val loading: Boolean = false,
    val offline: Boolean = false,
    val error: String? = null,
)

internal fun InsightState.forOwner(owner: String): InsightState = if (ownerKey == owner) this else InsightState()
