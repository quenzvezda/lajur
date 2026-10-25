"""Select affected CI components and validate their required job results."""
import json
import os
from pathlib import Path
import subprocess
import sys


COMPONENTS = {"backend", "android"}


def affected_components(paths):
    affected = set()
    for path in paths:
        # Documentation and agent tooling do not change either application.
        if path.lower().endswith(".md") or path.startswith(("docs/", ".agents/", ".claude/")):
            continue
        if path in {".gitignore", ".editorconfig", "skills-lock.json"}:
            continue
        if path.startswith(("apps/android/", "tests/smart-scan-eval/")):
            affected.add("android")
        elif path.startswith(("services/api/", "tests/workbook-fixtures/")):
            affected.add("backend")
        elif path in {"global.json", ".dockerignore", "docker-compose.yml", ".env.example"}:
            affected.add("backend")
        elif path.startswith(("Directory.Build.", "Directory.Packages.", "NuGet.", "nuget.")):
            affected.add("backend")
        elif path in {".github/scripts/publish-android-release.py", ".github/scripts/test_publish_android_release.py"}:
            affected.add("android")
        else:
            # Workflow/shared build changes and unknown inputs fail safe to both.
            affected.update(COMPONENTS)
    return affected


def git(*args):
    return subprocess.check_output(["git", *args], stderr=subprocess.PIPE)


def select_components(event_name, event, target="all"):
    if event_name == "workflow_dispatch":
        if target not in {"all", *COMPONENTS}:
            raise ValueError("Unknown manual CI target")
        return set(COMPONENTS) if target == "all" else {target}
    if event_name == "pull_request":
        base = event["pull_request"]["base"]["sha"]
        head = event["pull_request"]["head"]["sha"]
    elif event_name == "push":
        base, head = event["before"], event["after"]
        if base == "0" * 40:
            return set(COMPONENTS)
    else:
        raise ValueError("Unsupported CI event")
    try:
        if event_name == "pull_request":
            base = git("merge-base", base, head).decode().strip()
        # Disable rename detection so moving across components includes both paths.
        changed = git("diff", "--name-only", "--no-renames", "-z", base, head, "--")
    except subprocess.CalledProcessError:
        print("::warning::Cannot compare CI revisions; checking both components.")
        return set(COMPONENTS)
    return affected_components(os.fsdecode(path) for path in changed.split(b"\0") if path)


def check_results(needs):
    changes = needs["changes"]
    if changes["result"] != "success":
        raise ValueError("CI change detection/tests did not succeed")
    for component, jobs in {"backend": ("backend", "container"), "android": ("android",)}.items():
        selected = changes["outputs"].get(component)
        if selected not in {"true", "false"}:
            raise ValueError(f"Missing/invalid selection for {component}")
        for job in jobs:
            expected = "success" if selected == "true" else "skipped"
            if needs[job]["result"] != expected:
                raise ValueError(f"{job}: expected {expected}, got {needs[job]['result']}")


def main():
    if sys.argv[1:] == ["detect"]:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
        selected = select_components(os.environ["GITHUB_EVENT_NAME"], event, os.environ.get("CI_TARGET", "all"))
        outputs = {component: str(component in selected).lower() for component in sorted(COMPONENTS)}
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.writelines(f"{key}={value}\n" for key, value in outputs.items())
        summary = "Selected components: " + (", ".join(sorted(selected)) or "none (documentation/tooling only)")
        print(summary)
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary + "\n")
    elif sys.argv[1:] == ["check"]:
        check_results(json.loads(os.environ["CI_NEEDS"]))
        print("All selected CI jobs passed; unrelated jobs were skipped.")
    else:
        raise ValueError("Usage: ci.py detect|check")


if __name__ == "__main__":
    main()
