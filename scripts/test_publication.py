"""Verify the credential detector distinguishes literal tokens from embedded icon payloads."""
import importlib.util
from pathlib import Path
import unittest
import re
import struct
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("publication", Path(__file__).with_name("check-publication.py"))
publication = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publication)


class PublicationCheckTests(unittest.TestCase):
    def test_literal_provider_token_is_detected_without_printing_it(self):
        fake = b"gh" + b"p_" + b"A" * 36
        self.assertIsNotNone(publication.PATTERNS["provider_token"].search(b'key="' + fake + b'"'))

    def test_embedded_image_substring_is_not_a_literal_api_key(self):
        fake = b"AI" + b"za" + b"A" * 35
        self.assertIsNone(publication.PATTERNS["provider_token"].search(b"data:image/png;base64,abcd" + fake + b"zzzz"))

    def test_private_key_and_cloud_connection_credential_are_detected(self):
        marker = b"-----BEGIN " + b"PRIVATE KEY-----"
        self.assertIsNotNone(publication.PATTERNS["private_key"].search(marker))
        credential = b"SharedAccess" + b"Key=" + b"A" * 40
        self.assertIsNotNone(publication.PATTERNS["cloud_credential"].search(credential))


    def test_readme_media_are_case_correct_repository_pngs(self):
        source = (publication.ROOT / "README.md").read_text(encoding="utf-8")
        self.assertNotIn("<img", source)
        images = re.findall(r"!\[[^\]]*\]\(([^)]+)\)", source)
        expected = {"control-tower.png", "insured-app.png", "adjuster-app.png", "20-azure-components.png"}
        self.assertTrue(expected.issubset({Path(path).name for path in images}))
        for relative in images:
            self.assertTrue(relative.startswith("./assets/"), relative)
            path = publication.ROOT
            for segment in Path(relative).parts:
                self.assertIn(segment, {child.name for child in path.iterdir()}, relative)
                path = path / segment
            if path.suffix == ".svg":
                ET.parse(path)
                continue
            raw = path.read_bytes()
            self.assertTrue(raw.startswith(b"\x89PNG\r\n\x1a\n"), relative)
            width, height = struct.unpack(">II", raw[16:24])
            self.assertGreater(width, 0)
            self.assertGreater(height, 0)
            self.assertEqual(raw[-12:], b"\x00\x00\x00\x00IEND\xaeB`\x82")


if __name__ == "__main__":
    unittest.main()
