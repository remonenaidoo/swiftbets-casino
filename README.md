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

## Transfer wallet

`POST /providers/{providerId}/wallet/{transfer-in|transfer-out}`, same signing and body. Only providers whose `WalletModel` is `transfer` may call them (others get `wallet_model_mismatch`); round calls from a transfer provider are refused the same way. Transfer-in debits the player once and transfer-out credits once, both keyed on `providerTransactionId`.

## Reconciliation

From 00:30 UTC the gateway fetches each provider's report for the previous UTC day (`GET {ReportUrl}/{providerId}/{yyyy-MM-dd}`, header `X-Provider-Signature` = HMAC of `GET /reports/{providerId}/{yyyy-MM-dd}`), compares it with our transactions by `providerTransactionId` and by net money, stores the run in `casino.ReconciliationRuns` and publishes `ProviderReconciliationV1` (`Matched` or `Drift`). Operators: `POST /admin/casino/reconciliation/{providerId}/{yyyy-MM-dd}` re-runs a day, `GET /admin/casino/reconciliation?providerId=&limit=` lists runs.

## Catalogue (`SwiftBets.Casino.Catalog.Api`)

Postgres `sb_casino`, schema `catalog` (providers, categories, games, per-market availability), seeded by `SwiftBets.Casino.CatalogMigrator`.

- `GET /casino/lobby`: `{ categories: [{ key, name, games: [{ gameId, name, providerId, category, tag, minBet: { minorUnits, currency } }] }] }`, only games enabled for `Casino:Market` (default `ZA`).
- `GET /casino/games/{gameId}`: the game, its wallet model and whether it is available.
- `POST /admin/casino/games/{gameId}/availability` (Operator) `{ enabled }`.

## Simulated providers (`SwiftBets.Casino.Simulator`)

One host plays both demo providers: `sim-seamless` (Swift Studios, slots and crash) and `sim-transfer` (Transfer Games, live tables).

- `GET /play?session=&game=`: the self-contained game page. It only calls `POST play/start`, `play/spin` and `play/cashout` by RELATIVE url, so it works behind a path prefix (the public gateway serves it at `/casino-sim`, stripping the prefix) and inside a sandboxed iframe; it never navigates the top window or opens popups. The host makes the signed wallet calls server-side.
- Slots draw three reels with a cryptographic RNG. Middle line pays: three 7s 50x, three BARs 20x, three bells 10x, three cherries 5x, any two cherries 2x. The transfer wheel's 12 segments pay 0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 5 and 10x.
- Each call the gateway accepts goes into `simulator.transactions` (same database), and `GET /reports/{providerId}/{yyyy-MM-dd}` serves it, signed as above.
- With `FaultInjection:Enabled=true` (Operator or service token): `POST /faults/{providerId}/drop-from-report?count=1` hides transactions from the next report; `POST /faults/{providerId}/duplicate-next-callback` sends the next win twice.

## Pragmatic Play seamless wallet (D155)

`POST /providers/{providerId}/pragmatic/{method}[.html]`, form-encoded, for providers whose `Protocol` is `pragmatic`. Methods: `authenticate`, `balance`, `bet`, `result`, `bonusWin`, `jackpotWin`, `promoWin`, `endRound`, `refund`, `adjustment`.

- The caller's address must match the provider's allowlist (CIDR blocks, addresses, or internal host names) before anything else; otherwise 403. Callers must reach the service directly: no forwarded headers are trusted.
- `hash` = lower-case hex MD5 of the fields sorted by name, joined `key=value&...` without `hash`, with the secret appended; compared in constant time. Missing or wrong: `error 5`.
- Replies are always HTTP 200 JSON with `error` and `description`, plus `cash`, `bonus`, `currency`, `transactionId`, `usedPromo` as the method needs. Amounts are major units with at most two decimals.
- `reference` is the idempotency key. The first reply is stored with the transaction, in the same SQL transaction, and a repeat gets it back byte for byte; money moves once.
- `refund` pays back the stake we recorded, never the request's amount. A refund for a reference we never saw stores a marker and succeeds; that bet arriving later gets `error 3` and debits nothing. The check is repeated under the insert's key-range lock, and a bet that loses that race is refunded at once.
- Bets ask compliance first: self-exclusion, cooling-off or a betting block gives `error 6`; compliance unreachable gives `error 100`. Wallet limits give `210`, low balance `1`.
- Launch: `POST /casino/launch` asks the provider's `game/url` API (`ApiBaseUrl`, `SecureLogin`, secret) for the game URL with our session token. `POST /casino/demo` (anonymous) returns a free-play URL from `DemoBaseUrl`: no session, no wallet. `GET /casino/recent` (Punter) lists recently played games.
- Catalogue: `getCasinoGames` is synced into the catalogue daily and on demand (`POST /admin/casino/providers/{id}/sync`); new games arrive hidden unless the catalogue's `Casino:ShowNewGames` is on, and operator choices (shown, category, order) survive a resync.
- Console: `GET /admin/casino/providers` (`casino.read`) shows on/off, the allowlist, addresses, and only whether credentials are set. `PUT /admin/casino/providers/{id}` and `PUT .../credentials` (`casino.write`) change them; credentials are stored AES-GCM encrypted with `Casino:CredentialKey` and never returned.
- Reconciliation reuses the daily run above with the provider's `ReportUrl`.

The simulator speaks the same format under `/pragmatic`: the launch and game-list API, a game page that plays real signed rounds (`authenticate`, `bet`, `result`, `endRound`, retrying on `error 100` or no reply), free demo play at `/pragmatic/gs2c/openGame.do`, artwork at `/pragmatic/game_pic/{symbol}.png`, and with fault injection `POST /faults/pragmatic/drill` `{ session, scenario }` for `duplicate-bet`, `refund-before-bet`, `refund-inflated`, `bad-hash` and `bet`.

## Free spins

`POST /admin/casino/free-spins` (Operator) `{ punterId, gameId, spins, expiresAt }` and `GET /admin/casino/free-spins?punterId=`.

## Configuration

| Key | Meaning |
| --- | --- |
| `ConnectionStrings:SbCasino` | SQL Server |
| `Casino:Currency`, `Casino:SessionHours` | Defaults `ZAR`, `2` |
| `Casino:Providers:{id}:Secret` | The provider's signing secret |
| `Casino:Providers:{id}:LaunchBaseUrl` | Where its games are launched |
| `Casino:Providers:{id}:ReportUrl` | Its daily report base, e.g. `http://casino-sim:8080/reports` |
| `Casino:Reconciliation:Enabled` | Daily worker, default `true` |
| `Casino:Providers:{id}:Protocol` | `pragmatic` for Pragmatic Play's seamless API |
| `Casino:Providers:{id}:SecureLogin`, `ApiBaseUrl`, `DemoBaseUrl`, `ImageBaseUrl` | Pragmatic launch, game list, demo and artwork |
| `Casino:Providers:{id}:AllowedAddresses:{n}` | Callback allowlist entries |
| `Casino:CredentialKey` | Base64 of 32 bytes; encrypts credentials saved from the console |
| `Casino:CatalogAddress`, `Casino:CatalogueSync:Enabled` | Where game lists are synced to; daily sync, default `true` |
| `Casino:ShowNewGames` | Catalogue: show newly synced games at once, default `false` |
| `Simulator:PublicBaseUrl`, `Simulator:Providers:pragmatic:SecureLogin` | Simulator: where browsers reach it, and the login its Pragmatic API expects |
| `ConnectionStrings:SbCasinoCatalog` | Postgres `sb_casino` (catalogue API, simulator, catalogue migrator) |
| `Simulator:GatewayAddress`, `Simulator:Providers:{id}:Secret` | Simulator: where the gateway is, and each provider's secret |
| `FaultInjection:Enabled` | Simulator drills, default `false` |
| `Wallet:GrpcAddress` | Wallet gRPC (client-credentials token from `ServiceIdentity:*`) |
| `Kafka:*`, `Jwt:*` | As every SwiftBets service |

## Build and test

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .
dotnet build
dotnet test --project tests/SwiftBets.Casino.Infrastructure.Tests   # real SQL Server via Testcontainers
dotnet test --project tests/SwiftBets.Casino.Catalog.Tests          # real Postgres via Testcontainers
```

## License

MIT
