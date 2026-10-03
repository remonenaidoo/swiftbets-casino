SELECT ProviderId, WalletModel, Enabled, AllowedAddresses, ApiBaseUrl, DemoBaseUrl, SecureLoginCipher, SecretCipher, UpdatedAt
FROM casino.Providers
WHERE @ProviderId IS NULL OR ProviderId = @ProviderId
ORDER BY ProviderId;
