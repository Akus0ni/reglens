-- 005_readonly_role: a SELECT-only role for the API (spec 0006, D60, R6.16). The login (reglens_api) and its password are created by
-- scripts/create-api-db-user.ps1, never here. Roles are cluster-wide: another database may have created it already.
DO $$
BEGIN
    CREATE ROLE reglens_readonly NOLOGIN;
EXCEPTION
    WHEN duplicate_object THEN NULL;
END
$$;

GRANT USAGE ON SCHEMA public TO reglens_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO reglens_readonly;

-- The PostgreSQL 17 GRANT docs say ALL TABLES covers views and foreign tables; materialized views are named here.
GRANT SELECT ON lexeme_stats, chunk_lengths, corpus_stats TO reglens_readonly;

-- Tables the migration runner's role creates later. A later migration that adds a materialized view grants it by name.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO reglens_readonly;
