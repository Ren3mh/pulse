-- How Pulse itself is doing, as opposed to the data it copies. Safe to rerun.
CREATE SCHEMA IF NOT EXISTS ops;

-- One row per sync run, written by SyncWorker whether the run worked or not.
-- The dashboard reads sync health from here. Rows are kept (~96 per source a day).
CREATE TABLE IF NOT EXISTS ops.sync_runs (
    id           bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source       text        NOT NULL,            -- 'ax' or 'shopify'
    started_at   timestamptz NOT NULL,
    finished_at  timestamptz NOT NULL,
    ok           boolean     NOT NULL,
    error        text,                            -- exception type and message; the stack trace is in the logs
    rows_written jsonb       NOT NULL DEFAULT '{}' -- target table -> rows written, e.g. {"raw_ax.inventtable": 30}
);

CREATE INDEX IF NOT EXISTS sync_runs_source_started ON ops.sync_runs (source, started_at DESC);
