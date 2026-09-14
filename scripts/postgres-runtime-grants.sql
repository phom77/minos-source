-- Apply as minos_migrator after migrations. Grants cover current use cases only.
-- Do not grant default access to future tables, especially account/credential tables.
GRANT SELECT, INSERT ON problems.problem_drafts TO minos_api;
GRANT SELECT ON problems.problem_drafts TO minos_worker;
GRANT SELECT ON public."__EFMigrationsHistory" TO minos_api, minos_worker;
