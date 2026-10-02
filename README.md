# swiftbets-casino

The casino integration layer: game launch, the seamless wallet API providers call back into, and free spins. Providers host the games; this service owns sessions, money movement (through the wallet's gRPC API) and the record of every provider transaction.

| Host | Purpose |
| --- | --- |
| `SwiftBets.Casino.Gateway.Api` | Launch, provider wallet callbacks, free-spin admin |
| `SwiftBets.Casino.Migrator` | SQL Server `SbCasino` (schema `casino`); every migration has a rollback in `Rollbacks/` |

## Launch

`POST /casino/launch` (Punter) `{ "gameId": "sun-temple", "providerId": "sim-seamless" }` returns `{ sessionToken, launchUrl, expiresAt }`.

- Refused with 403 `casino_restricted` while the player has an active self-exclusion, cooling-off or betting block (compliance's compacted `compliance.restrictions-changed` topic).
- Refused with 503 `restrictions_unavailable` until that topic has loaded: launching fails closed.
- Only the token's SHA-256 is stored; sessions last `Casino:SessionHours` (2).

## Seamless wallet API

`POST /providers/{providerId}/wallet/{balance|bet|win|rollback}`, signed by the provider:

- Header `X-Provider-Signature`: lower-case hex HMAC-SHA256 of the exact request body with the provider's secret. A missing or wrong signature is 401 before the body is parsed.
- Body: `{ sessionToken, providerTransactionId, roundId, gameId, amount, currency, freeSpin?, referenceTransactionId? }` (amounts in minor units).
- Reply: `{ status, balance, transactionId }`. `status` is `ok`, `session_invalid`, `insufficient_funds`, `limit_exceeded`, `account_restricted`, `no_free_spins`, `bet_rolled_back`, `invalid_reference`, `currency_mismatch`, `invalid_request` (400) or `wallet_unavailable` (503, retry).

Rules:

- Every call is applied once, keyed on `(providerId, providerTransactionId)`. A duplicate returns the original transaction and moves no money.
- Bets debit and wins credit through the wallet under `casino:{providerId}:{providerTransactionId}`, so a retry after a crash is safe.
- A free-spin bet takes one spin from the player's grant for the game and debits nothing.
- A rollback refunds an applied bet once. A rollback for a bet never seen is stored and acknowledged with no money moved; if that bet arrives later it is refused with `bet_rolled_back`.
- Each applied transaction publishes `CasinoTransactionV1` (`casino.transaction.v1`) through the outbox in the same SQL transaction.

## Free spins

`POST /admin/casino/free-spins` (Operator) `{ punterId, gameId, spins, expiresAt }` and `GET /admin/casino/free-spins?punterId=`.

## Configuration

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:SbCasino` | SQL Server |
| `Casino:Currency`, `Casino:SessionHours` | Defaults `ZAR`, `2` |
| `Casino:Providers:{id}:Secret` | The provider's signing secret |
| `Casino:Providers:{id}:LaunchBaseUrl` | Where its games are launched |
| `Wallet:GrpcAddress` | Wallet gRPC (client-credentials token from `ServiceIdentity:*`) |
| `Kafka:*`, `Jwt:*` | As every SwiftBets service |

## Build and test

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .
dotnet build
dotnet test --project tests/SwiftBets.Casino.Infrastructure.Tests   # real SQL Server via Testcontainers
```

## License

MIT
