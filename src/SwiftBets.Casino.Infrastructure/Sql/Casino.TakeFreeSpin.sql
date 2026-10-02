-- Takes one spin from the grant that expires soonest.
WITH next AS (
    SELECT TOP (1) GrantId FROM casino.FreeSpinGrants WITH (UPDLOCK, ROWLOCK)
    WHERE PunterId = @PunterId AND GameId = @GameId AND Remaining > 0 AND ExpiresAt > @Now
    ORDER BY ExpiresAt)
UPDATE g SET Remaining = Remaining - 1
FROM casino.FreeSpinGrants g JOIN next ON next.GrantId = g.GrantId;
