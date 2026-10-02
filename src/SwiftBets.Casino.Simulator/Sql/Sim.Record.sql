INSERT INTO simulator.transactions (provider_id, provider_transaction_id, kind, amount, currency, round_id, game_id, sent_at)
VALUES (@ProviderId, @ProviderTransactionId, @Kind, @Amount, @Currency, @RoundId, @GameId, @SentAt)
ON CONFLICT (provider_id, provider_transaction_id) DO NOTHING;
