# KungFlow

KungFlow is a desktop focus-protection system that detects cognitive overload from computer activity and activates a notification firewall before interruptions take over.

The product watches real work signals from the Windows desktop, learns a personal baseline, and moves the user between three states:

- **Green**: the user is not overloaded, so notifications can pass through.
- **Yellow**: the system is learning or detecting rising load.
- **Red**: the user is overloaded, so the KungFlow firewall can protect focus by silencing notifications.

KungFlow started as a browser-focused prototype, but the current product is a Windows desktop app with OS-level activity collection and notification control.

---

## What KungFlow Does

- Collects desktop activity signals such as open windows, window switches, key presses, delete/backspace usage, typing pace, and mouse movement.
- Builds a personal cognitive-load baseline instead of using one fixed threshold for every user.
- Shows the current state clearly in the desktop app: safe, learning, or overloaded.
- Activates a notification firewall automatically when overload is detected, or manually when the user chooses.
- Supports both global notification protection and selective protection for common interruption-heavy apps such as Outlook, Teams, Slack, Discord, Chrome, Edge, and WhatsApp when Windows exposes their notification settings.
- Stores user accounts, sessions, metric samples, cognitive state, and firewall events in SQL Server.

---

## Product Flow

1. The user registers or logs in through the desktop app.
2. The desktop agent collects activity metrics from Windows in short time windows.
3. The server stores the metrics and updates the user's cognitive state.
4. The desktop app displays the current state and firewall recommendation.
5. When overload is detected, KungFlow activates the firewall according to the user's settings.
6. When the user returns to normal load, the firewall can deactivate and allow notifications again.

---

## Repository Map

### Engineering

| Folder | What it is | Technology |
| --- | --- | --- |
| `desktop/KungFlow.Desktop/KungFlow.Desktop.Agent/` | Desktop agent logic: metrics collection, API client, local settings, login credential storage, and notification firewall control | C# / .NET |
| `desktop/KungFlow.Desktop/KungFlow.Desktop.UI/` | Windows desktop interface: login, status, statistics, settings, privacy, tray behavior | C# / WPF |
| `server/` | Backend API, authentication, cognitive-load calculation, SQL Server storage, firewall events, landing page assets | Node.js / Express / SQL Server |
| `server/db/` | Database setup script, tables, constraints, and stored procedures | T-SQL |
| `scripts/` | Local development helpers for LocalDB, server startup, and desktop startup | PowerShell |
| `docs/` | Product, research, presentation, poster, demo assets, and workshop material | PDF / PPTX / DOCX / media |
| `docs/Hub/` | Final workshop hub, including the current investor pitch deck used for presentation | PPTX |

### Key Documents

| Document | What it contains |
| --- | --- |
| [`docs/product/PRD.docx`](docs/product/PRD.docx) | Product requirements and project definition |
| [`docs/presentation/KungFlow.pdf`](docs/presentation/KungFlow.pdf) | Main product presentation |
| [`docs/research/Kungflow_experiment.docx`](docs/research/Kungflow_experiment.docx) | Internal experiment and validation material |
| [`docs/poster/KungFlow_Project_Poster_15002936.pdf`](docs/poster/KungFlow_Project_Poster_15002936.pdf) | Final project poster for printing |
| [`docs/product-demo-assets/`](docs/product-demo-assets/) | Demo video and product screenshots |
| [`docs/Hub/KungFlow Investor Pitch.pptx`](docs/Hub/KungFlow%20Investor%20Pitch.pptx) | Current final investor pitch deck |
| [`docs/investors presentation/KungFlow Investor Pitch.pptx`](docs/investors%20presentation/KungFlow%20Investor%20Pitch.pptx) | Investor presentation copy |

---

## Current Prototype

The current prototype includes:

- Desktop register, login, logout, and change-password flows.
- Remembered last login credentials for faster sign-in without automatic token login.
- Background tray behavior so the app can keep running when the window is closed.
- Desktop activity collection with inactive-window filtering, so idle periods are not saved as misleading workload samples.
- Status screen with clear green/yellow/red cognitive state and firewall protection messaging.
- Statistics screen with current activity metrics and recent firewall history.
- Settings screen split into activity collection, account security, and firewall control.
- Manual and automatic firewall control.
- Global notification firewall for all Windows notifications.
- Selective notification firewall for available app notification settings.
- SQL-backed storage for users, sessions, metrics, cognitive states, and firewall events.

---

## Architecture

```text
Windows desktop user
        |
        v
KungFlow Desktop UI (WPF)
        |
        v
KungFlow Desktop Agent
  - collects OS activity metrics
  - applies notification firewall settings
  - stores local user preferences
        |
        v
Node.js API server
  - authentication
  - metrics ingestion
  - cognitive-load state calculation
  - firewall event history
        |
        v
SQL Server / SQL Server LocalDB
  - Users
  - Sessions
  - MetricsSamples
  - UserCognitiveStates
  - FirewallEvents
```

The desktop app owns the Windows-specific behavior. The server owns user identity, metric persistence, cognitive-state calculation, and event history. This separation keeps the UI from becoming the place where core product logic lives.

---

## Core Concepts

| Concept | Meaning |
| --- | --- |
| Cognitive-load score | A weighted score computed from activity metrics such as window switching, typing, delete/backspace usage, and mouse movement |
| Baseline | A personal reference score learned from the user's early activity samples |
| Adaptive baseline | A baseline that keeps updating over time with an exponential moving average |
| Notification firewall | The protection layer that silences interruptions when KungFlow detects overload |
| Global mode | Turns off Windows notifications globally while the firewall is active |
| Selective mode | Targets selected app notification settings when Windows exposes them |
| Manual control | User directly activates or deactivates the firewall |
| Automatic control | KungFlow follows the server's overload recommendation |

---

## Backend API

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Server health check |
| `POST /api/auth/register` | Create a new user |
| `POST /api/auth/login` | Log in and create a session token |
| `POST /api/auth/logout` | End the current session |
| `POST /api/auth/change-password` | Change the logged-in user's password |
| `POST /api/metrics` | Save a metrics sample and return updated cognitive status |
| `GET /api/status/current` | Return the user's current cognitive state |
| `POST /api/firewall/events` | Record firewall activation/deactivation events |
| `GET /api/firewall/events` | Return recent firewall events |
| `POST /api/demo/baseline` | Demo-only helper for filling baseline samples |
| `POST /api/demo/overload` | Demo-only helper for triggering overload |
| `POST /api/demo/reset-metrics` | Demo-only helper for clearing a user's metrics |

---

## Data And Privacy

KungFlow is designed around activity metadata, not content capture.

- The desktop agent collects counters and rates, not the text a user types.
- Stored metrics describe behavior windows, such as number of key presses, number of window switches, mouse speed, and visible app-window count.
- The server stores password hashes, not raw passwords.
- The firewall changes local Windows notification settings according to the user's configuration.
- Demo and development data can be reset locally through the database script or demo endpoints.

---

## Research And Validation

### Problem Validation

- Archie, 2026: 77% of employees experienced work-related stress in the past month - https://archieapp.co/blog/workplace-statistics/
- SpeakwiseApp, 2026: additional data on stress, burnout, cognitive load, and workplace burnout - https://speakwiseapp.com/blog/employee-burnout-statistics
- Livegroup, 2026: interruption and attention-economy data connecting alert frequency, overload, focus loss, and burnout - https://143889941.fs1.hubspotusercontent-eu1.net/hubfs/143889941/Downloadable%20Resources/Livegroup/Attention%20Economy%20Report%202026.pdf
- Insightful.io, 2025: stress and burnout effects from poor communication and excessive workplace demands - https://www.insightful.io/reports/stress-at-work
- PMC, 2018: health effects of stress - https://pmc.ncbi.nlm.nih.gov/articles/PMC8368405/
- Meditopia, 2026: economic costs of employee stress for businesses - https://meditopia.com/en/forwork/articles/workplace-stress-statistics
- McKinsey, 2023: cognitive overload and decision-making bias - https://www.mckinsey.com.br/our-insights/bias-busters-how-cognitive-overload-multiplies-every-bias

### Solution Validation

- HCI International, 2018: movement alerts as an indicator of cognitive load - https://journals.sagepub.com/doi/10.1177/1541931218621449
- ACM, 2013: activity analysis as an indicator for identifying workload - https://dl.acm.org/doi/10.1145/2541016.2541083
- PMC, 2023: reducing interruptions improves performance - https://pmc.ncbi.nlm.nih.gov/articles/PMC10244611/
- ResearchGate, 2010: context switching between browser tabs increases cognitive load - https://www.researchgate.net/publication/221515310_A_Study_of_Tabbed_Browsing_Among_Mozilla_Firefox_Users
- ScienceDirect, 2009: typing patterns as an indicator of stress - https://www.sciencedirect.com/science/article/abs/pii/S1071581909000937
- Internal experiment - [`docs/research/Kungflow_experiment.docx`](docs/research/Kungflow_experiment.docx)

### Existing Products

- Focus Bear - https://www.focusbear.io/
- Freedom / Opal - https://freedom.to/
- Soma Reality - https://somareality.com/
