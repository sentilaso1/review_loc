CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS resolved_ticket_snapshot (
    id uuid PRIMARY KEY,
    source text NOT NULL,
    external_ticket_id text NOT NULL,
    workspace_id text NOT NULL,
    category text NOT NULL,
    status text NOT NULL,
    decision text NOT NULL,
    cluster_id uuid NULL,
    payload jsonb NOT NULL,
    UNIQUE (source, external_ticket_id)
);
CREATE INDEX IF NOT EXISTS ix_ticket_workspace ON resolved_ticket_snapshot(workspace_id);
CREATE INDEX IF NOT EXISTS ix_ticket_category ON resolved_ticket_snapshot(category);
CREATE INDEX IF NOT EXISTS ix_ticket_status_decision ON resolved_ticket_snapshot(status, decision);
CREATE INDEX IF NOT EXISTS ix_ticket_cluster ON resolved_ticket_snapshot(cluster_id);
DO $$
BEGIN
    ALTER TABLE resolved_ticket_snapshot ADD COLUMN IF NOT EXISTS embedding vector(384) NULL;
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'resolved_ticket_snapshot'
          AND column_name = 'embedding'
          AND udt_name <> 'vector'
    ) THEN
        ALTER TABLE resolved_ticket_snapshot
            ALTER COLUMN embedding TYPE vector(384)
            USING CASE
                WHEN embedding IS NULL THEN NULL
                ELSE ('[' || array_to_string(embedding, ',') || ']')::vector(384)
            END;
    END IF;
    EXECUTE 'CREATE INDEX IF NOT EXISTS ix_ticket_embedding_hnsw ON resolved_ticket_snapshot USING hnsw (embedding vector_cosine_ops)';
END $$;

CREATE TABLE IF NOT EXISTS evidence (
    id uuid PRIMARY KEY,
    ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    kind text NOT NULL,
    content text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_evidence_ticket ON evidence(ticket_id);

CREATE TABLE IF NOT EXISTS knowledge_candidate (
    id uuid PRIMARY KEY,
    ticket_id uuid NOT NULL UNIQUE REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    workspace_id text NOT NULL,
    category text NOT NULL,
    decision text NOT NULL,
    quality_score integer NOT NULL,
    risk text NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_candidate_workspace_category ON knowledge_candidate(workspace_id, category);
CREATE INDEX IF NOT EXISTS ix_candidate_decision ON knowledge_candidate(decision);

CREATE TABLE IF NOT EXISTS "cluster" (
    id uuid PRIMARY KEY,
    workspace_id text NOT NULL,
    category text NOT NULL,
    subcategory text NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_cluster_taxonomy ON "cluster"(workspace_id, category, subcategory);
CREATE INDEX IF NOT EXISTS ix_cluster_status ON "cluster"(status);

CREATE TABLE IF NOT EXISTS cluster_member (
    cluster_id uuid NOT NULL REFERENCES "cluster"(id) ON DELETE CASCADE,
    ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    PRIMARY KEY (cluster_id, ticket_id)
);
CREATE INDEX IF NOT EXISTS ix_cluster_member_ticket ON cluster_member(ticket_id);

CREATE TABLE IF NOT EXISTS solution (
    id uuid PRIMARY KEY,
    cluster_id uuid NOT NULL,
    workspace_id text NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_solution_workspace_status ON solution(workspace_id, status);
CREATE INDEX IF NOT EXISTS ix_solution_cluster ON solution(cluster_id);

CREATE TABLE IF NOT EXISTS solution_source (
    solution_id uuid NOT NULL REFERENCES solution(id) ON DELETE CASCADE,
    ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    PRIMARY KEY (solution_id, ticket_id)
);
CREATE TABLE IF NOT EXISTS solution_approval (
    id uuid PRIMARY KEY,
    solution_id uuid NOT NULL REFERENCES solution(id) ON DELETE CASCADE,
    decision text NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_solution_approval_decision ON solution_approval(decision);

CREATE TABLE IF NOT EXISTS reuse_record (
    id uuid PRIMARY KEY,
    solution_id uuid NOT NULL REFERENCES solution(id) ON DELETE CASCADE,
    ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    confidence double precision NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS similarity_link (
    id uuid PRIMARY KEY,
    source_ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    target_ticket_id uuid NOT NULL REFERENCES resolved_ticket_snapshot(id) ON DELETE CASCADE,
    score double precision NOT NULL,
    payload jsonb NOT NULL
);

CREATE TABLE IF NOT EXISTS ai_run (
    id uuid PRIMARY KEY,
    cluster_id uuid NOT NULL,
    model text NOT NULL,
    prompt_hash text NOT NULL,
    latency_ms integer NOT NULL,
    cost numeric(18,6) NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ai_run_cluster ON ai_run(cluster_id);

CREATE TABLE IF NOT EXISTS audit_event (
    id bigserial PRIMARY KEY,
    action text NOT NULL,
    resource text NOT NULL,
    at timestamptz NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_action_at ON audit_event(action, at DESC);

CREATE TABLE IF NOT EXISTS pipeline_run (
    id uuid PRIMARY KEY,
    status text NOT NULL,
    queue_name text NOT NULL,
    payload jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pipeline_run_status ON pipeline_run(status);

CREATE TABLE IF NOT EXISTS tsolve_state_metadata (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    daily_sync_number integer NOT NULL DEFAULT 0,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb
);
INSERT INTO tsolve_state_metadata(singleton, daily_sync_number) VALUES (true, 0) ON CONFLICT (singleton) DO NOTHING;
