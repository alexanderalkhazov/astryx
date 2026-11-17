# Astryx: AI Trading Workstation

A private mobile workstation for researching US stocks and paper trading them: a scanner, deterministic indicators, AI-written research, risk-checked proposals, and simulated execution.

**[`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md) is the architectural source of truth. Read it before changing anything.**
**[`TASKS.md`](TASKS.md) holds the working rules and the ordered backlog.** Tick each task with its commit hash as described there.
Design references live in [`docs/design/png/`](docs/design/png).

> Status: **skeleton only**. No product features are implemented yet (see SYSTEM_DESIGN.md §4.5 and §24).
> Real-money trading is disabled. The API refuses to start if `Features:LiveTrading=true`.

## Layout

```
apps/mobile        Expo SDK 58 + Expo Router (routes in src/app)
apps/api           ASP.NET Core .NET 10 host: health, system status, module composition
src/BuildingBlocks SharedKernel, Contracts, Infrastructure (health checks, startup guards)
src/Modules/*      One project per module (Identity, MarketData, Ai, Trading, Paper, Portfolio, …)
src/Workers        BackgroundService registration (none yet)
packages/*         @astryx/design-tokens, @astryx/api-client, @astryx/tsconfig
tests/*            Unit, Architecture, Integration (Testcontainers)
infra/docker       docker-compose.yml + api.Dockerfile
docs/              design PNGs, ADRs
```

## Prerequisites (macOS)

| Tool | Install |
|---|---|
| Docker Desktop | running |
| .NET SDK 10 | `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 10.0` (installs to `~/.dotnet`, so add it to `PATH`) or `brew install --cask dotnet-sdk` |
| Node ≥ 22 + pnpm | `brew install node pnpm` |
| Ollama | `brew install ollama`, then `ollama serve` (or the app) and `ollama pull qwen3.5:0.8b` |
| Xcode (iOS simulator) | Run `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer` once, or prefix commands with `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer` |

## First run

```bash
cp .env.example .env          # set POSTGRES_PASSWORD, and JWT_SIGNING_KEYS from: dotnet run --project apps/api -- --generate-jwt-key
pnpm install
pnpm stack:up                 # postgres:17, mailpit, api → http://localhost:5080
curl localhost:5080/health/deps
pnpm api:test                 # .NET unit + architecture + integration tests (needs Docker)
pnpm typecheck
pnpm lint                     # ESLint (zero warnings); `pnpm format` / `pnpm format:check` run Prettier
dotnet format Astryx.slnx     # C# formatting; CI runs it with --verify-no-changes
pnpm mobile                   # Metro; press i for the iOS simulator
```

| URL | What |
|---|---|
| http://localhost:5080/health/ready | API + PostgreSQL |
| http://localhost:5080/health/deps | Adds Ollama and model presence (Degraded is OK) |
| http://localhost:5080/api/v1/system/status | Status consumed by the app |
| http://localhost:5080/openapi/v1.json | OpenAPI (Development) |
| http://localhost:8025 | Mailpit inbox |

Ports: the API uses **5080** on the host (`API_PORT` in `.env`) because 8080 is often taken. Ollama runs natively, and containers reach it at `host.docker.internal:11434`.

## Real market data and Alpaca Paper

The stack uses deterministic fake market data by default (tests, CI and the Maestro flows depend on it). To use real
US equity data and send approved trades to an Alpaca paper account (SYSTEM_DESIGN.md §16.2, §17):

```bash
# .env (never commit keys)
MARKET_DATA_PROVIDER=Alpaca
MARKET_DATA_REQUESTS_PER_MINUTE=80
ALPACA_KEY_ID=PK…                   # Alpaca dashboard › Paper trading › API keys (market data for the whole server)
ALPACA_SECRET_KEY=…
CREDENTIALS_MASTER_KEYS=$(dotnet run --project apps/api -- --generate-credentials-key)   # encrypts users' stored keys

pnpm stack:up
curl -X POST localhost:5080/dev/market/purge-fake   # once, when an existing database switches from fake data
```

The first sync lists the S&P 500 universe, then backfills two years of daily bars in about 30 minutes. Each user
connects their own paper keys in **Settings › Broker connection › Connect Alpaca**; from then on their approved trades
go to Alpaca as bracket orders and Alpaca's fills are mirrored. Disconnecting switches back to the simulated account.
`ALPACA_KEY_ID=… ALPACA_SECRET_KEY=… dotnet test tests/Astryx.IntegrationTests --filter AlpacaPaperSmokeTests` checks
the real Trading API without filling anything.

## Development build (iOS)

Expo Go can't load native modules beyond its own set, and it can't run SDK 58 on a physical iPhone. Daily work uses the dev client:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer   # once; or prefix commands with DEVELOPER_DIR=…
cd apps/mobile
npx expo run:ios --device "iPhone 17"   # prebuilds ios/ (gitignored), compiles, installs, starts Metro
```

After that, JavaScript changes only need `pnpm mobile` (Metro), and the installed **Astryx** app connects to it. Rebuild with `npx expo prebuild --clean -p ios && npx expo run:ios` after adding a native package, editing `app.json` or upgrading the SDK. `ios/` is generated, so never edit it by hand. The dev client registers the `astryx://` scheme, so emailed verify and reset links open the app directly.

If `pod install` fails with an encoding error, run `export LANG=en_US.UTF-8`.

## Notes

- Traces, metrics and the API's spans per job and outbox event are in the Aspire dashboard at http://localhost:18888 (compose service `otel`). Sentry stays off unless `SENTRY_DSN` (API) or `EXPO_PUBLIC_SENTRY_DSN` (app) is set.

- SDK 58 apps can't be opened in Expo Go on a physical device. Use the simulator, or build a dev client with `npx expo run:ios`.
- `qwen3.5:0.8b` does not support Ollama structured output (`format`). The AI layer prompts for JSON and validates it (SYSTEM_DESIGN.md §12).
- Add Expo packages with `npx expo install <pkg>` from `apps/mobile`, never with `pnpm add`. That keeps versions SDK-compatible.
- Simulator E2E flows live in `apps/mobile/e2e` (Maestro: `brew install openjdk mobile-dev-inc/tap/maestro`). Run them with `JAVA_HOME=/opt/homebrew/opt/openjdk DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer maestro --device <udid> test -e EMAIL=… -e PASSWORD=… e2e/sign-in.yaml`, then `e2e/shell.yaml`. They target the dev client by default. For Expo Go, add `-e APP_ID=host.exp.Exponent -e LINK=exp://127.0.0.1:8081/--/` (its floating dev-menu button covers the top-right header icons there).
- The ten-step journey (T-110) runs against the compose stack with fake data, at any hour:
  1. Create a verified user once: `node e2e/scripts/create-user.mjs` (uses Mailpit on :8025). It prints `EMAIL=… PASSWORD=…`.
  2. Stop the dev client's own menu from opening at launch and covering taps (once per simulator):
     `for k in EXDevMenuShowsAtLaunch EXDevMenuShowFloatingActionButton EXDevMenuTouchGestureEnabled EXDevMenuMotionGestureEnabled; do xcrun simctl spawn <udid> defaults write app.astryx.mobile $k -bool NO; done`
  3. With Metro running (`npx expo start --dev-client`), run `maestro --device <udid> test -e EMAIL=… -e PASSWORD=… e2e/journey.yaml`.

  `e2e/states.yaml` (T-111) runs the same way. It walks the failure states: empty Discover, the scanner error, the connection-lost banner, approval disabled on stale prices, and a stop hit. It takes a few minutes because it waits for real quote syncs.

  The setup script reseeds the fake market (`POST /dev/market/reseed`) and forces an open session with NVDA at 186.60 (`POST /dev/faults`). It also resets the paper account. These dev routes exist only in Development with `Features:DevFaultInjection=true`.
