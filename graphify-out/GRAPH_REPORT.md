# Graph Report - Avlone Uı VortexAI  (2026-07-01)

## Corpus Check
- 154 files · ~34,622 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1226 nodes · 2141 edges · 91 communities (72 shown, 19 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS · INFERRED: 4 edges (avg confidence: 0.5)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]
- [[_COMMUNITY_Community 19|Community 19]]
- [[_COMMUNITY_Community 20|Community 20]]
- [[_COMMUNITY_Community 21|Community 21]]
- [[_COMMUNITY_Community 22|Community 22]]
- [[_COMMUNITY_Community 23|Community 23]]
- [[_COMMUNITY_Community 24|Community 24]]
- [[_COMMUNITY_Community 25|Community 25]]
- [[_COMMUNITY_Community 26|Community 26]]
- [[_COMMUNITY_Community 27|Community 27]]
- [[_COMMUNITY_Community 28|Community 28]]
- [[_COMMUNITY_Community 29|Community 29]]
- [[_COMMUNITY_Community 30|Community 30]]
- [[_COMMUNITY_Community 32|Community 32]]
- [[_COMMUNITY_Community 33|Community 33]]
- [[_COMMUNITY_Community 34|Community 34]]
- [[_COMMUNITY_Community 35|Community 35]]
- [[_COMMUNITY_Community 36|Community 36]]
- [[_COMMUNITY_Community 37|Community 37]]
- [[_COMMUNITY_Community 38|Community 38]]
- [[_COMMUNITY_Community 39|Community 39]]
- [[_COMMUNITY_Community 40|Community 40]]
- [[_COMMUNITY_Community 41|Community 41]]
- [[_COMMUNITY_Community 42|Community 42]]
- [[_COMMUNITY_Community 43|Community 43]]
- [[_COMMUNITY_Community 44|Community 44]]
- [[_COMMUNITY_Community 45|Community 45]]
- [[_COMMUNITY_Community 46|Community 46]]
- [[_COMMUNITY_Community 47|Community 47]]
- [[_COMMUNITY_Community 48|Community 48]]
- [[_COMMUNITY_Community 49|Community 49]]
- [[_COMMUNITY_Community 50|Community 50]]
- [[_COMMUNITY_Community 51|Community 51]]
- [[_COMMUNITY_Community 52|Community 52]]
- [[_COMMUNITY_Community 53|Community 53]]
- [[_COMMUNITY_Community 54|Community 54]]
- [[_COMMUNITY_Community 55|Community 55]]
- [[_COMMUNITY_Community 57|Community 57]]
- [[_COMMUNITY_Community 60|Community 60]]
- [[_COMMUNITY_Community 61|Community 61]]
- [[_COMMUNITY_Community 62|Community 62]]
- [[_COMMUNITY_Community 63|Community 63]]
- [[_COMMUNITY_Community 64|Community 64]]
- [[_COMMUNITY_Community 65|Community 65]]
- [[_COMMUNITY_Community 66|Community 66]]
- [[_COMMUNITY_Community 69|Community 69]]
- [[_COMMUNITY_Community 70|Community 70]]
- [[_COMMUNITY_Community 71|Community 71]]
- [[_COMMUNITY_Community 72|Community 72]]
- [[_COMMUNITY_Community 73|Community 73]]
- [[_COMMUNITY_Community 74|Community 74]]
- [[_COMMUNITY_Community 75|Community 75]]
- [[_COMMUNITY_Community 76|Community 76]]
- [[_COMMUNITY_Community 77|Community 77]]
- [[_COMMUNITY_Community 78|Community 78]]
- [[_COMMUNITY_Community 79|Community 79]]
- [[_COMMUNITY_Community 80|Community 80]]

## God Nodes (most connected - your core abstractions)
1. `Vortex.Shared` - 42 edges
2. `AgentProfileDto` - 39 edges
3. `AuthResponse` - 23 edges
4. `MainWindowViewModel` - 19 edges
5. `MainWindowViewModel` - 19 edges
6. `Vortex.Web.Pages` - 18 edges
7. `UserProfileDto` - 17 edges
8. `AgentPolicyDto` - 17 edges
9. `BackendClient` - 15 edges
10. `Vortex.Desktop.Services` - 14 edges

## Surprising Connections (you probably didn't know these)
- `MainWindowViewModel` --references--> `BackendClient`  [EXTRACTED]
  VortexAIIskeletGTHUB/Vortex.Desktop/ViewModels/MainWindowViewModel.cs → Vortex.Desktop/Services/BackendClient.cs
- `FakeDesktopAuthenticationService` --implements--> `IDesktopAuthenticationService`  [EXTRACTED]
  Vortex.Tests/DesktopViewModelAuthTests.cs → Vortex.Desktop/Services/DesktopAuthenticationService.cs
- `MainWindowViewModel` --references--> `IDesktopAuthenticationService`  [EXTRACTED]
  VortexAIIskeletGTHUB/Vortex.Desktop/ViewModels/MainWindowViewModel.cs → Vortex.Desktop/Services/DesktopAuthenticationService.cs
- `FakeDesktopAuthenticationService` --implements--> `IDesktopAuthenticationService`  [EXTRACTED]
  VortexAIIskeletGTHUB/Vortex.Tests/DesktopViewModelAuthTests.cs → Vortex.Desktop/Services/DesktopAuthenticationService.cs
- `DesktopAuthServerFactory` --references--> `Program`  [EXTRACTED]
  Vortex.Tests/DesktopAuthIntegrationTests.cs → Vortex.Server/Program.cs

## Import Cycles
- None detected.

## Communities (91 total, 19 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.06
Nodes (37): CancellationToken, ConcurrentDictionary, Guid, SqliteConnection, Task, AgentIsolationService, AgentPolicyService, AgentUsageService (+29 more)

### Community 1 - "Community 1"
Cohesion: 0.08
Nodes (18): CancellationToken, Dictionary, HttpListenerResponse, HttpStatusCode, Task, DesktopAuthenticationService, HttpStatusCodeExtensions, IDesktopAuthenticationService (+10 more)

### Community 2 - "Community 2"
Cohesion: 0.10
Nodes (23): CancellationToken, Guid, IAsyncEnumerable, IReadOnlyList, Task, TimeSpan, AiProviderClient, ChatService (+15 more)

### Community 3 - "Community 3"
Cohesion: 0.04
Nodes (47): AgentChatRequest, AgentChatResponse, AgentExecutionStatus, AgentPolicyDto, AgentProfileDto, AgentStatusDto, AgentTaskDto, AgentUsageDto (+39 more)

### Community 4 - "Community 4"
Cohesion: 0.14
Nodes (15): CancellationToken, DesktopSession, Dictionary, Guid, Task, DesktopAuthService, DesktopAuthStatusResponse, StartDesktopAuthRequest (+7 more)

### Community 5 - "Community 5"
Cohesion: 0.12
Nodes (15): CancellationToken, IAsyncEnumerable, JsonSerializerOptions, string, Task, BackendClient, ExchangeResult, ChatCompletionChunk (+7 more)

### Community 6 - "Community 6"
Cohesion: 0.05
Nodes (54): DbTransaction, CancellationToken, ConcurrentDictionary, DateTimeOffset, Guid, HttpContext, SqliteConnection, string (+46 more)

### Community 7 - "Community 7"
Cohesion: 0.07
Nodes (36): Vortex.Admin, net8.0, Microsoft.NET.Sdk.Web, Vortex.Desktop, net8.0, Avalonia (11.3.6), Avalonia.Desktop (11.3.6), Avalonia.Fonts.Inter (11.3.6) (+28 more)

### Community 8 - "Community 8"
Cohesion: 0.07
Nodes (33): Vortex.Admin, net8.0, Microsoft.NET.Sdk.Web, Vortex.Desktop, net8.0, Avalonia (11.3.6), Avalonia.Desktop (11.3.6), Avalonia.Fonts.Inter (11.3.6) (+25 more)

### Community 9 - "Community 9"
Cohesion: 0.09
Nodes (25): byte, CancellationToken, ClaimsPrincipal, DateTimeOffset, Guid, SqliteConnection, string, Task (+17 more)

### Community 10 - "Community 10"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 11 - "Community 11"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 12 - "Community 12"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 13 - "Community 13"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 14 - "Community 14"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 15 - "Community 15"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 16 - "Community 16"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 17 - "Community 17"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 18 - "Community 18"
Cohesion: 0.08
Nodes (19): ObservableObject, Action, bool, CancellationToken, CancellationTokenSource, RelayCommand, string, Task (+11 more)

### Community 19 - "Community 19"
Cohesion: 0.20
Nodes (7): CancellationToken, JsonSerializerOptions, Task, HermesRuntime, WorkerConfig, WorkerJson, WorkerReadiness

### Community 20 - "Community 20"
Cohesion: 0.20
Nodes (7): IWebHostBuilder, string, DesktopAuthServerFactory, IWebHostBuilder, string, VortexServerFactory, WebApplicationFactory

### Community 21 - "Community 21"
Cohesion: 0.15
Nodes (13): CompleteDesktopAuthResponse, Fact, Guid, HttpClient, JsonSerializerOptions, Task, DesktopAuthIntegrationTests, Fact (+5 more)

### Community 22 - "Community 22"
Cohesion: 0.40
Nodes (3): IWebHostBuilder, string, VortexServerFactory

### Community 23 - "Community 23"
Cohesion: 0.40
Nodes (3): IWebHostBuilder, string, DesktopAuthServerFactory

### Community 24 - "Community 24"
Cohesion: 0.15
Nodes (13): Vortex.Server.Services, Vortex.Server.Data, CancellationToken, SqliteConnection, string, Task, VortexDb, DesktopSession (+5 more)

### Community 25 - "Community 25"
Cohesion: 0.48
Nodes (5): CancellationToken, SqliteConnection, string, Task, VortexDb

### Community 26 - "Community 26"
Cohesion: 0.18
Nodes (5): Vortex.Web.Pages, ILogger, IndexModel, route:/logout, LogoutModel

### Community 27 - "Community 27"
Cohesion: 0.20
Nodes (5): Vortex.Tests, Fact, UnitTest1, Fact, UnitTest1

### Community 28 - "Community 28"
Cohesion: 0.22
Nodes (4): escapeAttributeValue(), onError(), onReset(), validationInfo()

### Community 29 - "Community 29"
Cohesion: 0.22
Nodes (4): escapeAttributeValue(), onError(), onReset(), validationInfo()

### Community 32 - "Community 32"
Cohesion: 0.20
Nodes (7): HttpClient, HttpRequest, HttpResponse, IHttpClientFactory, JsonSerializerOptions, string, WebAuth

### Community 33 - "Community 33"
Cohesion: 0.20
Nodes (7): HttpClient, HttpRequest, HttpResponse, IHttpClientFactory, JsonSerializerOptions, string, WebAuth

### Community 34 - "Community 34"
Cohesion: 0.22
Nodes (3): Vortex.Shared, Class1, Class1

### Community 35 - "Community 35"
Cohesion: 0.22
Nodes (5): PageModel, route:/desktop/success, DesktopSuccessModel, ILogger, IndexModel

### Community 36 - "Community 36"
Cohesion: 0.36
Nodes (4): Exception, object, string, DesktopLogService

### Community 37 - "Community 37"
Cohesion: 0.25
Nodes (5): CancellationToken, IActionResult, Task, route:/desktop/authorize, DesktopAuthorizeModel

### Community 38 - "Community 38"
Cohesion: 0.22
Nodes (5): CancellationToken, IActionResult, Task, route:/login, LoginModel

### Community 39 - "Community 39"
Cohesion: 0.22
Nodes (5): CancellationToken, IActionResult, Task, route:/register, RegisterModel

### Community 40 - "Community 40"
Cohesion: 0.36
Nodes (4): Exception, object, string, DesktopLogService

### Community 41 - "Community 41"
Cohesion: 0.18
Nodes (12): HttpResponseMessage, AgentChatResponse, Fact, HttpClient, JsonSerializerOptions, Task, HermesIsolationIntegrationTests, Fact (+4 more)

### Community 42 - "Community 42"
Cohesion: 0.25
Nodes (5): CancellationToken, IActionResult, Task, route:/desktop/authorize, DesktopAuthorizeModel

### Community 43 - "Community 43"
Cohesion: 0.22
Nodes (5): CancellationToken, IActionResult, Task, route:/login, LoginModel

### Community 44 - "Community 44"
Cohesion: 0.22
Nodes (5): CancellationToken, IActionResult, Task, route:/register, RegisterModel

### Community 45 - "Community 45"
Cohesion: 0.32
Nodes (4): CancellationToken, string, Task, TokenStorageService

### Community 46 - "Community 46"
Cohesion: 0.29
Nodes (5): CancellationToken, Fact, Task, DesktopViewModelAuthTests, FakeDesktopAuthenticationService

### Community 47 - "Community 47"
Cohesion: 0.32
Nodes (4): CancellationToken, string, Task, TokenStorageService

### Community 48 - "Community 48"
Cohesion: 0.29
Nodes (5): CancellationToken, Fact, Task, DesktopViewModelAuthTests, FakeDesktopAuthenticationService

### Community 49 - "Community 49"
Cohesion: 0.15
Nodes (8): Application, Vortex.Desktop, Vortex.Desktop.ViewModels, App, MainWindow, App, MainWindow, Window

### Community 50 - "Community 50"
Cohesion: 0.29
Nodes (4): Fact, InlineData, Theory, SharedContractTests

### Community 51 - "Community 51"
Cohesion: 0.38
Nodes (3): f(), p(), u()

### Community 52 - "Community 52"
Cohesion: 0.29
Nodes (4): Fact, InlineData, Theory, SharedContractTests

### Community 53 - "Community 53"
Cohesion: 0.38
Nodes (3): f(), p(), u()

### Community 54 - "Community 54"
Cohesion: 0.40
Nodes (3): AppBuilder, STAThread, Program

### Community 55 - "Community 55"
Cohesion: 0.40
Nodes (3): AppBuilder, STAThread, Program

### Community 57 - "Community 57"
Cohesion: 0.33
Nodes (4): Guid, HttpContext, HttpContextAuthExtensions, Program

### Community 64 - "Community 64"
Cohesion: 0.50
Nodes (4): string, SupportedFileTypes, VortexFeatures, VortexRoles

## Knowledge Gaps
- **277 isolated node(s):** `$schema`, `windowsAuthentication`, `anonymousAuthentication`, `applicationUrl`, `sslPort` (+272 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **19 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Vortex.Shared` connect `Community 34` to `Community 0`, `Community 1`, `Community 2`, `Community 3`, `Community 5`, `Community 6`, `Community 9`, `Community 18`, `Community 19`, `Community 20`, `Community 22`, `Community 23`, `Community 24`, `Community 27`, `Community 30`, `Community 32`, `Community 33`, `Community 37`, `Community 38`, `Community 39`, `Community 42`, `Community 43`, `Community 44`, `Community 46`, `Community 48`, `Community 50`, `Community 52`, `Community 57`?**
  _High betweenness centrality (0.338) - this node is a cross-community bridge._
- **Why does `Vortex.Desktop.Services` connect `Community 30` to `Community 1`, `Community 5`, `Community 45`, `Community 46`, `Community 47`, `Community 48`, `Community 49`, `Community 18`?**
  _High betweenness centrality (0.080) - this node is a cross-community bridge._
- **Why does `Vortex.Web.Pages` connect `Community 26` to `Community 32`, `Community 65`, `Community 66`, `Community 35`, `Community 33`, `Community 37`, `Community 38`, `Community 39`, `Community 42`, `Community 43`, `Community 44`, `Community 60`, `Community 61`, `Community 62`, `Community 63`?**
  _High betweenness centrality (0.053) - this node is a cross-community bridge._
- **What connects `$schema`, `windowsAuthentication`, `anonymousAuthentication` to the rest of the system?**
  _277 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.05855855855855856 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.08461538461538462 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.10374149659863946 - nodes in this community are weakly interconnected._