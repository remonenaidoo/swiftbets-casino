-- Operator overrides for a provider, set from the console: allowlist, API addresses, and credentials encrypted with AES-GCM.
ALTER TABLE casino.Providers ADD
    AllowedAddresses  nvarchar(2000)    NULL,
    ApiBaseUrl        varchar(300)      NULL,
    DemoBaseUrl       varchar(300)      NULL,
    SecureLoginCipher varbinary(600)    NULL,
    SecretCipher      varbinary(600)    NULL,
    UpdatedAt         datetimeoffset(3) NULL,
    UpdatedBy         uniqueidentifier  NULL;

INSERT INTO casino.Providers (ProviderId, WalletModel, SigningSecretRef)
VALUES ('pragmatic', 'seamless', 'Casino:Providers:pragmatic:Secret');

-- The exact reply first sent for a transaction; a repeated callback gets it back unchanged.
ALTER TABLE casino.Transactions ADD Reply nvarchar(1000) NULL;

-- Pragmatic names the player, not the token, after authenticate: find their newest session with the provider.
CREATE INDEX IX_Sessions_Punter_Provider ON casino.Sessions (PunterId, ProviderId, CreatedAt DESC);
