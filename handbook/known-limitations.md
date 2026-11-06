# Known limitations

- **Azure is authored, not deployed.** Terraform profiles, private networking, WAF and Entra registrations have local/mocked validation; no Azure plan/apply or recovery drill is established by that evidence.
- **Current auth is custom JWT.** Entra login/account linking and client session hardening remain work. Local demo seeding must be explicitly enabled, with a caller-provided password, in Development. Previously persisted accounts require operator cleanup.
- **Regional ownership is not implemented.** Geographic DNS does not preserve a claim's writer/home region. Independent stamps are not a four-way shared claim database. RTO/RPO, residency, recovery destinations and fencing require decisions and implementation.
- **Backend scale-out is limited.** Terraform uses one replica/Recreate for backends. SignalR transport/session affinity, distributed rate limits and safe rolling migrations remain open. This is not an HA/SLO claim.
- **Documents use a compatibility SAS.** Replace the REST adapter with workload-identity SDK access before disabling shared storage keys. Coordinate credential/config rotation and redeployment.
- **Live-broker e2e needs credentials.** Public GitHub build/test checks do not imply the optional real-Service-Bus journey executed. Check job details and release evidence.
- **BUG-045 remains needs-reproduction.** Historical browser runs recorded an intermittent element-not-found failure; preserve trace/screenshots before retrying. No fresh reproduction or fix is claimed without evidence.
- **Mobile support is React/Capacitor Android.** No React Native or iOS implementation, physical-device background GPS, or network-loss validation is claimed.
- The system does not integrate ambulance dispatch, real insurer policy systems, SMS/push, payments or workshop logistics. Coverage uses the configured provider adapter/demo behavior. No compliance certification is claimed.

v0.1.0 is a public demonstration release. These limitations are explicit scope, not hidden production guarantees.
