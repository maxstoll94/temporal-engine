#!/bin/sh
# Creates the catalog and finance databases (sport is created by POSTGRES_DB).
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE DATABASE temporal_catalog OWNER temporal;
    CREATE DATABASE temporal_finance OWNER temporal;
EOSQL
