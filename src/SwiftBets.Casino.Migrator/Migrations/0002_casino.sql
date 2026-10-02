-- Providers we integrate with; signing secrets and launch URLs live in configuration, not here.
CREATE TABLE casino.Providers
(
    ProviderId       varchar(40)  NOT NULL CONSTRAINT PK_Providers PRIMARY KEY,
    WalletModel      varchar(10)  NOT NULL CONSTRAINT CK_Providers_WalletModel CHECK (WalletModel IN ('seamless', 'transfer')),
    SigningSecretRef varchar(120) NOT NULL,
    Enabled          bit          NOT NULL CONSTRAINT DF_Providers_Enabled DEFAULT (1)
);

INSERT INTO casino.Providers (ProviderId, WalletModel, SigningSecretRef)
VALUES ('sim-seamless', 'seamless', 'Casino:Providers:sim-seamless:Secret'),
       ('sim-transfer', 'transfer', 'Casino:Providers:sim-transfer:Secret');

-- A game session; only the token's SHA-256 is stored.
CREATE TABLE casino.Sessions
(
    SessionId     uniqueidentifier  NOT NULL CONSTRAINT PK_Sessions PRIMARY KEY,
    TokenHash     binary(32)        NOT NULL CONSTRAINT UQ_Sessions_TokenHash UNIQUE,
    PunterId      uniqueidentifier  NOT NULL,
    ProviderId    varchar(40)       NOT NULL CONSTRAINT FK_Sessions_Providers REFERENCES casino.Providers (ProviderId),
    GameId        varchar(80)       NOT NULL,
    Currency      char(3)           NOT NULL,
    CreatedAt     datetimeoffset(3) NOT NULL,
    ExpiresAt     datetimeoffset(3) NOT NULL,
    ClosedAt      datetimeoffset(3) NULL,
    TransferredIn bigint            NOT NULL CONSTRAINT DF_Sessions_TransferredIn DEFAULT (0)
);
CREATE INDEX IX_Sessions_Punter ON casino.Sessions (PunterId, CreatedAt DESC);

-- Every provider callback applied, once: the provider's transaction id is the idempotency key.
CREATE TABLE casino.Transactions
(
    TransactionId                   uniqueidentifier  NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY,
    ProviderId                      varchar(40)       NOT NULL,
    ProviderTransactionId           varchar(100)      NOT NULL,
    RoundId                         varchar(100)      NOT NULL,
    PunterId                        uniqueidentifier  NOT NULL,
    GameId                          varchar(80)       NOT NULL,
    Kind                            tinyint           NOT NULL,
    Amount                          bigint            NOT NULL CONSTRAINT CK_Transactions_Amount CHECK (Amount >= 0),
    Currency                        char(3)           NOT NULL,
    Status                          tinyint           NOT NULL,
    ReferencesProviderTransactionId varchar(100)      NULL,
    CreatedAt                       datetimeoffset(3) NOT NULL,
    CONSTRAINT UQ_Transactions_Provider UNIQUE (ProviderId, ProviderTransactionId)
);
CREATE INDEX IX_Transactions_Reference ON casino.Transactions (ProviderId, ReferencesProviderTransactionId) WHERE ReferencesProviderTransactionId IS NOT NULL;
CREATE INDEX IX_Transactions_Created ON casino.Transactions (ProviderId, CreatedAt);

-- Free spins an operator granted; each free-spin bet takes one.
CREATE TABLE casino.FreeSpinGrants
(
    GrantId   uniqueidentifier  NOT NULL CONSTRAINT PK_FreeSpinGrants PRIMARY KEY,
    PunterId  uniqueidentifier  NOT NULL,
    GameId    varchar(80)       NOT NULL,
    Granted   int               NOT NULL CONSTRAINT CK_FreeSpinGrants_Granted CHECK (Granted > 0),
    Remaining int               NOT NULL CONSTRAINT CK_FreeSpinGrants_Remaining CHECK (Remaining >= 0),
    ExpiresAt datetimeoffset(3) NOT NULL,
    CreatedBy uniqueidentifier  NOT NULL,
    CreatedAt datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_FreeSpinGrants_Punter ON casino.FreeSpinGrants (PunterId, GameId, ExpiresAt);
