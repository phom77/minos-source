-- Apply as minos_migrator after migrations. Grants cover current use cases only.
-- Do not grant default access to future tables, especially account/credential tables.
GRANT SELECT, INSERT, UPDATE ON problems.problems TO minos_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON problems.problem_samples, problems.problem_test_cases TO minos_api;
GRANT SELECT, INSERT ON problems.published_problem_versions, problems.published_problem_samples, problems.published_problem_test_cases TO minos_api;
GRANT SELECT ON problems.problems TO minos_worker;
GRANT SELECT ON public."__EFMigrationsHistory" TO minos_api, minos_worker;
GRANT USAGE ON SCHEMA accounts TO minos_api;
GRANT SELECT, INSERT, UPDATE ON accounts.accounts, accounts.credentials TO minos_api;
GRANT SELECT, INSERT ON accounts.external_logins TO minos_api;
GRANT SELECT ON accounts.claims TO minos_api;
-- Worker has no account, credential, external-login, claim or token access.
