UPDATE casino.Providers
SET Enabled = COALESCE(@Enabled, Enabled),
    AllowedAddresses = CASE WHEN @SetAllowlist = 1 THEN @AllowedAddresses ELSE AllowedAddresses END,
    ApiBaseUrl = COALESCE(@ApiBaseUrl, ApiBaseUrl),
    DemoBaseUrl = COALESCE(@DemoBaseUrl, DemoBaseUrl),
    UpdatedAt = @Now,
    UpdatedBy = @OperatorId
WHERE ProviderId = @ProviderId;
