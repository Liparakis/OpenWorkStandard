param(
    [string]$BaseUrl = "http://127.0.0.1:5078"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    $session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions"
}
catch {
    throw "Smoke test could not create a session at $BaseUrl. Run start-local-verifier first, then check status-local-verifier and logs-local-verifier."
}

try {
    $headers = @{ "Idempotency-Key" = "checkpoint-1" }
    $checkpointBody = @{
        sessionId = $session.sessionId
        sequenceNumber = 1
        timelineHeadHash = "head-1"
    } | ConvertTo-Json

    $receipt = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.sessionId)/checkpoints" -Headers $headers -ContentType "application/json" -Body $checkpointBody
    $retriedReceipt = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sessions/$($session.sessionId)/checkpoints" -Headers $headers -ContentType "application/json" -Body $checkpointBody
    $receipts = Invoke-RestMethod -Method Get -Uri "$BaseUrl/sessions/$($session.sessionId)/receipts"
    $head = Invoke-RestMethod -Method Get -Uri "$BaseUrl/sessions/$($session.sessionId)/head"
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

[pscustomobject]@{
    SessionId = $session.sessionId
    ReceiptHash = $receipt.receiptHash
    ReceiptCount = $receipts.receipts.Count
    HeadSequence = $head.lastSequenceNumber
    HeadTimelineHeadHash = $head.lastTimelineHeadHash
    IdempotentRetryMatched = $true
} | Format-List
