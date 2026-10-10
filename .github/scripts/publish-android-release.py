"""Publish only the verified APK artifact belonging to this trusted workflow run."""
import hashlib
import json
import os
from pathlib import Path
import subprocess


def gh(*args):
    return subprocess.check_output(["gh", *args], text=True)


def lookup_optional(endpoint):
    result = subprocess.run(["gh", "api", endpoint], text=True, capture_output=True)
    if result.returncode == 0:
        return json.loads(result.stdout)
    if "HTTP 404" in result.stderr:
        return None
    raise RuntimeError("Could not inspect release/ref; refusing publication")


def main():
    folder = Path("release-artifact")
    version = f"1.0.{int(os.environ['GITHUB_RUN_NUMBER'])}"
    code = 100000 + int(os.environ["GITHUB_RUN_NUMBER"])
    tag = f"v{version}"
    repository = os.environ["GITHUB_REPOSITORY"]
    if repository != "quenzvezda/lajur" or os.environ["GITHUB_REF"] != "refs/heads/main":
        raise ValueError("Publication is restricted to the official main branch")
    filename = f"lajur-{version}.apk"
    expected_files = {filename, "SHA256SUMS", "build-metadata.json"}
    if {p.name for p in folder.iterdir()} != expected_files:
        raise ValueError("Artifact contains missing or unexpected files")
    metadata = json.loads((folder / "build-metadata.json").read_text())
    expected = {
        "versionName": version, "versionCode": code, "apk": filename,
        "applicationId": "com.lajur.app", "commit": os.environ["GITHUB_SHA"],
        "certificateSha256": os.environ["LAJUR_EXPECTED_CERT_SHA256"],
    }
    if metadata != expected:
        raise ValueError("Artifact identity/version/source/certificate mismatch")
    apk = folder / filename
    digest = hashlib.sha256(apk.read_bytes()).hexdigest()
    if (folder / "SHA256SUMS").read_text() != f"{digest}  {filename}\n":
        raise ValueError("APK checksum mismatch")
    release_url = f"https://github.com/{repository}/releases/tag/{tag}"
    manifest = {
        "schemaVersion": 1, "versionCode": code, "versionName": version,
        "applicationId": "com.lajur.app", "minSdk": 26,
        "apkUrl": f"https://github.com/{repository}/releases/download/{tag}/{filename}",
        "sha256": digest, "sizeBytes": apk.stat().st_size,
        "certificateSha256": expected["certificateSha256"],
        "commit": expected["commit"], "releaseUrl": release_url,
    }
    (folder / "update.json").write_text(json.dumps(manifest, indent=2) + "\n")
    notes = Path("release-notes.md")
    notes.write_text(
        f"APK Android signed dari CI yang telah lolos pemeriksaan.\n\n"
        f"- Versi: {version} (versionCode {code}).\n"
        f"- Commit: `{expected['commit']}`.\n"
        f"- Android minimum: 8.0 (API 26).\n\n"
        "Unduh file `.apk` di Assets. `SHA256SUMS` tersedia untuk verifikasi.\n\n"
        "Backend VPS belum tersedia; alamat server dapat diubah saat login. "
        "APK release memakai key berbeda dari APK debug. Sinkronkan dan verifikasi "
        "transaksi yang masih antre sebelum melakukan perpindahan instalasi.\n"
    )
    ref = lookup_optional(f"repos/{repository}/git/ref/tags/{tag}")
    if ref:
        target = ref["object"]
        for _ in range(8):
            if target["type"] != "tag":
                break
            target = json.loads(gh("api", f"repos/{repository}/git/tags/{target['sha']}"))["object"]
        if target["type"] != "commit" or target["sha"] != expected["commit"]:
            raise ValueError("Existing version tag resolves to another source commit")
    existing = lookup_optional(f"repos/{repository}/releases/tags/{tag}")
    files = sorted(folder.iterdir())
    if existing and not existing["draft"]:
        # A rerun must not replace a published APK under the same version.
        assets = {a["name"]: a for a in existing["assets"]}
        for file in files:
            asset = assets.get(file.name)
            wanted = "sha256:" + hashlib.sha256(file.read_bytes()).hexdigest()
            if not asset or asset.get("digest") != wanted or asset["size"] != file.stat().st_size:
                raise ValueError("Published assets differ; publish a new version instead")
        print(f"Release already published with identical assets: {release_url}")
        return
    if existing:
        if existing["target_commitish"] != expected["commit"]:
            raise ValueError("Existing draft targets another commit")
        gh("release", "upload", tag, *map(str, files), "--clobber", "--repo", repository)
    else:
        gh("release", "create", tag, *map(str, files), "--repo", repository,
           "--target", expected["commit"], "--title", f"Lajur {version}",
           "--notes-file", str(notes), "--draft")
    gh("release", "edit", tag, "--repo", repository, "--draft=false", "--latest",
       "--notes-file", str(notes))
    print(f"Published {release_url}")


if __name__ == "__main__":
    main()
