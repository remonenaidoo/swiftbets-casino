SELECT CASE WHEN EXISTS (SELECT 1 FROM casino.ReconciliationRuns WHERE ProviderId = @ProviderId AND BusinessDate = @BusinessDate) THEN 1 ELSE 0 END;
