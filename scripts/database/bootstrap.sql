-- Run once against an empty Minos database as its bootstrap owner, using psql.
-- Supply database, migrator_password, api_password and worker_password as psql variables.
-- The bootstrap identity is never supplied to either runtime host.
CREATE ROLE minos_migrator LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'migrator_password';
CREATE ROLE minos_api LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'api_password';
CREATE ROLE minos_worker LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'worker_password';
REVOKE ALL ON DATABASE :"database" FROM PUBLIC;
GRANT CONNECT, CREATE ON DATABASE :"database" TO minos_migrator;
GRANT CONNECT ON DATABASE :"database" TO minos_api, minos_worker;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO minos_migrator;
GRANT USAGE ON SCHEMA public TO minos_api, minos_worker;
CREATE SCHEMA problems AUTHORIZATION minos_migrator;
GRANT USAGE ON SCHEMA problems TO minos_api, minos_worker;
