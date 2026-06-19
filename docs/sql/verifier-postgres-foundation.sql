-- Durable verifier storage foundation
-- Intended for PostgreSQL. Not wired into the ASP.NET Core server yet.

create table if not exists verifier_sessions (
    id text primary key,
    created_at timestamptz not null default now(),
    client_id text null,
    assessment_id text null,
    metadata_json jsonb not null default '{}'::jsonb,
    head_receipt_hash text not null default '',
    head_event_hash text not null default '',
    checkpoint_count integer not null default 0
);

create table if not exists verifier_checkpoints (
    id bigserial primary key,
    session_id text not null references verifier_sessions(id) on delete cascade,
    sequence_number integer not null,
    client_time timestamptz null,
    server_time timestamptz not null,
    previous_event_hash text not null,
    current_event_hash text not null,
    project_state_hash text not null,
    previous_receipt_hash text not null,
    receipt_hash text not null,
    server_signature text not null default '',
    idempotency_key text null,
    created_at timestamptz not null default now(),
    constraint uq_verifier_checkpoints_session_sequence unique (session_id, sequence_number),
    constraint uq_verifier_checkpoints_session_receipt unique (session_id, receipt_hash)
);

create unique index if not exists ix_verifier_checkpoints_session_idempotency
    on verifier_checkpoints (session_id, idempotency_key)
    where idempotency_key is not null;

create table if not exists verifier_audit_events (
    id bigserial primary key,
    session_id text null references verifier_sessions(id) on delete cascade,
    event_type text not null,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);
