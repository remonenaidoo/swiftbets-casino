SELECT SessionId, TokenHash, PunterId, ProviderId, GameId, Currency, CreatedAt, ExpiresAt
FROM casino.Sessions
WHERE TokenHash = @TokenHash AND ClosedAt IS NULL;
