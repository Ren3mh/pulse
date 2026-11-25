-- Copies of the AX tables Pulse reads (see ax/schema.sql), with the same
-- names in lower case, plus synced_at: when Pulse read the row. Replaced for
-- the company on every sync. Safe to rerun.
CREATE SCHEMA IF NOT EXISTS raw_ax;

CREATE TABLE IF NOT EXISTS raw_ax.inventtable (
    itemid           text        NOT NULL,
    itemname         text        NOT NULL,
    itemgroupid      text        NOT NULL,
    dataareaid       text        NOT NULL,
    modifieddatetime timestamptz NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_ax.inventitembarcode (
    itembarcode      text        NOT NULL,
    itemid           text        NOT NULL,
    inventdimid      text        NOT NULL,
    barcodesetupid   text        NOT NULL,
    useforinput      integer     NOT NULL,
    dataareaid       text        NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_ax.inventdim (
    inventdimid      text        NOT NULL,
    inventsiteid     text        NOT NULL,
    inventlocationid text        NOT NULL,
    inventcolorid    text        NOT NULL,
    inventsizeid     text        NOT NULL,
    configid         text        NOT NULL,
    dataareaid       text        NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

-- INVENTSUM holds only current stock, so every sync appends a snapshot of it
-- instead of replacing it. That history is what the slow-stock analysis reads.
CREATE TABLE IF NOT EXISTS raw_ax.stock_snapshots (
    itemid           text        NOT NULL,
    inventdimid      text        NOT NULL,
    physicalinvent   numeric     NOT NULL,
    reservphysical   numeric     NOT NULL,
    availphysical    numeric     NOT NULL,
    onorder          numeric     NOT NULL,
    dataareaid       text        NOT NULL,
    modifieddatetime timestamptz NOT NULL,
    recid            bigint      NOT NULL,
    synced_at        timestamptz NOT NULL,
    PRIMARY KEY (synced_at, recid)
);

CREATE TABLE IF NOT EXISTS raw_ax.pricedisctable (
    relation         integer     NOT NULL,
    itemcode         integer     NOT NULL,
    itemrelation     text        NOT NULL,
    accountcode      integer     NOT NULL,
    accountrelation  text        NOT NULL,
    quantityamount   numeric     NOT NULL,
    amount           numeric     NOT NULL,
    currency         text        NOT NULL,
    fromdate         timestamptz NOT NULL,
    todate           timestamptz NOT NULL,
    dataareaid       text        NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_ax.custtable (
    accountnum       text        NOT NULL,
    name             text        NOT NULL,
    custgroup        text        NOT NULL,
    currency         text        NOT NULL,
    dataareaid       text        NOT NULL,
    createddatetime  timestamptz NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_ax.salestable (
    salesid          text        NOT NULL,
    custaccount      text        NOT NULL,
    salesstatus      integer     NOT NULL,
    salestype        integer     NOT NULL,
    currencycode     text        NOT NULL,
    dataareaid       text        NOT NULL,
    createddatetime  timestamptz NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_ax.salesline (
    salesid          text        NOT NULL,
    linenum          numeric     NOT NULL,
    itemid           text        NOT NULL,
    inventdimid      text        NOT NULL,
    salesqty         numeric     NOT NULL,
    salesprice       numeric     NOT NULL,
    lineamount       numeric     NOT NULL,
    salesstatus      integer     NOT NULL,
    dataareaid       text        NOT NULL,
    createddatetime  timestamptz NOT NULL,
    recid            bigint      PRIMARY KEY,
    synced_at        timestamptz NOT NULL
);
