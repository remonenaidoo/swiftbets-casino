-- Rolls back 0002_casino. Sessions, transactions and grants are lost; run only before the casino has taken bets.
DROP TABLE IF EXISTS casino.FreeSpinGrants;
DROP TABLE IF EXISTS casino.Transactions;
DROP TABLE IF EXISTS casino.Sessions;
DROP TABLE IF EXISTS casino.Providers;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Casino.Migrator.Migrations.0002_casino.sql';
