# Architecture diagrams

GitHub draws these Mermaid diagrams directly. In VS Code, use a Markdown preview with Mermaid support.

## 1. System context

What the app talks to, and what stays on the machine.

```mermaid
flowchart LR
    user([User])

    subgraph pc["Windows PC"]
        app["Claude Usage Monitor<br/>(WPF, .NET 8)"]
        logs[("Claude Code logs<br/>%USERPROFILE%\.claude\projects\*.jsonl")]
        cfg[("Claude Code config<br/>.claude.json, .credentials.json")]
        store[("App data<br/>settings.json, app.log,<br/>WebView2 session")]
        run[("Registry<br/>HKCU Run key")]
        wv2["Microsoft Edge<br/>WebView2 Runtime"]
    end

    claude(["claude.ai<br/>/api/organizations<br/>/api/organizations/{id}/usage"])

    user -->|"dashboard, widget,<br/>settings, about"| app
    app -->|"read only"| logs
    app -->|"read only, no tokens kept"| cfg
    app <-->|"read / write"| store
    app -->|"start with Windows"| run
    app --> wv2
    wv2 -->|"HTTPS, inside the<br/>signed-in browser"| claude
```

## 2. Components

How the code is split. Arrows point from the caller to what it uses.

```mermaid
flowchart TB
    subgraph ui["Windows (UI)"]
        App["App<br/>single instance, startup mode,<br/>exit when no window is open"]
        Main["MainWindow<br/>dashboard"]
        Widget["WidgetWindow<br/>always-on-top glass"]
        Settings["SettingsWindow"]
        About["AboutWindow"]
        Login["LoginWindow<br/>WebView2 sign-in"]
    end

    subgraph controls["Controls"]
        Ring["RingGauge"]
        Glass["GlassEffect (blur)<br/>GlassDialog (base)"]
        MaxFix["MaximizeFix"]
        Palette["Palette"]
    end

    subgraph services["Services"]
        Hub["MonitorHub<br/>refresh timer, reset timer,<br/>shared data, Changed event"]
        LogReader["ClaudeLogReader<br/>dedupe by message + request ID"]
        AccReader["AccountReader"]
        Agg["UsageAggregator<br/>+ PricingTable"]
        Fmt["UsageFormatting"]
        Web["ClaudeWebSession<br/>hidden WebView2, /api/ paths only"]
        Live["LiveUsageService"]
        Parser["LiveUsageParser"]
        SetStore["SettingsStore"]
        Startup["StartupRegistration"]
        Info["AppInfo"]
        Log["DebugLog"]
    end

    App --> Hub
    App --> SetStore
    App --> Main
    App --> Widget
    Main --> Hub
    Widget --> Hub
    Main --> Fmt
    Widget --> Fmt
    Main --> Settings
    Main --> About
    Widget --> Settings
    Widget --> About
    App --> Login
    Settings --> Startup
    Settings --> SetStore
    About --> Info

    Hub --> LogReader
    Hub --> AccReader
    Hub --> Agg
    Hub --> Web
    Hub --> Live
    Live --> Web
    Live --> Parser
    Login -. "shares cookies via one<br/>WebView2 environment" .- Web

    Main --> Ring
    Widget --> Ring
    Main --> Glass
    Widget --> Glass
    Settings --> Glass
    About --> Glass
    Main --> MaxFix
    Fmt --> Palette

    Web --> Log
    Hub --> Log
```

## 3. Refresh cycle

One refresh feeds every open window. It runs on the timer (30 s to 1 h, default 1 min), on a reset timer 5 s after a limit resets (when that comes first), on F5, or from a Refresh button.

```mermaid
sequenceDiagram
    autonumber
    participant T as Timer / reset timer / F5 / button
    participant H as MonitorHub
    participant R as ClaudeLogReader + AccountReader
    participant A as UsageAggregator
    participant S as LiveUsageService
    participant W as ClaudeWebSession (hidden WebView2)
    participant C as claude.ai
    participant UI as MainWindow + WidgetWindow

    T->>H: RefreshAsync()
    H->>UI: Changed (loading)
    par Background thread
        H->>R: read logs and account files
        R-->>H: UsageRecord list, AccountInfo
        H->>A: Build(records, now)
        A-->>H: UsageSummary
    end
    H->>S: FetchAsync(Claude Code's org ID)
    S->>W: GET /api/organizations
    W->>C: fetch inside the page (cookies stay in the browser)
    C-->>W: 200 or 401/403
    alt Signed in
        S->>W: GET /api/organizations/{id}/usage
        W->>C: fetch
        C-->>W: limits JSON
        S-->>H: LiveUsageResult Ok (limits, extra usage, spend)
    else Not signed in
        S-->>H: LiveUsageResult SignedOut
    else Offline or error
        S-->>H: LiveUsageResult Error (last good data kept)
    end
    H->>UI: Changed (done)
    UI->>UI: redraw gauges, charts, tables
```

## 4. Sign-in and sign-out

The app never reads the password or the session cookie. Both stay inside WebView2.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant M as Dashboard / widget
    participant L as LoginWindow (WebView2)
    participant E as Shared WebView2 environment
    participant C as claude.ai
    participant H as MonitorHub

    U->>M: Sign in
    M->>L: ShowDialog()
    L->>E: same user data folder as the hidden browser
    L->>C: open /login
    U->>C: signs in (email link, Google, ...)
    C-->>L: navigates to an app page
    L->>C: GET /api/organizations (inside the page)
    C-->>L: 200
    L-->>M: DialogResult = true (window closes)
    M->>H: RefreshLiveAsync()
    Note over H,C: The hidden browser now shares the cookies, so live limits load.

    U->>M: Sign out
    M->>H: SignOutAsync()
    H->>E: start hidden browser if needed, then ClearBrowsingDataAsync()
    H-->>M: Changed (signed out)
```

## 5. Startup and lifetime

```mermaid
flowchart TD
    start(["ClaudeUsageMonitor.exe<br/>(optional --widget)"]) --> mutex{"Another copy<br/>running?"}
    mutex -- yes --> signal["Signal it to show the dashboard"] --> quit(["Exit"])
    mutex -- no --> load["Load settings.json<br/>create MonitorHub"]
    load --> mode{"--widget?"}
    mode -- no --> dash["Open dashboard"]
    mode -- yes --> widget["Open widget"]
    dash --> wasOpen{"Widget open<br/>last time?"}
    wasOpen -- yes --> widget
    wasOpen -- no --> hubStart
    widget --> hubStart["Hub.Start(): first refresh + timer"]
    hubStart --> running(["Running"])
    running --> closeAll{"Exit chosen<br/>(closing windows keeps the tray icon)"}
    closeAll -- yes --> dispose["Dispose hub (hidden browser),<br/>release mutex"] --> quit
```

Start with Windows writes `"<exe path>" --widget` (or without the switch) to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so at sign-in Windows takes the `--widget` path above.

## 6. Development workflow with agents

The main Claude Code session is the coordinator. It writes the code and hands checks to subagents defined in `.claude/agents/`.

```mermaid
flowchart LR
    user([User request]) --> coord["Coordinator<br/>(main session)"]
    coord -->|"after each change"| err["error-checker<br/>Sonnet, low effort<br/>build + exact fixes"]
    coord -->|"before commits"| rev["code-reviewer<br/>Sonnet<br/>bugs, security, privacy"]
    coord -->|"UI ideas"| uia["ui-advisor<br/>Haiku, low effort"]
    coord -->|"doc comments"| cw["comment-writer<br/>Haiku"]
    coord -->|"docs + commits"| dg["docs-git-manager<br/>Sonnet"]
    err -->|"suggested fixes"| coord
    rev -->|"findings"| coord
    uia -->|"recommendations"| coord
    cw -->|"/// comments only"| code[("Source code")]
    coord -->|"applies fixes"| code
    dg -->|"README, docs/, local commits"| repo[("Git repository")]
    coord -->|"push after user asks"| gh(["GitHub"])
    repo --> gh
```
