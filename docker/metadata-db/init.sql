-- Metadata bazası (artıq var)
SELECT 'metadata db ready' AS status;

-- Staging bazası (miqrasiya validasiyası üçün)
CREATE DATABASE migration_staging;
-- Miqrasiya sorğuları
CREATE TABLE IF NOT EXISTS migration_requests (
    id              serial PRIMARY KEY,
    pr_number       int NOT NULL,
    pr_title        text NOT NULL,
    commit_sha      text NOT NULL,
    author          text NOT NULL,
    branch          text NOT NULL,
    migration_files text[] NOT NULL,
    status          text NOT NULL DEFAULT 'pending',
    -- pending | validating | validation_failed | approved | applied | rejected
    validation_log  text,
    applied_log     text,
    requested_at    timestamptz NOT NULL DEFAULT now(),
    approved_by     text,
    approved_at     timestamptz,
    applied_at      timestamptz,
    rejected_by     text,
    rejected_at     timestamptz
);

CREATE INDEX IF NOT EXISTS idx_migration_requests_status ON migration_requests(status);
-- Miqrasiya skriptləri
CREATE TABLE IF NOT EXISTS migration_scripts (
    id              serial PRIMARY KEY,
    version         text NOT NULL UNIQUE,          -- "V1", "V2", "V3"...
    description     text NOT NULL,                  -- "create_users_table"
    sql_content     text NOT NULL,
    checksum        text NOT NULL,                  -- SHA256 (dəyişməzlik yoxlaması)
    status          text NOT NULL DEFAULT 'draft',
    -- draft | pending | validating | validation_failed | approved | applied | rejected
    created_by      text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    submitted_at    timestamptz,
    reviewed_by     text,
    reviewed_at     timestamptz,
    applied_by      text,
    applied_at      timestamptz,
    validation_log  text,
    apply_log       text,
    rollback_sql    text,                           -- opsional: geri qaytarma
    notes           text
);

CREATE INDEX IF NOT EXISTS idx_migration_scripts_status
    ON migration_scripts(status);

CREATE INDEX IF NOT EXISTS idx_migration_scripts_version
    ON migration_scripts(version);

-- Audit log
CREATE TABLE IF NOT EXISTS migration_audit_log (
    id              bigserial PRIMARY KEY,
    migration_id    int REFERENCES migration_scripts(id) ON DELETE SET NULL,
    version         text,
    action          text NOT NULL,
    -- created | submitted | validated | approved | applied | rejected | failed
    performed_by    text NOT NULL,
    performed_at    timestamptz NOT NULL DEFAULT now(),
    details         jsonb
);

CREATE INDEX IF NOT EXISTS idx_audit_migration_id
    ON migration_audit_log(migration_id);

CREATE INDEX IF NOT EXISTS idx_audit_performed_at
    ON migration_audit_log(performed_at DESC);

-- Flyway schema history (staging üçün)
-- Flyway özü yaradacaq, burada yalnız qeyd