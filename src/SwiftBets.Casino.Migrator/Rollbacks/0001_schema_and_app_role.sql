-- Rolls back 0001_schema_and_app_role. Run 0002's rollback first; the schema must be empty.
REVOKE SELECT, INSERT, UPDATE, DELETE ON SCHEMA::casino TO swiftbets_app;
IF SCHEMA_ID(N'casino') IS NOT NULL EXEC (N'DROP SCHEMA casino');
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Casino.Migrator.Migrations.0001_schema_and_app_role.sql';
