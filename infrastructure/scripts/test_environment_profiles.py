"""Protect environment/state isolation and the publishable README media contracts."""
import ipaddress
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
TF_ROOT = ROOT / "infrastructure/terraform"
PROFILES = TF_ROOT / "environments"


def read_json(path: Path) -> dict:
    """Read an authored input example without loading operator credentials."""
    return json.loads(path.read_text(encoding="utf-8"))


class EnvironmentProfileTests(unittest.TestCase):
    def test_state_network_and_registration_boundaries(self):
        accounts = set()
        states = set()
        networks = []
        for environment in ("dev", "qa", "prod"):
            folder = PROFILES / environment
            manifest = read_json(folder / "profile.json")
            platform = read_json(folder / "platform.tfvars.json.example")
            identity = read_json(folder / "identity.tfvars.json.example")
            self.assertEqual(platform["environment"], environment)
            self.assertEqual(identity["name"], f"s360-{environment}")
            self.assertEqual(set(platform["regions"]), set(manifest["regions"]))
            self.assertEqual(len(manifest["regions"]), 4 if environment == "prod" else 1)
            account = manifest["state_account_example"]
            self.assertNotIn(account, accounts)
            accounts.add(account)
            for region in platform["regions"].values():
                network = ipaddress.ip_network(region["vnet_cidr"])
                self.assertFalse(any(network.overlaps(existing) for existing in networks))
                networks.append(network)
            for stage in manifest["stages"]:
                backend = (folder / stage["backend"]).read_text(encoding="utf-8")
                self.assertIn(f'key = "{stage["state_key"]}"', backend)
                self.assertIn(f'storage_account_name = "{account}"', backend)
                self.assertTrue(stage["state_key"].startswith(environment + "/"))
                self.assertNotIn((account, stage["state_key"]), states)
                states.add((account, stage["state_key"]))

    def test_complete_region_inputs_and_closed_publication(self):
        expected_roots = {"bootstrap", "platform", "tenant", "identity", "edge", "regional-data", "workloads"}
        for environment in ("dev", "qa", "prod"):
            folder = PROFILES / environment
            manifest = read_json(folder / "profile.json")
            edge = read_json(folder / "edge.tfvars.json.example")
            self.assertEqual(set(edge["geographies"]), set(manifest["regions"]))
            self.assertEqual(set(edge["data_keys"]), set(manifest["regions"]))
            for gate in ("enable_public_traffic", "application_readiness_confirmed", "ownership_routing_confirmed"):
                self.assertIs(edge[gate], False)
            self.assertEqual({stage["root"] for stage in manifest["stages"]}, expected_roots)
            for stage in manifest["stages"]:
                values = read_json(folder / stage["inputs"])
                if "state" in values:
                    self.assertEqual(values["state"]["platform_key"], f"{environment}/platform.tfstate")
                    self.assertEqual(values["state"]["storage_account_name"], manifest["state_account_example"])
                if stage["region"]:
                    self.assertEqual(values["region_key"], stage["region"])
                    if stage["root"] == "workloads":
                        self.assertEqual(values["data_state_key"], edge["data_keys"][stage["region"]])
                        self.assertEqual(values["environment"], environment)
                        self.assertEqual(values["base_domain"], edge["base_domain"])
            for region in manifest["regions"]:
                roots = [stage["root"] for stage in manifest["stages"] if stage["region"] == region]
                self.assertCountEqual(roots, ["regional-data", "workloads"])



if __name__ == "__main__":
    unittest.main()
