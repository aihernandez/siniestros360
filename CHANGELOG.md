# Changelog

## 0.1.0 — First public demonstration release

- Publish the nine-service/three-app architecture, editable Azure diagrams, historical synthetic screenshots and a guided walkthrough.
- Add MIT licensing, contribution/security guidance, third-party attribution and a curated public handbook.
- Make demo seeding explicit, Development-only and dependent on caller-provided credentials; remove fixed login passwords from source frontends.
- Supply Terraform roots/modules plus separate dev/QA/prod profiles, container recipes and mocked infrastructure checks.
- Validate the release through GitHub build/test, container and infrastructure checks. Optional live-broker e2e is reported separately when no broker secret is configured.
- Keep Entra migration, home-region routing/fencing, cross-region recovery and application HA explicitly outside the demonstrated runtime scope.
