INSERT INTO casino.Sessions (SessionId, TokenHash, PunterId, ProviderId, GameId, Currency, CreatedAt, ExpiresAt)
VALUES (@SessionId, @TokenHash, @PunterId, @ProviderId, @GameId, @Currency, @CreatedAt, @ExpiresAt);
