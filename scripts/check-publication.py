"""Check public Markdown links/media and high-signal credential patterns without exposing values."""
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = {
    "private_key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    "provider_token": re.compile(rb"(?<![A-Za-z0-9+/_-])(?:gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}|sk_live_[A-Za-z0-9]{20,}|AIza[0-9A-Za-z_-]{35}|AKIA[A-Z0-9]{16})(?![A-Za-z0-9+/_-])"),
    "cloud_credential": re.compile(rb"(?:SharedAccessKey|AccountKey)=[A-Za-z0-9+/]{20,}"),
}


def main() -> None:
    """Fail on broken public links or credential-shaped values; do not validate credentials online."""
    files = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    errors = []
    links = 0
    for relative in sorted(set(files)):
        path = ROOT / relative
        if not path.is_file():
            continue
        raw = path.read_bytes()
        for kind, pattern in PATTERNS.items():
            if pattern.search(raw):
                errors.append({"file": relative, "problem": kind})
        if path.suffix == ".svg":
            ET.fromstring(raw)
        if path.suffix != ".md":
            continue
        text = raw.decode("utf-8")
        for target in re.findall(r"\]\(([^)]+)\)", text):
            if re.match(r"[a-z]+://|mailto:", target):
                continue
            name, _, fragment = target.strip("<>").partition("#")
            destination = (path.parent / unquote(name)).resolve() if name else path
            if not destination.exists():
                errors.append({"file": relative, "problem": "missing_link", "target": target})
            elif not destination.is_relative_to(ROOT):
                errors.append({"file": relative, "problem": "outside_repository_link"})
            elif destination.is_relative_to(ROOT / "docs"):
                errors.append({"file": relative, "problem": "private_docs_link", "target": target})
            elif fragment and destination.suffix == ".md":
                headings = re.findall(r"^#+\s+(.+)$", destination.read_text(encoding="utf-8"), re.M)
                anchors = {re.sub(r"[^\w\- ]", "", heading.lower()).replace(" ", "-") for heading in headings}
                if fragment not in anchors:
                    errors.append({"file": relative, "problem": "missing_anchor", "target": target})
            links += 1
    print(json.dumps({"public_files_checked": len(files), "local_links_checked": links, "findings": errors}, indent=2))
    if errors:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
