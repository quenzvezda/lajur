from contextlib import redirect_stdout
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import ci


class PathSelectionTests(unittest.TestCase):
    def test_documentation_and_agent_tooling_do_not_build_apps(self):
        self.assertEqual(ci.affected_components([
            "README.md", "docs/operations/ANDROID_RELEASE.md",
            "services/api/README.md", "apps/android/README.md",
            "tests/workbook-fixtures/README.md", ".agents/skills/example/config.json",
            "docs/product/branding/lajur-logo.svg", ".gitignore", "skills-lock.json",
        ]), set())

    def test_component_inputs_select_only_their_owner(self):
        for path in ["apps/android/app/src/main/AndroidManifest.xml",
                     "apps/android/gradle/libs.versions.toml", "apps/android/gradlew",
                     ".github/scripts/publish-android-release.py",
                     ".github/scripts/test_publish_android_release.py",
                     "tests/smart-scan-eval/cases.synthetic.json"]:
            with self.subTest(path=path):
                self.assertEqual(ci.affected_components([path]), {"android"})
        for path in ["services/api/src/Lajur.Api/wwwroot/css/app.css",
                     "services/api/src/Lajur.Api/Pages/Index.cshtml",
                     "services/api/src/Lajur.Domain/Auth/UserAccount.cs",
                     "services/api/deploy/compose.production.yml",
                     "services/api/tests/container-smoke.sh",
                     "tests/workbook-fixtures/LajurWorkbookFixture.xlsm",
                     "global.json", ".dockerignore", "docker-compose.yml",
                     ".env.example", "Directory.Build.props", "Directory.Packages.props",
                     "NuGet.Config"]:
            with self.subTest(path=path):
                self.assertEqual(ci.affected_components([path]), {"backend"})

    def test_shared_workflow_changes_and_unknown_inputs_check_both(self):
        for path in [".github/workflows/ci.yml", ".github/scripts/ci.py",
                     ".github/scripts/test_ci.py", ".gitattributes", "future-shared/config.json"]:
            with self.subTest(path=path):
                self.assertEqual(ci.affected_components([path]), {"android", "backend"})

    def test_mixed_changes_select_both(self):
        self.assertEqual(ci.affected_components([
            "README.md", "apps/android/app/build.gradle.kts", "global.json",
        ]), {"android", "backend"})

    def test_manual_runs_force_only_selected_components(self):
        for target, expected in [("android", {"android"}), ("backend", {"backend"}),
                                 ("all", {"android", "backend"})]:
            with self.subTest(target=target):
                self.assertEqual(ci.select_components("workflow_dispatch", {}, target), expected)
        with self.assertRaises(ValueError):
            ci.select_components("workflow_dispatch", {}, "unknown")


class GitSelectionTests(unittest.TestCase):
    """Exercise real Git ranges, including PR merge bases and moved/deleted files."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.old_cwd = os.getcwd()
        os.chdir(self.temp.name)
        self.git("init", "-b", "main")
        self.git("config", "user.name", "CI tests")
        self.git("config", "user.email", "ci@example.invalid")
        self.base = self.commit("README.md", "initial")

    def tearDown(self):
        os.chdir(self.old_cwd)
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", *args], stderr=subprocess.PIPE).decode().strip()

    def commit(self, path, content):
        file = Path(path)
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(content, encoding="utf-8")
        self.git("add", "--all")
        self.git("commit", "-m", "fixture")
        return self.git("rev-parse", "HEAD")

    def push(self, base, head):
        return ci.select_components("push", {"before": base, "after": head})

    def test_readme_only_push_selects_nothing(self):
        head = self.commit("README.md", "updated")
        self.assertEqual(self.push(self.base, head), set())

    def test_multi_commit_push_includes_entire_range(self):
        self.commit("apps/android/app/example.kt", "android")
        head = self.commit("README.md", "updated")
        self.assertEqual(self.push(self.base, head), {"android"})

    def test_pr_excludes_changes_made_only_on_base_branch(self):
        self.git("checkout", "-b", "feature")
        head = self.commit("apps/android/app/example.kt", "android")
        self.git("checkout", "main")
        base = self.commit("services/api/example.cs", "backend")
        event = {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}
        self.assertEqual(ci.select_components("pull_request", event), {"android"})

    def test_deleted_file_selects_its_component(self):
        base = self.commit("services/api/example.cs", "backend")
        self.git("rm", "services/api/example.cs")
        head = self.commit("README.md", "updated")
        self.assertEqual(self.push(base, head), {"backend"})

    def test_rename_between_components_selects_both(self):
        base = self.commit("apps/android/shared.json", "shared")
        Path("services/api").mkdir(parents=True)
        self.git("mv", "apps/android/shared.json", "services/api/shared.json")
        head = self.commit("README.md", "updated")
        self.assertEqual(self.push(base, head), {"android", "backend"})

    def test_rename_to_documentation_still_selects_old_component(self):
        base = self.commit("services/api/config.json", "configuration")
        self.git("mv", "services/api/config.json", "README-config.md")
        head = self.commit("README.md", "updated")
        self.assertEqual(self.push(base, head), {"backend"})

    def test_initial_push_checks_both(self):
        self.assertEqual(self.push("0" * 40, self.base), {"android", "backend"})

    def test_unavailable_revision_checks_both_instead_of_skipping(self):
        with redirect_stdout(io.StringIO()) as output:
            self.assertEqual(self.push("f" * 40, self.base), {"android", "backend"})
        self.assertIn("Cannot compare CI revisions", output.getvalue())


class ResultGateTests(unittest.TestCase):
    def results(self, backend, android):
        return {
            "changes": {"result": "success", "outputs": {
                "backend": str(backend).lower(), "android": str(android).lower(),
            }},
            "backend": {"result": "success" if backend else "skipped"},
            "container": {"result": "success" if backend else "skipped"},
            "android": {"result": "success" if android else "skipped"},
        }

    def test_accepts_docs_android_backend_and_combined_runs(self):
        for backend, android in [(False, False), (False, True), (True, False), (True, True)]:
            with self.subTest(backend=backend, android=android):
                ci.check_results(self.results(backend, android))

    def test_selected_job_failure_cancellation_or_skip_blocks_gate(self):
        for job in ["backend", "container", "android"]:
            for result in ["failure", "cancelled", "skipped"]:
                with self.subTest(job=job, result=result):
                    needs = self.results(True, True)
                    needs[job]["result"] = result
                    with self.assertRaises(ValueError):
                        ci.check_results(needs)

    def test_detection_failure_cannot_be_mistaken_for_docs_only(self):
        needs = self.results(False, False)
        needs["changes"] = {"result": "failure", "outputs": {}}
        with self.assertRaises(ValueError):
            ci.check_results(needs)

    def test_missing_selection_blocks_gate(self):
        needs = self.results(False, False)
        del needs["changes"]["outputs"]["android"]
        with self.assertRaises(ValueError):
            ci.check_results(needs)

    def test_unselected_job_running_blocks_gate(self):
        needs = self.results(False, True)
        needs["backend"]["result"] = "success"
        with self.assertRaises(ValueError):
            ci.check_results(needs)


class CommandTests(unittest.TestCase):
    def test_manual_detector_writes_github_outputs_and_summary(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            event, output, summary = (root / name for name in ("event.json", "output", "summary"))
            event.write_text("{}", encoding="utf-8")
            env = dict(os.environ, GITHUB_EVENT_NAME="workflow_dispatch", CI_TARGET="backend",
                       GITHUB_EVENT_PATH=str(event), GITHUB_OUTPUT=str(output),
                       GITHUB_STEP_SUMMARY=str(summary))
            subprocess.run([sys.executable, ci.__file__, "detect"], env=env,
                           check=True, capture_output=True)
            self.assertEqual(output.read_text(), "android=false\nbackend=true\n")
            self.assertEqual(summary.read_text(), "Selected components: backend\n")

    def test_gate_exits_nonzero_when_required_job_was_skipped(self):
        needs = ResultGateTests().results(False, True)
        needs["android"]["result"] = "skipped"
        result = subprocess.run([sys.executable, ci.__file__, "check"],
                                env=dict(os.environ, CI_NEEDS=json.dumps(needs)), capture_output=True)
        self.assertNotEqual(result.returncode, 0)


if __name__ == "__main__":
    unittest.main()
