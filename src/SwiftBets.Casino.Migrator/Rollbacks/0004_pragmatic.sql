-- Rolls back 0004_pragmatic. Stored replies and console overrides are lost; Pragmatic transactions must be gone first.
DROP INDEX IF EXISTS IX_Sessions_Punter_Provider ON casino.Sessions;
ALTER TABLE casino.Transactions DROP COLUMN IF EXISTS Reply;
DELETE FROM casino.Sessions WHERE ProviderId = 'pragmatic';
DELETE FROM casino.Providers WHERE ProviderId = 'pragmatic';
ALTER TABLE casino.Providers DROP COLUMN IF EXISTS AllowedAddresses, ApiBaseUrl, DemoBaseUrl, SecureLoginCipher, SecretCipher, UpdatedAt, UpdatedBy;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Casino.Migrator.Migrations.0004_pragmatic.sql';
