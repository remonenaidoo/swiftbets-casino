-- Returns 0 when inserted; 1 when a bet's refund is already recorded; 2 when an unseen-refund marker's bet is already recorded.
-- Both checks hold key-range locks, so a bet and its refund racing each other cannot both slip past.
IF @Kind IN (1, 4) AND EXISTS (SELECT 1 FROM casino.Transactions WITH (UPDLOCK, HOLDLOCK)
                               WHERE ProviderId = @ProviderId AND ReferencesProviderTransactionId = @ProviderTransactionId AND Kind = 3)
    SELECT 1;
ELSE IF @Status = 3 AND EXISTS (SELECT 1 FROM casino.Transactions WITH (UPDLOCK, HOLDLOCK)
                                WHERE ProviderId = @ProviderId AND ProviderTransactionId = @ReferencesProviderTransactionId)
    SELECT 2;
ELSE
BEGIN
    INSERT INTO casino.Transactions (TransactionId, ProviderId, ProviderTransactionId, RoundId, PunterId, GameId, Kind, Amount, Currency, Status, ReferencesProviderTransactionId, CreatedAt, Reply)
    VALUES (@TransactionId, @ProviderId, @ProviderTransactionId, @RoundId, @PunterId, @GameId, @Kind, @Amount, @Currency, @Status, @ReferencesProviderTransactionId, @CreatedAt, @Reply);
    SELECT 0;
END
