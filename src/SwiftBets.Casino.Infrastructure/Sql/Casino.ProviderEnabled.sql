SELECT CAST(COUNT(*) AS bit) FROM casino.Providers WHERE ProviderId = @ProviderId AND Enabled = 1;
