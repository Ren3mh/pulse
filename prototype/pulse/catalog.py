"""Read-only access to the product catalogue."""

from psycopg.rows import dict_row

from pulse.db import connect

COLUMNS = """product_id, name, designer, category, price_dkk::float AS price_dkk, materials,
             width_cm, depth_cm, height_cm, description_da, description_de"""


def list_products(query: str | None = None) -> list[dict]:
    with connect() as conn, conn.cursor(row_factory=dict_row) as cur:
        if query:
            # Simple case-insensitive match on name, designer and category.
            pattern = f"%{query}%"
            cur.execute(
                f"""SELECT {COLUMNS} FROM products
                    WHERE name ILIKE %s OR designer ILIKE %s OR category ILIKE %s
                    ORDER BY name""",
                (pattern, pattern, pattern),
            )
        else:
            cur.execute(f"SELECT {COLUMNS} FROM products ORDER BY name")
        return cur.fetchall()


def get_product(product_id: str) -> dict | None:
    with connect() as conn, conn.cursor(row_factory=dict_row) as cur:
        cur.execute(f"SELECT {COLUMNS} FROM products WHERE product_id = %s", (product_id,))
        return cur.fetchone()
