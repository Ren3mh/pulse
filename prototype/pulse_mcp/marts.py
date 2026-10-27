"""What an agent may read, and how a request becomes a safe query.

All access rules live here, layered so one mistake doesn't open everything:

1. Only dbt models marked `meta.agent_exposed: true` are offered.
2. Only columns documented in dbt are offered. Undocumented means unknown to the agent.
3. Queries are built from those names with psycopg's sql.Identifier, values are
   always parameters, and there is no free-form SQL.
4. The connection uses the pulse_agent role, which can only SELECT from the
   marts schema and runs every transaction read-only (analytics/setup_roles.sql).
5. Results are capped at MAX_ROWS.
"""

import json
import os
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any, Literal

import psycopg
from psycopg import sql
from psycopg.rows import dict_row
from pydantic import BaseModel

PROTOTYPE_DIR = Path(__file__).resolve().parent.parent
DEFAULT_MANIFEST = PROTOTYPE_DIR / "analytics" / "target" / "manifest.json"
DEFAULT_AGENT_URL = "postgresql://pulse_agent:pulse_agent@localhost:5432/pulse"
MAX_ROWS = 100

Operator = Literal["=", "!=", "<", "<=", ">", ">=", "contains", "includes"]
OPERATOR_SQL = {"=": "=", "!=": "<>", "<": "<", "<=": "<=", ">": ">", ">=": ">="}


class MartError(ValueError):
    """A request the agent can fix: unknown mart, unknown column, bad filter."""


class Filter(BaseModel):
    column: str
    op: Operator
    value: str | int | float | bool


@dataclass(frozen=True)
class Mart:
    name: str
    schema: str
    description: str
    grain: str
    columns: dict[str, str]  # column name -> description


def manifest_path() -> Path:
    return Path(os.environ.get("PULSE_DBT_MANIFEST", DEFAULT_MANIFEST))


def agent_database_url() -> str:
    return os.environ.get("PULSE_AGENT_DATABASE_URL", DEFAULT_AGENT_URL)


def load_marts(path: Path | None = None) -> dict[str, Mart]:
    path = path or manifest_path()
    if not path.exists():
        raise FileNotFoundError(f"{path} not found. Run `dbt build` in analytics/ first.")
    manifest = json.loads(path.read_text())
    marts = {}
    for node in manifest["nodes"].values():
        meta = node.get("config", {}).get("meta") or node.get("meta") or {}
        if node["resource_type"] != "model" or meta.get("agent_exposed") is not True:
            continue
        columns = {
            name: col["description"].strip()
            for name, col in node["columns"].items()
            if col.get("description", "").strip()
        }
        marts[node["name"]] = Mart(
            name=node["name"],
            schema=node["schema"],
            description=" ".join(node["description"].split()),
            grain=meta.get("grain", ""),
            columns=columns,
        )
    return marts


def get_mart(marts: dict[str, Mart], name: str) -> Mart:
    if name not in marts:
        raise MartError(f"Unknown mart '{name}'. Available: {', '.join(sorted(marts))}.")
    return marts[name]


def _check_column(mart: Mart, column: str) -> str:
    if column not in mart.columns:
        raise MartError(
            f"'{column}' is not a column of {mart.name}. Columns: {', '.join(mart.columns)}."
        )
    return column


def build_query(
    mart: Mart,
    columns: list[str] | None = None,
    filters: list[Filter] | None = None,
    order_by: str | None = None,
    descending: bool = True,
    limit: int = 20,
) -> tuple[sql.Composed, list[Any]]:
    selected = [_check_column(mart, c) for c in columns] if columns else list(mart.columns)
    params: list[Any] = []
    conditions = []
    for f in filters or []:
        column = sql.Identifier(_check_column(mart, f.column))
        if f.op == "contains":
            conditions.append(sql.SQL("{}::text ILIKE %s").format(column))
            escaped = str(f.value).replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
            params.append(f"%{escaped}%")
        elif f.op == "includes":
            conditions.append(sql.SQL("%s = ANY({})").format(column))
            params.append(str(f.value))
        else:
            conditions.append(sql.SQL("{} {} %s").format(column, sql.SQL(OPERATOR_SQL[f.op])))
            params.append(f.value)

    query = sql.SQL("SELECT {} FROM {}.{}").format(
        sql.SQL(", ").join(map(sql.Identifier, selected)),
        sql.Identifier(mart.schema),
        sql.Identifier(mart.name),
    )
    if conditions:
        query += sql.SQL(" WHERE ") + sql.SQL(" AND ").join(conditions)
    if order_by:
        direction = sql.SQL("DESC NULLS LAST" if descending else "ASC NULLS LAST")
        query += sql.SQL(" ORDER BY {} {}").format(sql.Identifier(_check_column(mart, order_by)), direction)
    limit = max(1, min(int(limit), MAX_ROWS))
    query += sql.SQL(" LIMIT %s")
    params.append(limit + 1)  # one extra row tells us whether the result was cut off
    return query, params


def _json_safe(value: Any) -> Any:
    if isinstance(value, Decimal):
        return float(value)
    if isinstance(value, (date, datetime)):
        return value.isoformat()
    return value


def run_query(mart: Mart, limit: int = 20, **kwargs) -> dict:
    query, params = build_query(mart, limit=limit, **kwargs)
    limit = params[-1] - 1
    with psycopg.connect(agent_database_url()) as conn:
        conn.read_only = True
        with conn.cursor(row_factory=dict_row) as cur:
            cur.execute(query, params)
            rows = cur.fetchall()
    return {
        "mart": mart.name,
        "rows": [{k: _json_safe(v) for k, v in row.items()} for row in rows[:limit]],
        "row_count": min(len(rows), limit),
        "truncated": len(rows) > limit,
    }
