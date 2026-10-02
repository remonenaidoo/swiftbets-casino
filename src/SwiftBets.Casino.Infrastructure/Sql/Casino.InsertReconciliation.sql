INSERT INTO casino.ReconciliationRuns (RunId, ProviderId, BusinessDate, OurNet, ProviderNet, Drift, MissingOnOurSide, MissingOnProviderSide, Status, Currency, ReconciledAt)
VALUES (@RunId, @ProviderId, @BusinessDate, @OurNet, @ProviderNet, @Drift, @MissingOnOurSide, @MissingOnProviderSide, @Status, @Currency, @ReconciledAt);
