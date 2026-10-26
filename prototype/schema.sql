-- pulse prototype schema. Applied on startup; safe to run more than once.

-- One row per event, in the shape defined by docs/event-format.md (v1).
CREATE TABLE IF NOT EXISTS events (
    event_id     uuid        PRIMARY KEY,          -- client-generated, so retries don't duplicate
    version      smallint    NOT NULL,
    event_name   text        NOT NULL,
    distinct_id  text        NOT NULL,
    occurred_at  timestamptz NOT NULL,              -- client clock
    received_at  timestamptz NOT NULL DEFAULT now(),-- server clock, never from the client
    properties   jsonb       NOT NULL DEFAULT '{}'::jsonb
);

-- The two questions asked most: "how many X between dates" and "what did this user do".
CREATE INDEX IF NOT EXISTS events_name_time_idx ON events (event_name, occurred_at);
CREATE INDEX IF NOT EXISTS events_user_time_idx ON events (distinct_id, occurred_at);

-- Product catalogue for the test shop. Nullable columns are on purpose:
-- real catalogues have gaps, and finding them is a later MCP tool (catalog_gaps).
CREATE TABLE IF NOT EXISTS products (
    product_id     text          PRIMARY KEY,       -- url-friendly slug
    name           text          NOT NULL,
    designer       text          NOT NULL,
    category       text          NOT NULL,
    price_dkk      numeric(10,2) NOT NULL CHECK (price_dkk > 0),
    materials      text[]        NOT NULL DEFAULT '{}',
    width_cm       integer,
    depth_cm       integer,
    height_cm      integer,
    description_da text,
    description_de text                             -- German shop text; often missing
);
