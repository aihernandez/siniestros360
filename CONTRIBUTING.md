# Contributing

Siniestros360 is a single-insurer demonstration, not a production claims platform. Discuss changes through GitHub Issues before expanding the business scope or adding infrastructure. See [architecture](handbook/architecture.md) and [known limitations](handbook/known-limitations.md).

Use a short-lived `feat/`, `fix/`, `docs/`, `test/`, `ci/` or `chore/` branch (optionally under `codex/`). Keep each commit focused and use Conventional Commits. Describe the resulting behavior, why it is needed, relevant checks and rollback in the PR. Include screenshots for visible changes and migration implications for data changes. Main is integrated through reviewed PRs; dev/QA/prod are deployment configurations, not permanent branches.

Use synthetic data and private local configuration. Never commit passwords, Azure connection strings, real claim/GPS/document data, generated bundles, state files or personal contact details. Report security problems using [SECURITY.md](SECURITY.md), not a public issue.

Follow existing C#/TypeScript/Python conventions. Preserve readable blocks and service-owned data boundaries. Claims is the CQRS pilot; do not convert unrelated services as incidental cleanup. Select tests by changed behavior; unchanged evidence can be reused. A release candidate additionally needs the repository CI checks. [Validation](handbook/validation.md) describes the layers and limits.

The owner can accept changes directly after successful required checks for this single-maintainer project. No independent human approval or production deployment is implied. Maintainers must not bypass failed checks to publish. New release tags identify reviewed main commits; v0.x releases retain demonstration status.
