-- One row per reconciliation of a provider's business day; a day can be reconciled again, the latest run counts.
CREATE TABLE casino.ReconciliationRuns
(
    RunId                 uniqueidentifier  NOT NULL CONSTRAINT PK_ReconciliationRuns PRIMARY KEY,
    ProviderId            varchar(40)       NOT NULL CONSTRAINT FK_ReconciliationRuns_Providers REFERENCES casino.Providers (ProviderId),
    BusinessDate          date              NOT NULL,
    OurNet                bigint            NOT NULL,
    ProviderNet           bigint            NOT NULL,
    Drift                 bigint            NOT NULL,
    MissingOnOurSide      int               NOT NULL,
    MissingOnProviderSide int               NOT NULL,
    Status                tinyint           NOT NULL,
    Currency              char(3)           NOT NULL,
    ReconciledAt          datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_ReconciliationRuns_Provider ON casino.ReconciliationRuns (ProviderId, BusinessDate DESC, ReconciledAt DESC);
