package com.parentaltrack.child.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [PendingLocationEntity::class],
    version = 1,
    // ksp room.schemaLocation is configured in build.gradle.kts, so the v1 schema is checked in.
    exportSchema = true,
)
abstract class AppDatabase : RoomDatabase() {

    abstract fun pendingLocationDao(): PendingLocationDao

    companion object {
        const val DATABASE_NAME = "parentaltrack.db"

        @Volatile
        private var instance: AppDatabase? = null

        fun getInstance(context: Context): AppDatabase =
            instance ?: synchronized(this) {
                instance ?: build(context.applicationContext).also { instance = it }
            }

        private fun build(appContext: Context): AppDatabase =
            Room.databaseBuilder(appContext, AppDatabase::class.java, DATABASE_NAME)
                // The only table is a transient upload buffer: at most a few thousand fixes that
                // are deleted as soon as the server accepts them. Losing a queued batch on a
                // schema change is preferable to shipping migrations for throwaway rows.
                .fallbackToDestructiveMigration(dropAllTables = true)
                .build()
    }
}
