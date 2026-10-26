"""Database connection. The only place that knows where Postgres is."""

import os
from pathlib import Path

import psycopg

DEFAULT_URL = "postgresql://pulse:pulse@localhost:5432/pulse"
PROTOTYPE_DIR = Path(__file__).resolve().parent.parent


def database_url() -> str:
    return os.environ.get("DATABASE_URL", DEFAULT_URL)


def connect() -> psycopg.Connection:
    # One connection per request keeps the prototype simple.
    # A connection pool is a phase 4 (Scale) concern.
    return psycopg.connect(database_url())


def apply_schema() -> None:
    sql = (PROTOTYPE_DIR / "schema.sql").read_text()
    with connect() as conn:
        conn.execute(sql)


def seed_products() -> None:
    sql = (PROTOTYPE_DIR / "seed_products.sql").read_text()
    with connect() as conn:
        conn.execute(sql)
