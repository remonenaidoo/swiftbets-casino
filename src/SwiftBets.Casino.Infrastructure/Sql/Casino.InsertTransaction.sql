INSERT INTO casino.Transactions (TransactionId, ProviderId, ProviderTransactionId, RoundId, PunterId, GameId, Kind, Amount, Currency, Status, ReferencesProviderTransactionId, CreatedAt)
VALUES (@TransactionId, @ProviderId, @ProviderTransactionId, @RoundId, @PunterId, @GameId, @Kind, @Amount, @Currency, @Status, @ReferencesProviderTransactionId, @CreatedAt);
