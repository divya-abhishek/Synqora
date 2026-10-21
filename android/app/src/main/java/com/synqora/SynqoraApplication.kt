package com.synqora

import android.app.Application
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.lifecycle.ViewModelStoreOwner
import com.synqora.data.SynqoraState

/** The session outlives individual Activities; the foreground service owns capture. */
class SynqoraApplication : Application(), ViewModelStoreOwner {
    override val viewModelStore = ViewModelStore()
    val state: SynqoraState by lazy {
        ViewModelProvider(this, ViewModelProvider.AndroidViewModelFactory.getInstance(this))[SynqoraState::class.java]
    }
}
