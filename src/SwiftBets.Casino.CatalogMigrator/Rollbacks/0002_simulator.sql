-- Rolls back 0002_simulator. The simulators' ledger is lost; reconciliation of past days then reports drift.
DROP SCHEMA IF EXISTS simulator CASCADE;
