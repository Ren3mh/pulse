"""MCP server: lets an AI agent read the dbt marts, and only the marts.

Run:  .venv/bin/python -m pulse_mcp.server      (stdio transport)
The access rules themselves are in pulse_mcp/marts.py.
"""

from contextlib import contextmanager
from functools import cache
from typing import Any

from mcp.server.mcpserver import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from mcp.server.mcpserver.tools.base import Tool
from mcp.types import ToolAnnotations

from pulse_mcp import marts as m

READ_ONLY = ToolAnnotations(read_only_hint=True, destructive_hint=False, idempotent_hint=True, open_world_hint=False)

INSTRUCTIONS = (
    "Analytics for Nordlys Design, a design-furniture webshop. You can read a small set of "
    "prepared marts (tables) built by dbt. Start with list_marts, read a mart's columns with "
    "describe_mart, then use query_mart. Numbers come from tracked shop events; cart adds are "
    "intent, not sales. Individual visitors and raw events are not available by design."
)


@cache
def catalog() -> dict[str, m.Mart]:
    return m.load_marts()


@contextmanager
def explained_to_agent():
    # A MartError is a mistake the agent can fix (wrong mart or column name), so
    # its message goes back to the model. Anything else stays a generic error.
    try:
        yield
    except m.MartError as error:
        raise ToolError(str(error)) from error


def list_marts() -> list[dict[str, str]]:
    """List the marts you can query, with what each one contains and its grain."""
    return [
        {"name": mart.name, "grain": mart.grain, "description": mart.description}
        for mart in catalog().values()
    ]


def describe_mart(name: str) -> dict[str, Any]:
    """Describe one mart: its grain and every column you can select, filter or sort on."""
    with explained_to_agent():
        mart = m.get_mart(catalog(), name)
    return {
        "name": mart.name,
        "grain": mart.grain,
        "description": mart.description,
        "columns": [{"name": c, "description": d} for c, d in mart.columns.items()],
    }


def query_mart(
    name: str,
    columns: list[str] | None = None,
    filters: list[m.Filter] | None = None,
    order_by: str | None = None,
    descending: bool = True,
    limit: int = 20,
) -> dict[str, Any]:
    """Read rows from one mart.

    columns: which columns to return (default: all).
    filters: all must match. Each is {"column", "op", "value"}; op is one of
      =, !=, <, <=, >, >=, contains (text contains value, case-insensitive) or
      includes (a list column such as missing_fields includes value).
    order_by: one column name to sort by; descending is the default.
    limit: rows to return, at most 100. The result says if more rows existed.
    """
    with explained_to_agent():
        mart = m.get_mart(catalog(), name)
        return m.run_query(mart, columns=columns, filters=filters, order_by=order_by,
                           descending=descending, limit=limit)


def strict_tool(fn) -> Tool:
    """Build a read-only tool that refuses arguments it doesn't know.

    By default an unknown argument is silently dropped, so an agent that sends
    {"query": ...} instead of {"filters": ...} gets unfiltered rows and believes
    they are filtered. Refusing it makes the agent fix the call instead.
    """
    tool = Tool.from_function(fn, annotations=READ_ONLY)
    base = tool.fn_metadata.arg_model
    tool.fn_metadata.arg_model = type(base.__name__, (base,), {"model_config": {**base.model_config, "extra": "forbid"}})
    tool.parameters["additionalProperties"] = False
    return tool


server = MCPServer(
    name="pulse-marts",
    instructions=INSTRUCTIONS,
    tools=[strict_tool(f) for f in (list_marts, describe_mart, query_mart)],
)


if __name__ == "__main__":
    server.run("stdio")
