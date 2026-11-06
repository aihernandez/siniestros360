# Repository governance

The publication scope is the owner-requested public demonstration release v0.1.0: curated docs/media, MIT license, safe opt-in demo data, validation, Azure/Terraform reference configurations and technology badges. Runtime production/cloud migration is outside this release.

Main is the sole permanent branch. Use short-lived Conventional Commit branches and focused PRs. Squash is the normal integration default. This initial publication uses ordered dependent PRs with merge commits to retain their reviewed source SHAs and avoid rewriting the dependency stack. Merge only after relevant checks pass, without force pushing main or bypassing failed checks. The owner explicitly authorized creating and merging this release's PRs. This single-maintainer repository does not claim an independent human approval.

Proposed source changes are grouped as: safe demo/runtime and checks; Terraform/reference infrastructure; public architecture/license/walkthrough and badges. Each PR must describe observable behavior, validation, known limits and rollback. No Azure deployment runs on merge. Environment profiles are not permanent branches.

GitHub Actions results and immutable commit/PR URLs are the execution authority. The release tag must point to integrated main. A tag/Release publishes source and evidence; it does not certify Azure deployment, all optional e2e capabilities or production readiness. Do not publish private logs, user secrets, state, plans or real user data in artifacts.
