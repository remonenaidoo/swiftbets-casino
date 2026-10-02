SELECT TOP (@Limit) RunId, ProviderId, BusinessDate, OurNet, ProviderNet, Drift, MissingOnOurSide, MissingOnProviderSide, Status, Currency, ReconciledAt
FROM casino.ReconciliationRuns
WHERE @ProviderId IS NULL OR ProviderId = @ProviderId
ORDER BY BusinessDate DESC, ReconciledAt DESC;
