"""Talk to the pulse MCP server the way an AI agent would, and print the transcript.

Starts pulse_mcp.server over stdio (exactly as Claude Code would), lists its
tools, then makes the calls an agent might make for one question:
"Which product texts should we write first for the German webshop, and what
are customers looking for that we don't have?"
Two calls at the end are meant to fail, to show what the agent cannot reach.

Usage: .venv/bin/python -m scripts.demo_agent_session > demo.md
"""

import asyncio
import json
import sys
from pathlib import Path

from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

PROTOTYPE_DIR = Path(__file__).resolve().parent.parent

CALLS = [
    ("list_marts", {}),
    ("describe_mart", {"name": "mart_catalog_gaps"}),
    ("query_mart", {
        "name": "mart_catalog_gaps",
        "columns": ["priority", "name", "missing_fields", "views", "view_to_cart_rate"],
        "filters": [{"column": "missing_german_text", "op": "=", "value": True}],
        "order_by": "priority", "descending": False, "limit": 5,
    }),
    ("query_mart", {
        "name": "mart_search_demand",
        "columns": ["search_query", "unique_searchers", "never_found_anything"],
        "filters": [{"column": "never_found_anything", "op": "=", "value": True}],
        "order_by": "unique_searchers", "limit": 10,
    }),
    ("query_mart", {
        "name": "mart_product_performance",
        "columns": ["name", "has_description_da", "unique_viewers", "view_to_cart_rate"],
        "order_by": "view_to_cart_rate", "descending": False, "limit": 5,
    }),
    # Should fail: raw events are not a mart.
    ("query_mart", {"name": "events", "limit": 5}),
    # Should fail: visitor ids are not a documented mart column.
    ("query_mart", {"name": "mart_daily_funnel", "columns": ["event_date", "distinct_id"]}),
]


def show(value) -> str:
    return "```json\n" + json.dumps(value, indent=2, ensure_ascii=False) + "\n```"


async def main() -> None:
    params = StdioServerParameters(
        command=sys.executable, args=["-m", "pulse_mcp.server"], cwd=str(PROTOTYPE_DIR)
    )
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            init = await session.initialize()
            print(f"# MCP session with `{init.server_info.name}`\n")
            print("**Server instructions given to the agent:**\n")
            print(f"> {init.instructions}\n")

            tools = await session.list_tools()
            print("## Tools offered\n")
            for tool in tools.tools:
                first_line = (tool.description or "").strip().splitlines()[0]
                read_only = tool.annotations and tool.annotations.read_only_hint
                print(f"- `{tool.name}`: {first_line}{' (read-only)' if read_only else ''}")

            for number, (name, arguments) in enumerate(CALLS, 1):
                result = await session.call_tool(name, arguments)
                print(f"\n## Call {number}: `{name}`\n")
                if arguments:
                    print("Arguments:\n\n" + show(arguments) + "\n")
                if result.is_error:
                    print("**Refused:** " + " ".join(c.text for c in result.content if hasattr(c, "text")))
                else:
                    payload = result.structured_content
                    if isinstance(payload, dict) and set(payload) == {"result"}:
                        payload = payload["result"]
                    print("Result:\n\n" + show(payload))


if __name__ == "__main__":
    asyncio.run(main())
