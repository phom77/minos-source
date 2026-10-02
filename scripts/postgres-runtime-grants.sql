-- Apply as minos_migrator after migrations. Grants cover current use cases only.
-- Do not grant default access to future tables, especially account/credential tables.
GRANT SELECT, INSERT, UPDATE ON problems.problems TO minos_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON problems.problem_samples, problems.problem_test_cases TO minos_api;
GRANT SELECT, INSERT ON problems.published_problem_versions, problems.published_problem_samples, problems.published_problem_test_cases TO minos_api;
GRANT SELECT ON problems.problems, problems.published_problem_versions, problems.published_problem_test_cases TO minos_worker;
GRANT USAGE ON SCHEMA evaluation TO minos_api, minos_worker;
GRANT SELECT, INSERT ON evaluation.submissions TO minos_api;
GRANT INSERT ON evaluation.judge_jobs TO minos_api;
GRANT SELECT ON evaluation.judging_attempts, evaluation.submission_results, evaluation.test_case_results TO minos_api;
GRANT SELECT, UPDATE ON evaluation.submissions, evaluation.judge_jobs TO minos_worker;
GRANT SELECT, INSERT, UPDATE ON evaluation.judging_attempts TO minos_worker;
GRANT INSERT ON evaluation.submission_results, evaluation.test_case_results TO minos_worker;
GRANT SELECT ON public."__EFMigrationsHistory" TO minos_api, minos_worker;
GRANT USAGE ON SCHEMA accounts TO minos_api;
GRANT SELECT, INSERT, UPDATE ON accounts.accounts, accounts.credentials TO minos_api;
GRANT SELECT, INSERT ON accounts.external_logins TO minos_api;
GRANT SELECT ON accounts.claims TO minos_api;
-- Worker has no account, credential, external-login, claim or token access.
