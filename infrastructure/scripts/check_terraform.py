"""Validate authored infrastructure and mocked plans; never run a real cloud plan or apply."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
TF_ROOT = ROOT / "infrastructure/terraform"
ROOTS = ("bootstrap", "platform", "tenant", "identity", "regional-data", "workloads", "edge", "modules/regional", "modules/identity", "modules/edge")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--terraform", default="terraform")
    parser.add_argument("--root", action="append", choices=ROOTS, help="Limit checks to changed roots/modules.")
    parser.add_argument("--skip-init", action="store_true", help="Reuse previously initialized providers.")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    env = dict(os.environ, TF_IN_AUTOMATION="1", TF_INPUT="0")
    evidence = {"checked_at_utc": datetime.now(timezone.utc).isoformat(), "scope": "Local syntax/provider schemas and mocked plans only; no real Azure plan/apply or image build.", "checks": []}

    def run(command, cwd):
        print(f"Checking {cwd.relative_to(ROOT)}: {' '.join(command)}", flush=True)
        result = subprocess.run(command, cwd=cwd, env=env, check=False)
        evidence["checks"].append({"directory": cwd.relative_to(ROOT).as_posix(), "command": [Path(command[0]).name, *command[1:]], "exit_code": result.returncode})
        if args.report:
            args.report.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        if result.returncode:
            raise SystemExit(result.returncode)

    run([args.terraform, "fmt", "-check", "-recursive"], TF_ROOT)
    run([sys.executable, "-m", "unittest", "discover", "-s", "infrastructure/scripts", "-p", "test_*.py"], ROOT)
    for name in args.root or ROOTS:
        path = TF_ROOT / name
        if not args.skip_init:
            run([args.terraform, "init", "-backend=false", "-input=false", "-no-color"], path)
        run([args.terraform, "validate", "-no-color"], path)
        if (path / "tests").is_dir():
            # Every infrastructure test is a credential-free plan with mocked providers.
            for test in (path / "tests").glob("*.tftest.hcl"):
                source = test.read_text(encoding="utf-8")
                if "mock_provider" not in source or len(re.findall(r'run\s+"[^"]+"\s*\{', source)) != len(re.findall(r'command\s*=\s*plan\b', source)):
                    raise SystemExit(f"Only mocked plan tests are permitted here: {test}")
            run([args.terraform, "test", "-no-color"], path)


if __name__ == "__main__":
    main()
