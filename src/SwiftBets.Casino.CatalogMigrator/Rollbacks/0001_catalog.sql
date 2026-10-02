-- Rolls back 0001_catalog. The lobby is empty until it is reapplied; no money state lives here.
DROP SCHEMA IF EXISTS catalog CASCADE;
