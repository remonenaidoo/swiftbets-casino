UPDATE casino.Transactions SET Status = 2
WHERE ProviderId = @ProviderId AND ProviderTransactionId = @Reference AND Status = 1;
