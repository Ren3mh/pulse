"""Tests for what the agent can and cannot reach.

The first group needs no database. The second builds the real marts in
pulse_test (simulated traffic + dbt build) and queries them as pulse_agent.
"""

import os
import shutil
import subprocess
from pathlib import Path

import psycopg
import pytest
from pydantic import ValidationError

from pulse_mcp import marts as m

PROTOTYPE_DIR = Path(__file__).resolve().parent.parent
TEST_AGENT_URL = "postgresql://pulse_agent:pulse_agent@localhost:5432/pulse_test"

GAPS = m.Mart(
    name="mart_catalog_gaps",
    schema="marts",
    description="Products with gaps.",
    grain="one row per product with at least one gap",
    columns={"product_id": "id", "name": "name", "missing_fields": "list", "views": "views"},
)


# --- Query building: no database needed -------------------------------------


def test_default_query_selects_documented_columns_only():
    query, params = m.build_query(GAPS)
    text = query.as_string(None)
    assert text.startswith('SELECT "product_id", "name", "missing_fields", "views" FROM "marts"."mart_catalog_gaps"')
    assert params == [21]  # limit 20, plus one row to detect truncation


def test_unknown_column_is_refused():
    for bad in ["price_dkk", "name; DROP TABLE events", "*"]:
        with pytest.raises(m.MartError):
            m.build_query(GAPS, columns=[bad])
        with pytest.raises(m.MartError):
            m.build_query(GAPS, order_by=bad)
        with pytest.raises(m.MartError):
            m.build_query(GAPS, filters=[m.Filter(column=bad, op="=", value=1)])


def test_unknown_operator_is_refused():
    with pytest.raises(ValidationError):
        m.Filter(column="views", op="; DROP TABLE events", value=1)


def test_filter_values_are_parameters_not_sql():
    query, params = m.build_query(GAPS, filters=[m.Filter(column="name", op="=", value="x' OR '1'='1")])
    assert "OR" not in query.as_string(None)
    assert params[0] == "x' OR '1'='1"


def test_contains_escapes_wildcards():
    _, params = m.build_query(GAPS, filters=[m.Filter(column="name", op="contains", value="50%_off")])
    assert params[0] == "%50\\%\\_off%"


def test_limit_is_capped():
    _, params = m.build_query(GAPS, limit=10_000)
    assert params[-1] == m.MAX_ROWS + 1
    _, params = m.build_query(GAPS, limit=0)
    assert params[-1] == 2


def test_unknown_mart_lists_the_real_ones():
    with pytest.raises(m.MartError, match="Available: mart_catalog_gaps"):
        m.get_mart({GAPS.name: GAPS}, "events")


# --- Against real marts, as the agent role -----------------------------------


@pytest.fixture(scope="session")
def built_marts(tmp_path_factory):
    dbt = PROTOTYPE_DIR / ".venv" / "bin" / "dbt"
    if not dbt.exists() and not shutil.which("dbt"):
        pytest.skip("dbt is not installed")
    from scripts import simulate_traffic  # uses DATABASE_URL = pulse_test (conftest)

    simulate_traffic.run()
    target = tmp_path_factory.mktemp("dbt_target")
    result = subprocess.run(
        [str(dbt) if dbt.exists() else "dbt", "build", "--profiles-dir", ".", "--target-path", str(target)],
        cwd=PROTOTYPE_DIR / "analytics",
        env={**os.environ, "PULSE_DB_NAME": "pulse_test"},
        capture_output=True,
        text=True,
    )
    assert result.returncode == 0, result.stdout[-3000:]
    os.environ["PULSE_AGENT_DATABASE_URL"] = TEST_AGENT_URL
    return m.load_marts(target / "manifest.json")


def test_only_agent_exposed_models_are_offered(built_marts):
    assert set(built_marts) == {
        "mart_product_performance", "mart_search_demand", "mart_catalog_gaps", "mart_daily_funnel",
    }


def test_descriptions_come_from_dbt(built_marts):
    gaps = built_marts["mart_catalog_gaps"]
    assert gaps.grain == "one row per product with at least one gap"
    assert "German webshop" in gaps.description
    assert "description_de" in gaps.columns["missing_fields"]


def test_agent_can_query_a_mart(built_marts):
    result = m.run_query(
        built_marts["mart_catalog_gaps"],
        columns=["priority", "product_id", "missing_fields"],
        filters=[m.Filter(column="missing_fields", op="includes", value="description_de")],
        order_by="priority",
        descending=False,
        limit=2,
    )
    assert result["row_count"] == 2
    assert result["truncated"] is True
    assert all("description_de" in row["missing_fields"] for row in result["rows"])


def test_rare_searches_never_reach_the_agent(built_marts):
    rows = m.run_query(built_marts["mart_search_demand"], limit=100)["rows"]
    assert rows
    assert min(row["unique_searchers"] for row in rows) >= 5
    assert "kyst pendel messing" not in {row["search_query"] for row in rows}


@pytest.mark.parametrize("table", ["public.events", "public.products", "staging.stg_events"])
def test_agent_role_cannot_read_raw_or_staging(built_marts, table):
    with psycopg.connect(TEST_AGENT_URL) as conn:
        with pytest.raises(psycopg.errors.InsufficientPrivilege):
            conn.execute(f"SELECT 1 FROM {table} LIMIT 1")


def test_agent_role_cannot_write(built_marts):
    with psycopg.connect(TEST_AGENT_URL) as conn:
        with pytest.raises(psycopg.errors.ReadOnlySqlTransaction):
            conn.execute("DELETE FROM marts.mart_daily_funnel")
