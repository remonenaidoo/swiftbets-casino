-- Rolls back 0003_reconciliation. Past runs are lost; the next daily run records a fresh one.
DROP TABLE IF EXISTS casino.ReconciliationRuns;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Casino.Migrator.Migrations.0003_reconciliation.sql';
