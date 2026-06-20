param(
    [string]$BaseUrl = "http://127.0.0.1:5078"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$authHeaders = @{}
if (-not [string]::IsNullOrWhiteSpace($env:OWS_VERIFIER_API_KEY)) {
    $authHeaders["X-OWS-Verifier-Key"] = $env:OWS_VERIFIER_API_KEY
}

try {
    $session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions" -Headers $authHeaders -ContentType "application/json" -Body "{}"
}
catch {
    throw "Smoke test could not create a session at $BaseUrl. Run start-local-verifier first, then check status-local-verifier and logs-local-verifier."
}

try {
    $headers = $authHeaders.Clone()
    $headers["Idempotency-Key"] = "checkpoint-1"
    $checkpointBody = @{
        sessionId = $session.sessionId
        sequenceNumber = 1
        timelineHeadHash = "head-1"
    } | ConvertTo-Json

    $receipt = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.sessionId)/checkpoints" -Headers $headers -ContentType "application/json" -Body $checkpointBody
    $retriedReceipt = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.sessionId)/checkpoints" -Headers $headers -ContentType "application/json" -Body $checkpointBody
    $receipts = Invoke-RestMethod -Method Get -Uri "$BaseUrl/sessions/$($session.sessionId)/receipts" -Headers $authHeaders
    $head = Invoke-RestMethod -Method Get -Uri "$BaseUrl/sessions/$($session.sessionId)/head" -Headers $authHeaders

    $packageHeaders = $authHeaders.Clone()
    $packageHeaders["Idempotency-Key"] = "package-$($session.sessionId)"
    $packageObjectKey = "smoke/$($session.sessionId).owspkg"
    $packageHash = "a" * 64
    $packageBody = @{
        sessionId = $session.sessionId
        objectStorageProvider = "s3"
        objectBucket = "ows-packages"
        objectKey = $packageObjectKey
        packageSha256 = $packageHash
        packageSizeBytes = 1024
    } | ConvertTo-Json
    $packageSubmission = Invoke-RestMethod -Method Post -Uri "$BaseUrl/packages" -Headers $packageHeaders -ContentType "application/json" -Body $packageBody
    $retriedPackageSubmission = Invoke-RestMethod -Method Post -Uri "$BaseUrl/packages" -Headers $packageHeaders -ContentType "application/json" -Body $packageBody
    $fetchedPackageSubmission = Invoke-RestMethod -Method Get -Uri "$BaseUrl/packages/$($packageSubmission.submissionId)" -Headers $authHeaders
    $sessionPackages = Invoke-RestMethod -Method Get -Uri "$BaseUrl/sessions/$($session.sessionId)/packages" -Headers $authHeaders
}
catch {
    throw "Smoke test failed while exercising verifier endpoints at $BaseUrl. Check status-local-verifier, logs-local-verifier, and confirm migrations succeeded."
}

if ($receipt.receiptHash -ne $retriedReceipt.receiptHash) {
    throw "Idempotent retry did not return the same receipt hash."
}

if ($receipts.receipts.Count -ne 1) {
    throw "Expected exactly one persisted receipt after retry, got $($receipts.receipts.Count)."
}

if ($head.lastSequenceNumber -ne 1) {
    throw "Expected head sequence number 1, got $($head.lastSequenceNumber)."
}

if ($head.lastTimelineHeadHash -ne "head-1") {
    throw "Expected head timeline hash 'head-1', got '$($head.lastTimelineHeadHash)'."
}

if ($packageSubmission.submissionId -ne $retriedPackageSubmission.submissionId) {
    throw "Package idempotent retry did not return the same submission id."
}

if ($fetchedPackageSubmission.objectKey -ne $packageObjectKey) {
    throw "Fetched package metadata did not preserve the object key."
}

if ($fetchedPackageSubmission.sessionHeadReceiptHash -ne $receipt.receiptHash) {
    throw "Package metadata did not anchor the current session receipt head."
}

if ($sessionPackages.Count -lt 1 -or $sessionPackages[0].submissionId -ne $packageSubmission.submissionId) {
    throw "Session package lookup did not return the expected package metadata."
}

[pscustomobject]@{
    SessionId = $session.sessionId
    ReceiptHash = $receipt.receiptHash
    ReceiptCount = $receipts.receipts.Count
    HeadSequence = $head.lastSequenceNumber
    HeadTimelineHeadHash = $head.lastTimelineHeadHash
    IdempotentRetryMatched = $true
    PackageSubmissionId = $packageSubmission.submissionId
    PackageMetadataFetched = $true
    SessionPackageLookup = $true
} | Format-List
