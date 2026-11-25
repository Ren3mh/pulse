-- The model Pulse answers from: AX and Shopify combined per SKU. Views only,
-- recreated at startup (after raw_ax.sql and raw_shopify.sql). Safe to rerun.
-- Assumes raw_ax holds one AX company.
CREATE SCHEMA IF NOT EXISTS core;
DROP VIEW IF EXISTS core.stock, core.stock_snapshots, core.order_lines, core.orders, core.products CASCADE;

-- One row per SKU. AX items and Shopify variants are matched on SKU, or on
-- GTIN when one side has no matching SKU. An item found in only one system
-- still gets a row, with the other side's columns empty.
CREATE VIEW core.products AS
WITH ax AS (
    SELECT i.itemid, i.itemname, i.itemgroupid,
           (SELECT b.itembarcode FROM raw_ax.inventitembarcode b
            WHERE b.dataareaid = i.dataareaid AND b.itemid = i.itemid
            ORDER BY b.recid LIMIT 1) AS gtin,
           -- List price: RELATION 4 (sales price), ITEMCODE 0 (one item),
           -- ACCOUNTCODE 2 (all customers). 1900-01-01 means no date limit.
           (SELECT p.amount FROM raw_ax.pricedisctable p
            WHERE p.dataareaid = i.dataareaid AND p.itemrelation = i.itemid
              AND p.relation = 4 AND p.itemcode = 0 AND p.accountcode = 2
              AND (p.fromdate <= now() OR p.fromdate = '1900-01-01')
              AND (p.todate >= now() OR p.todate = '1900-01-01')
            ORDER BY p.fromdate DESC LIMIT 1) AS list_price
    FROM raw_ax.inventtable i
),
shop AS (
    SELECT v.id AS variant_id, v.sku, v.barcode, v.price, v.title AS variant_title, v.inventory_item_id,
           p.id AS product_id, p.title, p.vendor, p.product_type, p.status
    FROM raw_shopify.variants v
    JOIN raw_shopify.products p ON p.id = v.product_id
),
pairs AS (
    SELECT ax.itemid, s.variant_id, 'sku' AS matched_on
    FROM ax JOIN shop s ON s.sku = ax.itemid
    UNION ALL
    SELECT ax.itemid, s.variant_id, 'gtin'
    FROM ax JOIN shop s ON s.barcode = ax.gtin
    WHERE NOT EXISTS (SELECT 1 FROM shop s2 WHERE s2.sku = ax.itemid)
      AND NOT EXISTS (SELECT 1 FROM ax a2 WHERE a2.itemid = s.sku)
),
joined AS (
    SELECT ax.*, s.*, pairs.matched_on
    FROM ax
    LEFT JOIN pairs ON pairs.itemid = ax.itemid
    LEFT JOIN shop s ON s.variant_id = pairs.variant_id
    UNION ALL
    SELECT NULL, NULL, NULL, NULL, NULL, s.*, NULL
    FROM shop s
    WHERE NOT EXISTS (SELECT 1 FROM pairs WHERE pairs.variant_id = s.variant_id)
)
SELECT
    COALESCE(itemid, sku)                             AS sku,
    COALESCE(gtin, barcode)                           AS gtin,
    COALESCE(itemname, title || ', ' || variant_title) AS name,
    COALESCE(NULLIF(product_type, ''), itemgroupid)   AS category,
    vendor                                            AS brand,
    itemid IS NOT NULL                                AS in_ax,
    variant_id IS NOT NULL                            AS in_shopify,
    matched_on,                                       -- 'sku', 'gtin', or null when in one system only
    itemid                                            AS ax_item_id,
    list_price                                        AS ax_list_price,
    variant_id                                        AS shopify_variant_id,
    product_id                                        AS shopify_product_id,
    inventory_item_id                                 AS shopify_inventory_item_id,
    status                                            AS shopify_status, -- ACTIVE, DRAFT or ARCHIVED
    price                                             AS shopify_price
FROM joined;

-- Stock history: one row per sync, source, SKU and location. Both sources
-- are kept side by side. AX's available is physical minus what open orders
-- have reserved; Shopify's is what the webshop offers.
CREATE VIEW core.stock_snapshots AS
SELECT 'ax'::text                                  AS source,
       s.synced_at,
       s.itemid                                    AS sku,
       COALESCE(NULLIF(d.inventlocationid, ''), s.inventdimid) AS location,
       s.physicalinvent                            AS on_hand,
       s.reservphysical                            AS reserved,
       s.availphysical                             AS available
FROM raw_ax.stock_snapshots s
LEFT JOIN raw_ax.inventdim d ON d.dataareaid = s.dataareaid AND d.inventdimid = s.inventdimid
UNION ALL
SELECT 'shopify',
       s.synced_at,
       COALESCE(p.sku, s.sku),   -- the matched SKU, so a GTIN match counts as the AX item
       s.location_name,
       s.on_hand,
       s.committed,
       s.available
FROM raw_shopify.stock_snapshots s
LEFT JOIN core.products p ON p.shopify_inventory_item_id = s.inventory_item_id;

-- Current stock: each source's latest snapshot.
CREATE VIEW core.stock AS
SELECT s.*
FROM core.stock_snapshots s
WHERE s.synced_at = (SELECT max(m.synced_at) FROM core.stock_snapshots m WHERE m.source = s.source);

-- Orders from both systems. AX sales orders (SALESTYPE 3) with the customer
-- from CUSTTABLE; Shopify orders carry only the customer id. Status is one of
-- open, delivered, invoiced or cancelled. AX's total is the sum of its lines;
-- Shopify's includes shipping and tax.
CREATE VIEW core.orders AS
SELECT 'ax'::text           AS source,
       t.salesid            AS order_id,
       t.salesid            AS order_number,
       t.custaccount        AS customer_id,
       c.name               AS customer_name,
       c.custgroup          AS customer_group,
       t.createddatetime    AS created_at,
       CASE t.salesstatus WHEN 1 THEN 'open' WHEN 2 THEN 'delivered' WHEN 3 THEN 'invoiced' WHEN 4 THEN 'cancelled' END AS status,
       t.currencycode       AS currency,
       (SELECT COALESCE(sum(l.lineamount), 0) FROM raw_ax.salesline l
        WHERE l.dataareaid = t.dataareaid AND l.salesid = t.salesid) AS total
FROM raw_ax.salestable t
LEFT JOIN raw_ax.custtable c ON c.dataareaid = t.dataareaid AND c.accountnum = t.custaccount
WHERE t.salestype = 3
UNION ALL
SELECT 'shopify',
       o.id,
       o.name,
       o.customer_id,
       NULL,
       NULL,
       o.created_at,
       CASE WHEN o.cancelled_at IS NOT NULL THEN 'cancelled'
            WHEN o.fulfillment_status = 'FULFILLED' THEN 'delivered'
            ELSE 'open' END,
       o.currency_code,
       o.total_price
FROM raw_shopify.orders o;

-- One row per order line, with the matched SKU.
CREATE VIEW core.order_lines AS
SELECT 'ax'::text      AS source,
       l.salesid       AS order_id,
       l.recid::text   AS line_id,
       l.itemid        AS sku,
       l.salesqty      AS quantity,
       l.salesprice    AS unit_price,
       l.lineamount    AS amount
FROM raw_ax.salesline l
JOIN raw_ax.salestable t ON t.dataareaid = l.dataareaid AND t.salesid = l.salesid
WHERE t.salestype = 3
UNION ALL
SELECT 'shopify',
       l.order_id,
       l.id,
       COALESCE(p.sku, l.sku),
       l.quantity,
       l.unit_price,
       l.quantity * l.unit_price
FROM raw_shopify.order_lines l
LEFT JOIN core.products p ON p.shopify_variant_id = l.variant_id;
