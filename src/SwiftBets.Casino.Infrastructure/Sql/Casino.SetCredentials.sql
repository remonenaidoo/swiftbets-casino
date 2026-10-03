UPDATE casino.Providers
SET SecureLoginCipher = COALESCE(@SecureLoginCipher, SecureLoginCipher),
    SecretCipher = COALESCE(@SecretCipher, SecretCipher),
    UpdatedAt = @Now,
    UpdatedBy = @OperatorId
WHERE ProviderId = @ProviderId;
