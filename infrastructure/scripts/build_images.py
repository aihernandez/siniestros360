"""Print the release build plan; --build executes it, --push also publishes to ACR."""
import argparse
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
APPS = ("insured-app", "adjuster-app", "control-tower")


def release_plan(registry, release, sdk, runtime, node, nginx):
    if not re.fullmatch(r"[a-z0-9]+\.azurecr\.io", registry):
        raise ValueError("Use the ACR login server, without a scheme or path.")
    if not re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}", release) or release.lower() == "latest":
        raise ValueError("Use an immutable release tag; latest is prohibited.")
    for image in (sdk, runtime, node, nginx):
        if not re.fullmatch(r"(?:[^@\s]+@sha256:[a-f0-9]{64}|[^@\s]+:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})", image) or image.lower().endswith(":latest"):
            raise ValueError("Each base image needs an explicit tag or SHA256 digest.")
    services = json.loads((ROOT / "infrastructure/terraform/services.json").read_text())
    images = {"release_id": release, "service_images": {}, "migration_images": {}, "frontend_images": {}}
    commands = []

    def add(group, key, recipe, arguments):
        tag = f"{registry}/siniestros360/{key}{'-migration' if group == 'migration_images' else ''}:{release}"
        images[group][key] = tag
        command = ["docker", "build", "--platform", "linux/amd64", "-f", f"infrastructure/containers/{recipe}.Dockerfile", "-t", tag]
        for name, value in arguments.items():
            command.extend(["--build-arg", f"{name}={value}"])
        commands.append(command + ["."])

    for key, service in services.items():
        assembly = f"Siniestros360.{service['project']}Service"
        add("service_images", key, "backend", {"SDK_IMAGE": sdk, "RUNTIME_IMAGE": runtime, "PROJECT": f"src/services/{assembly}/{assembly}.csproj", "ASSEMBLY": assembly})
        add("migration_images", key, "migrations", {"SDK_IMAGE": sdk, "RUNTIME_IMAGE": runtime, "SERVICE": service["project"], "DB_KEY": service["db_key"]})
    add("service_images", "gateway", "backend", {"SDK_IMAGE": sdk, "RUNTIME_IMAGE": runtime, "PROJECT": "src/gateway/Siniestros360.Gateway/Siniestros360.Gateway.csproj", "ASSEMBLY": "Siniestros360.Gateway"})
    for app in APPS:
        add("frontend_images", app, "frontend", {"NODE_IMAGE": node, "NGINX_IMAGE": nginx, "APP": app})
    return images, commands


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("registry", "release", "sdk", "runtime", "node", "nginx"):
        parser.add_argument(f"--{name}", required=True)
    parser.add_argument("--output", type=Path, required=True, help="Non-secret workload image variables JSON; keep outside source control.")
    parser.add_argument("--build", action="store_true")
    parser.add_argument("--push", action="store_true")
    parser.add_argument("--group", choices=("service_images", "migration_images", "frontend_images"), help="Build only one release group; useful for parallel CI without publishing images.")
    args = parser.parse_args()
    if args.push and not args.build:
        parser.error("--push requires --build")
    try:
        images, commands = release_plan(args.registry, args.release, args.sdk, args.runtime, args.node, args.nginx)
    except ValueError as error:
        parser.error(str(error))
    if args.group:
        selected = set(images[args.group].values())
        commands = [command for command in commands if command[command.index("-t") + 1] in selected]
        images = {"release_id": images["release_id"], args.group: images[args.group]}
    for command in commands:
        print(subprocess.list2cmdline(command), flush=True)
        if args.build:
            subprocess.run(command, cwd=ROOT, check=True)
            if args.push:
                subprocess.run(["docker", "push", command[command.index("-t") + 1]], cwd=ROOT, check=True)
    args.output.write_text(json.dumps(images, indent=2) + "\n", encoding="utf-8")
    print(f"{'Built' if args.build else 'Planned'} {len(commands)} images. Variables: {args.output}")


if __name__ == "__main__":
    main()
