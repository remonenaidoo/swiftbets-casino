SELECT CAST(COUNT(*) AS bit) FROM casino.Transactions
WHERE ProviderId = @ProviderId AND ReferencesProviderTransactionId = @Reference AND Kind = 3;
