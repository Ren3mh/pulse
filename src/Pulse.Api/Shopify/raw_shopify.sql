-- The webshop as Shopify reports it, plus synced_at: when Pulse read the row.
-- Ids are Shopify's GraphQL ids (gid://shopify/...). Safe to rerun.
CREATE SCHEMA IF NOT EXISTS raw_shopify;

-- Products and variants as they are now: replaced on every sync.
CREATE TABLE IF NOT EXISTS raw_shopify.products (
    id                 text        PRIMARY KEY,
    handle             text        NOT NULL,
    title              text        NOT NULL,
    vendor             text        NOT NULL,
    product_type       text        NOT NULL,
    status             text        NOT NULL, -- ACTIVE, DRAFT or ARCHIVED
    updated_at         timestamptz NOT NULL,
    synced_at          timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_shopify.variants (
    id                 text        PRIMARY KEY,
    product_id         text        NOT NULL,
    sku                text,
    barcode            text,       -- GTIN
    title              text        NOT NULL,
    price              numeric     NOT NULL,
    inventory_item_id  text        NOT NULL,
    updated_at         timestamptz NOT NULL,
    synced_at          timestamptz NOT NULL
);

-- Shopify only shows current stock, so every sync appends a snapshot.
CREATE TABLE IF NOT EXISTS raw_shopify.stock_snapshots (
    inventory_item_id  text        NOT NULL,
    sku                text,
    location_id        text        NOT NULL,
    location_name      text        NOT NULL,
    available          integer     NOT NULL,
    on_hand            integer     NOT NULL,
    committed          integer     NOT NULL,
    synced_at          timestamptz NOT NULL,
    PRIMARY KEY (synced_at, inventory_item_id, location_id)
);

-- Orders are kept, not replaced: without the read_all_orders scope Shopify
-- only returns the last 60 days, so older orders would otherwise disappear.
CREATE TABLE IF NOT EXISTS raw_shopify.orders (
    id                 text        PRIMARY KEY,
    name               text        NOT NULL, -- e.g. #1001
    customer_id        text,
    currency_code      text        NOT NULL,
    total_price        numeric     NOT NULL,
    financial_status   text,
    fulfillment_status text,
    created_at         timestamptz NOT NULL,
    updated_at         timestamptz NOT NULL,
    cancelled_at       timestamptz,
    synced_at          timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_shopify.order_lines (
    id                 text        PRIMARY KEY,
    order_id           text        NOT NULL,
    sku                text,
    variant_id         text,
    quantity           integer     NOT NULL,
    unit_price         numeric     NOT NULL,
    synced_at          timestamptz NOT NULL
);
