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

-- Backup əməliyyatları (full/diff/incr)
CREATE TABLE IF NOT EXISTS backup_jobs (
    id              serial PRIMARY KEY,
    backup_type     text NOT NULL,          -- full | diff | incr
    status          text NOT NULL DEFAULT 'running',
    -- running | completed | failed
    started_by      text NOT NULL,
    started_at      timestamptz NOT NULL DEFAULT now(),
    finished_at     timestamptz,
    label           text,                   -- pgBackRest label
    log             text,
    error           text
);

CREATE INDEX IF NOT EXISTS idx_backup_jobs_status ON backup_jobs(status);

-- PITR restore əməliyyatları
CREATE TABLE IF NOT EXISTS restore_jobs (
    id              serial PRIMARY KEY,
    backup_label    text,                   -- hansı backup-dan
    target_time     timestamptz NOT NULL,   -- hansı ana qaytarılır
    target_action   text NOT NULL DEFAULT 'promote',  -- promote | pause | shutdown
    status          text NOT NULL DEFAULT 'pending',
    -- pending | running | completed | failed | cancelled
    requested_by    text NOT NULL,
    requested_at    timestamptz NOT NULL DEFAULT now(),
    confirmed_at    timestamptz,            -- iki addımlı təsdiq anı
    started_at      timestamptz,
    finished_at     timestamptz,
    log             text,
    error           text,
    snapshot_path   text                    -- bərpadan əvvəl snapshot (əgər varsa)
);

CREATE INDEX IF NOT EXISTS idx_restore_jobs_status ON restore_jobs(status);

-- Maintenance mode (klasterin hazırkı vəziyyəti)
CREATE TABLE IF NOT EXISTS cluster_state (
    id              int PRIMARY KEY DEFAULT 1,
    maintenance     boolean NOT NULL DEFAULT false,
    maintenance_reason text,
    updated_by      text,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT single_row CHECK (id = 1)
);
INSERT INTO cluster_state (id) VALUES (1) ON CONFLICT DO NOTHING;