package com.parentaltrack.child.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

@Dao
interface PendingLocationDao {

    @Insert
    suspend fun insert(entity: PendingLocationEntity): Long

    /** Oldest queued fixes first, so the server receives them in chronological order. */
    @Query(
        "SELECT * FROM pending_locations ORDER BY recordedAtEpochMillis ASC, id ASC LIMIT :limit"
    )
    suspend fun oldestBatch(limit: Int): List<PendingLocationEntity>

    @Query("DELETE FROM pending_locations WHERE id IN (:ids)")
    suspend fun deleteByIds(ids: List<Long>): Int

    @Query("UPDATE pending_locations SET attemptCount = attemptCount + 1 WHERE id IN (:ids)")
    suspend fun incrementAttempts(ids: List<Long>): Int

    @Query("DELETE FROM pending_locations WHERE attemptCount >= :maxAttempts")
    suspend fun deleteExhausted(maxAttempts: Int): Int

    @Query("SELECT COUNT(*) FROM pending_locations")
    suspend fun count(): Int

    /** Live count for the status screen; LocationRepository.pendingCount binds to this. */
    @Query("SELECT COUNT(*) FROM pending_locations")
    fun countFlow(): Flow<Int>

    /** Enforces the queue cap: keeps the [max] newest rows and deletes everything older. */
    @Query(
        "DELETE FROM pending_locations WHERE id NOT IN (" +
            "SELECT id FROM pending_locations ORDER BY recordedAtEpochMillis DESC, id DESC LIMIT :max" +
            ")"
    )
    suspend fun deleteOldestBeyond(max: Int): Int

    @Query("DELETE FROM pending_locations")
    suspend fun deleteAll(): Int
}
