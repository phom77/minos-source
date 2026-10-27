"""Exercise the real same-origin HTTPS API and containerized Worker in local Compose.

Creates disposable author/solver accounts and an A+B fixture; archives the problem
and disables those accounts afterward. Existing user accounts are never changed.
"""

import argparse
import http.cookiejar
import io
import json
import os
from datetime import datetime, timedelta, timezone
from pathlib import Path
import secrets
import ssl
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import zipfile


REPOSITORY = Path(__file__).resolve().parents[2]


class Session:
    def __init__(self, base_url, context):
        self.base_url = base_url
        self.cookies = http.cookiejar.CookieJar()
        self.client = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=context),
            urllib.request.HTTPCookieProcessor(self.cookies),
        )
        self.token = None

    def request(self, path, method="GET", value=None, data=None, content_type="application/json", expected=200):
        headers = {}
        if value is not None:
            data = json.dumps(value).encode()
        if data is not None:
            headers["Content-Type"] = content_type
        if method not in ("GET", "HEAD", "OPTIONS"):
            if self.token is None:
                self.token = self.request("/accounts/csrf")["requestToken"]
            headers["X-CSRF-TOKEN"] = self.token
        request = urllib.request.Request(self.base_url + path, data=data, headers=headers, method=method)
        try:
            response = self.client.open(request, timeout=20)
        except urllib.error.HTTPError as error:
            if error.code != expected:
                raise AssertionError(f"{method} {path}: HTTP {error.code}, expected {expected}") from None
            return None
        with response:
            assert response.status == expected, (method, path, response.status)
            body = response.read()
            if not body:
                return None
            return json.loads(body) if response.headers.get_content_type() == "application/json" else body.decode()

    def register(self, email, password, name):
        account = self.request("/accounts/register", "POST",
                               {"email": email, "password": password, "displayName": name}, expected=201)
        self.token = None
        return account["id"]

    def login(self, email, password):
        self.request("/accounts/login", "POST", {"email": email, "password": password})
        self.token = None


def check_problem_comments(author, solver, stranger, base_url, context, problem_id, version_id):
    """Only the disposable problem/accounts created by this script are mutated."""
    path = f"/api/problems/{problem_id}/comments"
    guest = Session(base_url, context)
    root = solver.request(path, "POST", {"publishedVersionId": version_id, "body": "Comment HTTPS fixture — câu hỏi", "isSpoiler": True}, expected=201)
    reply = stranger.request(path, "POST", {"publishedVersionId": version_id, "body": "Reply HTTPS fixture", "parentCommentId": root["id"]}, expected=201)
    with guest.client.open(urllib.request.Request(base_url + path), timeout=20) as response:
        assert response.headers.get("Cache-Control") == "no-store"
        public = json.loads(response.read())
    visible = next(value for value in public["items"] if value["id"] == root["id"])
    assert visible["replyCount"] == 1 and not any(visible[key] for key in ("canEdit", "canDelete", "canHide"))
    assert "authorAccountId" not in json.dumps(public)
    assert next(value for value in solver.request(path)["items"] if value["id"] == root["id"])["canEdit"]
    stranger.request(path + "/" + root["id"], "PUT", {"expectedRevision": 1, "body": "Unauthorized"}, expected=403)
    stranger.request(path + "/" + reply["id"] + "/hide", "POST", {"expectedRevision": 1}, expected=403)
    guest.request(path, "POST", {"publishedVersionId": version_id, "body": "Anonymous"}, expected=401)
    # Preserve the authenticated cookie jar but deliberately omit the CSRF header.
    request = urllib.request.Request(base_url + path, data=json.dumps({"publishedVersionId": version_id, "body": "No CSRF"}).encode(),
        headers={"Content-Type": "application/json"}, method="POST")
    try:
        solver.client.open(request, timeout=20).close()
        raise AssertionError("Comment mutation without CSRF was accepted.")
    except urllib.error.HTTPError as failure:
        assert failure.code == 400
    edited = solver.request(path + "/" + root["id"], "PUT", {"expectedRevision": 1, "body": "Edited HTTPS fixture", "isSpoiler": False})
    assert edited["revision"] == 2 and edited["body"] == "Edited HTTPS fixture"
    solver.request(path + "/" + root["id"], "PUT", {"expectedRevision": 1, "body": "Stale edit"}, expected=409)
    author.request(path + "/" + reply["id"] + "/hide", "POST", {"expectedRevision": 1}, expected=204)
    hidden = guest.request(path + "/" + root["id"] + "/replies")["items"][0]
    assert hidden["state"] == "Hidden" and hidden["body"] is None and not hidden["isSpoiler"]
    solver.request(path + "/" + root["id"] + "/delete", "POST", {"expectedRevision": 2}, expected=204)
    deleted = next(value for value in guest.request(path)["items"] if value["id"] == root["id"])
    assert deleted["state"] == "Deleted" and deleted["body"] is None and deleted["replyCount"] == 1
    solver.request(path, "POST", {"publishedVersionId": version_id, "body": "Late reply", "parentCommentId": root["id"]}, expected=400)
    print("Real HTTPS comments: persistence, public/owner projections, CSRF, ownership, revisions, reply and moderation passed", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="https://localhost:8443")
    parser.add_argument("--ca", type=Path, default=REPOSITORY / "artifacts" / "minos-local-ca.crt")
    parser.add_argument("--browser-smoke", action="store_true", help="Also run real browser practice/contest journeys with the temporary solver.")
    parser.add_argument("--browser-only", action="store_true", help="Create the same temporary fixture and run only browser journeys, without repeating the seven API verdict cases.")
    parser.add_argument("--comments-browser", action="store_true", help="Also exercise real browser comment posting/editing/replies/deletion on the temporary problem.")
    parser.add_argument("--comments-only", action="store_true", help="Run comment API/browser journeys on the temporary fixture without submitting judging work or creating a contest.")
    parser.add_argument("--compose-project", help="Use a separately named disposable Compose stack.")
    parser.add_argument("--compose-env-file", type=Path, help="Use that stack's private Compose environment file.")
    args = parser.parse_args()
    compose = ["docker", "compose"]
    if args.compose_project:
        compose += ["--project-name", args.compose_project]
    if args.compose_env_file:
        compose += ["--env-file", str(args.compose_env_file)]
    if not args.ca.is_file():
        args.ca.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(compose + ["cp", "web:/certs/ca.crt", str(args.ca)], cwd=REPOSITORY, check=True)
    context = ssl.create_default_context(cafile=str(args.ca))
    author = Session(args.base_url, context)
    solver = Session(args.base_url, context)
    stranger = Session(args.base_url, context)
    suffix = uuid.uuid4().hex
    password = secrets.token_hex(24)
    email = f"docker-author-{suffix}@example.test"
    registered = []
    problem_id = None
    try:
        author.request("/health/ready")
        author_id = author.register(email, password, "Docker fixture author")
        registered.append(author)
        maintenance = subprocess.run(
            compose + ["run", "--rm", "-T", "--no-deps", "migrate", "--provision-admin", author_id],
            cwd=REPOSITORY, capture_output=True, text=True,
        )
        assert maintenance.returncode == 0, "Fixture administrator provisioning failed."
        author.login(email, password)
        assert author.request("/accounts/me")["role"] == "Administrator"
        assert all(cookie.secure for cookie in author.cookies), "Authentication cookies must be Secure."
        author.request("/accounts/me", "PATCH", {"displayName": "Docker HTTPS author"})

        problem = author.request("/api/authoring/problems/", "POST",
                                 {"title": "Docker fixture A + B", "statement": "Print the sum."}, expected=201)
        problem_id = problem["id"]
        author.request(f"/api/authoring/problems/{problem_id}/draft", "PUT", {
            "title": "Docker fixture A + B", "statement": "Print the sum.",
            "timeLimitMilliseconds": 1000, "memoryLimitMegabytes": 64, "checker": "Exact",
            "samples": [{"input": "1 2\n", "output": "3\n"}],
        })
        for input_text, output_text in (("2 3\n", "5\n"), ("19 23\n", "42\n"), ("-7 10\n", "3\n")):
            package = io.BytesIO()
            with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("input.txt", input_text)
                archive.writestr("output.txt", output_text)
            author.request(f"/api/authoring/problems/{problem_id}/draft/tests", "POST",
                           data=package.getvalue(), content_type="application/zip")
        published = author.request(f"/api/authoring/problems/{problem_id}/publish", "POST", {
            "statementReviewed": True, "samplesVerified": True, "referenceSolutionChecked": True,
        }, expected=201)
        public = stranger.request(f"/api/problems/{problem_id}")
        assert public["versionId"] == published["versionId"] and public["privateTestCount"] == 3
        assert "artifact" not in json.dumps(public).lower()
        assert any(item["problemId"] == problem_id for item in stranger.request("/api/problems/?page=1&pageSize=100&sort=Newest")["items"])
        print("HTTPS proxy, protected cookies, CSRF, profile and problem publication: passed", flush=True)

        solver_email = f"docker-solver-{suffix}@example.test"
        solver.register(solver_email, password, "Docker fixture solver")
        registered.append(solver)
        stranger.register(f"docker-stranger-{suffix}@example.test", password, "Docker fixture stranger")
        registered.append(stranger)
        check_problem_comments(author, solver, stranger, args.base_url, context, problem_id, published["versionId"])
        cases = {
            "Accepted": '#include <iostream>\nint main(){long long a,b;std::cin>>a>>b;std::cout<<a+b<<"\\n";}\n',
            "WrongAnswer": '#include <iostream>\nint main(){std::cout<<5<<"\\n";}\n',
            "CompilationError": 'int main(){ this is not valid C++; }\n',
            "RuntimeError": 'int main(){return 7;}\n',
            "TimeLimitExceeded": 'int main(){volatile unsigned long long n=0;for(;;)++n;}\n',
            "MemoryLimitExceeded": '#include <cstddef>\nint main(){volatile char* p=new char[128*1024*1024];for(std::size_t i=0;i<128*1024*1024;i+=4096)p[i]=1;}\n',
            "OutputLimitExceeded": '#include <iostream>\nint main(){for(;;)std::cout<<"xxxxxxxxxxxxxxxx";}\n',
        }
        for verdict, source in ({} if args.browser_only or args.comments_only else cases).items():
            request_key = str(uuid.uuid4())
            submission = solver.request(f"/api/problems/{problem_id}/submissions", "POST",
                                        {"language": "cpp20", "source": source, "requestKey": request_key}, expected=201)
            path = f"/api/submissions/{submission['id']}"
            deadline = time.monotonic() + 120
            while True:
                result = solver.request(path)
                if result["status"] in ("Completed", "SystemFailed"):
                    break
                assert time.monotonic() < deadline, f"Worker did not complete {verdict} within 120 seconds."
                time.sleep(0.25)
            assert result["status"] == "Completed", f"{verdict}: {result['status']}"
            assert result["result"]["verdict"] == verdict, (verdict, result["result"]["verdict"])
            assert result["problemVersionId"] == published["versionId"]
            assert result["result"]["attemptId"] == result["attempts"][-1]["id"]
            statuses = [test["status"] for test in result["result"]["tests"]]
            expected_statuses = (["Passed"] * 3 if verdict == "Accepted" else
                                 ["Passed", "WrongAnswer", "WrongAnswer"] if verdict == "WrongAnswer" else
                                 ["NotExecuted"] * 3 if verdict == "CompilationError" else
                                 [verdict] * 3)
            assert statuses == expected_statuses, (verdict, statuses)
            executed = sum(status != "NotExecuted" for status in statuses)
            passed = statuses.count("Passed")
            assert result["result"]["testSummary"] == {
                "totalTests": 3, "executedTests": executed,
                "passedTests": passed, "failedTests": executed - passed,
            }
            evidence = json.dumps(result["result"]).lower()
            assert all(key not in evidence for key in ("stdout", "stderr", "expectedoutput", "inputartifact", "outputartifact"))
            if verdict != "CompilationError":
                assert result["result"]["maximumCpuTimeMilliseconds"] is not None
                assert result["result"]["maximumMemoryBytes"] is not None
            stranger.request(path, expected=403)
            replay = solver.request(f"/api/problems/{problem_id}/submissions", "POST",
                                    {"language": "cpp20", "source": source, "requestKey": request_key}, expected=201)
            assert replay["id"] == submission["id"] and replay["attempts"] == result["attempts"]
            print(f"Containerized judging: {verdict} passed", flush=True)
        if not args.browser_only and not args.comments_only:
            print("Cross-account denial and private result projections: passed", flush=True)
        if args.comments_browser or args.comments_only:
            browser_env = {**os.environ, "MINOS_LIVE_BROWSER_URL": args.base_url,
                "MINOS_LIVE_BROWSER_EMAIL": solver_email, "MINOS_LIVE_BROWSER_PASSWORD": password,
                "MINOS_LIVE_BROWSER_PROBLEM": problem_id}
            subprocess.run(["node", str(REPOSITORY / "web/minos-web/testing/comments-live-browser.cjs")],
                cwd=REPOSITORY, env=browser_env, check=True)
        if args.browser_smoke or args.browser_only:
            # Leave room for the contest endpoint's first-use JIT and fixture persistence before publication.
            starts = datetime.now(timezone.utc) + timedelta(seconds=30)
            contest = author.request("/api/contests", "POST", {
                "title": "Browser smoke contest", "description": "Temporary acceptance fixture.",
                "startsAtUtc": starts.isoformat(), "endsAtUtc": (starts + timedelta(minutes=5)).isoformat(),
                "versionIds": [published["versionId"]]}, expected=201)
            contest = author.request(f"/api/contests/{contest['id']}/publish", "POST", {"revision": contest["revision"]})
            solver.request(f"/api/contests/{contest['id']}/registration", "PUT", value={})
            time.sleep(max(0, (starts - datetime.now(timezone.utc)).total_seconds()))
            browser_env = {**os.environ, "MINOS_LIVE_BROWSER_URL": args.base_url,
                           "MINOS_LIVE_BROWSER_EMAIL": solver_email, "MINOS_LIVE_BROWSER_PASSWORD": password,
                           "MINOS_LIVE_BROWSER_PROBLEM": problem_id, "MINOS_LIVE_BROWSER_SOURCE": cases["Accepted"],
                           "MINOS_LIVE_BROWSER_CONTEST": contest["id"],
                           "MINOS_LIVE_BROWSER_CONTEST_PROBLEM": contest["problems"][0]["id"]}
            subprocess.run(["node", str(REPOSITORY / "web/minos-web/testing/live-browser.cjs")],
                           cwd=REPOSITORY, env=browser_env, check=True)
    finally:
        if problem_id is not None:
            author.request(f"/api/authoring/problems/{problem_id}/archive", "POST", expected=204)
        for session in reversed(registered):
            session.request("/accounts/disable", "POST", {"password": password}, expected=204)


if __name__ == "__main__":
    main()
