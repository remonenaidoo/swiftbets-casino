SELECT TransactionId, ProviderId, ProviderTransactionId, RoundId, PunterId, GameId, Kind, Amount, Currency, Status, ReferencesProviderTransactionId, CreatedAt, Reply
FROM casino.Transactions
WHERE ProviderId = @ProviderId AND ProviderTransactionId = @ProviderTransactionId;
