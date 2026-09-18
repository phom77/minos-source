-- Apply as minos_migrator after migrations. Grants cover current use cases only.
-- Do not grant default access to future tables, especially account/credential tables.
GRANT SELECT, INSERT ON problems.problem_drafts TO minos_api;
GRANT SELECT ON problems.problem_drafts TO minos_worker;
GRANT SELECT ON public."__EFMigrationsHistory" TO minos_api, minos_worker;
GRANT USAGE ON SCHEMA accounts TO minos_api;
GRANT SELECT, INSERT, UPDATE ON accounts.accounts, accounts.credentials TO minos_api;
GRANT SELECT, INSERT ON accounts.external_logins TO minos_api;
GRANT SELECT ON accounts.claims TO minos_api;
-- Worker has no account, credential, external-login, claim or token access.
