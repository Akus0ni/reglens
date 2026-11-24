<#
.SYNOPSIS
  Creates (or updates) the API's SELECT-only Postgres login, reglens_api (spec 0006, D60, R6.17).

.DESCRIPTION
  - The role reglens_readonly comes from migration 005: run `dotnet run --project src/RegLens.Ingest` first.
  - The password comes from REGLENS_API_DB_PASSWORD (process, then user environment). It never appears in a command
    line: docker passes the variable by name (`-e NAME`), and psql reads it with \getenv.
  - Prints the connection string for the API's user-secrets, without the password.
  Safe to re-run: an existing login gets the current password and the role membership again.
#>
param(
    [string]$Service = "postgres",
    [string]$Database = "reglens",
    [string]$Owner = "reglens"
)

$ErrorActionPreference = "Stop"

if (-not $env:REGLENS_API_DB_PASSWORD) {
    $env:REGLENS_API_DB_PASSWORD = [Environment]::GetEnvironmentVariable("REGLENS_API_DB_PASSWORD", "User")
}
if (-not $env:REGLENS_API_DB_PASSWORD) {
    throw "Set REGLENS_API_DB_PASSWORD (the password for the reglens_api login) and run this again."
}

$sql = @'
\set ON_ERROR_STOP on
\getenv api_password REGLENS_API_DB_PASSWORD
SELECT EXISTS (SELECT FROM pg_roles WHERE rolname = 'reglens_readonly') AS has_role \gset
\if :has_role
\else
  \echo 'Role reglens_readonly is missing: run the ingest host once to apply migration 005.'
  \quit 3
\endif
SELECT format('CREATE ROLE reglens_api LOGIN PASSWORD %L', :'api_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'reglens_api') \gexec
SELECT format('ALTER ROLE reglens_api LOGIN PASSWORD %L', :'api_password') \gexec
GRANT reglens_readonly TO reglens_api;
\echo 'reglens_api is ready (SELECT only).'
'@

$sql | docker compose exec -T -e REGLENS_API_DB_PASSWORD $Service psql -U $Owner -d $Database -q
if ($LASTEXITCODE -ne 0) {
    throw "psql failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Put this in the API's user-secrets (add ;Password=<your REGLENS_API_DB_PASSWORD>):"
Write-Host "  dotnet user-secrets set ""ConnectionStrings:RegLens"" ""Host=localhost;Port=5432;Database=$Database;Username=reglens_api;Password=..."" --project src/RegLens.Api"
