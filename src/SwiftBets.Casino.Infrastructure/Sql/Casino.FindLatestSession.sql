SELECT TOP (1) SessionId, TokenHash, PunterId, ProviderId, GameId, Currency, CreatedAt, ExpiresAt
FROM casino.Sessions
WHERE PunterId = @PunterId AND ProviderId = @ProviderId AND ClosedAt IS NULL
ORDER BY CreatedAt DESC;
