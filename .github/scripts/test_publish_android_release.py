import importlib.util
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("publisher", Path(__file__).with_name("publish-android-release.py"))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.old_cwd = os.getcwd()
        os.chdir(self.temp.name)
        self.env = patch.dict(os.environ, {
            "GITHUB_REPOSITORY": "quenzvezda/lajur", "GITHUB_REF": "refs/heads/main",
            "GITHUB_RUN_NUMBER": "4", "GITHUB_SHA": "a" * 40,
            "LAJUR_EXPECTED_CERT_SHA256": "b" * 64,
        })
        self.env.start()
        folder = Path("release-artifact")
        folder.mkdir()
        apk = folder / "lajur-1.0.4.apk"
        apk.write_bytes(b"verified test artifact")
        digest = hashlib.sha256(apk.read_bytes()).hexdigest()
        (folder / "SHA256SUMS").write_text(f"{digest}  {apk.name}\n")
        (folder / "build-metadata.json").write_text(json.dumps({
            "versionName": "1.0.4", "versionCode": 100004, "apk": apk.name,
            "applicationId": "com.lajur.app", "commit": "a" * 40,
            "certificateSha256": "b" * 64,
        }))

    def tearDown(self):
        self.env.stop()
        os.chdir(self.old_cwd)
        self.temp.cleanup()

    def test_creates_draft_with_all_assets_before_publishing(self):
        missing = type("Result", (), {"returncode": 1, "stderr": "HTTP 404", "stdout": ""})()
        with patch.object(publisher.subprocess, "run", return_value=missing), patch.object(publisher, "gh", return_value="") as cli:
            publisher.main()
        calls = cli.call_args_list
        self.assertEqual(calls[0].args[:3], ("release", "create", "v1.0.4"))
        self.assertIn("--draft", calls[0].args)
        self.assertEqual(calls[-1].args[:3], ("release", "edit", "v1.0.4"))
        self.assertIn("--draft=false", calls[-1].args)
        manifest = json.loads(Path("release-artifact/update.json").read_text())
        self.assertEqual(manifest["versionCode"], 100004)
        self.assertEqual(manifest["apkUrl"], "https://github.com/quenzvezda/lajur/releases/download/v1.0.4/lajur-1.0.4.apk")

    def test_rejects_wrong_source_commit_before_any_network_call(self):
        path = Path("release-artifact/build-metadata.json")
        metadata = json.loads(path.read_text())
        metadata["commit"] = "c" * 40
        path.write_text(json.dumps(metadata))
        with patch.object(publisher.subprocess, "run") as network, self.assertRaises(ValueError):
            publisher.main()
        network.assert_not_called()

    def test_rejects_corrupted_apk(self):
        Path("release-artifact/lajur-1.0.4.apk").write_bytes(b"changed")
        with patch.object(publisher.subprocess, "run") as network, self.assertRaises(ValueError):
            publisher.main()
        network.assert_not_called()

    def test_rejects_non_main_publication(self):
        with patch.dict(os.environ, {"GITHUB_REF": "refs/pull/1/merge"}), self.assertRaises(ValueError):
            publisher.main()

    def test_does_not_replace_existing_published_assets(self):
        existing = {"draft": False, "assets": []}
        with patch.object(publisher, "lookup_optional", side_effect=[None, existing]), patch.object(publisher, "gh") as cli, self.assertRaises(ValueError):
            publisher.main()
        cli.assert_not_called()

    def test_rejects_existing_tag_for_another_commit(self):
        with patch.object(publisher, "lookup_optional", return_value={"object": {"type": "commit", "sha": "c" * 40}}), patch.object(publisher, "gh") as cli, self.assertRaises(ValueError):
            publisher.main()
        cli.assert_not_called()

    def test_api_failure_does_not_create_release(self):
        error = type("Result", (), {"returncode": 1, "stderr": "HTTP 403", "stdout": ""})()
        with patch.object(publisher.subprocess, "run", return_value=error), patch.object(publisher, "gh") as cli, self.assertRaises(RuntimeError):
            publisher.main()
        cli.assert_not_called()


if __name__ == "__main__":
    unittest.main()
