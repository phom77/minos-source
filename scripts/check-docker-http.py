"""Exercise the real same-origin HTTPS API and containerized Worker in local Compose.

Creates disposable author/solver accounts and an A+B fixture; archives the problem
and disables those accounts afterward. Existing user accounts are never changed.
"""

import argparse
import http.cookiejar
import io
import json
from pathlib import Path
import secrets
import ssl
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import zipfile


REPOSITORY = Path(__file__).resolve().parent.parent


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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="https://localhost:8443")
    parser.add_argument("--ca", type=Path, default=REPOSITORY / "artifacts" / "minos-local-ca.crt")
    args = parser.parse_args()
    if not args.ca.is_file():
        args.ca.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["docker", "compose", "cp", "web:/certs/ca.crt", str(args.ca)], cwd=REPOSITORY, check=True)
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
            ["docker", "compose", "run", "--rm", "-T", "--no-deps", "migrate", "--provision-admin", author_id],
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
        assert any(item["problemId"] == problem_id for item in stranger.request("/api/problems/?page=1&pageSize=100")["items"])
        print("HTTPS proxy, protected cookies, CSRF, profile and problem publication: passed", flush=True)

        solver.register(f"docker-solver-{suffix}@example.test", password, "Docker fixture solver")
        registered.append(solver)
        stranger.register(f"docker-stranger-{suffix}@example.test", password, "Docker fixture stranger")
        registered.append(stranger)
        cases = {
            "Accepted": '#include <iostream>\nint main(){long long a,b;std::cin>>a>>b;std::cout<<a+b<<"\\n";}\n',
            "WrongAnswer": '#include <iostream>\nint main(){std::cout<<5<<"\\n";}\n',
            "CompilationError": 'int main(){ this is not valid C++; }\n',
            "RuntimeError": 'int main(){return 7;}\n',
            "TimeLimitExceeded": 'int main(){volatile unsigned long long n=0;for(;;)++n;}\n',
            "MemoryLimitExceeded": '#include <cstddef>\nint main(){volatile char* p=new char[128*1024*1024];for(std::size_t i=0;i<128*1024*1024;i+=4096)p[i]=1;}\n',
            "OutputLimitExceeded": '#include <iostream>\nint main(){for(;;)std::cout<<"xxxxxxxxxxxxxxxx";}\n',
        }
        for verdict, source in cases.items():
            submission = solver.request(f"/api/problems/{problem_id}/submissions", "POST",
                                        {"language": "cpp20", "source": source}, expected=201)
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
                                 ["Passed", "WrongAnswer", "NotExecuted"] if verdict == "WrongAnswer" else
                                 ["NotExecuted"] * 3 if verdict == "CompilationError" else
                                 [verdict, "NotExecuted", "NotExecuted"])
            assert statuses == expected_statuses, (verdict, statuses)
            evidence = json.dumps(result["result"]).lower()
            assert all(key not in evidence for key in ("stdout", "stderr", "expectedoutput", "inputartifact", "outputartifact"))
            if verdict != "CompilationError":
                assert result["result"]["maximumCpuTimeMilliseconds"] is not None
                assert result["result"]["maximumMemoryBytes"] is not None
            stranger.request(path, expected=403)
            print(f"Containerized judging: {verdict} passed", flush=True)
        print("Cross-account denial and private result projections: passed", flush=True)
    finally:
        if problem_id is not None:
            author.request(f"/api/authoring/problems/{problem_id}/archive", "POST", expected=204)
        for session in reversed(registered):
            session.request("/accounts/disable", "POST", {"password": password}, expected=204)


if __name__ == "__main__":
    main()
