SELECT provider_transaction_id AS ProviderTransactionId, kind AS Kind, amount AS Amount
FROM simulator.transactions
WHERE provider_id = @ProviderId AND sent_at >= @From AND sent_at < @To
ORDER BY sent_at, provider_transaction_id;
