# Security policy

Only the latest v0.x demonstration release is maintained. This repository is educational software and does not claim production hardening, compliance certification or recovery guarantees.

Report vulnerabilities privately through [GitHub private vulnerability reporting](https://github.com/aihernandez/siniestros360/security/advisories/new). Do not include real credentials or personal/claim information in public issues, screenshots, traces or logs. Provide the affected revision, a synthetic reproduction, impact and expected behavior.

Demo account creation is opt-in and restricted to Development. Its password must come from local secrets or environment configuration; no fixed working password is shipped in the UI. Disabling seeding does not revoke accounts already created in an old database. Use a new disposable demo volume, or explicitly remove/disable previous accounts before any shared use. Never expose a demo instance on the Internet.

Use generated local JWT/database credentials, an isolated development broker and synthetic data. Terraform inputs are examples; state and saved plans are sensitive and must remain private. Public WAF/Traffic Manager publication is disabled by default. Entra client migration, home-region ownership/fencing, SignalR scale-out and cross-region recovery remain open work.

Secret scanning and push protection supplement review. They do not prove that all credentials are absent or safe. If a real credential has been committed, revoke/rotate it first and coordinate history cleanup; deleting a current file does not remove Git history.
