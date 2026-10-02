SELECT GrantId, PunterId, GameId, Granted, Remaining, ExpiresAt
FROM casino.FreeSpinGrants
WHERE PunterId = @PunterId
ORDER BY ExpiresAt DESC;
