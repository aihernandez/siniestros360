"""Check release inventory and unsafe-input rejection without Docker or Azure."""
import unittest
import json
import subprocess
import sys
import tempfile
from pathlib import Path
from build_images import release_plan, ROOT


class ReleasePlanTests(unittest.TestCase):
    def test_complete_release_matches_real_projects(self):
        images, commands = release_plan("example.azurecr.io", "r1", "sdk:10.0.100", "aspnet:10.0.0", "node:22.0.0", "nginx:1.28.0")
        self.assertEqual((len(images["service_images"]), len(images["migration_images"]), len(images["frontend_images"])), (10, 9, 3))
        self.assertEqual(len(commands), 22)
        self.assertEqual(len({command[command.index("-t") + 1] for command in commands}), 22)
        for command in commands:
            self.assertTrue((ROOT / command[command.index("-f") + 1]).is_file())
            project = next((value.split("=", 1)[1] for value in command if value.startswith("PROJECT=")), None)
            if project:
                self.assertTrue((ROOT / project).is_file(), project)

    def test_rejects_unsafe_or_unversioned_inputs(self):
        for registry, release, base in (("https://example.azurecr.io", "r1", "sdk:1"), ("example.azurecr.io", "latest", "sdk:1"), ("example.azurecr.io", "bad;tag", "sdk:1"), ("example.azurecr.io", "r1", "sdk"), ("example.azurecr.io", "r1", "sdk@sha256:bad")):
            with self.assertRaises(ValueError):
                release_plan(registry, release, base, "aspnet:1", "node:1", "nginx:1")

    def test_partial_cli_manifest_does_not_advertise_other_image_groups(self):
        for group, count in (("service_images", 10), ("migration_images", 9), ("frontend_images", 3)):
            with self.subTest(group=group), tempfile.TemporaryDirectory(prefix="s360-image-plan-") as temporary:
                output = Path(temporary) / "images.json"
                command = [sys.executable, str(ROOT / "infrastructure/scripts/build_images.py"), "--registry", "example.azurecr.io", "--release", "r1", "--sdk", "sdk:10.0.100", "--runtime", "aspnet:10.0.0", "--node", "node:22.0.0", "--nginx", "nginx:1.28.0", "--group", group, "--output", str(output)]
                result = subprocess.run(command, capture_output=True, text=True, check=False)
                self.assertEqual(result.returncode, 0, result.stderr)
                manifest = json.loads(output.read_text(encoding="utf-8"))
                self.assertEqual(set(manifest), {"release_id", group})
                self.assertEqual(len(manifest[group]), count)


if __name__ == "__main__":
    unittest.main()
