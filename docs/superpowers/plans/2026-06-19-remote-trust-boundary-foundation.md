# Remote Trust Boundary Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add trust grading and remote receipt foundation to the current OWS MVP without building the server yet.

**Architecture:** Keep the current solution shape. Extend `Ows.Core.Verification` so results carry trust status and findings, add minimal receipt/session records in `Ows.Core`, and update docs to reflect that the local client is untrusted and the remote verifier is the future trust boundary.

**Tech Stack:** .NET 9, xUnit, FluentAssertions, System.Text.Json

---

### Task 1: Add trust status to verification results and reports

**Files:**
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Verification\TrustStatus.cs`
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Verification\VerificationFinding.cs`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Verification\VerificationResult.cs`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Verification\OwsPackageVerifier.cs`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Reporting\OwsReportGenerator.cs`
- Test: `C:\Users\Liparakis\Desktop\Open Work Standard\tests\Ows.Core.Tests\VerificationResultTests.cs`
- Test: `C:\Users\Liparakis\Desktop\Open Work Standard\tests\Ows.Core.Tests\VerificationNamespaceTests.cs`
- Test: `C:\Users\Liparakis\Desktop\Open Work Standard\tests\Ows.Core.Tests\ReportingNamespaceTests.cs`

- [ ] **Step 1: Write the failing trust-status tests**

```csharp
[Fact]
public void Success_ShouldCreateSuccessfulResult()
{
    var result = VerificationResult.Success("Verified", TrustStatus.Verified);

    result.IsSuccess.Should().BeTrue();
    result.TrustStatus.Should().Be(TrustStatus.Verified);
    result.Findings.Should().BeEmpty();
}

[Fact]
public async Task VerifyAsync_ShouldReturnUnverifiedForLocalOnlyPackage()
{
    var verifier = new OwsPackageVerifier();
    var result = await verifier.VerifyAsync(
        new PackageVerificationRequest { PackagePath = packagePath },
        CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.TrustStatus.Should().Be(TrustStatus.Unverified);
    result.Findings.Should().Contain(finding => finding.Code == "remote-receipts-missing");
}

[Fact]
public async Task GenerateAsync_ShouldIncludeTrustStatus()
{
    var result = await generator.GenerateAsync(
        new ReportRequest
        {
            Format = ReportFormat.Text,
            VerificationResult = VerificationResult.Success("OWS verify succeeded.", TrustStatus.Unverified)
        },
        CancellationToken.None);

    result.Content.Should().Contain("Trust: Unverified");
}
```

- [ ] **Step 2: Run targeted tests to verify they fail**

Run: `dotnet test tests/Ows.Core.Tests/Ows.Core.Tests.csproj --filter "VerificationResultTests|VerificationNamespaceTests|ReportingNamespaceTests"`

Expected: FAIL with missing `TrustStatus`, missing `Findings`, and report text mismatch.

- [ ] **Step 3: Write the minimal verification trust implementation**

```csharp
public enum TrustStatus
{
    Verified,
    Degraded,
    Unverified,
    Invalid
}

public sealed record VerificationFinding
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record VerificationResult
{
    public bool IsSuccess { get; init; }
    public TrustStatus TrustStatus { get; init; }
    public IReadOnlyList<VerificationFinding> Findings { get; init; } = Array.Empty<VerificationFinding>();

    public static VerificationResult Success(
        string summary,
        TrustStatus trustStatus = TrustStatus.Verified,
        IReadOnlyList<VerificationFinding>? findings = null,
        IReadOnlyList<ReviewSignal>? reviewSignals = null) => new()
        {
            IsSuccess = true,
            Summary = summary,
            TrustStatus = trustStatus,
            Findings = findings ?? Array.Empty<VerificationFinding>(),
            ReviewSignals = reviewSignals ?? Array.Empty<ReviewSignal>()
        };

    public static VerificationResult Failure(
        string summary,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<VerificationFinding>? findings = null,
        IReadOnlyList<ReviewSignal>? reviewSignals = null) => new()
        {
            IsSuccess = false,
            Summary = summary,
            TrustStatus = TrustStatus.Invalid,
            Errors = errors ?? Array.Empty<string>(),
            Findings = findings ?? Array.Empty<VerificationFinding>(),
            ReviewSignals = reviewSignals ?? Array.Empty<ReviewSignal>()
        };
}
```

```csharp
return errors.Count == 0
    ? VerificationResult.Success(
        "OWS verify succeeded.",
        TrustStatus.Unverified,
        [new VerificationFinding
        {
            Code = "remote-receipts-missing",
            Title = "Remote receipts missing",
            Detail = "The package is locally consistent, but no remote verifier receipts were provided."
        }])
    : VerificationResult.Failure("OWS verify failed.", errors);
```

```csharp
var content = $"Status: {status}{Environment.NewLine}Trust: {request.VerificationResult.TrustStatus}{Environment.NewLine}Summary: {request.VerificationResult.Summary}{Environment.NewLine}Errors: {errors}";
```

- [ ] **Step 4: Run targeted tests to verify they pass**

Run: `dotnet test tests/Ows.Core.Tests/Ows.Core.Tests.csproj --filter "VerificationResultTests|VerificationNamespaceTests|ReportingNamespaceTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Ows.Core/Verification src/Ows.Core/Reporting tests/Ows.Core.Tests
git commit -m "feat: add trust grading to verification results"
```

### Task 2: Add remote receipt and session domain models

**Files:**
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Notarization\AssessmentSessionId.cs`
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Notarization\ServerTimestamp.cs`
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Notarization\Checkpoint.cs`
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Notarization\CheckpointReceipt.cs`
- Create: `C:\Users\Liparakis\Desktop\Open Work Standard\src\Ows.Core\Notarization\ReceiptChain.cs`
- Test: `C:\Users\Liparakis\Desktop\Open Work Standard\tests\Ows.Core.Tests\NotarizationNamespaceTests.cs`

- [ ] **Step 1: Write the failing notarization model test**

```csharp
[Fact]
public void ReceiptChain_ShouldPreserveOrderedReceipts()
{
    var sessionId = AssessmentSessionId.Create();
    var checkpoint = new Checkpoint
    {
        SessionId = sessionId,
        SequenceNumber = 1,
        TimelineHeadHash = "abc123"
    };

    var receipt = new CheckpointReceipt
    {
        SessionId = sessionId,
        SequenceNumber = 1,
        TimelineHeadHash = "abc123",
        ReceiptHash = "def456"
    };

    var chain = new ReceiptChain
    {
        SessionId = sessionId,
        Receipts = [receipt]
    };

    chain.SessionId.Should().Be(sessionId);
    chain.Receipts.Should().ContainSingle();
    checkpoint.SequenceNumber.Should().Be(1);
}
```

- [ ] **Step 2: Run targeted test to verify it fails**

Run: `dotnet test tests/Ows.Core.Tests/Ows.Core.Tests.csproj --filter "NotarizationNamespaceTests"`

Expected: FAIL with missing notarization types.

- [ ] **Step 3: Add minimal records for protocol foundation**

```csharp
public readonly record struct AssessmentSessionId(string Value)
{
    public static AssessmentSessionId Create() => new(Guid.NewGuid().ToString("N"));
    public override string ToString() => Value;
}
```

```csharp
public sealed record ServerTimestamp
{
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

```csharp
public sealed record Checkpoint
{
    public AssessmentSessionId SessionId { get; init; }
    public int SequenceNumber { get; init; }
    public string TimelineHeadHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

```csharp
public sealed record CheckpointReceipt
{
    public AssessmentSessionId SessionId { get; init; }
    public int SequenceNumber { get; init; }
    public string TimelineHeadHash { get; init; } = string.Empty;
    public string ReceiptHash { get; init; } = string.Empty;
    public ServerTimestamp ServerTimestamp { get; init; } = new();
}
```

```csharp
public sealed record ReceiptChain
{
    public AssessmentSessionId SessionId { get; init; }
    public IReadOnlyList<CheckpointReceipt> Receipts { get; init; } = Array.Empty<CheckpointReceipt>();
}
```

- [ ] **Step 4: Run targeted test to verify it passes**

Run: `dotnet test tests/Ows.Core.Tests/Ows.Core.Tests.csproj --filter "NotarizationNamespaceTests"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Ows.Core/Notarization tests/Ows.Core.Tests/NotarizationNamespaceTests.cs
git commit -m "feat: add remote receipt domain models"
```

### Task 3: Update stale docs to the remote trust boundary framing

**Files:**
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\docs\CLI.md`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\docs\ARCHITECTURE.md`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\README.md`
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\docs\PROJECT_STATUS.md`

- [ ] **Step 1: Write the doc assertions as a failing review checklist**

```text
- CLI doc must no longer say every command is a placeholder.
- Architecture doc must state that the local client is not the final trust authority.
- README must describe OWS as assessment provenance / notarization infrastructure.
- Project status must mention remote trust boundary foundation as the next milestone.
```

- [ ] **Step 2: Review current docs and confirm they fail the checklist**

Run: `Get-Content README.md`

Expected: README still frames OWS as purely local-first provenance and bootstrap-stage.

- [ ] **Step 3: Write the minimal doc updates**

```markdown
OWS is an open, self-hostable assessment notarization protocol with an optional managed verifier service.
```

```markdown
The client observes. The server notarizes. The final package proves. The professor decides.
```

```markdown
`ows watch` currently performs a one-shot local scan. `ows package`, `ows verify`, and `ows report` are implemented MVP commands.
```

- [ ] **Step 4: Run build and tests to ensure docs-only changes did not drift code**

Run:

```powershell
dotnet build
dotnet test
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add README.md docs/development/CLI.md docs/core/ARCHITECTURE.md docs/development/PROJECT_STATUS.md
git commit -m "docs: reframe OWS around remote trust boundaries"
```

### Task 4: Final integration check and milestone commit

**Files:**
- Modify: `C:\Users\Liparakis\Desktop\Open Work Standard\OWS.sln` if needed only when new files are not included automatically by SDK globs
- Review: `C:\Users\Liparakis\Desktop\Open Work Standard\git status`

- [ ] **Step 1: Run the full solution verification**

Run:

```powershell
dotnet build
dotnet test
```

Expected: PASS across `Ows.Core.Tests` and `Ows.Cli.Tests`.

- [ ] **Step 2: Review the diff for scope control**

Run: `git diff --stat`

Expected: verification/reporting/domain/doc changes only, no watcher/server sprawl.

- [ ] **Step 3: Create the milestone commit**

```bash
git add README.md docs src tests
git commit -m "feat: add trust grading and remote receipt foundation"
```

- [ ] **Step 4: Record outcome**

```text
- Trust grading added.
- Remote receipt models added.
- Docs updated to remote trust boundary framing.
- Build and tests passing.
```

