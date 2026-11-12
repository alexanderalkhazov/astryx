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

- SDK 58 apps can't be opened in Expo Go on a physical device. Use the simulator, or build a dev client with `npx expo run:ios`.
- `qwen3.5:0.8b` does not support Ollama structured output (`format`). The AI layer prompts for JSON and validates it (SYSTEM_DESIGN.md §12).
- Add Expo packages with `npx expo install <pkg>` from `apps/mobile`, never with `pnpm add`. That keeps versions SDK-compatible.
- Simulator E2E flows live in `apps/mobile/e2e` (Maestro: `brew install openjdk mobile-dev-inc/tap/maestro`). Run them with `JAVA_HOME=/opt/homebrew/opt/openjdk DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer maestro --device <udid> test -e EMAIL=… -e PASSWORD=… e2e/sign-in.yaml`, then `e2e/shell.yaml`. They target the dev client by default. For Expo Go, add `-e APP_ID=host.exp.Exponent -e LINK=exp://127.0.0.1:8081/--/` (its floating dev-menu button covers the top-right header icons there).
