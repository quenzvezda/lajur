package com.lajur.app.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [LocalTransactionEntity::class, OutboxEntity::class, MasterCacheEntity::class],
    version = 1,
    exportSchema = true,
)
abstract class LajurDatabase : RoomDatabase() {
    abstract fun dao(): LajurDao

    companion object {
        fun create(context: Context): LajurDatabase = Room.databaseBuilder(
            context.applicationContext,
            LajurDatabase::class.java,
            "lajur.db",
        ).build()
    }
}
