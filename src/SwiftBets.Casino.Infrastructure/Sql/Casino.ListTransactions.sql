SELECT ProviderTransactionId, Kind, Amount
FROM casino.Transactions
WHERE ProviderId = @ProviderId AND CreatedAt >= @From AND CreatedAt < @To;
